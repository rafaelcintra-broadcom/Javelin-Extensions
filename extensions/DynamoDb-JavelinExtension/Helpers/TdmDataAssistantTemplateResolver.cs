using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using GridTools.Javelin.Common;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.DynamoDbActivities.Helpers
{
    public class TdmTagRuleDefinition
    {
        public string Tag { get; set; }
        public string Name { get; set; }
        public Regex Pattern { get; set; }
        public string DataPainterExpr { get; set; }
        public string FdmFunction { get; set; }
        public string Category { get; set; }
        public string RiskLevel { get; set; }
    }

    /// <summary>
    /// Consulta os templates de tags e regras do Data Assistant diretamente via API REST do TDM Portal.
    /// Ordem de Prioridade:
    /// 1. Endpoint Oficial de Templates do Data Assistant (/data-assistant/templates/{templateName})
    /// 2. Árvore de Classificadores / Tags do Profiler (/profiler/classifiers/containers/...)
    /// 3. Classificadores e Definições de Tags (/profiler/classifiers e /tagDefinitions)
    /// </summary>
    public static class TdmDataAssistantTemplateResolver
    {
        private static readonly ConcurrentDictionary<string, List<TdmTagRuleDefinition>> TemplateCache =
            new ConcurrentDictionary<string, List<TdmTagRuleDefinition>>(StringComparer.OrdinalIgnoreCase);

        private static readonly HttpClient HttpClient;

        static TdmDataAssistantTemplateResolver()
        {
#if !NETFRAMEWORK
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            HttpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
#else
            HttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
#endif
        }

        /// <summary>
        /// Busca as regras do template de TAG informado pelo nome diretamente na API do TDM Portal.
        /// Prioriza os endpoints do Data Assistant antes de consultar outros serviços do Profiler.
        /// Se o template não existir ou não contiver regras, lança exceção para impedir vazamento de dados.
        /// </summary>
        public static List<TdmTagRuleDefinition> FetchTemplateRules(
            string templateName,
            string tdmUrl = null,
            string tdmToken = null,
            string tdmUser = null,
            string tdmPass = null,
            long? projectId = null,
            bool enableDebugLogs = false)
        {
            if (string.IsNullOrWhiteSpace(templateName))
            {
                throw new InvalidOperationException(
                    "SEGURANÇA TDM: O nome do Tag Template do Data Assistant (DataAssistantTagTemplate) é obrigatório e não foi informado. " +
                    "A execução foi bloqueada para garantir que nenhum dado não mascarado seja carregado.");
            }

            string cleanTemplateName = templateName.Trim();

            // 1. Checa cache em memória
            if (TemplateCache.TryGetValue(cleanTemplateName, out var cachedRules) && cachedRules.Count > 0)
            {
                return cachedRules;
            }

            string baseUrl = TdmPortalConnectionProfileResolver.ResolveTdmBaseUrl(tdmUrl);
            string authHeader = TdmPortalConnectionProfileResolver.ResolveTdmAuthHeader(
                baseUrl, tdmToken, tdmUser, tdmPass, enableDebugLogs);

            GlobalSettings.MessageLogger.LogMessage(
                "[DataAssistant] Buscando Tag Template '{0}' na API do Data Assistant do TDM Portal ({1})...",
                cleanTemplateName, baseUrl);

            // 2. LISTA DE ENDPOINTS EM ORDEM ESTRITA DE PRIORIDADE:
            // ➔ A 1ª OPÇÃO É A API DIRETA DE TEMPLATES DO DATA ASSISTANT (TDMGeneratorService)
            var endpoints = new List<string>
            {
                // PRIORIDADE 1: Endpoints oficiais do TDMGeneratorService (onde residem os Templates de Data Assistant)
                $"{baseUrl}/TDMGeneratorService/api/ca/v1/data-assistant/template",
                $"{baseUrl}/TDMGeneratorService/api/ca/v1/data-assistant/templates",
                $"{baseUrl}/TDMGeneratorService/api/ca/v1/data-assistant/template/{Uri.EscapeDataString(cleanTemplateName)}",
                $"{baseUrl}/TDMGeneratorService/api/ca/v1/data-assistant/templates/{Uri.EscapeDataString(cleanTemplateName)}",

                // PRIORIDADE 2: Model Service / Project Data Assistant Templates
                projectId.HasValue && projectId.Value > 0
                    ? $"{baseUrl}/TDMModelService/api/ca/v1/projects/{projectId.Value}/data-assistant/templates/{Uri.EscapeDataString(cleanTemplateName)}"
                    : $"{baseUrl}/TDMModelService/api/ca/v1/data-assistant/templates/{Uri.EscapeDataString(cleanTemplateName)}",

                projectId.HasValue && projectId.Value > 0
                    ? $"{baseUrl}/TDMModelService/api/ca/v1/projects/{projectId.Value}/data-assistant/templates"
                    : $"{baseUrl}/TDMModelService/api/ca/v1/data-assistant/templates",

                $"{baseUrl}/TestDataManager/api/ca/v1/data-assistant/templates/{Uri.EscapeDataString(cleanTemplateName)}",
                $"{baseUrl}/TestDataManager/api/ca/v1/data-assistant/templates",

                // PRIORIDADE 3: Árvore hierárquica do Profiler / Tag Containers (Fallback Global)
                projectId.HasValue && projectId.Value > 0
                    ? $"{baseUrl}/TDMModelService/api/ca/v1/profiler/classifiers/containers/0/project/{projectId.Value}"
                    : $"{baseUrl}/TDMModelService/api/ca/v1/profiler/classifiers/containers/0?recursive=1&scope=global",

                // PRIORIDADE 4: Classificadores diretos do Profiler e Definições de Tag
                $"{baseUrl}/TDMModelService/api/ca/v1/profiler/classifiers",
                $"{baseUrl}/TDMModelService/api/ca/v1/tagDefinitions"
            };

            List<TdmTagRuleDefinition> resolvedRules = new List<TdmTagRuleDefinition>();

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
                        continue;
                    }

                    var parsed = ParseClassifierRulesFromJson(json, cleanTemplateName);
                    if (parsed.Count > 0)
                    {
                        resolvedRules.AddRange(parsed);
                        GlobalSettings.MessageLogger.LogMessage(
                            "[DataAssistant] Tag Template '{0}' encontrado via {1}! ({2} regras carregadas)",
                            cleanTemplateName, endpoint, parsed.Count);
                        TemplateCache[cleanTemplateName] = resolvedRules;
                        return resolvedRules;
                    }
                }
                catch (Exception ex)
                {
                    if (enableDebugLogs)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[DataAssistant][DEBUG] Tentativa no endpoint {0} falhou: {1}", endpoint, ex.Message);
                    }
                }
            }

            // 3. Fallback canônico oficial para os templates de convenção padrão Broadcom
            // quando a conexão com o TDMModelService estiver em modo offline/desconectado
            if (resolvedRules.Count == 0 && (
                cleanTemplateName.Equals("MONGODB_PII_TAG_TEMPLATE", StringComparison.OrdinalIgnoreCase) ||
                cleanTemplateName.Equals("PII_DEFAULT", StringComparison.OrdinalIgnoreCase) ||
                cleanTemplateName.Equals("LGPD_DEFAULT", StringComparison.OrdinalIgnoreCase) ||
                cleanTemplateName.Equals("DATA_ASSISTANT_PII", StringComparison.OrdinalIgnoreCase)))
            {
                resolvedRules = GetCanonicalTdmRules();
                GlobalSettings.MessageLogger.LogMessage(
                    "[DataAssistant] Template canônico '{0}' carregado ({1} regras ativas).",
                    cleanTemplateName, resolvedRules.Count);
            }

            // 4. Barreira de Segurança Rigorosa: Se nenhuma regra for encontrada para o template especificado, aborta a execução!
            if (resolvedRules.Count == 0)
            {
                throw new InvalidOperationException(
                    $"SEGURANÇA TDM: O Tag Template '{cleanTemplateName}' não foi encontrado na API do Data Assistant do TDM Portal ou não possui regras de mascaramento ativas. " +
                    "A execução foi cancelada para impedir o vazamento de dados confidenciais.");
            }

            TemplateCache[cleanTemplateName] = resolvedRules;
            GlobalSettings.MessageLogger.LogMessage(
                "[DataAssistant] Tag Template '{0}' validado com sucesso! ({1} regras de mascaramento ativas)",
                cleanTemplateName, resolvedRules.Count);

            return resolvedRules;
        }

        private static List<TdmTagRuleDefinition> ParseClassifierRulesFromJson(string json, string templateName)
        {
            var list = new List<TdmTagRuleDefinition>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try
            {
                var token = JToken.Parse(json);
                var items = new List<JToken>();

                void CollectTokens(JToken t)
                {
                    if (t is JObject obj)
                    {
                        // Se for um objeto de Template do Data Assistant (ex: { "templateName": "...", "ruleList": [...] })
                        if (obj["ruleList"] != null && obj["ruleList"] is JArray rArr)
                        {
                            string tName = obj.Value<string>("ruleName") ?? obj.Value<string>("templateName") ?? obj.Value<string>("name");
                            if (string.IsNullOrWhiteSpace(tName) || tName.Equals(templateName, StringComparison.OrdinalIgnoreCase) || templateName.Contains(tName))
                            {
                                foreach (var r in rArr) items.Add(r);
                                return;
                            }
                        }

                        if (obj["classifier_id"] != null || obj["classifierId"] != null || obj["tagName"] != null || obj["tag"] != null)
                        {
                            items.Add(obj);
                        }

                        foreach (var prop in obj.Properties())
                        {
                            if (prop.Name != "ruleList")
                            {
                                CollectTokens(prop.Value);
                            }
                        }
                    }
                    else if (t is JArray arr)
                    {
                        foreach (var elem in arr)
                        {
                            CollectTokens(elem);
                        }
                    }
                }

                CollectTokens(token);

                foreach (var item in items)
                {
                    string tagName = item.Value<string>("columnTypeDesc") ?? item.Value<string>("dataType") ?? item.Value<string>("columnType") ?? item.Value<string>("tagName") ?? item.Value<string>("tag") ?? item.Value<string>("name") ?? "PII";
                    string desc = item.Value<string>("description");
                    string dataPainterExpr = item.Value<string>("rule") ?? item.Value<string>("defaultValue") ?? item.Value<string>("defaultDataPainterExpr") ?? item.Value<string>("dataPainterExpr");
                    string pattern = item.Value<string>("regexPattern") ?? item.Value<string>("pattern");
                    string configName = item.Value<string>("columnType") ?? item.Value<string>("configName") ?? tagName;

                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        string lower = (tagName + " " + configName).ToLowerInvariant();
                        if (lower.Contains("first name") || lower.Equals("name") || lower.Contains("first_name"))
                            pattern = @"(?i).*(first.*name|primeiro.*nome|prenome|^name$|^nome$).*";
                        else if (lower.Contains("last name") || lower.Contains("last_name") || lower.Contains("surname"))
                            pattern = @"(?i).*(last.*name|sobrenome|surname|ultimo.*nome).*";
                        else if (lower.Contains("city") || lower.Contains("cidade"))
                            pattern = @"(?i).*(city|cidade|municipio).*";
                        else if (lower.Contains("state") || lower.Contains("estado") || lower.Contains("uf"))
                            pattern = @"(?i).*(state|estado|^uf$).*";
                        else if (lower.Contains("cep") || lower.Contains("zip"))
                            pattern = @"(?i).*(cep|zip|postal).*";
                        else if (lower.Contains("address") || lower.Contains("endereco") || lower.Contains("rua"))
                            pattern = @"(?i).*(address|endereco|logradouro|rua|street).*";
                        else if (lower.Contains("cpf"))
                            pattern = @"(?i).*(cpf|num.*cpf|cpf.*cli|cpfcnpj).*";
                        else if (lower.Contains("cnpj"))
                            pattern = @"(?i).*(cnpj|num.*cnpj|cgc).*";
                        else if (lower.Contains("email"))
                            pattern = @"(?i).*(email|e_mail|correio_e).*";
                        else if (lower.Contains("phone") || lower.Contains("cel") || lower.Contains("tel"))
                            pattern = @"(?i).*(phone|telefone|celular|mobile).*";
                        else if (lower.Contains("age") || lower.Contains("idade"))
                            pattern = @"(?i).*(^age$|^idade$).*";
                        else
                            pattern = $"(?i).*{Regex.Escape(configName)}.*";
                    }

                    if (string.IsNullOrWhiteSpace(dataPainterExpr))
                    {
                        string lower = (tagName + " " + configName).ToLowerInvariant();
                        if (lower.Contains("cpf")) dataPainterExpr = "@seedlist(BRAZIL_VALID_CPF)@";
                        else if (lower.Contains("cnpj")) dataPainterExpr = "@seedlist(BRAZIL_VALID_CNPJ)@";
                        else if (lower.Contains("email")) dataPainterExpr = "@seedlist(First Name)@ + '.' + @seedlist(Last Name)@ + '@example.com'";
                        else if (lower.Contains("phone") || lower.Contains("cel")) dataPainterExpr = "'(' + @randval(11,99)@ + ') 9' + @randdigits(8)@";
                        else if (lower.Contains("card") || lower.Contains("cartao")) dataPainterExpr = "@randdigits(16)@";
                        else if (lower.Contains("name") || lower.Contains("nome")) dataPainterExpr = "@seedlist(FULL_NAME_PT_BR)@";
                        else dataPainterExpr = "@hash()";
                    }

                    list.Add(new TdmTagRuleDefinition
                    {
                        Tag = tagName,
                        Name = configName,
                        Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                        DataPainterExpr = dataPainterExpr
                    });
                }
            }
            catch { }

            return list;
        }

        private static List<TdmTagRuleDefinition> GetCanonicalTdmRules()
        {
            return new List<TdmTagRuleDefinition>
            {
                new TdmTagRuleDefinition { Tag = "CPF", Pattern = new Regex(@"(?i).*(CPF|NUM.*CPF|CPF.*CLI|CPF.*CONJ|CPFCNPJ).*", RegexOptions.Compiled), DataPainterExpr = "@seedlist(BRAZIL_VALID_CPF)@" },
                new TdmTagRuleDefinition { Tag = "CNPJ", Pattern = new Regex(@"(?i).*(CNPJ|NUM.*CNPJ|CGC).*", RegexOptions.Compiled), DataPainterExpr = "@seedlist(BRAZIL_VALID_CNPJ)@" },
                new TdmTagRuleDefinition { Tag = "RG", Pattern = new Regex(@"(?i).*(RG|IDENTIDADE|DOC.*IDENT).*", RegexOptions.Compiled), DataPainterExpr = "@randdigits(8)@ + '-' + @randval(0,9)@" },
                new TdmTagRuleDefinition { Tag = "FIRST_NAME", Pattern = new Regex(@"(?i).*(FIRST.*NAME|PRIMEIRO.*NOME|PRENOME).*", RegexOptions.Compiled), DataPainterExpr = "@seedlist(First Name)@" },
                new TdmTagRuleDefinition { Tag = "LAST_NAME", Pattern = new Regex(@"(?i).*(LAST.*NAME|SURNAME|SOBRENOME|ULTIMO.*NOME).*", RegexOptions.Compiled), DataPainterExpr = "@seedlist(Last Name)@" },
                new TdmTagRuleDefinition { Tag = "FULL_NAME", Pattern = new Regex(@"(?i).*(NAME|NOME|CLIENTE|SACADO|RAZAO.*SOC|BENEFICIARIO|NOMEMPR|NOMBANC).*", RegexOptions.Compiled), DataPainterExpr = "@seedlist(FULL_NAME_PT_BR)@" },
                new TdmTagRuleDefinition { Tag = "EMAIL", Pattern = new Regex(@"(?i).*(EMAIL|E_MAIL|CORREIO_E).*", RegexOptions.Compiled), DataPainterExpr = "@seedlist(First Name)@ + '.' + @seedlist(Last Name)@ + '@example.com'" },
                new TdmTagRuleDefinition { Tag = "PHONE_CELL", Pattern = new Regex(@"(?i).*(CELULAR|CELL|MOBILE|TEL.*CEL).*", RegexOptions.Compiled), DataPainterExpr = "'(' + @randval(11,99)@ + ') 9' + @randdigits(8)@" },
                new TdmTagRuleDefinition { Tag = "PHONE_LAND", Pattern = new Regex(@"(?i).*(PHONE|TELEFONE|TEL(?!ER)|FIXO|FAX).*", RegexOptions.Compiled), DataPainterExpr = "'(' + @randval(11,99)@ + ') ' + @randdigits(8)@" },
                new TdmTagRuleDefinition { Tag = "ZIP_CEP", Pattern = new Regex(@"(?i).*(CEP|POSTAL|ZIP).*", RegexOptions.Compiled), DataPainterExpr = "@randdigits(5)@ + '-' + @randdigits(3)@" },
                new TdmTagRuleDefinition { Tag = "STREET", Pattern = new Regex(@"(?i).*(LOGRADOURO|STREET|RUA|ENDERECO|AVENIDA).*", RegexOptions.Compiled), DataPainterExpr = "@seedlist(FULL_ADDRESS_PT_BR)@" },
                new TdmTagRuleDefinition { Tag = "ADDRESS_NUM", Pattern = new Regex(@"(?i).*(NUMERO|NUM_END|NRO_END).*", RegexOptions.Compiled), DataPainterExpr = "@randrange(1, 9999)@" },
                new TdmTagRuleDefinition { Tag = "DOB", Pattern = new Regex(@"(?i).*(DOB|BIRTH|BRTH|NASC|DATA.*NASC).*", RegexOptions.Compiled), DataPainterExpr = "@dob(18, 70)@" },
                new TdmTagRuleDefinition { Tag = "BANK_AGENCY", Pattern = new Regex(@"(?i).*(AGENCIA|AGCY|BRANCH|AGECOBR|AGENCIA_BANCARIA|BANK_AGENCY).*", RegexOptions.Compiled), DataPainterExpr = "@randdigits(4)@" },
                new TdmTagRuleDefinition { Tag = "BANK_CODE", Pattern = new Regex(@"(?i).*(NUMBANC|BCOCOBR|CODBANC|CODBCO).*", RegexOptions.Compiled), DataPainterExpr = "@randlov(\"237\",\"001\",\"033\",\"341\",\"104\")@" },
                new TdmTagRuleDefinition { Tag = "BALANCE_MONEY", Pattern = new Regex(@"(?i).*(BALANCE|SALDO|VALOR|AMOUNT|PRECO|TOTAL|LIMITE|VLR|VLRTITU|VLRDESP|VLROUTR|VLRJURO|VLRIOF|VLRABAT|VLRDESC|VLRPAGO|VLRMORA|VLRCRED|VLRTOTA|VALOOC|VALLIQ|TOTRATEIO).*", RegexOptions.Compiled), DataPainterExpr = "@randrange(100.0, 50000.0, 2)@" },
                new TdmTagRuleDefinition { Tag = "BANK_ACCOUNT", Pattern = new Regex(@"(?i).*(^CONTA$|NUM_CTA|NOSSONR|NUMCONT|NUM_CONTA|BANK_ACCOUNT|^ACCT$|^ACCOUNT$).*", RegexOptions.Compiled), DataPainterExpr = "@randdigits(7)@" },
                new TdmTagRuleDefinition { Tag = "STATUS_FLAG", Pattern = new Regex(@"(?i).*(FL_|_FL$|STATUS|FLAG|SITUACAO|ATIVO|IND_|RATEIOC|INDCART|INDOCOR).*", RegexOptions.Compiled), DataPainterExpr = "@randlov(\"ATIVO\",\"INATIVO\",\"PENDENTE\",\"BLOQUEADO\")@" },
                new TdmTagRuleDefinition { Tag = "CARD_NUMBER", Pattern = new Regex(@"(?i).*(CARTAO|CARD|NUM.*CART|CARD_NUMBER|PAN).*", RegexOptions.Compiled), DataPainterExpr = "@randdigits(16)@" },
                new TdmTagRuleDefinition { Tag = "CARD_CVV", Pattern = new Regex(@"(?i).*(CVV|CVC|SECURITY_CODE|COD_SEG).*", RegexOptions.Compiled), DataPainterExpr = "@randdigits(3)@" }
            };
        }
    }
}
