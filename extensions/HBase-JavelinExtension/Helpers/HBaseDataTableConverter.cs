using System;
using System.Collections.Generic;
using System.Data;
using GridTools.Javelin.HBaseActivities.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.HBaseActivities.Helpers
{
    public static class HBaseDataTableConverter
    {
        /// <summary>
        /// Converts decoded HBase rows into a standard ADO.NET DataTable with columns: RowKey, [Family:Qualifier], ...
        /// </summary>
        public static DataTable ToDataTable(List<Dictionary<string, object>> rows, string tableName = "HBaseData")
        {
            var dt = new DataTable(tableName);
            if (rows == null || rows.Count == 0)
            {
                dt.Columns.Add("RowKey", typeof(string));
                return dt;
            }

            var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            columnNames.Add("RowKey");
            dt.Columns.Add("RowKey", typeof(string));

            foreach (var rowDict in rows)
            {
                foreach (var key in rowDict.Keys)
                {
                    if (!columnNames.Contains(key))
                    {
                        columnNames.Add(key);
                        dt.Columns.Add(key, typeof(string));
                    }
                }
            }

            dt.BeginLoadData();
            foreach (var rowDict in rows)
            {
                var dr = dt.NewRow();
                foreach (var col in columnNames)
                {
                    if (rowDict.TryGetValue(col, out var val) && val != null)
                    {
                        dr[col] = val.ToString();
                    }
                    else
                    {
                        dr[col] = DBNull.Value;
                    }
                }
                dt.Rows.Add(dr);
            }
            dt.EndLoadData();

            return dt;
        }

        /// <summary>
        /// Decodes a raw HBase REST JSON response (containing Base64 key, column, and $) into human-readable dictionary rows.
        /// </summary>
        public static List<Dictionary<string, object>> DecodeHBaseRestJson(string rawJson)
        {
            var result = new List<Dictionary<string, object>>();
            if (string.IsNullOrWhiteSpace(rawJson))
                return result;

            var rowSet = JsonConvert.DeserializeObject<HBaseRowSet>(rawJson);
            if (rowSet?.Rows == null)
                return result;

            foreach (var row in rowSet.Rows)
            {
                var rowDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                string decodedKey = HBaseClientHelper.Base64Decode(row.Key);
                rowDict["RowKey"] = decodedKey;

                if (row.Cells != null)
                {
                    foreach (var cell in row.Cells)
                    {
                        string decodedCol = HBaseClientHelper.Base64Decode(cell.Column);
                        string decodedVal = HBaseClientHelper.Base64Decode(cell.Value);

                        rowDict[decodedCol] = decodedVal;

                        // Also store timestamp if present with suffix _ts
                        if (cell.Timestamp.HasValue)
                        {
                            rowDict[decodedCol + "_ts"] = cell.Timestamp.Value;
                        }
                    }
                }
                result.Add(rowDict);
            }

            return result;
        }
    }
}
