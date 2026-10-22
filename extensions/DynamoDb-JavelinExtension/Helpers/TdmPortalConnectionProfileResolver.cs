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

namespace GridTools.Javelin.DynamoDbActivities.Helpers
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
#if NETFRAMEWORK
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
#endif
#if !NETFRAMEWORK
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            HttpClient = new HttpClient(handler)
#else
            HttpClient = new HttpClient()
#endif
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        /// <summary>
        /// Obtém a URL base do TDM a partir de argumentos, variáveis de ambiente ou propriedades padrão.
        /// Se o Javelin estiver na mesma máquina do Portal, tenta detectar a porta real configurada no application.properties.
        /// </summary>
        public static string ResolveTdmBaseUrl(string customUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(customUrl))
            {
                string clean = customUrl.Trim().TrimEnd('/');
                if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    clean = "https://" + clean;
                }
                return clean;
            }

            string envUrl = Environment.GetEnvironmentVariable("TDM_URL") ??
                            Environment.GetEnvironmentVariable("TDM_PORTAL_URL");
            if (!string.IsNullOrWhiteSpace(envUrl))
                return envUrl.Trim().TrimEnd('/');

            // Tenta obter porta, host e protocolo do application.properties local
            string propPath = FindTdmApplicationPropertiesPath();
            if (!string.IsNullOrWhiteSpace(propPath))
            {
                try
                {
                    var props = ReadPropertiesFile(propPath);
                    string port = null;
                    if (props.TryGetValue("server.port", out string sPort) && !string.IsNullOrWhiteSpace(sPort))
                        port = sPort.Trim();

                    string sslEnabled = null;
                    if (props.TryGetValue("server.ssl.enabled", out string sSsl))
                        sslEnabled = sSsl.Trim();

                    string host = "localhost";
                    if (props.TryGetValue("server.address", out string sAddr) && !string.IsNullOrWhiteSpace(sAddr))
                    {
                        string cleanAddr = sAddr.Trim();
                        if (!cleanAddr.Equals("0.0.0.0") && !cleanAddr.Equals("::"))
                            host = cleanAddr;
                    }
                    else if (props.TryGetValue("tdmweb.address", out string sTdmAddr) && !string.IsNullOrWhiteSpace(sTdmAddr))
                    {
                        host = sTdmAddr.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(port))
                    {
                        bool isHttps = !string.Equals(sslEnabled, "false", StringComparison.OrdinalIgnoreCase);
                        string proto = isHttps ? "https" : "http";
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmProfileResolver] URL do Portal detectada via application.properties local ({0}): {1}://{2}:{3}",
                            propPath, proto, host, port);
                        return $"{proto}://{host}:{port}";
                    }
                }
                catch { }
            }

            return "https://localhost:8443";
        }

        /// <summary>
        /// Localiza o arquivo application.properties seguindo o padrão oficial do TDM:
        /// 1. %CATDMWEB_HOME%\conf\application.properties (padrão oficial PropertyReaderUtils / TDM installer)
        /// 2. %TDM_HOME%\conf\application.properties
        /// 3. spring.config.additional-location / TDM_PROPERTIES_PATH
        /// 4. Diretórios padrões conhecidos da Broadcom / CA
        /// </summary>
        private static string FindTdmApplicationPropertiesPath()
        {
            var candidates = new List<string>();

            // 1. Variável oficial CATDMWEB_HOME (definida pelo instalador MSI do TDM Portal)
            string catdmwebHome = Environment.GetEnvironmentVariable("CATDMWEB_HOME");
            if (!string.IsNullOrWhiteSpace(catdmwebHome))
            {
                candidates.Add(Path.Combine(catdmwebHome, "conf", "application.properties"));
                candidates.Add(Path.Combine(catdmwebHome, "application.properties"));
            }

            // 2. Variável oficial TDM_HOME (definida para storage/appdata e conf do TDM)
            string tdmHome = Environment.GetEnvironmentVariable("TDM_HOME");
            if (!string.IsNullOrWhiteSpace(tdmHome))
            {
                candidates.Add(Path.Combine(tdmHome, "conf", "application.properties"));
                candidates.Add(Path.Combine(tdmHome, "application.properties"));
            }

            // 3. Variáveis adicionais e propriedades de ambiente
            string springConfig = Environment.GetEnvironmentVariable("spring.config.additional-location") ??
                                 Environment.GetEnvironmentVariable("SPRING_CONFIG_ADDITIONAL_LOCATION") ??
                                 Environment.GetEnvironmentVariable("TDM_PROPERTIES_PATH") ??
                                 Environment.GetEnvironmentVariable("TDM_CONFIG_PATH");
            if (!string.IsNullOrWhiteSpace(springConfig))
            {
                // Se contiver múltiplos caminhos separados por vírgula (formato Spring Boot)
                var parts = springConfig.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    string cleaned = part.Trim();
                    if (cleaned.StartsWith("optional:file:", StringComparison.OrdinalIgnoreCase))
                        cleaned = cleaned.Substring("optional:file:".Length);
                    else if (cleaned.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                        cleaned = cleaned.Substring("file:".Length);

                    if (!string.IsNullOrWhiteSpace(cleaned))
                        candidates.Add(cleaned);
                }
            }

            // 4. Pastas padrão de instalação no Windows Program Files
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(progFiles))
            {
                candidates.Add(Path.Combine(progFiles, "CA", "CA Test Data Manager Portal", "conf", "application.properties"));
                candidates.Add(Path.Combine(progFiles, "CA", "CA Test Data Manager Portal", "application.properties"));
                candidates.Add(Path.Combine(progFiles, "Broadcom", "CA Test Data Manager Portal", "conf", "application.properties"));
                candidates.Add(Path.Combine(progFiles, "CA", "Test Data Manager", "conf", "application.properties"));
            }

            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(progFilesX86))
            {
                candidates.Add(Path.Combine(progFilesX86, "CA", "CA Test Data Manager Portal", "conf", "application.properties"));
                candidates.Add(Path.Combine(progFilesX86, "CA", "CA Test Data Manager Portal", "application.properties"));
            }

            // Diretórios padrão comuns no disco C:\
            candidates.Add(@"C:\Program Files\CA\CA Test Data Manager Portal\conf\application.properties");
            candidates.Add(@"C:\Program Files\CA\CA Test Data Manager Portal\application.properties");
            candidates.Add(@"C:\CA\CA Test Data Manager Portal\conf\application.properties");
            candidates.Add(@"C:\CA\CA Test Data Manager Portal\application.properties");
            candidates.Add(@"C:\CA_TDM\conf\application.properties");
            candidates.Add(@"C:\TDM\conf\application.properties");
            candidates.Add(@"C:\Grid-Tools\TDMWeb\conf\application.properties");

            foreach (var path in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        return path;
                }
                catch { }
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
        public static string ResolveTdmAuthHeader(
            string tdmUrl, 
            string customToken = null, 
            string user = null, 
            string pass = null,
            bool enableDebugLogs = false)
        {
            if (!string.IsNullOrWhiteSpace(customToken))
            {
                string token = customToken.Trim();
                if (enableDebugLogs)
                {
                    GlobalSettings.MessageLogger.LogMessage(
                        "[TdmProfileResolver][DEBUG] Utilizando Token JWT customizado informado pelo usuário (prefixo: {0}...).",
                        token.Length > 15 ? token.Substring(0, 15) : "***");
                }
                return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
            }

            string envToken = Environment.GetEnvironmentVariable("TDM_JWT") ??
                              Environment.GetEnvironmentVariable("TDM_TOKEN");
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                string token = envToken.Trim();
                if (enableDebugLogs)
                {
                    GlobalSettings.MessageLogger.LogMessage(
                        "[TdmProfileResolver][DEBUG] Utilizando Token JWT da variável de ambiente TDM_JWT/TDM_TOKEN.");
                }
                return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token : "Bearer " + token;
            }

            // 1. Tenta carregar credenciais das propriedades ou argumentos
            string adminUser = user;
            string adminPass = pass;
            string sourceDesc = "Argumento Javelin";

            if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPass))
            {
                string propPath = FindTdmApplicationPropertiesPath();
                if (!string.IsNullOrWhiteSpace(propPath))
                {
                    try
                    {
                        var props = ReadPropertiesFile(propPath);

                        // Busca chaves conhecidas do TDM para usuário de integração/admin
                        string[] userKeys = {
                            "tdmweb.security.integration.userName",
                            "security.integration.userName",
                            "tdm.integrator.user",
                            "integrator.user",
                            "tdm.admin.user",
                            "admin.user"
                        };

                        string[] passKeys = {
                            "tdmweb.security.integration.password",
                            "security.integration.password",
                            "tdm.integrator.password",
                            "integrator.password",
                            "tdm.admin.password",
                            "admin.password"
                        };

                        foreach (var uk in userKeys)
                        {
                            if (props.TryGetValue(uk, out string val) && !string.IsNullOrWhiteSpace(val))
                            {
                                adminUser = val.Trim();
                                break;
                            }
                        }

                        foreach (var pk in passKeys)
                        {
                            if (props.TryGetValue(pk, out string val) && !string.IsNullOrWhiteSpace(val))
                            {
                                adminPass = GridTools.Javelin.DynamoDbActivities.Helpers.Crypto.CryptUtil.DecryptPasswordIfNeeded(val.Trim());
                                break;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(adminUser))
                        {
                            sourceDesc = $"application.properties ({propPath})";
                        }
                    }
                    catch (Exception ex)
                    {
                        if (enableDebugLogs)
                        {
                            GlobalSettings.MessageLogger.LogMessage(
                                "[TdmProfileResolver][DEBUG] Erro ao ler application.properties: {0}", ex.Message);
                        }
                    }
                }
            }

            // Fallbacks de variáveis de ambiente
            if (string.IsNullOrWhiteSpace(adminUser))
            {
                adminUser = Environment.GetEnvironmentVariable("TDM_ADMIN_USER");
                if (!string.IsNullOrWhiteSpace(adminUser)) sourceDesc = "Env Var TDM_ADMIN_USER";
            }

            if (string.IsNullOrWhiteSpace(adminPass))
            {
                adminPass = Environment.GetEnvironmentVariable("TDM_ADMIN_PASS");
            }

            // Fallback padrão se não configurado
            if (string.IsNullOrWhiteSpace(adminUser))
            {
                adminUser = "integrator";
                sourceDesc = "Padrão TDM (integrator)";
            }
            if (string.IsNullOrWhiteSpace(adminPass))
            {
                adminPass = "integrator";
            }

            GlobalSettings.MessageLogger.LogMessage(
                "[TdmProfileResolver] Autenticando no TDM Portal com usuário '{0}' (Origem: {1})...", adminUser, sourceDesc);

            // Tenta obter JWT via endpoints de autenticação oficiais do TDM
            string authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{adminUser}:{adminPass}"));
            var loginEndpoints = new List<string>
            {
                $"{tdmUrl}/TestDataManager/user/login",
                $"{tdmUrl}/user/login",
                $"{tdmUrl}/TestDataManager/api/ca/v1/auth/login",
                $"{tdmUrl}/api/ca/v1/auth/login"
            };

            foreach (var loginUrl in loginEndpoints)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
                    request.Headers.TryAddWithoutValidation("Authorization", $"Basic {authString}");

                    var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode)
                    {
                        var jObj = JObject.Parse(body);
                        string jwt = jObj.Value<string>("token") ??
                                     jObj.Value<string>("jwt") ??
                                     jObj.Value<string>("accessToken") ??
                                     jObj.Value<string>("access_token");

                        if (!string.IsNullOrWhiteSpace(jwt))
                        {
                            GlobalSettings.MessageLogger.LogMessage(
                                "[TdmProfileResolver] ✅ Login efetuado com sucesso em {0}! JWT obtido para usuário '{1}'.",
                                loginUrl, adminUser);
                            return "Bearer " + jwt;
                        }
                    }
                    else
                    {
                        string bodyExcerpt = !string.IsNullOrWhiteSpace(body) && body.Length > 200
                            ? body.Substring(0, 200) + "..."
                            : body;
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmProfileResolver] Tentativa de login em {0} retornou HTTP {1}: {2}",
                            loginUrl, (int)response.StatusCode, bodyExcerpt);
                    }
                }
                catch (Exception ex)
                {
                    GlobalSettings.MessageLogger.LogMessage(
                        "[TdmProfileResolver] Falha de conexão ao tentar login em {0}: {1}", loginUrl, ex.Message);
                }
            }

            GlobalSettings.MessageLogger.LogMessage(
                "[TdmProfileResolver] ⚠️ Não foi possível obter JWT via login. Continuando com Basic Auth para '{0}'.", adminUser);

            return "Basic " + authString;
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
            long? projectId = null,
            bool enableDebugLogs = false)
        {
            if (string.IsNullOrWhiteSpace(profileIdentifier))
                return null;

            string cleanId = profileIdentifier.Trim();

            // 1. Checa cache em memória
            if (ProfileCache.TryGetValue(cleanId, out var cached))
                return cached;

            string baseUrl = ResolveTdmBaseUrl(tdmUrl);
            string authHeader = ResolveTdmAuthHeader(baseUrl, tdmToken, tdmUser, tdmPass, enableDebugLogs);

            // 2. Endpoints candidatos do TDM Portal em ordem de precisão:
            string encodedName = Uri.EscapeDataString(cleanId);
            var endpoints = new List<string>
            {
                // Microserviço oficial ConnectionProfileManager
                $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/connectionProfiles/{encodedName}",
                $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/connectionProfiles",
                $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/connectionProfiles?name={encodedName}&size=100",
                $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/connectionProfiles?size=1000",
                $"{baseUrl}/connectionProfileManager/api/ca/v1/connectionProfiles/{encodedName}",
                $"{baseUrl}/connectionProfileManager/api/ca/v1/connectionProfiles",
                // Microserviço TDMModelService / DataModel
                $"{baseUrl}/TDMModelService/api/ca/v1/connectionProfiles/{encodedName}",
                $"{baseUrl}/TDMModelService/api/ca/v1/connectionProfiles",
                // Endpoints no contextPath padrão /TestDataManager e raiz
                $"{baseUrl}/TestDataManager/api/ca/v1/connectionProfiles/{encodedName}",
                $"{baseUrl}/TestDataManager/api/ca/v1/connectionProfiles",
                $"{baseUrl}/TestDataManager/api/ca/v1/connectionProfiles?name={encodedName}&size=100",
                $"{baseUrl}/TestDataManager/api/ca/v1/connectionProfiles?size=1000",
                $"{baseUrl}/api/ca/v1/connectionProfiles/{encodedName}",
                $"{baseUrl}/api/ca/v1/connectionProfiles"
            };

            if (projectId.HasValue && projectId.Value > 0)
            {
                endpoints.Insert(0, $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/projects/{projectId.Value}/connectionProfiles/{encodedName}");
                endpoints.Insert(0, $"{baseUrl}/TDMConnectionProfileService/api/ca/v1/projects/{projectId.Value}/connectionProfiles?name={encodedName}&size=100");
                endpoints.Insert(0, $"{baseUrl}/connectionProfileManager/api/ca/v1/projects/{projectId.Value}/connectionProfiles/{encodedName}");
                endpoints.Insert(0, $"{baseUrl}/TDMModelService/api/ca/v1/projects/{projectId.Value}/connectionProfiles/{encodedName}");
                endpoints.Insert(0, $"{baseUrl}/TestDataManager/api/ca/v1/projects/{projectId.Value}/connectionProfiles/{encodedName}");
                endpoints.Insert(0, $"{baseUrl}/TestDataManager/api/ca/v1/projects/{projectId.Value}/connectionProfiles?name={encodedName}&size=100");
            }

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    req.Headers.TryAddWithoutValidation("Authorization", authHeader);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
                    string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!resp.IsSuccessStatusCode)
                    {
                        string respExcerpt = !string.IsNullOrWhiteSpace(json) && json.Length > 200
                            ? json.Substring(0, 200) + "..."
                            : json;
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmProfileResolver] Consulta GET {0} retornou HTTP {1}: {2}",
                            endpoint, (int)resp.StatusCode, respExcerpt);
                        continue;
                    }

                    if (enableDebugLogs)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmProfileResolver][DEBUG] Resposta HTTP {0} de {1}: {2}",
                            (int)resp.StatusCode, endpoint, json.Length > 500 ? json.Substring(0, 500) + "..." : json);
                    }

                    var profiles = ParseProfilesFromJson(json);

                    foreach (var p in profiles)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Name))
                            ProfileCache[p.Name] = p;
                        if (p.Id > 0)
                            ProfileCache[p.Id.ToString()] = p;
                    }

                    // Tenta busca case-insensitive no cache
                    var found = FindProfileInCache(cleanId);
                    if (found != null)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmProfileResolver] ✅ Perfil '{0}' resolvido com sucesso via TDM API (ID: {1}, DB: {2}, Host: {3}, URL: {4})",
                            found.Name, found.Id, found.DatabaseName, found.Host, found.ConnectionUrl);
                        return found;
                    }
                }
                catch (Exception ex)
                {
                    GlobalSettings.MessageLogger.LogMessage(
                        "[TdmProfileResolver] Tentativa no endpoint {0} falhou: {1}", endpoint, ex.Message);
                }
            }

            return null;
        }

        private static TdmConnectionProfileInfo FindProfileInCache(string searchName)
        {
            if (string.IsNullOrWhiteSpace(searchName)) return null;

            if (ProfileCache.TryGetValue(searchName, out var exact))
                return exact;

            foreach (var kvp in ProfileCache)
            {
                if (string.Equals(kvp.Key, searchName, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
                if (kvp.Value != null && string.Equals(kvp.Value.Name, searchName, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
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
                var items = new List<JObject>();

                if (token is JArray arr)
                {
                    foreach (var elem in arr)
                    {
                        if (elem is JObject objElem) items.Add(objElem);
                    }
                }
                else if (token is JObject obj)
                {
                    JArray nestedArr = obj["connectionProfiles"] as JArray ??
                                       obj["profiles"] as JArray ??
                                       obj["elements"] as JArray ??
                                       obj["content"] as JArray ??
                                       obj["data"] as JArray;

                    if (nestedArr != null)
                    {
                        foreach (var elem in nestedArr)
                        {
                            if (elem is JObject objElem) items.Add(objElem);
                        }
                    }
                    else if (obj.ContainsKey("name") || obj.ContainsKey("profileName") || obj.ContainsKey("server") || obj.ContainsKey("connectionUrl"))
                    {
                        // Resposta de profile único direto do endpoint /connectionProfiles/{name}
                        items.Add(obj);
                    }
                }

                foreach (var item in items)
                {
                    long id = item.Value<long?>("id") ?? item.Value<long?>("profileId") ?? item.Value<long?>("connectionProfileId") ?? item.Value<long?>("cpId") ?? 0;
                    string name = item.Value<string>("name") ?? item.Value<string>("profileName") ?? item.Value<string>("connectionProfileName") ?? item.Value<string>("cpName");
                    string desc = item.Value<string>("description") ?? item.Value<string>("profileDescription");
                    string dbType = item.Value<string>("dbType") ?? item.Value<string>("databaseType") ?? item.Value<string>("rdbmsType") ?? item.Value<string>("profileType");
                    string driver = item.Value<string>("driverClass") ?? item.Value<string>("datasourceDriver");
                    string url = item.Value<string>("datasourceUrl") ?? item.Value<string>("connectionUrl") ?? item.Value<string>("jdbcUrl") ?? item.Value<string>("baseUrl") ?? item.Value<string>("uri");
                    string user = item.Value<string>("username") ?? item.Value<string>("user") ?? item.Value<string>("userName");
                    string pass = item.Value<string>("password") ?? item.Value<string>("userPassword") ?? item.Value<string>("profPassword");
                    string dbName = item.Value<string>("database") ?? item.Value<string>("databaseName") ?? item.Value<string>("dbName") ?? item.Value<string>("schema");
                    
                    string host = item.Value<string>("server") ?? item.Value<string>("host") ?? item.Value<string>("serverName") ?? item.Value<string>("hostname");
                    int port = 27017;
                    var portToken = item["port"];
                    if (portToken != null)
                    {
                        if (portToken.Type == JTokenType.Integer)
                            port = portToken.Value<int>();
                        else if (portToken.Type == JTokenType.String && int.TryParse(portToken.Value<string>(), out int parsedPort) && parsedPort > 0)
                            port = parsedPort;
                    }

                    // Se server contiver host:porta (ex: "localhost:27017")
                    if (!string.IsNullOrWhiteSpace(host) && host.Contains(":"))
                    {
                        var parts = host.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int hpPort) && hpPort > 0)
                        {
                            host = parts[0];
                            port = hpPort;
                        }
                    }

                    // Se a URL estiver dentro de um objeto de propriedades ou config
                    if (string.IsNullOrWhiteSpace(url) && item["properties"] is JObject props)
                    {
                        url = props.Value<string>("connectionUrl") ?? props.Value<string>("uri") ?? props.Value<string>("url");
                        if (string.IsNullOrWhiteSpace(user)) user = props.Value<string>("username") ?? props.Value<string>("user");
                        if (string.IsNullOrWhiteSpace(pass)) pass = props.Value<string>("password");
                        if (string.IsNullOrWhiteSpace(dbName)) dbName = props.Value<string>("database") ?? props.Value<string>("databaseName");
                        if (string.IsNullOrWhiteSpace(host)) host = props.Value<string>("server") ?? props.Value<string>("host");
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        // Decripta senha caso venha criptografada pelo TDM/Javelin
                        string effectivePass = pass;
                        if (!string.IsNullOrWhiteSpace(pass))
                        {
                            try
                            {
                                effectivePass = GridTools.Javelin.DynamoDbActivities.Helpers.Crypto.CryptUtil.DecryptPasswordIfNeeded(pass);
                                effectivePass = new JavelinPasswordDecryptor().SubstitutePasswordsInString(effectivePass);
                            }
                            catch { }
                        }

                        list.Add(new TdmConnectionProfileInfo
                        {
                            Id = id,
                            Name = name,
                            Description = desc,
                            DbType = dbType,
                            DriverClass = driver,
                            ConnectionUrl = url,
                            Username = user,
                            Password = effectivePass,
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
        /// Se customTdmUrl for nulo/vazio, busca automaticamente no application.properties local.
        /// </summary>
        public static string ResolveMongoConnectionString(
            string inputOrProfileName,
            out string resolvedDbName,
            string fallbackDbName = null,
            string customTdmUrl = null,
            string customToken = null,
            string customUser = null,
            string customPass = null,
            bool enableDebugLogs = false)
        {
            resolvedDbName = fallbackDbName;
            if (string.IsNullOrWhiteSpace(inputOrProfileName))
                return inputOrProfileName;

            string trimmed = inputOrProfileName.Trim();

            // 1. Se já for uma URI do MongoDB direta (mongodb:// ou mongodb+srv://)
            if (trimmed.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            {
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

            // 2. Se for uma URL no formato JDBC do TDM Portal (ex: jdbc:mongodb://...)
            if (trimmed.StartsWith("jdbc:mongodb://", StringComparison.OrdinalIgnoreCase))
            {
                string cleanUri = trimmed.Substring(5); // Remove o 'jdbc:'
                if (string.IsNullOrWhiteSpace(resolvedDbName))
                {
                    var match = Regex.Match(cleanUri, @"mongodb://[^/]+/([^?/\s]+)");
                    if (match.Success)
                    {
                        resolvedDbName = match.Groups[1].Value;
                    }
                }
                return cleanUri;
            }

            // 3. Caso contrário, trata como Nome ou ID de Connection Profile do TDM Portal
            string effectiveBaseUrl = ResolveTdmBaseUrl(customTdmUrl);
            GlobalSettings.MessageLogger.LogMessage(
                "[TdmProfileResolver] Tentando resolver Connection Profile '{0}' na API do TDM Portal ({1})...", trimmed, effectiveBaseUrl);

            var profile = ResolveProfile(
                trimmed, 
                tdmUrl: effectiveBaseUrl, 
                tdmToken: customToken, 
                tdmUser: customUser, 
                tdmPass: customPass,
                enableDebugLogs: enableDebugLogs);

            if (profile != null)
            {
                if (!string.IsNullOrWhiteSpace(profile.DatabaseName))
                {
                    resolvedDbName = profile.DatabaseName;
                }

                if (!string.IsNullOrWhiteSpace(profile.ConnectionUrl))
                {
                    string rawUrl = profile.ConnectionUrl.Trim();
                    if (rawUrl.StartsWith("jdbc:mongodb://", StringComparison.OrdinalIgnoreCase))
                    {
                        rawUrl = rawUrl.Substring(5);
                    }

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
                    authPart = $"{Uri.EscapeDataString(profile.Username)}:{Uri.EscapeDataString(pass)}@";
                }

                string db = !string.IsNullOrWhiteSpace(resolvedDbName) ? resolvedDbName : "admin";
                return $"mongodb://{authPart}{host}:{port}/{db}?authSource=admin";
            }

            // Se falhou ao resolver o perfil e não é uma URI válida do MongoDB, emite erro claro
            throw new ArgumentException(
                $"Não foi possível resolver o Connection Profile '{trimmed}' no TDM Portal ({effectiveBaseUrl}). " +
                $"Certifique-se de que o perfil com este nome exato existe no TDM Portal, que a URL do Portal está acessível ou informe uma URI MongoDB completa (ex: mongodb://localhost:27017).");
        }
    }
}
