using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace GridTools.Javelin.HBaseActivities.Helpers
{
    public static class HBaseShellParser
    {
        // Regex matches lines formatted as:
        //  row_key                           column=cf:qualifier, timestamp=12345678, value=my_val
        // Note: handles optional timestamp and values that might be empty
        private static readonly Regex CellWithKeyRegex = new Regex(
            @"^\s*(?<key>\S+)\s+column=(?<col>[^,\r\n]+?)(?:,\s*timestamp=(?<ts>\d+))?,\s*value=(?<val>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Regex matches row count output like: "3 row(s)" or "1 row(s)"
        private static readonly Regex RowCountRegex = new Regex(
            @"(?<count>\d+)\s+row\(s\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<Dictionary<string, string>> ParseShellOutputToRows(string shellOutput, out int detectedRowCount)
        {
            var rowsMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            detectedRowCount = 0;

            if (string.IsNullOrWhiteSpace(shellOutput))
                return new List<Dictionary<string, string>>();

            var matchCount = RowCountRegex.Match(shellOutput);
            if (matchCount.Success && int.TryParse(matchCount.Groups["count"].Value, out int count))
            {
                detectedRowCount = count;
            }

            var lines = shellOutput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string currentKey = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                string trimmedStart = line.TrimStart();

                // Skip shell banners, prompt, SLF4J logs, header row ("ROW   COLUMN+CELL") and timing/footer lines
                if (trimmedStart.StartsWith("hbase(main):", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("hbase>", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("SLF4J:", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("Took ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("ROW ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.StartsWith("Scanning ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedStart.Contains("row(s)"))
                {
                    continue;
                }

                int colIdx = line.IndexOf("column=", StringComparison.OrdinalIgnoreCase);
                if (colIdx >= 0)
                {
                    string beforeCol = line.Substring(0, colIdx).Trim();
                    string afterCol = line.Substring(colIdx + 7); // skip "column="

                    string key = !string.IsNullOrEmpty(beforeCol) ? beforeCol : currentKey;

                    if (!string.IsNullOrEmpty(key))
                    {
                        if (!rowsMap.TryGetValue(key, out var rowDict))
                        {
                            rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            rowDict["RowKey"] = key;
                            rowsMap[key] = rowDict;
                        }

                        currentKey = key;

                        // Parse "cf:col, timestamp=..., value=..." or "cf:col, value=..."
                        int valIdx = afterCol.IndexOf(", value=", StringComparison.OrdinalIgnoreCase);
                        if (valIdx >= 0)
                        {
                            string colAndTs = afterCol.Substring(0, valIdx);
                            string val = afterCol.Substring(valIdx + 8);

                            int tsIdx = colAndTs.IndexOf(", timestamp=", StringComparison.OrdinalIgnoreCase);
                            string col;
                            string ts = "";
                            if (tsIdx >= 0)
                            {
                                col = colAndTs.Substring(0, tsIdx).Trim();
                                ts = colAndTs.Substring(tsIdx + 12).Trim();
                            }
                            else
                            {
                                col = colAndTs.Trim();
                            }

                            rowDict[col] = val;
                            if (!string.IsNullOrEmpty(ts))
                            {
                                rowDict[col + "_ts"] = ts;
                            }
                        }
                        else
                        {
                            // If there is no ", value=" token, save whatever is in column
                            rowDict[afterCol.Trim()] = "";
                        }
                    }
                }
                else
                {
                    // Fallback to CellWithKeyRegex if column= wasn't lowercased or has different spacing
                    var m = CellWithKeyRegex.Match(line);
                    if (m.Success)
                    {
                        string key = m.Groups["key"].Value.Trim();
                        string col = m.Groups["col"].Value.Trim();
                        string val = m.Groups["val"].Value;
                        string ts = m.Groups["ts"]?.Value ?? "";

                        if (!rowsMap.TryGetValue(key, out var rowDict))
                        {
                            rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            rowDict["RowKey"] = key;
                            rowsMap[key] = rowDict;
                        }

                        rowDict[col] = val;
                        if (!string.IsNullOrEmpty(ts))
                        {
                            rowDict[col + "_ts"] = ts;
                        }
                        currentKey = key;
                    }
                }
            }

            var result = new List<Dictionary<string, string>>(rowsMap.Values);
            if (detectedRowCount == 0)
            {
                detectedRowCount = result.Count;
            }

            return result;
        }

        public static DataTable ToDataTable(List<Dictionary<string, string>> rows, string tableName = "HBaseShellData")
        {
            var dt = new DataTable(tableName);
            if (rows == null || rows.Count == 0)
            {
                dt.Columns.Add("RowKey", typeof(string));
                return dt;
            }

            var colNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            colNames.Add("RowKey");
            dt.Columns.Add("RowKey", typeof(string));

            foreach (var r in rows)
            {
                foreach (var k in r.Keys)
                {
                    if (!colNames.Contains(k))
                    {
                        colNames.Add(k);
                        dt.Columns.Add(k, typeof(string));
                    }
                }
            }

            dt.BeginLoadData();
            foreach (var r in rows)
            {
                var dr = dt.NewRow();
                foreach (var col in colNames)
                {
                    if (r.TryGetValue(col, out var val) && val != null)
                        dr[col] = val;
                    else
                        dr[col] = DBNull.Value;
                }
                dt.Rows.Add(dr);
            }
            dt.EndLoadData();

            return dt;
        }
    }
}
