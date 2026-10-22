using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Net.Http;
using System.Text;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;
using GridTools.Javelin.HBaseActivities.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase Put Row")]
    [Description("Inserts or updates rows and columns in an Apache HBase table via REST / Stargate")]
    public class HBasePutRowActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("HBase REST URL")]
        [Description("Base URL of HBase REST daemon, e.g. http://hbase-host:8080")]
        public InArgument<string> RestUrl { get; set; }

        [RequiredArgument]
        [DisplayName("Table Name")]
        [Description("Name of the HBase table (e.g. customers or ns:customers)")]
        public InArgument<string> TableName { get; set; }

        [DisplayName("Row Key")]
        [Description("Row Key if inserting a single row (optional if using Input DataTable or Multi-row JSON)")]
        public InArgument<string> RowKey { get; set; }

        [DisplayName("Columns (JSON)")]
        [Description("JSON object of column:value pairs, e.g. {\"cf:name\": \"John\", \"cf:status\": \"ACTIVE\"} or an array of rows")]
        public InArgument<string> ColumnsJson { get; set; }

        [DisplayName("Input DataTable")]
        [Description("Optional DataTable to bulk write to HBase. Must have a 'RowKey' column.")]
        public InArgument<DataTable> InputDataTable { get; set; }

        [DisplayName("Username")]
        public InArgument<string> Username { get; set; }

        [DisplayName("Password")]
        [JavelinPassword]
        public InArgument<string> Password { get; set; }

        [DisplayName("Bearer Token")]
        [JavelinPassword]
        public InArgument<string> BearerToken { get; set; }

        [DisplayName("Timeout (Seconds)")]
        [DefaultValue(60)]
        public InArgument<int> TimeoutSeconds { get; set; }

        [DisplayName("Ignore SSL Certificate Errors")]
        [DefaultValue(true)]
        public InArgument<bool> IgnoreSslErrors { get; set; }

        // --- Outputs ---

        [DisplayName("Rows Processed")]
        public OutArgument<int> RowsProcessed { get; set; }

        [DisplayName("HTTP Status Code")]
        public OutArgument<int> StatusCode { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string table = null;
            try
            {
                base.Execute(context);

                string restUrl = RestUrl.Get(context);
                table = TableName.Get(context);
                string rowKey = RowKey.Get(context);
                string colsJson = ColumnsJson.Get(context);
                DataTable inputDt = InputDataTable.Get(context);
                string user = Username.Get(context);
                string pass = Password.Get(context);
                string token = BearerToken.Get(context);
                int timeout = TimeoutSeconds.Get(context);
                bool ignoreSsl = IgnoreSslErrors.Get(context);

                if (string.IsNullOrWhiteSpace(restUrl))
                    throw new ArgumentException("HBase REST URL cannot be empty.");
                if (string.IsNullOrWhiteSpace(table))
                    throw new ArgumentException("Table Name cannot be empty.");

                var hbaseRowSet = new HBaseRowSet();

                // 1. Check if InputDataTable was provided
                if (inputDt != null && inputDt.Rows.Count > 0)
                {
                    string rowKeyColName = "RowKey";
                    if (!inputDt.Columns.Contains(rowKeyColName))
                    {
                        // Check case-insensitive
                        foreach (DataColumn dc in inputDt.Columns)
                        {
                            if (string.Equals(dc.ColumnName, "RowKey", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dc.ColumnName, "Key", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dc.ColumnName, "id", StringComparison.OrdinalIgnoreCase))
                            {
                                rowKeyColName = dc.ColumnName;
                                break;
                            }
                        }
                    }

                    if (!inputDt.Columns.Contains(rowKeyColName))
                        throw new InvalidOperationException("Input DataTable must contain a 'RowKey' or 'Key' column.");

                    foreach (DataRow dr in inputDt.Rows)
                    {
                        string rk = dr[rowKeyColName]?.ToString();
                        if (string.IsNullOrWhiteSpace(rk)) continue;

                        var hbRow = new HBaseRow { Key = HBaseClientHelper.Base64Encode(rk) };

                        foreach (DataColumn col in inputDt.Columns)
                        {
                            if (string.Equals(col.ColumnName, rowKeyColName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (dr.IsNull(col)) continue;

                            string colName = col.ColumnName;
                            // Ensure column family format (e.g. if no colon, default to "d:" or cf)
                            if (!colName.Contains(":"))
                            {
                                colName = "d:" + colName;
                            }

                            string valStr = dr[col].ToString();
                            hbRow.Cells.Add(new HBaseCell
                            {
                                Column = HBaseClientHelper.Base64Encode(colName),
                                Value = HBaseClientHelper.Base64Encode(valStr)
                            });
                        }
                        hbaseRowSet.Rows.Add(hbRow);
                    }
                }
                // 2. Or parse ColumnsJson
                else if (!string.IsNullOrWhiteSpace(colsJson))
                {
                    colsJson = colsJson.Trim();

                    // Case A: User passed a full JSON array of rows: [{"RowKey":"1", "cf:name":"Alice"}, ...]
                    if (colsJson.StartsWith("["))
                    {
                        var arr = JArray.Parse(colsJson);
                        foreach (JObject item in arr)
                        {
                            string itemKey = rowKey;
                            if (item["RowKey"] != null) itemKey = item["RowKey"].ToString();
                            else if (item["rowKey"] != null) itemKey = item["rowKey"].ToString();
                            else if (item["key"] != null) itemKey = item["key"].ToString();

                            if (string.IsNullOrWhiteSpace(itemKey))
                                throw new InvalidOperationException("Each item in array must provide 'RowKey' or use the activity RowKey property.");

                            var hbRow = new HBaseRow { Key = HBaseClientHelper.Base64Encode(itemKey) };

                            foreach (var prop in item.Properties())
                            {
                                if (string.Equals(prop.Name, "RowKey", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(prop.Name, "key", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                string colName = prop.Name;
                                if (!colName.Contains(":")) colName = "d:" + colName;

                                hbRow.Cells.Add(new HBaseCell
                                {
                                    Column = HBaseClientHelper.Base64Encode(colName),
                                    Value = HBaseClientHelper.Base64Encode(prop.Value?.ToString() ?? "")
                                });
                            }
                            hbaseRowSet.Rows.Add(hbRow);
                        }
                    }
                    // Case B: User passed a JSON object of column-values for the specified RowKey
                    else
                    {
                        if (string.IsNullOrWhiteSpace(rowKey))
                            throw new ArgumentException("RowKey must be specified when ColumnsJson is a single object.");

                        var obj = JObject.Parse(colsJson);
                        var hbRow = new HBaseRow { Key = HBaseClientHelper.Base64Encode(rowKey) };

                        foreach (var prop in obj.Properties())
                        {
                            string colName = prop.Name;
                            if (!colName.Contains(":")) colName = "d:" + colName;

                            hbRow.Cells.Add(new HBaseCell
                            {
                                Column = HBaseClientHelper.Base64Encode(colName),
                                Value = HBaseClientHelper.Base64Encode(prop.Value?.ToString() ?? "")
                            });
                        }
                        hbaseRowSet.Rows.Add(hbRow);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Either InputDataTable or ColumnsJson must be provided.");
                }

                if (hbaseRowSet.Rows.Count == 0)
                {
                    RowsProcessed.Set(context, 0);
                    StatusCode.Set(context, 200);
                    return;
                }

                var client = HBaseClientHelper.GetOrCreateClient(restUrl, user, pass, token, timeout, ignoreSsl);
                string escapedTable = Uri.EscapeDataString(table);
                string endpoint = $"{escapedTable}/false-row-key"; // Standard Stargate multi-row PUT endpoint convention

                string payload = JsonConvert.SerializeObject(hbaseRowSet);
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                GlobalSettings.MessageLogger.LogMessage("Sending HBase PUT request to table {0} with {1} row(s)", table, hbaseRowSet.Rows.Count);

                var response = client.PutAsync(endpoint, content).GetAwaiter().GetResult();
                StatusCode.Set(context, (int)response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    string errContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new HttpRequestException($"HBase PUT failed with status {response.StatusCode}: {errContent}");
                }

                RowsProcessed.Set(context, hbaseRowSet.Rows.Count);
                GlobalSettings.MessageLogger.LogMessage("HBase Put completed successfully. Rows written: {0}", hbaseRowSet.Rows.Count);
            }
            catch (Exception ex)
            {
                string friendlyMessage = HBaseErrorMessageFormatter.FormatErrorMessage(ex.Message, DisplayName, table);
                var bex = new BusinessException(friendlyMessage, ex);
                if (!continueOnErrorInExecMode)
                {
                    throw bex;
                }
            }
        }
    }
}
