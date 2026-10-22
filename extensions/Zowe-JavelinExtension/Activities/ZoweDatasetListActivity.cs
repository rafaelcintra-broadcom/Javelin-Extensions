using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe Dataset List")]
    [Description("Lists data sets matching a pattern (e.g. 'HLQ.PROD.*') or lists members in a Partitioned Data Set (PDS)")]
    public class ZoweDatasetListActivity : ZoweBaseActivity
    {
        [Category("Query Target")]
        [RequiredArgument]
        [DisplayName("Pattern / Data Set Name")]
        [Description("Data Set pattern (e.g. 'TEST.DATA.*') or PDS name (e.g. 'TEST.COPYLIB')")]
        public InArgument<string> TargetPattern { get; set; }

        [Category("Query Target")]
        [DisplayName("List Members Only")]
        [DefaultValue(false)]
        [Description("If true, lists all members inside the specified PDS/PDSE")]
        public InArgument<bool> ListMembers { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Items DataTable")]
        [Description("DataTable contendo dsname, volume, dsorg, recfm, lrecl, blksz ou nomes de membros")]
        public OutArgument<DataTable> ItemsDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Item Count")]
        public OutArgument<int> ItemCount { get; set; }

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
            string target = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                target = TargetPattern.Get(context);
                bool membersOnly = ListMembers.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(target))
                    throw new ArgumentException("Pattern / Data Set Name não pode ser vazio.");

                string cmdArgs;
                if (membersOnly)
                {
                    cmdArgs = $"zos-files list all-members \"{target.Trim()}\" --response-format-json";
                }
                else
                {
                    cmdArgs = $"zos-files list data-set \"{target.Trim()}\" --response-format-json";
                }

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                RawJson.Set(context, execResult.StdOut);
                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    string json = ZoweOutputParser.ExtractJsonFromCliOutput(execResult.StdOut);
                    DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(json, membersOnly ? "MemberList" : "DatasetList");
                    ItemsDataTable.Set(context, dt);
                    ItemCount.Set(context, dt.Rows.Count);
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe Dataset List", target);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe Dataset List", target);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
