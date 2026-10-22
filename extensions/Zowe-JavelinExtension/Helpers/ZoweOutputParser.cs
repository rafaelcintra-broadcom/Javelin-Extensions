using System;
using System.Data;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.ZoweActivities.Helpers
{
    public static class ZoweOutputParser
    {
        public static DataTable ParseJsonArrayToDataTable(string jsonText, string tableName = "ZoweData")
        {
            var dt = new DataTable(tableName);
            if (string.IsNullOrWhiteSpace(jsonText))
                return dt;

            try
            {
                var token = JToken.Parse(jsonText.Trim());
                if (token is JArray arr)
                {
                    foreach (var item in arr)
                    {
                        if (item is JObject obj)
                        {
                            foreach (var prop in obj.Properties())
                            {
                                if (!dt.Columns.Contains(prop.Name))
                                {
                                    dt.Columns.Add(prop.Name, typeof(string));
                                }
                            }
                        }
                    }

                    foreach (var item in arr)
                    {
                        if (item is JObject obj)
                        {
                            var row = dt.NewRow();
                            foreach (var prop in obj.Properties())
                            {
                                row[prop.Name] = prop.Value?.ToString() ?? "";
                            }
                            dt.Rows.Add(row);
                        }
                        else
                        {
                            if (!dt.Columns.Contains("Value"))
                                dt.Columns.Add("Value", typeof(string));

                            var row = dt.NewRow();
                            row["Value"] = item.ToString();
                            dt.Rows.Add(row);
                        }
                    }
                }
                else if (token is JObject singleObj)
                {
                    foreach (var prop in singleObj.Properties())
                    {
                        if (!dt.Columns.Contains(prop.Name))
                            dt.Columns.Add(prop.Name, typeof(string));
                    }
                    var row = dt.NewRow();
                    foreach (var prop in singleObj.Properties())
                    {
                        row[prop.Name] = prop.Value?.ToString() ?? "";
                    }
                    dt.Rows.Add(row);
                }
            }
            catch
            {
                // Fallback: parse line by line
                return ParseTextLinesToDataTable(jsonText, tableName);
            }

            return dt;
        }

        public static DataTable ParseTextLinesToDataTable(string text, string tableName = "ZoweLines")
        {
            var dt = new DataTable(tableName);
            dt.Columns.Add("LineNumber", typeof(int));
            dt.Columns.Add("Content", typeof(string));

            if (string.IsNullOrWhiteSpace(text))
                return dt;

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                var row = dt.NewRow();
                row["LineNumber"] = i + 1;
                row["Content"] = lines[i];
                dt.Rows.Add(row);
            }

            return dt;
        }

        public static string ExtractJsonFromCliOutput(string rawOutput)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
                return "";

            int startArr = rawOutput.IndexOf('[');
            int lastArr = rawOutput.LastIndexOf(']');
            int startObj = rawOutput.IndexOf('{');
            int lastObj = rawOutput.LastIndexOf('}');

            if (startArr >= 0 && lastArr > startArr)
            {
                return rawOutput.Substring(startArr, (lastArr - startArr) + 1);
            }
            if (startObj >= 0 && lastObj > startObj)
            {
                return rawOutput.Substring(startObj, (lastObj - startObj) + 1);
            }

            return rawOutput.Trim();
        }
    }
}
