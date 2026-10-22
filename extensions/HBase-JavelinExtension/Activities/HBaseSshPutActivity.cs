using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using System.Text;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase SSH Put")]
    [Description("Executes put commands into an HBase table over SSH using JSON data or an input DataTable")]
    public class HBaseSshPutActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("SSH Host")]
        public InArgument<string> Host { get; set; }

        [DisplayName("SSH Port")]
        [DefaultValue(22)]
        public InArgument<int> Port { get; set; }

        [RequiredArgument]
        [DisplayName("SSH Username")]
        public InArgument<string> Username { get; set; }

        [DisplayName("SSH Password")]
        [JavelinPassword]
        public InArgument<string> Password { get; set; }

        [DisplayName("Private Key Path")]
        public InArgument<string> PrivateKeyPath { get; set; }

        [DisplayName("Private Key Passphrase")]
        [JavelinPassword]
        public InArgument<string> Passphrase { get; set; }

        [RequiredArgument]
        [DisplayName("Table Name")]
        public InArgument<string> TableName { get; set; }

        [DisplayName("Row Key")]
        public InArgument<string> RowKey { get; set; }

        [DisplayName("Columns (JSON)")]
        [Description("Column-value pairs, e.g. {\"cf:name\": \"Alice\", \"cf:tier\": \"Gold\"} or an array of rows")]
        public InArgument<string> ColumnsJson { get; set; }

        [DisplayName("Input DataTable")]
        [Description("Optional DataTable to write. Must contain a 'RowKey' column.")]
        public InArgument<DataTable> InputDataTable { get; set; }

        [DisplayName("HBase Binary Path")]
        [DefaultValue("hbase")]
        public InArgument<string> HBaseBinPath { get; set; }

        [DisplayName("Kerberos Principal")]
        public InArgument<string> KinitPrincipal { get; set; }

        [DisplayName("Kerberos Keytab Path")]
        public InArgument<string> KinitKeytabPath { get; set; }

        [DisplayName("Timeout (Seconds)")]
        [DefaultValue(180)]
        public InArgument<int> TimeoutSeconds { get; set; }

        // --- Outputs ---

        [DisplayName("Statements Executed")]
        public OutArgument<int> StatementsExecuted { get; set; }

        [DisplayName("Raw Shell Output")]
        public OutArgument<string> RawOutput { get; set; }

        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string table = string.Empty;
            try
            {
                base.Execute(context);

                string host = Host.Get(context);
                int port = Port.Get(context);
                string user = Username.Get(context);
                string pass = Password.Get(context);
                string keyPath = PrivateKeyPath.Get(context);
                string passphrase = Passphrase.Get(context);
                table = TableName.Get(context);
                string rowKey = RowKey.Get(context);
                string colsJson = ColumnsJson.Get(context);
                DataTable inputDt = InputDataTable.Get(context);
                string binPath = HBaseBinPath.Get(context);
                string kPrincipal = KinitPrincipal.Get(context);
                string kKeytab = KinitKeytabPath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("SSH Host cannot be empty.");
                if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("SSH Username cannot be empty.");
                if (string.IsNullOrWhiteSpace(table)) throw new ArgumentException("Table Name cannot be empty.");

                var sbStatements = new StringBuilder();
                int stmtCount = 0;

                // 1. Check InputDataTable
                if (inputDt != null && inputDt.Rows.Count > 0)
                {
                    string rowKeyCol = "RowKey";
                    if (!inputDt.Columns.Contains(rowKeyCol))
                    {
                        foreach (DataColumn dc in inputDt.Columns)
                        {
                            if (string.Equals(dc.ColumnName, "RowKey", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(dc.ColumnName, "Key", StringComparison.OrdinalIgnoreCase))
                            {
                                rowKeyCol = dc.ColumnName;
                                break;
                            }
                        }
                    }

                    if (!inputDt.Columns.Contains(rowKeyCol))
                        throw new InvalidOperationException("Input DataTable must contain a 'RowKey' or 'Key' column.");

                    foreach (DataRow row in inputDt.Rows)
                    {
                        string rk = row[rowKeyCol]?.ToString();
                        if (string.IsNullOrWhiteSpace(rk)) continue;

                        foreach (DataColumn col in inputDt.Columns)
                        {
                            if (string.Equals(col.ColumnName, rowKeyCol, StringComparison.OrdinalIgnoreCase)) continue;
                            if (row.IsNull(col)) continue;

                            string cName = col.ColumnName;
                            if (!cName.Contains(":")) cName = "d:" + cName;
                            string val = row[col]?.ToString().Replace("'", "\\'");

                            sbStatements.AppendLine($"put '{table}', '{rk}', '{cName}', '{val}'");
                            stmtCount++;
                        }
                    }
                }
                // 2. Parse ColumnsJson
                else if (!string.IsNullOrWhiteSpace(colsJson))
                {
                    colsJson = colsJson.Trim();
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
                                throw new InvalidOperationException("RowKey must be supplied for each row.");

                            foreach (var prop in item.Properties())
                            {
                                if (string.Equals(prop.Name, "RowKey", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(prop.Name, "key", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                string cName = prop.Name;
                                if (!cName.Contains(":")) cName = "d:" + cName;
                                string val = prop.Value?.ToString().Replace("'", "\\'") ?? "";

                                sbStatements.AppendLine($"put '{table}', '{itemKey}', '{cName}', '{val}'");
                                stmtCount++;
                            }
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(rowKey))
                            throw new ArgumentException("RowKey is required when ColumnsJson is a single object.");

                        var obj = JObject.Parse(colsJson);
                        foreach (var prop in obj.Properties())
                        {
                            string cName = prop.Name;
                            if (!cName.Contains(":")) cName = "d:" + cName;
                            string val = prop.Value?.ToString().Replace("'", "\\'") ?? "";

                            sbStatements.AppendLine($"put '{table}', '{rowKey}', '{cName}', '{val}'");
                            stmtCount++;
                        }
                    }
                }
                else
                {
                    throw new InvalidOperationException("Either InputDataTable or ColumnsJson must be provided.");
                }

                if (stmtCount == 0)
                {
                    StatementsExecuted.Set(context, 0);
                    IsSuccess.Set(context, true);
                    return;
                }

                var client = HBaseSshHelper.GetOrCreateSshClient(host, port, user, pass, keyPath, passphrase, timeout);
                GlobalSettings.MessageLogger.LogMessage("Executing {0} HBase put statements via SSH on {1}", stmtCount, host);

                var result = HBaseSshHelper.RunHBaseShellCommand(client, sbStatements.ToString(), binPath, kPrincipal, kKeytab, timeout);

                RawOutput.Set(context, result.Output ?? string.Empty);
                StatementsExecuted.Set(context, stmtCount);
                IsSuccess.Set(context, result.IsSuccess);

                if (!result.IsSuccess && !continueOnErrorInExecMode)
                {
                    string rawError = !string.IsNullOrWhiteSpace(result.Error) ? result.Error : result.Output;
                    string friendlyMessage = HBaseErrorMessageFormatter.FormatErrorMessage(rawError, DisplayName, table);
                    throw new BusinessException(friendlyMessage);
                }
            }
            catch (BusinessException)
            {
                if (!continueOnErrorInExecMode)
                {
                    throw;
                }
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
