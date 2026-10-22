using System;
using System.Activities;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase Delete")]
    [Description("Deletes an entire row, a specific column family, or a specific cell qualifier in an Apache HBase table")]
    public class HBaseDeleteActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("HBase REST URL")]
        [Description("Base URL of HBase REST daemon, e.g. http://hbase-host:8080")]
        public InArgument<string> RestUrl { get; set; }

        [RequiredArgument]
        [DisplayName("Table Name")]
        [Description("Name of the HBase table")]
        public InArgument<string> TableName { get; set; }

        [RequiredArgument]
        [DisplayName("Row Key")]
        [Description("Row Key to delete")]
        public InArgument<string> RowKey { get; set; }

        [DisplayName("Column / Family")]
        [Description("Optional: Specific column (cf:col) or family (cf) to delete. Leave empty to delete the entire row.")]
        public InArgument<string> Column { get; set; }

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

        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

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
                string rowKey = RowKey.Get(context);
                string col = Column.Get(context);
                string user = Username.Get(context);
                string pass = Password.Get(context);
                string token = BearerToken.Get(context);
                int timeout = TimeoutSeconds.Get(context);
                bool ignoreSsl = IgnoreSslErrors.Get(context);

                if (string.IsNullOrWhiteSpace(restUrl))
                    throw new ArgumentException("HBase REST URL cannot be empty.");
                if (string.IsNullOrWhiteSpace(table))
                    throw new ArgumentException("Table Name cannot be empty.");
                if (string.IsNullOrWhiteSpace(rowKey))
                    throw new ArgumentException("Row Key cannot be empty.");

                var client = HBaseClientHelper.GetOrCreateClient(restUrl, user, pass, token, timeout, ignoreSsl);

                string escapedTable = Uri.EscapeDataString(table);
                string escapedRow = Uri.EscapeDataString(rowKey);
                string requestPath = $"{escapedTable}/{escapedRow}";

                if (!string.IsNullOrWhiteSpace(col))
                {
                    requestPath += "/" + Uri.EscapeDataString(col.Trim());
                }

                GlobalSettings.MessageLogger.LogMessage("Executing HBase Delete on {0}", requestPath);

                var response = client.DeleteAsync(requestPath).GetAwaiter().GetResult();
                StatusCode.Set(context, (int)response.StatusCode);

                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                {
                    IsSuccess.Set(context, true);
                    GlobalSettings.MessageLogger.LogMessage("HBase Delete finished with status {0}", response.StatusCode);
                }
                else
                {
                    string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new HttpRequestException($"HBase DELETE failed with status {response.StatusCode}: {err}");
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
