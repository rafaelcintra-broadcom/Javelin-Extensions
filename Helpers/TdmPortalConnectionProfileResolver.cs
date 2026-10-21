using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using GridTools.Javelin.Common;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.MongoDbActivities.Helpers
{
    public class TdmConnectionProfileInfo
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string DbType { get; set; }
        public string DriverClass { get; set; }
        public string ConnectionUrl { get; set; }
        public string DatabaseName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
    }

    /// <summary>
    /// Resolvedor automático de Connection Profiles integrado à API REST do Broadcom TDM Portal.
    /// Permite que as atividades do Javelin usem diretamente nomes de perfis (ex: 'MONGO_PROD_CLUSTER').
    /// </summary>
    public static class TdmPortalConnectionProfileResolver
    {
        private static readonly ConcurrentDictionary<string, TdmConnectionProfileInfo> ProfileCache =
            new ConcurrentDictionary<string, TdmConnectionProfileInfo>(StringComparer.OrdinalIgnoreCase);

        private static readonly HttpClient HttpClient;

        static TdmPortalConnectionProfileResolver()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            HttpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        /// <summary>
        /// Obtém a URL base do TDM a partir de argumentos, variáveis de ambiente ou propriedades padrão.
        /// </summary>
        public static string ResolveTdmBaseUrl(string customUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(customUrl))
                return customUrl.Trim().TrimEnd('/');

            string envUrl = Environment.GetEnvironmentVariable("TDM_URL") ??
                            Environment.GetEnvironmentVariable("TDM_PORTAL_URL");
            if (!string.IsNullOrWhiteSpace(envUrl))
                return envUrl.Trim().TrimEnd('/');

            return "https://localhost:8443";
        }

        /// <summary>
        /// Localiza o arquivo application.properties nas pastas padrão de instalação do TDM.
        /// </summary>
        private static string FindTdmApplicationPropertiesPath()
        {
            var candidates = new List<string>
            {
                Environment.GetEnvironmentVariable("TDM_PROPERTIES_PATH"),
                Environment.GetEnvironmentVariable("TDM_CONFIG_PATH")
            };

            string tdmHome = Environment.GetEnvironmentVariable("TDM_HOME") ??
                             Environment.GetEnvironmentVariable("CATDM_HOME") ??
                             Environment.GetEnvironmentVariable("TDM_WEB_HOME");

            if (!string.IsNullOrWhiteSpace(tdmHome))
            {
                candidates.Add(Path.Combine(tdmHome, "application.properties"));
                candidates.Add(Path.Combine(tdmHome, "conf", "application.properties"));
                candidates.Add(Path.Combine(tdmHome, "config", "application.properties"));
                candidates.Add(Path.Combine(tdmHome, "TDMWeb", "application.properties"));
            }

            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(progFiles))
            {
                candidates.Add(Path.Combine(progFiles, "CA", "CA Test Data Manager Portal", "application.properties"));
                candidates.Add(Path.Combine(progFiles, "CA", "CA Test Data Manager Portal", "conf", "application.properties"));
                candidates.Add(Path.Combine(progFiles, "CA", "Test Data Manager", "TDMWeb", "application.properties"));
                candidates.Add(Path.Combine(progFiles, "Broadcom", "CA Test Data Manager Portal", "application.properties"));
            }

            foreach (var path in candidates)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Carrega pares chave-valor do application.properties do TDM de forma segura.
        /// </summary>
        private static Dictionary<string, string> ReadPropertiesFile(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return dict;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("!"))
                    continue;

                int idx = trimmed.IndexOf('=');
                if (idx > 0)
                {
                    string key = trimmed.Substring(0, idx).Trim();
                    string val = trimmed.Substring(idx + 1).Trim();
                    dict[key] = val;
                }
            }

            return dict;
        }

        /// <summary>
        /// Obtém o token JWT do TDM Portal ou efetua login automático via Basic Auth se fornecido.
        /// Prioriza leitura blindada das credenciais do 'integrator' no application.properties local.
        /// </summary>
        public static string ResolveTdmAuthHeader(string tdmUrl, string customToken = null, string user = null, string pass = null)
        {
            if (!string.IsNullOrWhiteSpace(customToken))
            {
                string token = customToken.Trim();
                return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
            }

            string envToken = Environment.GetEnvironmentVariable("TDM_JWT") ??
                              Environment.GetEnvironmentVariable("TDM_TOKEN");
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                string token = envToken.Trim();
                return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
            }

            // 1. Tenta carregar credenciais do 'integrator' direto do application.properties do TDM
            string adminUser = user;
            string adminPass = pass;

            if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPass))
            {
                string propPath = FindTdmApplicationPropertiesPath();
                if (!string.IsNullOrWhiteSpace(propPath))
                {
                    try
                    {
                        var props = ReadPropertiesFile(propPath);
                        if (props.TryGetValue("tdm.integrator.user", out string intUser) && !string.IsNullOrWhiteSpace(intUser))
                            adminUser = intUser;
                        else if (props.TryGetValue("integrator.user", out string intUser2) && !string.IsNullOrWhiteSpace(intUser2))
                            adminUser = intUser2;

                        if (props.TryGetValue("tdm.integrator.password", out string intPass) && !string.IsNullOrWhiteSpace(intPass))
                            adminPass = intPass;
                        else if (props.TryGetValue("integrator.password", out string intPass2) && !string.IsNullOrWhiteSpace(intPass2))
                            adminPass = intPass2;
                    }
                    catch { }
                }
            }

            // Fallbacks de variáveis de ambiente
            if (string.IsNullOrWhiteSpace(adminUser))
                adminUser = Environment.GetEnvironmentVariable("TDM_ADMIN_USER") ?? "integrator";

            if (string.IsNullOrWhiteSpace(adminPass))
                adminPass = Environment.GetEnvironmentVariable("TDM_ADMIN_PASS") ?? "integrator";

            // Tenta obter JWT via endpoint de autenticação
            try
            {
                string authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{adminUser}:{adminPass}"));
                var request = new HttpRequestMessage(HttpMethod.Post, $"{tdmUrl}/TestDataManager/api/ca/v1/auth/login")
                {
                    Headers = { { "Authorization", $"Basic {authString}" } }
                };

                var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var jObj = JObject.Parse(body);
                    string jwt = jObj.Value<string>("token") ?? jObj.Value<string>("jwt");
                    if (!string.IsNullOrWhiteSpace(jwt))
                    {
                        return "Bearer " + jwt;
                    }
                }
            }
            catch { }

            // Fallback para Basic Auth direto
            string basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{adminUser}:{adminPass}"));
            return "Basic " + basicAuth;
        }

        /// <summary>
        /// Resolve um Connection Profile por Nome ou ID consultando os microserviços REST do TDM.
        /// </summary>
        public static TdmConnectionProfileInfo ResolveProfile(
            string profileIdentifier,
            string tdmUrl = null,
            string tdmToken = null,
            string tdmUser = null,
            string tdmPass = null,
            long? projectId = null)
        {
            if (string.IsNullOrWhiteSpace(profileIdentifier))
                return null;

            string cleanId = profileIdentifier.Trim();

            // 1. Checa cache em memória
            if (ProfileCache.TryGetValue(cleanId, out var cached))
                return cached;

            string baseUrl = ResolveTdmBaseUrl(tdmUrl);
            string authHeader = ResolveTdmAuthHeader(baseUrl, tdmToken, tdmUser, tdmPass);

            // 2. Endpoints candidatos do TDM Portal
            var endpoints = new List<string>
            {
                $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/connectionProfiles?size=1000",
                $"{baseUrl}/TestDataManager/api/ca/v1/connectionProfiles?size=1000"
            };

            if (projectId.HasValue && projectId.Value > 0)
            {
                endpoints.Insert(0, $"{baseUrl}/TestDataManager/api/ca/v1/projects/{projectId.Value}/connectionProfiles?size=1000");
                endpoints.Insert(0, $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/projects/{projectId.Value}/connectionProfiles?size=1000");
            }

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    req.Headers.TryAddWithoutValidation("Authorization", authHeader);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode) continue;

                    string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var profiles = ParseProfilesFromJson(json);

                    foreach (var p in profiles)
                    {
                        ProfileCache[p.Name] = p;
                        ProfileCache[p.Id.ToString()] = p;
                    }

                    if (ProfileCache.TryGetValue(cleanId, out var found))
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmProfileResolver] Perfil '{0}' resolvido via TDM API (ID: {1}, DB: {2}, URL: {3})",
                            found.Name, found.Id, found.DatabaseName, found.ConnectionUrl);
                        return found;
                    }
                }
                catch (Exception ex)
                {
                    GlobalSettings.MessageLogger.LogMessage(
                        "[TdmProfileResolver] Tentativa no endpoint {0} retornou: {1}", endpoint, ex.Message);
                }
            }

            return null;
        }

        private static List<TdmConnectionProfileInfo> ParseProfilesFromJson(string json)
        {
            var list = new List<TdmConnectionProfileInfo>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try
            {
                var token = JToken.Parse(json);
                JArray items = null;

                if (token is JArray arr) items = arr;
                else if (token is JObject obj)
                {
                    items = obj["connectionProfiles"] as JArray ??
                            obj["profiles"] as JArray ??
                            obj["elements"] as JArray ??
                            obj["content"] as JArray;
                }

                if (items == null) return list;

                foreach (var item in items)
                {
                    long id = item.Value<long?>("id") ?? item.Value<long?>("profileId") ?? item.Value<long?>("connectionProfileId") ?? item.Value<long?>("cpId") ?? 0;
                    string name = item.Value<string>("name") ?? item.Value<string>("profileName") ?? item.Value<string>("connectionProfileName") ?? item.Value<string>("cpName");
                    string desc = item.Value<string>("description") ?? item.Value<string>("profileDescription");
                    string dbType = item.Value<string>("dbType") ?? item.Value<string>("databaseType") ?? item.Value<string>("rdbmsType");
                    string driver = item.Value<string>("driverClass") ?? item.Value<string>("datasourceDriver");
                    string url = item.Value<string>("connectionUrl") ?? item.Value<string>("jdbcUrl") ?? item.Value<string>("datasourceUrl") ?? item.Value<string>("baseUrl");
                    string user = item.Value<string>("user") ?? item.Value<string>("username") ?? item.Value<string>("userName");
                    string pass = item.Value<string>("password");
                    string dbName = item.Value<string>("databaseName") ?? item.Value<string>("dbName") ?? item.Value<string>("schema");
                    string host = item.Value<string>("host") ?? item.Value<string>("serverName");
                    int port = item.Value<int?>("port") ?? 27017;

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        list.Add(new TdmConnectionProfileInfo
                        {
                            Id = id,
                            Name = name,
                            Description = desc,
                            DbType = dbType,
                            DriverClass = driver,
                            ConnectionUrl = url,
                            Username = user,
                            Password = pass,
                            DatabaseName = dbName,
                            Host = host,
                            Port = port
                        });
                    }
                }
            }
            catch { }

            return list;
        }

        /// <summary>
        /// Resolve ou constrói a Connection String definitiva do MongoDB:
        /// Se o input for uma URI (mongodb:// ou mongodb+srv://), retorna a própria URI.
        /// Se for o nome de um Connection Profile cadastrado no TDM, busca os dados na API do TDM e monta a URI.
        /// </summary>
        public static string ResolveMongoConnectionString(
            string inputOrProfileName,
            out string resolvedDbName,
            string fallbackDbName = null)
        {
            resolvedDbName = fallbackDbName;
            if (string.IsNullOrWhiteSpace(inputOrProfileName))
                return inputOrProfileName;

            string trimmed = inputOrProfileName.Trim();

            // Se já for uma URI do MongoDB direta
            if (trimmed.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            {
                // Tenta inferir database da URI se não fornecido
                if (string.IsNullOrWhiteSpace(resolvedDbName))
                {
                    var match = Regex.Match(trimmed, @"mongodb(?:\+srv)?://[^/]+/([^?/\s]+)");
                    if (match.Success)
                    {
                        resolvedDbName = match.Groups[1].Value;
                    }
                }
                return trimmed;
            }

            // Caso contrário, trata como Nome ou ID de Connection Profile do TDM
            var profile = ResolveProfile(trimmed);
            if (profile != null)
            {
                if (!string.IsNullOrWhiteSpace(profile.DatabaseName))
                {
                    resolvedDbName = profile.DatabaseName;
                }

                if (!string.IsNullOrWhiteSpace(profile.ConnectionUrl))
                {
                    string rawUrl = profile.ConnectionUrl;
                    if (rawUrl.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
                        rawUrl.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
                    {
                        return rawUrl;
                    }
                }

                // Reconstrói URI padrão com host/port/user/pass do profile se disponível
                string host = !string.IsNullOrWhiteSpace(profile.Host) ? profile.Host : "localhost";
                int port = profile.Port > 0 ? profile.Port : 27017;
                string authPart = "";

                if (!string.IsNullOrWhiteSpace(profile.Username))
                {
                    string pass = !string.IsNullOrWhiteSpace(profile.Password) ? profile.Password : "";
                    authPart = $"{profile.Username}:{pass}@";
                }

                string db = !string.IsNullOrWhiteSpace(resolvedDbName) ? resolvedDbName : "admin";
                return $"mongodb://{authPart}{host}:{port}/{db}?authSource=admin";
            }

            // Retorna o valor original caso a resolução falhe
            return inputOrProfileName;
        }
    }
}
