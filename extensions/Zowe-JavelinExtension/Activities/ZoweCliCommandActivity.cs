using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Raw CLI Command")]
    [Description("Executes arbitrary Zowe CLI commands (e.g. 'zowe zos-files ...', 'zowe cics ...', 'zowe db2 ...') against the mainframe and returns Output, JSON, and DataTable")]
    public class ZoweCliCommandActivity : ZoweBaseActivity
    {
        [Category("Command")]
        [RequiredArgument]
        [DisplayName("Arguments / Subcommand")]
        [Description("Subcommand arguments passed to zowe, e.g. 'zos-files list ds HLQ.* --response-format-json' or 'zos-jobs list jobs'")]
        public InArgument<string> Arguments { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Raw Output")]
        public OutArgument<string> RawOutput { get; set; }

        [Category("Output")]
        [DisplayName("Output JSON / Text")]
        public OutArgument<string> OutJson { get; set; }

        [Category("Output")]
        [DisplayName("Output DataTable")]
        public OutArgument<DataTable> OutDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Exit Code")]
        public OutArgument<int> ExitCode { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string args = Arguments.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(args))
                    throw new ArgumentException("Arguments / Subcommand não pode ser vazio.");

                var options = BuildProfileOptions(context);
                var execResult = ZoweCliHelper.ExecuteCommand(args, options, customPath, timeout);

                RawOutput.Set(context, execResult.StdOut);
                ExitCode.Set(context, execResult.ExitCode);
                IsSuccess.Set(context, execResult.Success);

                string extractedJson = ZoweOutputParser.ExtractJsonFromCliOutput(execResult.StdOut);
                OutJson.Set(context, extractedJson);

                DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(extractedJson, "ZoweCliResult");
                OutDataTable.Set(context, dt);

                if (!execResult.Success)
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Raw CLI Command", args);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
                else
                {
                    ErrorMessage.Set(context, string.Empty);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Raw CLI Command");
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
