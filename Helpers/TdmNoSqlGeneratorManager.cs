using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using GridTools.Javelin.Common;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.MongoDbActivities.Helpers
{
    /// <summary>
    /// Registra e publica Data Generators do tipo NoSQL (MongoDB) no TDM Portal
    /// via API REST do TDMGeneratorService.
    /// </summary>
    public static class TdmNoSqlGeneratorManager
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// Cria e publica um NoSQL Data Generator no TDM Portal baseado na estrutura dos documentos e regras de mascaramento.
        /// </summary>
        public static long CreateNoSqlGenerator(
            string baseUrl,
            string authHeader,
            long projectId,
            long versionId,
            string generatorName,
            string collectionName,
            List<TdmTagRuleDefinition> rules,
            BsonDocument sampleDoc,
            bool enableDebugLogs = false)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(authHeader))
            {
                GlobalSettings.MessageLogger.LogMessage("[TdmGenerator] Não foi possível criar o Generator: URL do TDM ou credenciais ausentes.");
                return 0;
            }

            string cleanBaseUrl = baseUrl.TrimEnd('/');
            string genName = !string.IsNullOrWhiteSpace(generatorName)
                ? generatorName.Trim()
                : $"GEN_NOSQL_{collectionName}_{DateTime.Now:yyyyMMdd_HHmmss}";

            GlobalSettings.MessageLogger.LogMessage(
                "[TdmGenerator] Criando NoSQL Data Generator '{0}' no TDM Portal (Projeto: {1}, Versão: {2})...",
                genName, projectId, versionId);

            // 1. Monta as definições de colunas e regras do gerador
            var columnDefinitions = new List<object>();

            if (sampleDoc != null)
            {
                foreach (var elem in sampleDoc.Elements)
                {
                    string fieldName = elem.Name;
                    string formula = "";

                    // Procura se tem regra mapeada para este campo
                    if (rules != null)
                    {
                        foreach (var r in rules)
                        {
                            if (r.Pattern != null && r.Pattern.IsMatch(fieldName))
                            {
                                formula = r.DataPainterExpr ?? "";
                                break;
                            }
                        }
                    }

                    // Se não tiver fórmula de PII, define geração padrão com base no tipo BSON
                    if (string.IsNullOrWhiteSpace(formula))
                    {
                        if (fieldName == "_id")
                        {
                            formula = "@formatencrypt(@randdigits(24)@)@";
                        }
                        else if (elem.Value.IsInt32 || elem.Value.IsInt64)
                        {
                            formula = "@randrange(1, 1000)@";
                        }
                        else if (elem.Value.IsDouble)
                        {
                            formula = "@randrange(10.0, 500.0, 2)@";
                        }
                        else if (elem.Value.IsBoolean)
                        {
                            formula = "@randval('true', 'false')@";
                        }
                        else if (elem.Value.IsBsonDateTime)
                        {
                            formula = "@dob(18, 65)@";
                        }
                        else
                        {
                            formula = $"'{elem.Value.ToString()}'";
                        }
                    }

                    columnDefinitions.Add(new
                    {
                        columnName = fieldName,
                        columnValue = formula,
                        excluded = false
                    });
                }
            }

            // 2. Chamada POST /TDMGeneratorService/api/ca/v1/generators
            var generatorPayload = new
            {
                name = genName,
                description = $"Auto-generated NoSQL MongoDB Generator for collection '{collectionName}' via Javelin Pipeline",
                projectId = projectId,
                versionId = versionId,
                type = "NOSQL",
                dbType = "MONGODB",
                onDemand = "N"
            };

            string endpoint = $"{cleanBaseUrl}/TDMGeneratorService/api/ca/v1/generators?projectId={projectId}&versionId={versionId}";

            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    req.Headers.TryAddWithoutValidation("Authorization", authHeader);
                    req.Content = new StringContent(JsonConvert.SerializeObject(generatorPayload), Encoding.UTF8, "application/json");

                    var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
                    string respJson = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!resp.IsSuccessStatusCode)
                    {
                        if (enableDebugLogs)
                        {
                            GlobalSettings.MessageLogger.LogMessage(
                                "[TdmGenerator][DEBUG] Falha ao criar Generator em {0} (HTTP {1}): {2}",
                                endpoint, (int)resp.StatusCode, respJson);
                        }
                        return 0;
                    }

                    var token = JToken.Parse(respJson);
                    long generatorId = token.Value<long?>("generatorId") ?? token.Value<long?>("id") ?? 0;

                    if (generatorId > 0)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmGenerator] ✅ NoSQL Data Generator '{0}' criado com sucesso no TDM! (GeneratorID: {1})",
                            genName, generatorId);

                        // 3. Cadastra as definições de dados (colunas/fórmulas) no gerador
                        if (columnDefinitions.Count > 0)
                        {
                            RegisterDataDefinitions(cleanBaseUrl, authHeader, projectId, versionId, generatorId, columnDefinitions, enableDebugLogs);
                        }

                        return generatorId;
                    }
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                {
                    GlobalSettings.MessageLogger.LogMessage("[TdmGenerator][DEBUG] Exceção ao criar NoSQL Generator: {0}", ex.Message);
                }
            }

            return 0;
        }

        private static void RegisterDataDefinitions(
            string baseUrl,
            string authHeader,
            long projectId,
            long versionId,
            long generatorId,
            List<object> columnDefinitions,
            bool enableDebugLogs)
        {
            var defPayload = new
            {
                rowNumber = 1,
                definitions = columnDefinitions
            };

            // Tenta adicionar linhas de definição de dados no Generator
            var endpoints = new[]
            {
                $"{baseUrl}/TDMGeneratorService/api/ca/v1/generators/{generatorId}/tables/0/definitionRows?projectId={projectId}&versionId={versionId}",
                $"{baseUrl}/TDMGeneratorService/api/ca/v1/generators/{generatorId}/tables/1/definitionRows?projectId={projectId}&versionId={versionId}"
            };

            foreach (var ep in endpoints)
            {
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Post, ep))
                    {
                        req.Headers.TryAddWithoutValidation("Authorization", authHeader);
                        req.Content = new StringContent(JsonConvert.SerializeObject(defPayload), Encoding.UTF8, "application/json");

                        var resp = HttpClient.SendAsync(req).GetAwaiter().GetResult();
                        if (resp.IsSuccessStatusCode)
                        {
                            GlobalSettings.MessageLogger.LogMessage(
                                "[TdmGenerator] ✅ {0} regras de geração e mascaramento persistidas no Generator {1} do TDM Portal.",
                                columnDefinitions.Count, generatorId);
                            return;
                        }
                    }
                }
                catch { }
            }
        }
    }
}
