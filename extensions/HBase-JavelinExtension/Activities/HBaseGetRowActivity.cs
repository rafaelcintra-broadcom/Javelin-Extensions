using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using System.Net;
using System.Net.Http;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;
using Newtonsoft.Json;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase Get Row")]
    [Description("Fetches a row by its RowKey or specific column(s) from an Apache HBase table via REST")]
    public class HBaseGetRowActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("HBase REST URL")]
        [Description("Base URL of HBase REST / Stargate daemon, e.g. http://hbase-host:8080")]
        public InArgument<string> RestUrl { get; set; }

        [RequiredArgument]
        [DisplayName("Table Name")]
        [Description("Name of the HBase table (can include namespace: ns:table)")]
        public InArgument<string> TableName { get; set; }

        [RequiredArgument]
        [DisplayName("Row Key")]
        [Description("Row Key to retrieve")]
        public InArgument<string> RowKey { get; set; }

        [DisplayName("Columns")]
        [Description("Optional comma-separated columns or column families, e.g. cf1:col1,cf1:col2 or cf1")]
        public InArgument<string> Columns { get; set; }

        [DisplayName("Username")]
        [Description("Optional Basic Auth username")]
        public InArgument<string> Username { get; set; }

        [DisplayName("Password")]
        [Description("Optional Basic Auth password")]
        [JavelinPassword]
        public InArgument<string> Password { get; set; }

        [DisplayName("Bearer Token")]
        [Description("Optional JWT / Knox OAuth Bearer token")]
        [JavelinPassword]
        public InArgument<string> BearerToken { get; set; }

        [DisplayName("Timeout (Seconds)")]
        [DefaultValue(60)]
        public InArgument<int> TimeoutSeconds { get; set; }

        [DisplayName("Ignore SSL Certificate Errors")]
        [DefaultValue(true)]
        public InArgument<bool> IgnoreSslErrors { get; set; }

        // --- Outputs ---

        [DisplayName("Found")]
        [Description("True if the row exists and was returned; False if 404 Not Found")]
        public OutArgument<bool> Found { get; set; }

        [DisplayName("Output JSON (Decoded)")]
        [Description("Clean decoded JSON array of retrieved row and cells")]
        public OutArgument<string> OutJson { get; set; }

        [DisplayName("Output DataTable")]
        [Description("Populated DataTable with RowKey and column qualifiers as fields")]
        public OutArgument<DataTable> OutDataTable { get; set; }

        [DisplayName("Raw REST JSON")]
        [Description("Original Base64-encoded JSON response directly from HBase REST")]
        public OutArgument<string> RawJson { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string table = null;
            try
            {
                base.Execute(context);

                string restUrl = RestUrl.Get(context);
                table = TableName.Get(context);
                string rowKey = RowKey.Get(context);
                string columns = Columns.Get(context);
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

                // Build request path: /{table}/{row}/{column}
                string escapedTable = Uri.EscapeDataString(table);
                string escapedRow = Uri.EscapeDataString(rowKey);
                string requestPath = $"{escapedTable}/{escapedRow}";

                if (!string.IsNullOrWhiteSpace(columns))
                {
                    requestPath += "/" + Uri.EscapeDataString(columns.Trim());
                }

                GlobalSettings.MessageLogger.LogMessage("Executing HBase Get Row: {0}/{1}", table, rowKey);

                var response = client.GetAsync(requestPath).GetAwaiter().GetResult();

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Found.Set(context, false);
                    OutJson.Set(context, "[]");
                    RawJson.Set(context, string.Empty);
                    OutDataTable.Set(context, HBaseDataTableConverter.ToDataTable(null, table));
                    GlobalSettings.MessageLogger.LogMessage("HBase Get Row: Row '{0}' not found (404)", rowKey);
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    string errContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new HttpRequestException($"HBase REST request failed with status {response.StatusCode}: {errContent}");
                }

                string rawContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                RawJson.Set(context, rawContent);

                var decodedRows = HBaseDataTableConverter.DecodeHBaseRestJson(rawContent);
                bool isFound = decodedRows.Count > 0;
                Found.Set(context, isFound);

                string decodedJson = JsonConvert.SerializeObject(decodedRows, Formatting.Indented);
                OutJson.Set(context, decodedJson);

                var dt = HBaseDataTableConverter.ToDataTable(decodedRows, table);
                OutDataTable.Set(context, dt);

                GlobalSettings.MessageLogger.LogMessage("HBase Get Row completed successfully for row: {0}", rowKey);
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
