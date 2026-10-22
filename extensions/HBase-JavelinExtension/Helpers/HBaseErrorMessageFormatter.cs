using System;
using System.Text.RegularExpressions;

namespace GridTools.Javelin.HBaseActivities.Helpers
{
    public static class HBaseErrorMessageFormatter
    {
        // Matches "Column family <cf> does not exist in region <reg> in table '<tbl>', ... {NAME => 'fam1'}, {NAME => 'fam2'}"
        private static readonly Regex ColumnFamilyRegex = new Regex(
            @"NoSuchColumnFamilyException:\s*Column family\s+(?<cf>\S+)\s+does not exist(?:.*?table '(?<tbl>[^']+)')?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Matches "TableNotFoundException: <table_name>"
        private static readonly Regex TableNotFoundRegex = new Regex(
            @"TableNotFoundException:\s*(?<tbl>\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches "TableNotEnabledException: <table_name>"
        private static readonly Regex TableNotEnabledRegex = new Regex(
            @"TableNotEnabledException:\s*(?<tbl>\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches "TableExistsException: <table_name>"
        private static readonly Regex TableExistsRegex = new Regex(
            @"TableExistsException:\s*(?<tbl>\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches "hbase: not found" or "No such file or directory"
        private static readonly Regex BinaryNotFoundRegex = new Regex(
            @"(?:sh:\s*\d*:\s*(?<bin>\S+):\s*not found|No such file or directory)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches Kerberos authentication failures
        private static readonly Regex KerberosErrorRegex = new Regex(
            @"(GSSException|LoginException|kinit:.*|KrbException|Ticket expired|Clock skew)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches Zookeeper / Master connection timeouts
        private static readonly Regex ConnectionErrorRegex = new Regex(
            @"(MasterNotRunningException|ZooKeeperConnectionException|ConnectionRefused|RetriesExhaustedException)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Converts lengthy, raw Java/HBase stack traces into clean, actionable, wrapped error messages.
        /// </summary>
        public static string FormatErrorMessage(string rawError, string activityName, string tableName = null)
        {
            if (string.IsNullOrWhiteSpace(rawError))
                return $"Error executing {activityName}.";

            string msg = null;

            // 1. Column Family does not exist
            var matchCf = ColumnFamilyRegex.Match(rawError);
            if (matchCf.Success)
            {
                string cf = matchCf.Groups["cf"].Value;
                string tbl = !string.IsNullOrEmpty(matchCf.Groups["tbl"].Value) ? matchCf.Groups["tbl"].Value : tableName;

                // Extract valid families if present in error message
                var famMatches = Regex.Matches(rawError, @"\{NAME\s*=>\s*'([^']+)'\}");
                string validFams = "";
                if (famMatches.Count > 0)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (Match m in famMatches)
                    {
                        list.Add($"'{m.Groups[1].Value}'");
                    }
                    validFams = $"\nColumn families existentes: [{string.Join(", ", list)}].";
                }

                msg = $"A Column Family '{cf}' não existe na tabela '{tbl}'.{validFams}\nVerifique o prefixo 'familia:coluna' informado no Javelin.";
            }
            // 2. Table Not Found
            else if (TableNotFoundRegex.IsMatch(rawError))
            {
                var matchTblNotFound = TableNotFoundRegex.Match(rawError);
                string tbl = matchTblNotFound.Groups["tbl"].Value;
                msg = $"A tabela '{tbl}' não foi encontrada no cluster HBase.\nVerifique se o nome ou namespace estão corretos.";
            }
            // 3. Table Disabled
            else if (TableNotEnabledRegex.IsMatch(rawError))
            {
                var matchDisabled = TableNotEnabledRegex.Match(rawError);
                string tbl = matchDisabled.Groups["tbl"].Value;
                msg = $"A tabela '{tbl}' está desabilitada (DISABLED).\nHabilite-a com: enable '{tbl}'.";
            }
            // 4. Table Already Exists
            else if (TableExistsRegex.IsMatch(rawError))
            {
                var matchExists = TableExistsRegex.Match(rawError);
                string tbl = matchExists.Groups["tbl"].Value;
                msg = $"A tabela '{tbl}' já existe no HBase.";
            }
            // 5. Binary Not Found
            else if (BinaryNotFoundRegex.IsMatch(rawError))
            {
                msg = "O comando 'hbase' não foi encontrado no PATH do servidor SSH.\nConfigure a propriedade 'HBaseBinPath' com o caminho completo (ex: /usr/bin/hbase).";
            }
            // 6. Kerberos
            else if (KerberosErrorRegex.IsMatch(rawError))
            {
                msg = $"Falha de autenticação Kerberos:\n{ExtractFirstMeaningfulLine(rawError)}\nVerifique KinitPrincipal e KinitKeytabPath.";
            }
            // 7. Zookeeper / Master unreachable
            else if (ConnectionErrorRegex.IsMatch(rawError))
            {
                msg = $"Não foi possível conectar ao HBase Master / Zookeeper:\n{ExtractFirstMeaningfulLine(rawError)}";
            }
            else
            {
                // Fallback clean line
                msg = ExtractFirstMeaningfulLine(rawError);
            }

            // Clean any trailing period or spaces
            return msg?.Trim();
        }

        private static string ExtractFirstMeaningfulLine(string raw)
        {
            var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("at ") || line.StartsWith("onCallFinished") || line.StartsWith("run at") ||
                    line.StartsWith("channelRead") || line.StartsWith("invokeChannelRead") || line.StartsWith("processSelected") ||
                    line.StartsWith("SLF4J:") || line.StartsWith("Java HotSpot"))
                {
                    continue;
                }

                if (line.Contains("Exception:") || line.Contains("Error:"))
                {
                    return line;
                }
            }

            return lines.Length > 0 ? lines[0] : raw;
        }
    }
}
