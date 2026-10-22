using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe USS Command")]
    [Description("Executes UNIX System Services (USS / z/OS UNIX) shell commands or scripts on the mainframe")]
    public class ZoweUssCommandActivity : ZoweBaseActivity
    {
        [Category("USS Command")]
        [RequiredArgument]
        [DisplayName("Shell Command")]
        [Description("USS shell command or script, e.g. 'ls -la /u/myuser', 'df -k', or '/u/myuser/bin/run_mask.sh'")]
        public InArgument<string> Command { get; set; }

        [Category("USS Command")]
        [DisplayName("Working Directory (CWD)")]
        [Description("Current working directory in USS (e.g. /u/myuser/tdm)")]
        public InArgument<string> WorkingDirectory { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Command Output (Stdout)")]
        public OutArgument<string> StdOut { get; set; }

        [Category("Output")]
        [DisplayName("Lines DataTable")]
        public OutArgument<DataTable> LinesDataTable { get; set; }

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
            string ussCmd = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                ussCmd = Command.Get(context);
                string cwd = WorkingDirectory.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(ussCmd))
                    throw new ArgumentException("Shell Command não pode ser vazio.");

                string cmdArgs = $"zos-uss issue command \"{ussCmd.Trim()}\"";
                if (!string.IsNullOrWhiteSpace(cwd))
                    cmdArgs += $" --cwd \"{cwd.Trim()}\"";

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                StdOut.Set(context, execResult.StdOut);
                ExitCode.Set(context, execResult.ExitCode);
                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    DataTable dt = ZoweOutputParser.ParseTextLinesToDataTable(execResult.StdOut, "UssOutputLines");
                    LinesDataTable.Set(context, dt);
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe USS Command", ussCmd);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe USS Command", ussCmd);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
