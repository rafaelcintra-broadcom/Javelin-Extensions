using System;
using System.Activities;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using GridTools.Javelin.Common;
using GridTools.Javelin.HBaseActivities.Helpers;

namespace GridTools.Javelin.HBaseActivities.Activities
{
    [DisplayName("HBase Cluster Status")]
    [Description("Fetches Apache HBase cluster version, status, or node metrics via REST /status/cluster or /version")]
    public class HBaseClusterStatusActivity : JavelinNativeActivity
    {
        public enum HBaseClusterInfoType
        {
            ClusterStatus,
            Version,
            TableNames
        }

        [RequiredArgument]
        [DisplayName("HBase REST URL")]
        [Description("Base URL of HBase REST daemon, e.g. http://hbase-host:8080")]
        public InArgument<string> RestUrl { get; set; }

        [DisplayName("Info Type")]
        [Description("Information to query: ClusterStatus, Version, or TableNames")]
        public InArgument<HBaseClusterInfoType> InfoType { get; set; }

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

        [DisplayName("Response JSON")]
        public OutArgument<string> ResponseJson { get; set; }

        [DisplayName("Is Accessible")]
        public OutArgument<bool> IsAccessible { get; set; }

        [DisplayName("Version")]
        public OutArgument<string> ClusterVersion { get; set; }

        [DisplayName("HTTP Status Code")]
        public OutArgument<int> StatusCode { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string restUrl = RestUrl.Get(context);
                var infoType = InfoType.Get(context);
                string user = Username.Get(context);
                string pass = Password.Get(context);
                string token = BearerToken.Get(context);
                int timeout = TimeoutSeconds.Get(context);
                bool ignoreSsl = IgnoreSslErrors.Get(context);

                if (string.IsNullOrWhiteSpace(restUrl))
                    throw new ArgumentException("HBase REST URL cannot be empty.");

                var client = HBaseClientHelper.GetOrCreateClient(restUrl, user, pass, token, timeout, ignoreSsl);

                string endpoint = "version";
                if (infoType == HBaseClusterInfoType.ClusterStatus)
                {
                    endpoint = "status/cluster";
                }
                else if (infoType == HBaseClusterInfoType.TableNames)
                {
                    endpoint = ""; // Root endpoint lists tables
                }

                GlobalSettings.MessageLogger.LogMessage("Checking HBase info: {0}", endpoint);

                var response = client.GetAsync(endpoint).GetAwaiter().GetResult();
                StatusCode.Set(context, (int)response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    IsAccessible.Set(context, false);
                    ResponseJson.Set(context, "{}");
                    string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new HttpRequestException($"HBase Cluster query failed with status {response.StatusCode}: {err}");
                }

                string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ResponseJson.Set(context, content);
                IsAccessible.Set(context, true);

                if (content.Contains("ServerVersion"))
                {
                    try
                    {
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(content);
                        ClusterVersion.Set(context, obj["ServerVersion"]?.ToString() ?? obj["JVMVersion"]?.ToString());
                    }
                    catch
                    {
                        ClusterVersion.Set(context, string.Empty);
                    }
                }

                GlobalSettings.MessageLogger.LogMessage("HBase Cluster query succeeded.");
            }
            catch (Exception ex)
            {
                string friendlyMessage = HBaseErrorMessageFormatter.FormatErrorMessage(ex.Message, DisplayName, "HBase Cluster");
                var bex = new BusinessException(friendlyMessage, ex);
                if (!continueOnErrorInExecMode)
                {
                    throw bex;
                }
            }
        }
    }
}
