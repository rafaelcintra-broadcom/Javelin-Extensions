using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Console Command")]
    [Description("Issues an MVS System Console command (e.g. 'D A,L', 'D IPLINFO', 'D TCPIP', '$DA') via z/OSMF / EMCS console")]
    public class ZoweConsoleCommandActivity : ZoweBaseActivity
    {
        [Category("Console Command")]
        [RequiredArgument]
        [DisplayName("MVS Console Command")]
        [Description("MVS console command, e.g. 'D A,L', 'D IPLINFO', '$DA', 'F CICS1,CEMT I TASK'")]
        public InArgument<string> Command { get; set; }

        [Category("Console Command")]
        [DisplayName("Console Name")]
        [Description("Optional EMCS console name. Defaults to generated console session")]
        public InArgument<string> ConsoleName { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Console Response")]
        public OutArgument<string> ResponseText { get; set; }

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
            string mvsCmd = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                mvsCmd = Command.Get(context);
                string consName = ConsoleName.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(mvsCmd))
                    throw new ArgumentException("MVS Console Command não pode ser vazio.");

                string cmdArgs = $"zos-console issue command \"{mvsCmd.Trim()}\"";
                if (!string.IsNullOrWhiteSpace(consName))
                    cmdArgs += $" --console-name \"{consName.Trim()}\"";

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    ResponseText.Set(context, execResult.StdOut);
                    DataTable dt = ZoweOutputParser.ParseTextLinesToDataTable(execResult.StdOut, "ConsoleResponseLines");
                    ResponseDataTable.Set(context, dt);
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Console Command", mvsCmd);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Console Command", mvsCmd);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
