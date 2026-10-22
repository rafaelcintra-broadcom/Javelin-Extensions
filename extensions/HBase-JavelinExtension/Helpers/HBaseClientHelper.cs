using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using GridTools.Javelin.Common;

namespace GridTools.Javelin.HBaseActivities.Helpers
{
    public static class HBaseClientHelper
    {
        private static readonly ConcurrentDictionary<string, HttpClient> ClientCache =
            new ConcurrentDictionary<string, HttpClient>();

        public static HttpClient GetOrCreateClient(
            string rawRestUrl,
            string rawUsername,
            string rawPassword,
            string rawBearerToken,
            int timeoutSeconds,
            bool ignoreSslErrors)
        {
            var decryptor = new JavelinPasswordDecryptor();
            string restUrl = (rawRestUrl != null) ? decryptor.SubstitutePasswordsInString(rawRestUrl).Trim().TrimEnd('/') : string.Empty;
            string username = (rawUsername != null) ? decryptor.SubstitutePasswordsInString(rawUsername).Trim() : string.Empty;
            string password = (rawPassword != null) ? decryptor.SubstitutePasswordsInString(rawPassword) : string.Empty;
            string bearerToken = (rawBearerToken != null) ? decryptor.SubstitutePasswordsInString(rawBearerToken).Trim() : string.Empty;

            string cacheKey = string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                restUrl, username, password, bearerToken, timeoutSeconds, ignoreSslErrors);

            return ClientCache.GetOrAdd(cacheKey, key =>
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                var handler = new HttpClientHandler();
                if (ignoreSslErrors)
                {
                    ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
                }

                var client = new HttpClient(handler)
                {
                    BaseAddress = new Uri(restUrl + "/"),
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 60)
                };

                // Default Accept header
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    string token = bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? bearerToken.Substring(7).Trim()
                        : bearerToken;
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                else if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
                {
                    string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Format("{0}:{1}", username, password)));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }

                return client;
            });
        }

        public static string Base64Encode(string plainText)
        {
            if (plainText == null) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(bytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            if (string.IsNullOrEmpty(base64EncodedData)) return string.Empty;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64EncodedData);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return base64EncodedData;
            }
        }
    }
}
