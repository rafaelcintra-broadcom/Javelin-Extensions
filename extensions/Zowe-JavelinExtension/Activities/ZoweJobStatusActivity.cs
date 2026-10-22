using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Job Status")]
    [Description("Queries JES job execution status, return code (CC), or lists active/completed jobs by owner, prefix or Job ID")]
    public class ZoweJobStatusActivity : ZoweBaseActivity
    {
        [Category("Query Filters")]
        [DisplayName("Job ID")]
        [Description("Specific Job ID to query, e.g. JOB01234. Leave blank to list multiple jobs")]
        public InArgument<string> JobId { get; set; }

        [Category("Query Filters")]
        [DisplayName("Job Prefix / Name Pattern")]
        [Description("Prefix filter for job names, e.g. TDM* or BAT*")]
        public InArgument<string> Prefix { get; set; }

        [Category("Query Filters")]
        [DisplayName("Owner")]
        [Description("Job owner user ID filter, e.g. IBMUSER or *")]
        public InArgument<string> Owner { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Status")]
        [Description("Status of single job: ACTIVE, OUTPUT, INPUT, HELD")]
        public OutArgument<string> Status { get; set; }

        [Category("Output")]
        [DisplayName("Return Code")]
        [Description("Return Code / Completion code (e.g. 'CC 0000', 'ABEND S0C4')")]
        public OutArgument<string> ReturnCode { get; set; }

        [Category("Output")]
        [DisplayName("Is Completed")]
        [Description("True if job is in OUTPUT status (finished)")]
        public OutArgument<bool> IsCompleted { get; set; }

        [Category("Output")]
        [DisplayName("Jobs DataTable")]
        [Description("DataTable containing all matched jobs (JobId, JobName, Status, ReturnCode, Owner)")]
        public OutArgument<DataTable> JobsDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Raw JSON")]
        public OutArgument<string> RawJson { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string targetJob = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                targetJob = JobId.Get(context);
                string prefix = Prefix.Get(context);
                string owner = Owner.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                string cmdArgs;
                if (!string.IsNullOrWhiteSpace(targetJob))
                {
                    cmdArgs = $"zos-jobs view job-status-by-jobid {targetJob.Trim()} --response-format-json";
                }
                else
                {
                    cmdArgs = "zos-jobs list jobs --response-format-json";
                    if (!string.IsNullOrWhiteSpace(prefix))
                        cmdArgs += $" --prefix \"{prefix.Trim()}\"";
                    if (!string.IsNullOrWhiteSpace(owner))
                        cmdArgs += $" --owner \"{owner.Trim()}\"";
                }

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                RawJson.Set(context, execResult.StdOut);
                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    string json = ZoweOutputParser.ExtractJsonFromCliOutput(execResult.StdOut);
                    DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(json, "JobsList");
                    JobsDataTable.Set(context, dt);

                    if (!string.IsNullOrWhiteSpace(targetJob) || dt.Rows.Count == 1)
                    {
                        var row = dt.Rows[0];
                        string st = row.Table.Columns.Contains("status") ? row["status"].ToString() : "";
                        string rc = row.Table.Columns.Contains("retcode") ? row["retcode"].ToString() : "";

                        Status.Set(context, st);
                        ReturnCode.Set(context, rc);
                        IsCompleted.Set(context, string.Equals(st, "OUTPUT", StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        Status.Set(context, dt.Rows.Count > 0 ? "FOUND" : "NOT_FOUND");
                        ReturnCode.Set(context, "");
                        IsCompleted.Set(context, false);
                    }

                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Job Status", targetJob);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Job Status", targetJob);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
