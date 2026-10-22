using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe CICS Command")]
    [Description("Interacts with IBM CICS regions via Zowe CLI / CICS management (list/inquire/newcopy programs, transactions, tasks, resources)")]
    public class ZoweCicsCommandActivity : ZoweBaseActivity
    {
        [Category("CICS Operation")]
        [RequiredArgument]
        [DisplayName("CICS Region Name / CICSplex")]
        [Description("CICS region APPLID or CICSplex name (e.g. CICSRGN1)")]
        public InArgument<string> CicsRegion { get; set; }

        [Category("CICS Operation")]
        [RequiredArgument]
        [DisplayName("Action / Resource Type")]
        [Description("Action: ListPrograms, NewCopyProgram, PhaseInProgram, ListTransactions, ListTasks, or RawCicsArgs")]
        [DefaultValue("ListPrograms")]
        public InArgument<string> Action { get; set; }

        [Category("CICS Operation")]
        [DisplayName("Program / Resource Name")]
        [Description("Name of program or transaction, e.g. 'PROG01' or 'TRN1'")]
        public InArgument<string> ResourceName { get; set; }

        [Category("CICS Operation")]
        [DisplayName("Additional CICS Arguments")]
        [Description("Extra arguments passed to 'zowe cics ...' command")]
        public InArgument<string> ExtraArguments { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Raw Output")]
        public OutArgument<string> RawOutput { get; set; }

        [Category("Output")]
        [DisplayName("Output JSON")]
        public OutArgument<string> OutJson { get; set; }

        [Category("Output")]
        [DisplayName("Resources DataTable")]
        public OutArgument<DataTable> ResourcesDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string region = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                region = CicsRegion.Get(context);
                string action = Action.Get(context) ?? "ListPrograms";
                string resName = ResourceName.Get(context);
                string extra = ExtraArguments.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                string cmdArgs;
                if (action.Equals("NewCopyProgram", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(resName))
                        throw new ArgumentException("Program / Resource Name é obrigatório para NewCopyProgram.");

                    cmdArgs = $"cics refresh program \"{resName.Trim()}\" --region-name \"{region}\" --response-format-json";
                }
                else if (action.Equals("PhaseInProgram", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(resName))
                        throw new ArgumentException("Program / Resource Name é obrigatório para PhaseInProgram.");

                    cmdArgs = $"cics phasein program \"{resName.Trim()}\" --region-name \"{region}\" --response-format-json";
                }
                else if (action.Equals("ListTransactions", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs = $"cics list transactions --region-name \"{region}\" --response-format-json";
                }
                else if (action.Equals("ListTasks", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs = $"cics list tasks --region-name \"{region}\" --response-format-json";
                }
                else if (action.Equals("RawCicsArgs", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs = $"cics {extra} --response-format-json";
                }
                else
                {
                    // ListPrograms
                    cmdArgs = $"cics list programs --region-name \"{region}\" --response-format-json";
                }

                if (!string.IsNullOrWhiteSpace(extra) && !action.Equals("RawCicsArgs", StringComparison.OrdinalIgnoreCase))
                {
                    cmdArgs += $" {extra}";
                }

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                RawOutput.Set(context, execResult.StdOut);
                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    string json = ZoweOutputParser.ExtractJsonFromCliOutput(execResult.StdOut);
                    OutJson.Set(context, json);
                    DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(json, "CicsResources");
                    ResourcesDataTable.Set(context, dt);
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe CICS Command", region);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe CICS Command", region);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
