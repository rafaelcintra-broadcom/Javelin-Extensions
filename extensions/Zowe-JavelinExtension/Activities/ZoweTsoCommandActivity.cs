using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe TSO Command")]
    [Description("Issues interactive or batch TSO/E commands (e.g. LISTCAT, ALLOCATE, EXEC REXX, STATUS, PROFILE) on z/OS")]
    public class ZoweTsoCommandActivity : ZoweBaseActivity
    {
        [Category("TSO Command")]
        [RequiredArgument]
        [DisplayName("TSO Command")]
        [Description("TSO command string, e.g. 'LISTCAT ENT(\\'HLQ.TEST.DATA\\') ALL' or 'EXEC \\'MY.REXX(TEST)\\' '")]
        public InArgument<string> Command { get; set; }

        [Category("TSO Command")]
        [DisplayName("Account Number")]
        [Description("Optional TSO account number / logon procedure account")]
        public InArgument<string> AccountNumber { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("TSO Output Response")]
        public OutArgument<string> TsoResponse { get; set; }

        [Category("Output")]
        [DisplayName("Response Lines DataTable")]
        public OutArgument<DataTable> ResponseDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string tsoCmd = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                tsoCmd = Command.Get(context);
                string account = AccountNumber.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(tsoCmd))
                    throw new ArgumentException("TSO Command não pode ser vazio.");

                string cmdArgs = $"zos-tso issue command \"{tsoCmd.Trim()}\"";
                if (!string.IsNullOrWhiteSpace(account))
                    cmdArgs += $" --account \"{account.Trim()}\"";

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    TsoResponse.Set(context, execResult.StdOut);
                    DataTable dt = ZoweOutputParser.ParseTextLinesToDataTable(execResult.StdOut, "TsoResponseLines");
                    ResponseDataTable.Set(context, dt);
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe TSO Command", tsoCmd);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe TSO Command", tsoCmd);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
