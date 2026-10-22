using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using System.Text;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;
using Newtonsoft.Json;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase SSH Scan")]
    [Description("Executes an HBase scan query over SSH with start/stop keys, filters, and batch limits, returning a DataTable")]
    public class HBaseSshScanActivity : JavelinNativeActivity
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

        [DisplayName("Start Row")]
        [Description("Start row key (inclusive)")]
        public InArgument<string> StartRow { get; set; }

        [DisplayName("Stop Row")]
        [Description("Stop row key (exclusive)")]
        public InArgument<string> StopRow { get; set; }

        [DisplayName("Columns (CSV)")]
        [Description("Optional comma-separated columns or column families, e.g. 'cf:col1,cf:col2'")]
        public InArgument<string> Columns { get; set; }

        [DisplayName("Limit")]
        [Description("Max number of rows to return")]
        [DefaultValue(100)]
        public InArgument<int?> Limit { get; set; }

        [DisplayName("Custom Filter String")]
        [Description("HBase shell FILTER string, e.g. \"PrefixFilter('cust_')\"")]
        public InArgument<string> FilterString { get; set; }

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

        [DisplayName("Output DataTable")]
        public OutArgument<DataTable> OutDataTable { get; set; }

        [DisplayName("Output JSON")]
        public OutArgument<string> OutJson { get; set; }

        [DisplayName("Row Count")]
        public OutArgument<int> RowCount { get; set; }

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
                string startRow = StartRow.Get(context);
                string stopRow = StopRow.Get(context);
                string columns = Columns.Get(context);
                int? limit = Limit.Get(context);
                string filterStr = FilterString.Get(context);
                string binPath = HBaseBinPath.Get(context);
                string kPrincipal = KinitPrincipal.Get(context);
                string kKeytab = KinitKeytabPath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("SSH Host cannot be empty.");
                if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("SSH Username cannot be empty.");
                if (string.IsNullOrWhiteSpace(table)) throw new ArgumentException("Table Name cannot be empty.");

                var sbScanOpts = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(startRow))
                    sbScanOpts.Append($"STARTROW => '{startRow}', ");
                if (!string.IsNullOrWhiteSpace(stopRow))
                    sbScanOpts.Append($"STOPROW => '{stopRow}', ");
                if (limit.HasValue && limit.Value > 0)
                    sbScanOpts.Append($"LIMIT => {limit.Value}, ");

                if (!string.IsNullOrWhiteSpace(columns))
                {
                    var cols = columns.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    var sbCols = new StringBuilder();
                    for (int i = 0; i < cols.Length; i++)
                    {
                        if (i > 0) sbCols.Append(", ");
                        sbCols.Append($"'{cols[i].Trim()}'");
                    }
                    sbScanOpts.Append($"COLUMNS => [{sbCols}], ");
                }

                if (!string.IsNullOrWhiteSpace(filterStr))
                {
                    sbScanOpts.Append($"FILTER => \"{filterStr}\", ");
                }

                string scanCmd;
                string options = sbScanOpts.ToString().TrimEnd(' ', ',');
                if (string.IsNullOrWhiteSpace(options))
                {
                    scanCmd = $"scan '{table}'";
                }
                else
                {
                    scanCmd = $"scan '{table}', {{{options}}}";
                }

                var client = HBaseSshHelper.GetOrCreateSshClient(host, port, user, pass, keyPath, passphrase, timeout);
                GlobalSettings.MessageLogger.LogMessage("Executing HBase scan via SSH: {0}", scanCmd);

                var result = HBaseSshHelper.RunHBaseShellCommand(client, scanCmd, binPath, kPrincipal, kKeytab, timeout);

                RawOutput.Set(context, result.Output ?? string.Empty);
                IsSuccess.Set(context, result.IsSuccess);

                int detectedRows;
                var parsedRows = HBaseShellParser.ParseShellOutputToRows(result.Output, out detectedRows);
                RowCount.Set(context, detectedRows);

                string jsonStr = JsonConvert.SerializeObject(parsedRows, Formatting.Indented);
                OutJson.Set(context, jsonStr);

                var dt = HBaseShellParser.ToDataTable(parsedRows, table);
                OutDataTable.Set(context, dt);

                GlobalSettings.MessageLogger.LogMessage("HBase Scan via SSH finished. Parsed {0} rows into JSON/DataTable (HBase detected: {1})", parsedRows.Count, detectedRows);
                if (parsedRows.Count == 0 && !string.IsNullOrWhiteSpace(result.Output))
                {
                    GlobalSettings.MessageLogger.LogMessage("HBase Scan Shell Raw Output:\n{0}", result.Output);
                }

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
