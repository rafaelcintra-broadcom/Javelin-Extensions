using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using GridTools.Javelin.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.MongoDbActivities.Helpers
{
    /// <summary>
    /// Cliente para o motor oficial de avaliação do TDM (PublishEngine / MetaFunctions)
    /// via ContextGenerators (TDoD API), permitindo avaliar fórmulas do Data Painter
    /// em memória com 100% de paridade, sem criar objetos ou geradores permanentes.
    /// </summary>
    public class TdmGeneratorContextClient : IDisposable
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public string BaseUrl { get; }
        public string AuthHeader { get; }
        public int ContextId { get; private set; }
        public bool IsActive => ContextId > 0;
        public bool EnableDebugLogs { get; }

        public TdmGeneratorContextClient(string baseUrl, string authHeaderOrToken, bool enableDebugLogs = false)
        {
            BaseUrl = (baseUrl ?? "").TrimEnd('/');
            string token = (authHeaderOrToken ?? "").Trim();
            if (!string.IsNullOrEmpty(token) && !token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = "Bearer " + token;
            }
            AuthHeader = token;
            EnableDebugLogs = enableDebugLogs;
        }

        /// <summary>
        /// Inicializa um contexto volátil em memória no TDM usando IDs dummy (0, 0, 0),
        /// dispensando a criação de projetos, versões ou geradores no repositório.
        /// </summary>
        public bool InitializeDummyContext(int projectId = 0, int versionId = 0, int levelId = 0)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(AuthHeader))
            {
                return false;
            }

            var endpoints = new[]
            {
                $"{BaseUrl}/TestDataManager/api/ca/v1/tdod/contextGenerators",
                $"{BaseUrl}/TDMService/api/contextGenerators"
            };

            var payload = new
            {
                projectID = projectId,
                versionID = versionId,
                levelID = levelId
            };

            string jsonBody = JsonConvert.SerializeObject(payload);

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint))
                    {
                        req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);
                        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                        var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
                        string respText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                        if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(respText))
                        {
                            var token = JToken.Parse(respText);
                            int ctxId = token.Value<int?>("contextID") ?? token.Value<int?>("contextId") ?? 0;
                            if (ctxId > 0)
                            {
                                ContextId = ctxId;
                                GlobalSettings.MessageLogger.LogMessage(
                                    "[TdmGeneratorEngine] ✅ Contexto oficial do TDM alocado com sucesso em memória! (ContextID: {0}, ProjectID: {1})",
                                    ContextId, projectId);
                                return true;
                            }
                        }
                        else if (EnableDebugLogs)
                        {
                            GlobalSettings.MessageLogger.LogMessage(
                                "[TdmGeneratorEngine][DEBUG] Falha ao alocar contexto em {0} (HTTP {1}): {2}",
                                endpoint, (int)resp.StatusCode, respText);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (EnableDebugLogs)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmGeneratorEngine][DEBUG] Erro ao contactar {0}: {1}", endpoint, ex.Message);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Resolve um lote de expressões do Data Painter no motor oficial do TDM em uma única requisição.
        /// </summary>
        public List<string> ResolveMultipleExpressions(List<string> expressions)
        {
            if (!IsActive || expressions == null || expressions.Count == 0)
                return null;

            var endpoints = new[]
            {
                $"{BaseUrl}/TestDataManager/api/ca/v1/tdod/contextGenerators/{ContextId}/resolveMultipleExpressionsWithoutDependencies",
                $"{BaseUrl}/TDMService/api/contextGenerators/{ContextId}/resolveMultipleExpressionsWithoutDependencies"
            };

            var map = new Dictionary<string, string>();
            for (int i = 0; i < expressions.Count; i++)
            {
                map[i.ToString()] = expressions[i];
            }

            string jsonBody = JsonConvert.SerializeObject(map);

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint))
                    {
                        req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);
                        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                        var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
                        string respText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                        if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(respText))
                        {
                            var token = JToken.Parse(respText);
                            if (token is JArray arr)
                            {
                                var resultList = new List<string>();
                                foreach (var item in arr)
                                {
                                    resultList.Add(item.ToString());
                                }
                                return resultList;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (EnableDebugLogs)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmGeneratorEngine][DEBUG] Erro ao resolver expressões em lote: {0}", ex.Message);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Libera os recursos alocados na memória do TDM ao término do processo.
        /// </summary>
        public void Close()
        {
            if (ContextId <= 0) return;

            int oldCtx = ContextId;
            ContextId = 0;

            var endpoints = new[]
            {
                $"{BaseUrl}/TestDataManager/api/ca/v1/tdod/contextGenerators/{oldCtx}",
                $"{BaseUrl}/TDMService/api/contextGenerators/{oldCtx}"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Delete, endpoint))
                    {
                        req.Headers.TryAddWithoutValidation("Authorization", AuthHeader);
                        HttpClient.SendAsync(req).GetAwaiter().GetResult();
                    }
                }
                catch { }
            }

            GlobalSettings.MessageLogger.LogMessage(
                "[TdmGeneratorEngine] Contexto TDM {0} liberado da memória.", oldCtx);
        }

        public void Dispose()
        {
            Close();
        }
    }
}
