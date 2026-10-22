using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Submit Job")]
    [Description("Submits JCL from a local file, mainframe dataset/member, or inline text to JES, optionally waiting for completion and returning JobId and Return Code")]
    public class ZoweSubmitJobActivity : ZoweBaseActivity
    {
        [Category("JCL Source")]
        [DisplayName("Source Type")]
        [Description("Source of JCL: LocalFile, DatasetMember, or InlineText")]
        [DefaultValue("DatasetMember")]
        public InArgument<string> SourceType { get; set; }

        [Category("JCL Source")]
        [DisplayName("Data Set / Member or File Path")]
        [Description("z/OS dataset member (e.g. 'PROD.JCLLIB(MYJOB)') or local file path ('C:\\jcl\\myjob.jcl')")]
        public InArgument<string> JclLocation { get; set; }

        [Category("JCL Source")]
        [DisplayName("Inline JCL Text")]
        [Description("Raw JCL text content to submit directly to JES")]
        public InArgument<string> InlineJclText { get; set; }

        [Category("Execution Options")]
        [DisplayName("Wait For Completion")]
        [DefaultValue(true)]
        [Description("If true, waits for the job to complete and retrieves final return code (e.g. CC 0000)")]
        public InArgument<bool> WaitForCompletion { get; set; }

        [Category("Execution Options")]
        [DisplayName("Wait For Output (Spool)")]
        [DefaultValue(false)]
        [Description("If true, retrieves job output spool directly on completion")]
        public InArgument<bool> WaitForOutput { get; set; }

        [Category("Execution Options")]
        [DisplayName("View Spool DD Name")]
        [Description("Optional DD name to view spool when output is enabled (e.g. SYSOUT, SYSPRINT, JESMSGLG)")]
        public InArgument<string> ViewAllSpoolDd { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Job ID")]
        public OutArgument<string> JobId { get; set; }

        [Category("Output")]
        [DisplayName("Job Name")]
        public OutArgument<string> JobName { get; set; }

        [Category("Output")]
        [DisplayName("Job Status")]
        public OutArgument<string> JobStatus { get; set; }

        [Category("Output")]
        [DisplayName("Return Code")]
        [Description("Final return code, e.g. 'CC 0000', 'ABEND S0C4', or 'ACTIVE'")]
        public OutArgument<string> ReturnCode { get; set; }

        [Category("Output")]
        [DisplayName("Spool Output")]
        public OutArgument<string> SpoolOutput { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Raw JSON")]
        public OutArgument<string> RawJson { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string loc = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                string srcType = SourceType.Get(context) ?? "DatasetMember";
                loc = JclLocation.Get(context);
                string inlineJcl = InlineJclText.Get(context);
                bool waitComplete = WaitForCompletion.Get(context);
                bool waitOutput = WaitForOutput.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                string cmdArgs;
                string tempJclFile = null;

                try
                {
                    if (srcType.Equals("InlineText", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(inlineJcl))
                    {
                        tempJclFile = System.IO.Path.GetTempFileName() + ".jcl";
                        System.IO.File.WriteAllText(tempJclFile, inlineJcl ?? string.Empty);
                        cmdArgs = $"zos-jobs submit local-file \"{tempJclFile}\" --response-format-json";
                    }
                    else if (srcType.Equals("LocalFile", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(loc))
                            throw new ArgumentException("Data Set / Member or File Path não pode ser vazio para LocalFile.");

                        cmdArgs = $"zos-jobs submit local-file \"{loc}\" --response-format-json";
                    }
                    else
                    {
                        // DatasetMember
                        if (string.IsNullOrWhiteSpace(loc))
                            throw new ArgumentException("Data Set / Member or File Path não pode ser vazio.");

                        cmdArgs = $"zos-jobs submit data-set \"{loc}\" --response-format-json";
                    }

                    if (waitOutput)
                    {
                        cmdArgs += " --wait-for-output";
                    }
                    else if (waitComplete)
                    {
                        cmdArgs += " --wait-for-active --wait-for-complete";
                    }

                    var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                    RawJson.Set(context, execResult.StdOut);
                    IsSuccess.Set(context, execResult.Success);

                    if (execResult.Success)
                    {
                        string json = ZoweOutputParser.ExtractJsonFromCliOutput(execResult.StdOut);
                        try
                        {
                            var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                            string jId = token["jobid"]?.ToString() ?? token["data"]?["jobid"]?.ToString() ?? "";
                            string jName = token["jobname"]?.ToString() ?? token["data"]?["jobname"]?.ToString() ?? "";
                            string status = token["status"]?.ToString() ?? token["data"]?["status"]?.ToString() ?? "";
                            string retCode = token["retcode"]?.ToString() ?? token["data"]?["retcode"]?.ToString() ?? "";

                            JobId.Set(context, jId);
                            JobName.Set(context, jName);
                            JobStatus.Set(context, status);
                            ReturnCode.Set(context, retCode);
                            SpoolOutput.Set(context, execResult.StdOut);
                        }
                        catch
                        {
                            SpoolOutput.Set(context, execResult.StdOut);
                        }

                        ErrorMessage.Set(context, string.Empty);
                    }
                    else
                    {
                        string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Submit Job", loc);
                        ErrorMessage.Set(context, formatted);
                        throw new InvalidOperationException(formatted);
                    }
                }
                finally
                {
                    if (tempJclFile != null && System.IO.File.Exists(tempJclFile))
                    {
                        try { System.IO.File.Delete(tempJclFile); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Submit Job", loc);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
