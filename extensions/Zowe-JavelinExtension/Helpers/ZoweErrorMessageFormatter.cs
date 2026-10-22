using System;
using System.Text.RegularExpressions;

namespace GridTools.Javelin.ZoweActivities.Helpers
{
    public static class ZoweErrorMessageFormatter
    {
        private static readonly Regex AuthErrorRegex = new Regex(
            @"(Unauthorized|Authentication failed|Invalid credentials|HTTP status 401|HTTP status 403|password expired|user revoked|SEC_E_LOGON_DENIED)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ConnectionErrorRegex = new Regex(
            @"(ECONNREFUSED|ENOTFOUND|ETIMEDOUT|ConnectTimeoutException|Connection refused|Host unreachable|HTTP status 502|HTTP status 503|HTTP status 504)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SslCertRegex = new Regex(
            @"(UNABLE_TO_VERIFY_LEAF_SIGNATURE|CERT_HAS_EXPIRED|DEPTH_ZERO_SELF_SIGNED_CERT|self signed certificate|certificate has expired)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MemberNotFoundRegex = new Regex(
            @"(Member\s+['""]?\S+['""]?\s+not found|MEMBER\s+\S+\s+NOT FOUND|IKJ56702I)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex JobNotFoundRegex = new Regex(
            @"(Job\s+not\s+found|JOBNOTFOUND|No\s+jobs\s+found|JOB\s+NOT\s+FOUND)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DatasetNotFoundRegex = new Regex(
            @"(Dataset not found|Data set .* not found|DATA SET .* NOT IN CATALOG|RC=0004|RC=0008|ARC0140I|IKJ56228I)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ZoweCliNotFoundRegex = new Regex(
            @"(zowe: command not found|'zowe' is not recognized as an internal or external command|The term 'zowe' is not recognized)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string FormatErrorMessage(string rawError, string activityName, string targetResource = null)
        {
            if (string.IsNullOrWhiteSpace(rawError))
                return $"Erro na execução da atividade Zowe Mainframe [{activityName}].";

            if (ZoweCliNotFoundRegex.IsMatch(rawError))
            {
                return $"[Zowe CLI Não Encontrado]\n" +
                       $"O comando 'zowe' não foi localizado no PATH do sistema onde o Javelin está executando.\n" +
                       $"Solução: Instale o Zowe CLI (npm install -g @zowe/cli) ou especifique o caminho completo em 'ZoweCliPath'.";
            }

            if (AuthErrorRegex.IsMatch(rawError))
            {
                return $"[Falha de Autenticação z/OSMF / RACF / ACF2]\n" +
                       $"As credenciais informadas para o mainframe foram rejeitadas ou o usuário está sem permissão / revogado.\n" +
                       $"Detalhes: {rawError.Trim()}";
            }

            if (SslCertRegex.IsMatch(rawError))
            {
                return $"[Erro de Certificado SSL / TLS z/OSMF]\n" +
                       $"O certificado HTTPS do z/OSMF é auto-assinado ou não confiável.\n" +
                       $"Solução: Marque a opção 'RejectUnauthorized = false' ou 'IgnoreSslErrors = true' no Javelin.";
            }

            if (ConnectionErrorRegex.IsMatch(rawError))
            {
                return $"[Falha de Conexão com Mainframe]\n" +
                       $"Não foi possível conectar ao servidor z/OSMF ou host z/OS.\n" +
                       $"Verifique Host, Porta e se o z/OSMF está ativo na LPAR.\n" +
                       $"Detalhes: {rawError.Trim()}";
            }

            if (MemberNotFoundRegex.IsMatch(rawError))
            {
                string res = !string.IsNullOrEmpty(targetResource) ? targetResource : "especificado";
                return $"[Membro de PDS Não Encontrado]\n" +
                       $"O membro '{res}' não foi encontrado no particionado z/OS.\n" +
                       $"Detalhes: {rawError.Trim()}";
            }

            if (JobNotFoundRegex.IsMatch(rawError))
            {
                string res = !string.IsNullOrEmpty(targetResource) ? targetResource : "especificado";
                return $"[Job z/OS / JES Não Encontrado]\n" +
                       $"O Job '{res}' não foi encontrado no spool JES.\n" +
                       $"Detalhes: {rawError.Trim()}";
            }

            if (DatasetNotFoundRegex.IsMatch(rawError))
            {
                string res = !string.IsNullOrEmpty(targetResource) ? targetResource : "especificado";
                return $"[Data Set Não Encontrado no Mainframe]\n" +
                       $"O Data Set '{res}' não existe nos catálogos / volumes do z/OS.\n" +
                       $"Detalhes: {rawError.Trim()}";
            }

            return $"[Erro Zowe Mainframe - {activityName}]\n{rawError.Trim()}";
        }
    }
}
