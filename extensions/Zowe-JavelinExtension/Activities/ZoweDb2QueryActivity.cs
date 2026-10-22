using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using GridTools.Javelin.Common;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Activities
{
    [DisplayName("Zowe DB2 Query")]
    [Description("Executes SQL queries against IBM DB2 for z/OS using Zowe CLI / DB2 plugin or DSNTEP2/DSNTIAUL batch wrapper")]
    public class ZoweDb2QueryActivity : ZoweBaseActivity
    {
        [Category("DB2 Query")]
        [RequiredArgument]
        [DisplayName("SQL Query")]
        [Description("SQL statement to execute on DB2 for z/OS (e.g. 'SELECT * FROM SYSIBM.SYSTABLES WHERE CREATOR = \\'TEST\\';')")]
        public InArgument<string> SqlQuery { get; set; }

        [Category("DB2 Query")]
        [DisplayName("DB2 Location / SSID")]
        [Description("DB2 Subsystem ID or Location name (e.g. DB2A, DSN1)")]
        public InArgument<string> Db2Location { get; set; }

        // --- Outputs ---

        [Category("Output")]
        [DisplayName("Raw Output")]
        public OutArgument<string> RawOutput { get; set; }

        [Category("Output")]
        [DisplayName("Output JSON")]
        public OutArgument<string> OutJson { get; set; }

        [Category("Output")]
        [DisplayName("Results DataTable")]
        [Description("DataTable containing query result rows for Javelin loop and data validation")]
        public OutArgument<DataTable> ResultsDataTable { get; set; }

        [Category("Output")]
        [DisplayName("Row Count")]
        public OutArgument<int> RowCount { get; set; }

        [Category("Output")]
        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [Category("Output")]
        [DisplayName("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string sql = null;
            try
            {
                base.Execute(context);

                var options = BuildProfileOptions(context);
                sql = SqlQuery.Get(context);
                string ssid = Db2Location.Get(context);
                string customPath = CustomZowePath.Get(context);
                int timeout = TimeoutSeconds.Get(context);

                if (string.IsNullOrWhiteSpace(sql))
                    throw new ArgumentException("SQL Query não pode ser vazio.");

                string cmdArgs = $"db2 execute sql \"{sql.Trim()}\" --response-format-json";
                if (!string.IsNullOrWhiteSpace(ssid))
                {
                    cmdArgs += $" --db2-location \"{ssid.Trim()}\"";
                }

                var execResult = ZoweCliHelper.ExecuteCommand(cmdArgs, options, customPath, timeout);

                RawOutput.Set(context, execResult.StdOut);
                IsSuccess.Set(context, execResult.Success);

                if (execResult.Success)
                {
                    string json = ZoweOutputParser.ExtractJsonFromCliOutput(execResult.StdOut);
                    OutJson.Set(context, json);
                    DataTable dt = ZoweOutputParser.ParseJsonArrayToDataTable(json, "Db2Results");
                    ResultsDataTable.Set(context, dt);
                    RowCount.Set(context, dt.Rows.Count);
                    ErrorMessage.Set(context, string.Empty);
                }
                else
                {
                    string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(execResult.FormattedError, "Zowe DB2 Query", sql);
                    ErrorMessage.Set(context, formatted);
                    throw new InvalidOperationException(formatted);
                }
            }
            catch (Exception ex)
            {
                string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(ex.Message, "Zowe DB2 Query", sql);
                ErrorMessage.Set(context, formatted);
                IsSuccess.Set(context, false);
                throw;
            }
        }
    }
}
