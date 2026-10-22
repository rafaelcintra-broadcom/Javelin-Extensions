using System;
using System.Activities;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase Table Admin")]
    [Description("Manages HBase table schema: Create Table (with column families), Check Exists, or Drop Table via REST")]
    public class HBaseTableAdminActivity : JavelinNativeActivity
    {
        public enum HBaseAdminAction
        {
            CheckExists,
            CreateTable,
            DropTable,
            GetSchema
        }

        [RequiredArgument]
        [DisplayName("HBase REST URL")]
        [Description("Base URL of HBase REST daemon, e.g. http://hbase-host:8080")]
        public InArgument<string> RestUrl { get; set; }

        [RequiredArgument]
        [DisplayName("Table Name")]
        [Description("Name of the HBase table (can include namespace: ns:table)")]
        public InArgument<string> TableName { get; set; }

        [DisplayName("Action")]
        [Description("Administrative action to execute")]
        public InArgument<HBaseAdminAction> Action { get; set; }

        [DisplayName("Column Families (CSV)")]
        [Description("Comma-separated column families when creating a table (e.g. 'cf1,cf2,data')")]
        public InArgument<string> ColumnFamilies { get; set; }

        [DisplayName("Username")]
        public InArgument<string> Username { get; set; }

        [DisplayName("Password")]
        [JavelinPassword]
        public InArgument<string> Password { get; set; }

        [DisplayName("Bearer Token")]
        [JavelinPassword]
        public InArgument<string> BearerToken { get; set; }

        [DisplayName("Timeout (Seconds)")]
        [DefaultValue(60)]
        public InArgument<int> TimeoutSeconds { get; set; }

        [DisplayName("Ignore SSL Certificate Errors")]
        [DefaultValue(true)]
        public InArgument<bool> IgnoreSslErrors { get; set; }

        // --- Outputs ---

        [DisplayName("Table Exists")]
        public OutArgument<bool> TableExists { get; set; }

        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [DisplayName("Schema / Result JSON")]
        public OutArgument<string> ResultJson { get; set; }

        [DisplayName("HTTP Status Code")]
        public OutArgument<int> StatusCode { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string table = null;
            try
            {
                base.Execute(context);

                string restUrl = RestUrl.Get(context);
                table = TableName.Get(context);
                var action = Action.Get(context);
                string cfString = ColumnFamilies.Get(context);
                string user = Username.Get(context);
                string pass = Password.Get(context);
                string token = BearerToken.Get(context);
                int timeout = TimeoutSeconds.Get(context);
                bool ignoreSsl = IgnoreSslErrors.Get(context);

                if (string.IsNullOrWhiteSpace(restUrl))
                    throw new ArgumentException("HBase REST URL cannot be empty.");
                if (string.IsNullOrWhiteSpace(table))
                    throw new ArgumentException("Table Name cannot be empty.");

                var client = HBaseClientHelper.GetOrCreateClient(restUrl, user, pass, token, timeout, ignoreSsl);
                string escapedTable = Uri.EscapeDataString(table);

                GlobalSettings.MessageLogger.LogMessage("Executing HBase Table Admin: {0} on table {1}", action, table);

                switch (action)
                {
                    case HBaseAdminAction.CheckExists:
                    {
                        var resp = client.GetAsync($"{escapedTable}/schema").GetAwaiter().GetResult();
                        StatusCode.Set(context, (int)resp.StatusCode);
                        bool exists = resp.IsSuccessStatusCode;
                        TableExists.Set(context, exists);
                        IsSuccess.Set(context, true);
                        if (exists)
                        {
                            string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            ResultJson.Set(context, json);
                        }
                        else
                        {
                            ResultJson.Set(context, "{}");
                        }
                        break;
                    }

                    case HBaseAdminAction.GetSchema:
                    {
                        var resp = client.GetAsync($"{escapedTable}/schema").GetAwaiter().GetResult();
                        StatusCode.Set(context, (int)resp.StatusCode);
                        if (!resp.IsSuccessStatusCode)
                        {
                            TableExists.Set(context, false);
                            IsSuccess.Set(context, false);
                            ResultJson.Set(context, "{}");
                        }
                        else
                        {
                            string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            TableExists.Set(context, true);
                            IsSuccess.Set(context, true);
                            ResultJson.Set(context, json);
                        }
                        break;
                    }

                    case HBaseAdminAction.CreateTable:
                    {
                        if (string.IsNullOrWhiteSpace(cfString))
                            cfString = "cf";

                        var families = cfString.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        var sbColumns = new StringBuilder();
                        for (int i = 0; i < families.Length; i++)
                        {
                            if (i > 0) sbColumns.Append(",");
                            sbColumns.Append($"{{\"name\":\"{families[i].Trim()}\"}}");
                        }

                        string schemaJson = $"{{\"name\":\"{table}\",\"ColumnSchema\":[{sbColumns}]}}";
                        var content = new StringContent(schemaJson, Encoding.UTF8, "application/json");

                        var resp = client.PutAsync($"{escapedTable}/schema", content).GetAwaiter().GetResult();
                        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.Created)
                        {
                            // Try POST as alternative
                            resp = client.PostAsync($"{escapedTable}/schema", content).GetAwaiter().GetResult();
                        }

                        StatusCode.Set(context, (int)resp.StatusCode);
                        if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.Created)
                        {
                            IsSuccess.Set(context, true);
                            TableExists.Set(context, true);
                            ResultJson.Set(context, schemaJson);
                            GlobalSettings.MessageLogger.LogMessage("Table {0} created successfully.", table);
                        }
                        else
                        {
                            string err = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            throw new HttpRequestException($"Failed to create table {table}. Status {resp.StatusCode}: {err}");
                        }
                        break;
                    }

                    case HBaseAdminAction.DropTable:
                    {
                        var resp = client.DeleteAsync($"{escapedTable}/schema").GetAwaiter().GetResult();
                        StatusCode.Set(context, (int)resp.StatusCode);
                        if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NotFound)
                        {
                            IsSuccess.Set(context, true);
                            TableExists.Set(context, false);
                            ResultJson.Set(context, "{\"dropped\": true}");
                            GlobalSettings.MessageLogger.LogMessage("Table {0} dropped.", table);
                        }
                        else
                        {
                            string err = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            throw new HttpRequestException($"Failed to drop table {table}. Status {resp.StatusCode}: {err}");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                string friendlyMessage = HBaseErrorMessageFormatter.FormatErrorMessage(ex.Message, DisplayName, table);
                var bex = new BusinessException(friendlyMessage, ex);
                if (!continueOnErrorInExecMode)
                {
                    throw bex;
                }
            }
        }
    }
}
