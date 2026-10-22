using System;
using System.Activities;
using System.ComponentModel;
using System.Data;
using System.Net;
using System.Net.Http;
using System.Text;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;
using GridTools.Javelin.HBaseActivities.Models;
using Newtonsoft.Json;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase Scan")]
    [Description("Scans rows in an Apache HBase table with start/stop keys, filters, batch limits, and returns a DataTable")]
    public class HBaseScanActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("HBase REST URL")]
        [Description("Base URL of HBase REST daemon, e.g. http://hbase-host:8080")]
        public InArgument<string> RestUrl { get; set; }

        [RequiredArgument]
        [DisplayName("Table Name")]
        [Description("Name of the HBase table")]
        public InArgument<string> TableName { get; set; }

        [DisplayName("Start Row Key")]
        [Description("Optional start row key (inclusive)")]
        public InArgument<string> StartRowKey { get; set; }

        [DisplayName("Stop Row Key")]
        [Description("Optional stop row key (exclusive)")]
        public InArgument<string> StopRowKey { get; set; }

        [DisplayName("Batch Limit")]
        [Description("Maximum number of rows to retrieve (default is 100)")]
        [DefaultValue(100)]
        public InArgument<int?> BatchLimit { get; set; }

        [DisplayName("Columns (Filter)")]
        [Description("Optional comma-separated columns or column families, e.g. cf1:col1,cf1:col2 or cf1")]
        public InArgument<string> Columns { get; set; }

        [DisplayName("Filter Definition (JSON)")]
        [Description("HBase scanner filter JSON string, e.g. {\"type\": \"PrefixFilter\", \"value\": \"cust_\"}")]
        public InArgument<string> FilterJson { get; set; }

        [DisplayName("Username")]
        public InArgument<string> Username { get; set; }

        [DisplayName("Password")]
        [JavelinPassword]
        public InArgument<string> Password { get; set; }

        [DisplayName("Bearer Token")]
        [JavelinPassword]
        public InArgument<string> BearerToken { get; set; }

        [DisplayName("Timeout (Seconds)")]
        [DefaultValue(120)]
        public InArgument<int> TimeoutSeconds { get; set; }

        [DisplayName("Ignore SSL Certificate Errors")]
        [DefaultValue(true)]
        public InArgument<bool> IgnoreSslErrors { get; set; }

        // --- Outputs ---

        [DisplayName("Output JSON (Decoded)")]
        public OutArgument<string> OutJson { get; set; }

        [DisplayName("Output DataTable")]
        public OutArgument<DataTable> OutDataTable { get; set; }

        [DisplayName("Output Row Count")]
        public OutArgument<int> OutCount { get; set; }

        [DisplayName("Raw REST JSON")]
        public OutArgument<string> RawJson { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string table = null;
            try
            {
                base.Execute(context);

                string restUrl = RestUrl.Get(context);
                table = TableName.Get(context);
                string startKey = StartRowKey.Get(context);
                string stopKey = StopRowKey.Get(context);
                int? batch = BatchLimit.Get(context);
                string cols = Columns.Get(context);
                string filterJson = FilterJson.Get(context);
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

                // Build Scanner definition
                var scannerModel = new HBaseScannerModel
                {
                    Batch = (batch.HasValue && batch.Value > 0) ? batch.Value : 100
                };

                if (!string.IsNullOrWhiteSpace(startKey))
                {
                    scannerModel.StartRow = HBaseClientHelper.Base64Encode(startKey);
                }
                if (!string.IsNullOrWhiteSpace(stopKey))
                {
                    scannerModel.StopRow = HBaseClientHelper.Base64Encode(stopKey);
                }
                if (!string.IsNullOrWhiteSpace(cols))
                {
                    scannerModel.Columns = new System.Collections.Generic.List<string>();
                    foreach (var c in cols.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        scannerModel.Columns.Add(HBaseClientHelper.Base64Encode(c.Trim()));
                    }
                }
                if (!string.IsNullOrWhiteSpace(filterJson))
                {
                    scannerModel.Filter = filterJson;
                }

                string scannerPayload = JsonConvert.SerializeObject(scannerModel);
                var content = new StringContent(scannerPayload, Encoding.UTF8, "application/json");

                GlobalSettings.MessageLogger.LogMessage("Creating HBase Scanner on table {0}", table);

                // Step 1: POST to /{table}/scanner to obtain scanner location URI
                var scannerResponse = client.PutAsync($"{escapedTable}/scanner", content).GetAwaiter().GetResult();
                if (!scannerResponse.IsSuccessStatusCode && scannerResponse.StatusCode != HttpStatusCode.Created)
                {
                    // Fallback to POST if PUT isn't supported on older REST versions
                    scannerResponse = client.PostAsync($"{escapedTable}/scanner", content).GetAwaiter().GetResult();
                }

                if (!scannerResponse.IsSuccessStatusCode && scannerResponse.StatusCode != HttpStatusCode.Created)
                {
                    string err = scannerResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new HttpRequestException($"Failed to create HBase scanner. Status {scannerResponse.StatusCode}: {err}");
                }

                Uri scannerLocation = scannerResponse.Headers.Location;
                if (scannerLocation == null)
                {
                    throw new InvalidOperationException("HBase REST did not return a Location header for the scanner.");
                }

                string scannerUri = scannerLocation.ToString();
                GlobalSettings.MessageLogger.LogMessage("HBase Scanner created at: {0}", scannerUri);

                string rawRowsJson = string.Empty;
                try
                {
                    // Step 2: GET scanner URI to fetch matching rows
                    var fetchResponse = client.GetAsync(scannerUri).GetAwaiter().GetResult();

                    if (fetchResponse.StatusCode == HttpStatusCode.NoContent || fetchResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        rawRowsJson = "{\"Row\":[]}";
                    }
                    else if (!fetchResponse.IsSuccessStatusCode)
                    {
                        string err = fetchResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        throw new HttpRequestException($"Failed to fetch data from HBase scanner. Status {fetchResponse.StatusCode}: {err}");
                    }
                    else
                    {
                        rawRowsJson = fetchResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
                finally
                {
                    // Step 3: DELETE scanner to release server resources
                    try
                    {
                        client.DeleteAsync(scannerUri).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }

                RawJson.Set(context, rawRowsJson);
                var decodedRows = HBaseDataTableConverter.DecodeHBaseRestJson(rawRowsJson);
                OutCount.Set(context, decodedRows.Count);

                string decodedJson = JsonConvert.SerializeObject(decodedRows, Formatting.Indented);
                OutJson.Set(context, decodedJson);

                var dt = HBaseDataTableConverter.ToDataTable(decodedRows, table);
                OutDataTable.Set(context, dt);

                GlobalSettings.MessageLogger.LogMessage("HBase Scan completed. Retrieved {0} row(s).", decodedRows.Count);
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
