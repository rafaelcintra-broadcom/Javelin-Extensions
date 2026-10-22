using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;
using Newtonsoft.Json;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase SSH Shell Command")]
    [Description("Executes arbitrary HBase shell commands (e.g. get, scan, count, status, describe) over SSH and parses output")]
    public class HBaseSshShellActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("SSH Host")]
        [Description("HBase Gateway / Edge Node / Master host IP or DNS")]
        public InArgument<string> Host { get; set; }

        [DisplayName("SSH Port")]
        [DefaultValue(22)]
        public InArgument<int> Port { get; set; }

        [RequiredArgument]
        [DisplayName("SSH Username")]
        [Description("SSH Login user (e.g. hbase or service account)")]
        public InArgument<string> Username { get; set; }

        [DisplayName("SSH Password")]
        [Description("SSH Password (leave blank if using Private Key)")]
        [JavelinPassword]
        public InArgument<string> Password { get; set; }

        [DisplayName("Private Key Path")]
        [Description("Path to private key file (e.g. C:\\keys\\id_rsa or /home/user/.ssh/id_rsa)")]
        public InArgument<string> PrivateKeyPath { get; set; }

        [DisplayName("Private Key Passphrase")]
        [JavelinPassword]
        public InArgument<string> Passphrase { get; set; }

        [RequiredArgument]
        [DisplayName("HBase Shell Command")]
        [Description("HBase shell command, e.g. \"get 'customers', '1001'\" or \"scan 'customers', {LIMIT => 10}\"")]
        public InArgument<string> ShellCommand { get; set; }

        [DisplayName("HBase Binary Path")]
        [Description("Path to hbase script on remote system (default: 'hbase')")]
        [DefaultValue("hbase")]
        public InArgument<string> HBaseBinPath { get; set; }

        [DisplayName("Kerberos Principal")]
        [Description("Optional Kerberos principal for kinit before command, e.g. hbase/cluster@REALM")]
        public InArgument<string> KinitPrincipal { get; set; }

        [DisplayName("Kerberos Keytab Path")]
        [Description("Optional path to keytab file on remote server, e.g. /etc/security/keytabs/hbase.keytab")]
        public InArgument<string> KinitKeytabPath { get; set; }

        [DisplayName("Timeout (Seconds)")]
        [DefaultValue(120)]
        public InArgument<int> TimeoutSeconds { get; set; }

        // --- Outputs ---

        [DisplayName("Raw Shell Output")]
        public OutArgument<string> RawOutput { get; set; }

        [DisplayName("Output JSON (Parsed)")]
        [Description("Decoded JSON list of key-value rows parsed from hbase shell stdout")]
        public OutArgument<string> OutJson { get; set; }

        [DisplayName("Output DataTable")]
        [Description("ADO.NET DataTable containing parsed RowKey and columns for Javelin loops")]
        public OutArgument<DataTable> OutDataTable { get; set; }

        [DisplayName("Row Count")]
        public OutArgument<int> RowCount { get; set; }

        [DisplayName("Exit Code")]
        public OutArgument<int> ExitCode { get; set; }

        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [DisplayName("Error Output")]
        public OutArgument<string> ErrorOutput { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string host = Host.Get(context);
                int port = Port.Get(context);
                string user = Username.Get(context);
                string pass = Password.Get(context);
                string keyPath = PrivateKeyPath.Get(context);
                string passphrase = Passphrase.Get(context);
                string command = ShellCommand.Get(context);
                string binPath = HBaseBinPath.Get(context);
                string kPrincipal = KinitPrincipal.Get(context);
                string kKeytab = KinitKeytabPath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(host))
                    throw new ArgumentException("SSH Host cannot be empty.");
                if (string.IsNullOrWhiteSpace(user))
                    throw new ArgumentException("SSH Username cannot be empty.");
                if (string.IsNullOrWhiteSpace(command))
                    throw new ArgumentException("HBase Shell Command cannot be empty.");

                if (port <= 0) port = 22;
                if (timeout <= 0) timeout = 120;
                if (string.IsNullOrWhiteSpace(binPath)) binPath = "hbase";

                var client = HBaseSshHelper.GetOrCreateSshClient(host, port, user, pass, keyPath, passphrase, timeout);

                GlobalSettings.MessageLogger.LogMessage("Executing remote HBase Shell via SSH on {0}: {1}", host, command);

                var execResult = HBaseSshHelper.RunHBaseShellCommand(client, command, binPath, kPrincipal, kKeytab, timeout);

                RawOutput.Set(context, execResult.Output ?? string.Empty);
                ErrorOutput.Set(context, execResult.Error ?? string.Empty);
                ExitCode.Set(context, execResult.ExitCode);
                IsSuccess.Set(context, execResult.IsSuccess);

                int detectedRows;
                var parsedRows = HBaseShellParser.ParseShellOutputToRows(execResult.Output, out detectedRows);
                RowCount.Set(context, detectedRows);

                string jsonStr = JsonConvert.SerializeObject(parsedRows, Formatting.Indented);
                OutJson.Set(context, jsonStr);

                var dt = HBaseShellParser.ToDataTable(parsedRows);
                OutDataTable.Set(context, dt);

                GlobalSettings.MessageLogger.LogMessage("HBase SSH Shell finished. ExitCode: {0}, Parsed Rows: {1}", execResult.ExitCode, detectedRows);

                if (!execResult.IsSuccess && !continueOnErrorInExecMode)
                {
                    string rawError = !string.IsNullOrWhiteSpace(execResult.Error) ? execResult.Error : execResult.Output;
                    string friendlyMessage = HBaseErrorMessageFormatter.FormatErrorMessage(rawError, DisplayName);
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
                string friendlyMessage = HBaseErrorMessageFormatter.FormatErrorMessage(ex.Message, DisplayName);
                var bex = new BusinessException(friendlyMessage, ex);
                if (!continueOnErrorInExecMode)
                {
                    throw bex;
                }
            }
        }
    }
}
