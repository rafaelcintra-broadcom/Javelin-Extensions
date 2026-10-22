using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Download Spool")]
    [Description("Downloads JES spool output / sysout logs (JESMSGLG, JESJCL, JESYSMSG, SYSOUT, SYSPRINT) for a completed or active Job")]
    public class ZoweDownloadSpoolActivity : ZoweBaseActivity
    {
        [Category("Target Job")]
        [RequiredArgument]
        [DisplayName("Job ID")]
        [Description("Mainframe Job ID, e.g. JOB00123")]
        public InArgument<string> JobId { get; set; }

        [Category("Spool Target")]
        [DisplayName("Spool ID")]
        [Description("Optional specific Spool File ID (e.g. 1, 2, 102). Leave empty to download all spool")]
        public InArgument<int?> SpoolId { get; set; }

        [Category("Spool Target")]
        [DisplayName("Local File Path")]
        [Description("Optional local file path to save spool content directly (e.g. C:\\temp\\job_output.log)")]
        public InArgument<string> LocalFilePath { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Spool Text Content")]
        public OutArgument<string> SpoolText { get; set; }

        [Category("Output")]
        [DisplayName("Spool Lines DataTable")]
        [Description("DataTable with LineNumber and Content for easy Javelin looping / validation")]
        public OutArgument<DataTable> SpoolLinesDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string jId = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                jId = JobId.Get(context);
                int? spoolId = SpoolId.Get(context);
                string localFile = LocalFilePath.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(jId))
                    throw new ArgumentException("Job ID não pode ser vazio.");

                string cmdArgs;
                if (spoolId.HasValue && spoolId.Value > 0)
                {
                    cmdArgs = $"zos-jobs view spool-file-by-id {jId.Trim()} {spoolId.Value}";
                }
                else
                {
                    cmdArgs = $"zos-jobs view all-spool-content {jId.Trim()}";
                }

                if (!string.IsNullOrWhiteSpace(localFile))
                {
                    cmdArgs += $" --file \"{localFile.Trim()}\"";
                }

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    string spoolContent = execResult.StdOut;
                    if (!string.IsNullOrWhiteSpace(localFile) && System.IO.File.Exists(localFile))
                    {
                        spoolContent = System.IO.File.ReadAllText(localFile);
                    }

                    SpoolText.Set(context, spoolContent);
                    DataTable dt = ZoweOutputParser.ParseTextLinesToDataTable(spoolContent, "SpoolLines");
                    SpoolLinesDataTable.Set(context, dt);
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Download Spool", jId);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Download Spool", jId);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
