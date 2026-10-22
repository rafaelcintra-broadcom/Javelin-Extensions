using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using GridTools.Javelin.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.NoSqlFileActivities.Helpers
{
    public enum GeneratorDataMode
    {
        /// <summary>
        /// Fórmulas generativas autônomas do Data Painter (@seedlist, @dob, @randrange, etc.)
        /// </summary>
        Formulas,

        /// <summary>
        /// Valores literais já mascarados da extração atual (replay exato de dados mascarados)
        /// </summary>
        LiteralMaskedValues
    }

    /// <summary>
    /// Registra e publica Data Generators do tipo NoSQL (Cassandra / CQL) no TDM Portal
    /// via API REST do TDMGeneratorService.
    /// </summary>
    public static class TdmNoSqlGeneratorManager
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// Cria e publica um NoSQL Data Generator no TDM Portal baseado na estrutura dos registros e regras de mascaramento.
        /// </summary>
        public static long CreateNoSqlGenerator(
            string baseUrl,
            string authHeader,
            long projectId,
            long versionId,
            string generatorName,
            string tableName,
            List<TdmTagRuleDefinition> rules,
            JObject sampleRow,
            JObject maskedRow = null,
            GeneratorDataMode dataMode = GeneratorDataMode.Formulas,
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
                : $"GEN_NOSQLFILE_{tableName}_{DateTime.Now:yyyyMMdd_HHmmss}";

            GlobalSettings.MessageLogger.LogMessage(
                "[TdmGenerator] Criando NoSQL Data Generator '{0}' no TDM Portal (Modo: {1}, Projeto: {2}, Versão: {3})...",
                genName, dataMode, projectId, versionId);

            var columnDefinitions = new List<object>();

            var refDoc = (dataMode == GeneratorDataMode.LiteralMaskedValues && maskedRow != null)
                ? maskedRow
                : sampleRow;

            if (refDoc != null)
            {
                foreach (var prop in refDoc.Properties())
                {
                    string fieldName = prop.Name;
                    string formula = "";

                    if (dataMode == GeneratorDataMode.LiteralMaskedValues)
                    {
                        var jv = prop.Value as JValue;
                        if (jv != null && jv.Type == JTokenType.String)
                        {
                            formula = $"'{jv.Value?.ToString().Replace("'", "''")}'";
                        }
                        else if (jv != null && jv.Type == JTokenType.Boolean)
                        {
                            formula = ((bool)jv.Value) ? "'true'" : "'false'";
                        }
                        else
                        {
                            formula = prop.Value?.ToString() ?? "''";
                        }
                    }
                    else
                    {
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

                        if (string.IsNullOrWhiteSpace(formula))
                        {
                            string lower = fieldName.ToLowerInvariant();
                            if (lower.Contains("id") || lower.Contains("uuid") || lower.Contains("guid"))
                            {
                                formula = "@randguid()@";
                            }
                            else if (prop.Value.Type == JTokenType.Integer)
                            {
                                formula = "@randrange(1, 1000)@";
                            }
                            else if (prop.Value.Type == JTokenType.Float)
                            {
                                formula = "@randrange(10.0, 500.0, 2)@";
                            }
                            else if (prop.Value.Type == JTokenType.Boolean)
                            {
                                formula = "@randval('true', 'false')@";
                            }
                            else if (prop.Value.Type == JTokenType.Date)
                            {
                                formula = "@dob(18, 65)@";
                            }
                            else
                            {
                                formula = $"'{prop.Value.ToString().Replace("'", "''")}'";
                            }
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

            var generatorPayload = new
            {
                name = genName,
                description = $"Auto-generated NoSQL Cassandra Generator for table '{tableName}' (Mode: {dataMode}) via Javelin Pipeline",
                projectId = projectId,
                versionId = versionId,
                type = "NOSQL",
                dbType = "FILE",
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
                            "[TdmGenerator] ✅ NoSQL Data Generator '{0}' criado com sucesso no TDM! (GeneratorID: {1}, Modo: {2})",
                            genName, generatorId, dataMode);

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
