using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.BigQuery.V2;
using GridTools.Javelin.BigQueryActivities.Helpers;
using GridTools.Javelin.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.BigQueryActivities
{
    [DisplayName("BigQuery Prod To Non-Prod Masking Pipeline")]
    [Description("Executa pipeline in-flight de extração, mascaramento via Tag Template TDM e carga do Google BigQuery de Produção para Não-Produção com Cofre Seguro.")]
    public class BigQueryProdToNonProdPipelineActivity : CodeActivity
    {
        [Category("1. Source BigQuery (PROD)")]
        [RequiredArgument]
        [DisplayName("Source Project ID")]
        [Description("Google Cloud Project ID de Origem (ou nome do TDM Connection Profile).")]
        public InArgument<string> SourceProjectId { get; set; }

        [Category("1. Source BigQuery (PROD)")]
        [DisplayName("Source Credentials JSON or Path")]
        [Description("Conteúdo JSON da Service Account Key ou caminho do arquivo .json de credenciais GCP de Origem.")]
        public InArgument<string> SourceCredentialsJson { get; set; }

        [Category("1. Source BigQuery (PROD)")]
        [RequiredArgument]
        [DisplayName("Source Dataset Name")]
        [Description("Nome do Dataset de Origem no BigQuery.")]
        public InArgument<string> SourceDatasetId { get; set; }

        [Category("1. Source BigQuery (PROD)")]
        [RequiredArgument]
        [DisplayName("Source Table Name")]
        [Description("Nome da Tabela de Origem no BigQuery.")]
        public InArgument<string> SourceTableId { get; set; }

        [Category("1. Source BigQuery (PROD)")]
        [DisplayName("Source WHERE Filter")]
        [Description("Filtro SQL opcional para extração incremental (ex: status = 'ACTIVE' AND created_at >= '2026-01-01').")]
        public InArgument<string> SourceWhereClause { get; set; }

        [Category("1. Source BigQuery (PROD)")]
        [DisplayName("Extraction Limit")]
        [Description("Limite máximo de registros a extrair.")]
        public InArgument<int?> Limit { get; set; }

        [Category("2. Target BigQuery (NON-PROD)")]
        [RequiredArgument]
        [DisplayName("Target Project ID")]
        [Description("Google Cloud Project ID de Destino (ou nome do TDM Connection Profile de Não-Produção).")]
        public InArgument<string> TargetProjectId { get; set; }

        [Category("2. Target BigQuery (NON-PROD)")]
        [DisplayName("Target Credentials JSON or Path")]
        [Description("Conteúdo JSON da Service Account Key ou caminho do arquivo .json de credenciais GCP de Destino (se omitido, usa Source).")]
        public InArgument<string> TargetCredentialsJson { get; set; }

        [Category("2. Target BigQuery (NON-PROD)")]
        [RequiredArgument]
        [DisplayName("Target Dataset Name")]
        [Description("Nome do Dataset de Destino no BigQuery.")]
        public InArgument<string> TargetDatasetId { get; set; }

        [Category("2. Target BigQuery (NON-PROD)")]
        [DisplayName("Target Table Name")]
        [Description("Nome da Tabela de Destino (se omitido, usa o mesmo nome da Origem).")]
        public InArgument<string> TargetTableId { get; set; }

        [Category("2. Target BigQuery (NON-PROD)")]
        [DisplayName("Truncate Target Table If Exists")]
        [Description("Se True, limpa a tabela de destino antes de carregar.")]
        public InArgument<bool> TruncateTableIfExists { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [RequiredArgument]
        [DisplayName("Data Assistant Tag Template")]
        [Description("Nome do Tag Template do Data Assistant no TDM Portal (ex: 'BIGQUERY_PII_TAG_TEMPLATE', 'PII_DEFAULT').")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("Project Identifier")]
        [Description("ID numérico ou nome do Projeto TDM.")]
        public InArgument<string> ProjectIdentifier { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("TDM Version ID")]
        public InArgument<long?> VersionIdentifier { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("Create TDM NoSQL Generator")]
        [Description("Se habilitado, registra e publica um NoSQL Data Generator permanente no TDM Portal com as regras e esquema mascarados")]
        [DefaultValue(false)]
        public InArgument<bool> CreateTdmNoSqlGenerator { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("Generator Generation Mode")]
        [Description("Formulas (fórmulas generativas autônomas do Data Painter) ou ReplayMaskedData (grava os valores literais mascarados da extração para replay rápido)")]
        [DefaultValue("Formulas")]
        public InArgument<string> GeneratorMode { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("TDM Generator Name")]
        [Description("Nome personalizado para o NoSQL Generator criado no TDM Portal (opcional)")]
        public InArgument<string> TdmGeneratorName { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("TDM Portal URL")]
        public InArgument<string> TdmPortalUrl { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("TDM Username")]
        public InArgument<string> TdmUsername { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("TDM Password")]
        public InArgument<string> TdmPassword { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("TDM Auth Token")]
        public InArgument<string> TdmAuthToken { get; set; }

        [Category("4. Performance & Safe Shredding")]
        [DisplayName("Batch Size")]
        public InArgument<int?> BatchSize { get; set; }

        [Category("4. Performance & Safe Shredding")]
        [DisplayName("Dry Run")]
        public InArgument<bool> DryRun { get; set; }

        [Category("4. Performance & Safe Shredding")]
        [DisplayName("Auto Shred Vault")]
        public InArgument<bool> AutoShredVault { get; set; }

        [Category("4. Performance & Safe Shredding")]
        [DisplayName("Enable Debug Logs")]
        public InArgument<bool> EnableDebugLogs { get; set; }

        [Category("5. Outputs")]
        [DisplayName("Total Records Extracted")]
        public OutArgument<long> TotalExtracted { get; set; }

        [Category("5. Outputs")]
        [DisplayName("Total Records Loaded")]
        public OutArgument<long> TotalLoaded { get; set; }

        [Category("5. Outputs")]
        [DisplayName("Masked Fields Count")]
        public OutArgument<long> MaskedFieldsCount { get; set; }

        [Category("5. Outputs")]
        [DisplayName("Audit Summary JSON")]
        public OutArgument<string> AuditSummaryJson { get; set; }

        [Category("5. Outputs")]
        [DisplayName("Vault Session ID")]
        public OutArgument<string> VaultSessionId { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            var swTotal = Stopwatch.StartNew();
            string vaultDir = null;
            string vaultFile = null;
            bool autoShred = true;

            try
            {
                string templateName = DataAssistantTagTemplate.Get(context);
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    throw new InvalidOperationException("SEGURANÇA TDM: O nome do Tag Template do Data Assistant é obrigatório.");
                }

                string srcProj = SourceProjectId.Get(context);
                string srcCreds = SourceCredentialsJson.Get(context);
                string srcDs = SourceDatasetId.Get(context);
                string srcTbl = SourceTableId.Get(context);
                string whereClause = SourceWhereClause.Get(context);
                int? limit = Limit.Get(context);

                string tgtProj = TargetProjectId.Get(context);
                string tgtCreds = TargetCredentialsJson.Get(context);
                if (string.IsNullOrWhiteSpace(tgtCreds)) tgtCreds = srcCreds;
                string tgtDs = TargetDatasetId.Get(context);
                string tgtTbl = TargetTableId.Get(context);
                if (string.IsNullOrWhiteSpace(tgtTbl)) tgtTbl = srcTbl;

                bool truncateTgt = TruncateTableIfExists.Get(context);
                int batchSize = BatchSize.Get(context) ?? 500;
                bool dryRun = DryRun.Get(context);
                autoShred = AutoShredVault.Get(context);
                bool debugLogs = EnableDebugLogs.Get(context);
                string portalUrl = TdmPortalUrl.Get(context);
                string tdmUser = TdmUsername.Get(context);
                string tdmPass = TdmPassword.Get(context);
                string tdmToken = TdmAuthToken.Get(context);

                long? projId = null;
                string projIdStr = ProjectIdentifier.Get(context);
                if (!string.IsNullOrWhiteSpace(projIdStr) && long.TryParse(projIdStr, out long pVal)) projId = pVal;
                long? versionIdParam = VersionIdentifier.Get(context);
                bool createNoSqlGen = CreateTdmNoSqlGenerator.Get(context);
                string customGenName = TdmGeneratorName.Get(context);

                GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] ==========================================================");
                GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] Validando Tag Template '{0}' no TDM Portal...", templateName);

                var resolvedRules = TdmDataAssistantTemplateResolver.FetchTemplateRules(
                    templateName, portalUrl, tdmToken, tdmUser, tdmPass, projId, debugLogs);

                GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] Tag Template validado com sucesso! ({0} regras ativas)", resolvedRules.Count);

                TdmGeneratorContextClient generatorContext = null;
                try
                {
                    string resolvedTdmUrl = TdmPortalConnectionProfileResolver.ResolveTdmBaseUrl(portalUrl);
                    string authHeader = TdmPortalConnectionProfileResolver.ResolveTdmAuthHeader(
                        resolvedTdmUrl, tdmToken, tdmUser, tdmPass, debugLogs);

                    if (!string.IsNullOrWhiteSpace(resolvedTdmUrl) && !string.IsNullOrWhiteSpace(authHeader))
                    {
                        generatorContext = new TdmGeneratorContextClient(resolvedTdmUrl, authHeader, debugLogs);
                        generatorContext.InitializeDummyContext(projectId: projId.HasValue ? (int)projId.Value : 0, versionId: 0, levelId: 0);
                    }
                }
                catch { }

                // 1. Cria clientes BigQuery
                var srcClient = CreateBigQueryClient(srcProj, srcCreds);

                string sqlQuery = $"SELECT * FROM `{srcProj}.{srcDs}.{srcTbl}`";
                if (!string.IsNullOrWhiteSpace(whereClause))
                {
                    string cleanWhere = whereClause.Trim();
                    if (!cleanWhere.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase)) cleanWhere = "WHERE " + cleanWhere;
                    sqlQuery += " " + cleanWhere;
                }
                if (limit.HasValue && limit.Value > 0)
                {
                    sqlQuery += $" LIMIT {limit.Value}";
                }

                GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] Executando consulta SQL na Origem: {0}", sqlQuery);

                var queryResults = srcClient.ExecuteQuery(sqlQuery, parameters: null);
                var maskingEngine = new BigQueryDataPainterEngine(resolvedRules, generatorContext);
                int totalMaskedFields = 0;
                var piiTypeStats = new Dictionary<string, int>();

                // Opcional: Criar NoSQL Data Generator permanente no TDM Portal (Formulas ou Replay)
                if (createNoSqlGen)
                {
                    try
                    {
                        string resolvedTdmUrl = TdmPortalConnectionProfileResolver.ResolveTdmBaseUrl(portalUrl);
                        string authHeader = TdmPortalConnectionProfileResolver.ResolveTdmAuthHeader(
                            resolvedTdmUrl, tdmToken, tdmUser, tdmPass, debugLogs);

                        long effectiveProjId = projId.HasValue && projId.Value > 0 ? projId.Value : 1;
                        long effectiveVerId = versionIdParam.HasValue && versionIdParam.Value > 0 ? versionIdParam.Value : 1;

                        string modeStr = (GeneratorMode.Get(context) ?? "Formulas").Trim();
                        GeneratorDataMode dataMode = modeStr.Equals("ReplayMaskedData", StringComparison.OrdinalIgnoreCase) ||
                                                     modeStr.Equals("Replay", StringComparison.OrdinalIgnoreCase) ||
                                                     modeStr.Equals("Literal", StringComparison.OrdinalIgnoreCase)
                            ? GeneratorDataMode.LiteralMaskedValues
                            : GeneratorDataMode.Formulas;

                        var sampleRow = queryResults.FirstOrDefault();
                        if (sampleRow != null)
                        {
                            var sRowObj = new JObject();
                            foreach (var field in sampleRow.Schema.Fields)
                            {
                                var val = sampleRow[field.Name];
                                sRowObj[field.Name] = val != null ? JToken.FromObject(val) : JValue.CreateNull();
                            }
                            int dummy;
                            var mObj = maskingEngine.MaskRow(sRowObj, out dummy);

                            TdmNoSqlGeneratorManager.CreateNoSqlGenerator(
                                resolvedTdmUrl, authHeader, effectiveProjId, effectiveVerId, customGenName, tgtTbl, resolvedRules, sRowObj, mObj, dataMode, debugLogs);
                        }
                    }
                    catch (Exception exGen)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[TdmGenerator] Aviso: Não foi possível criar o NoSQL Generator no TDM: {0}", exGen.Message);
                    }
                }

                // 2. Instancia Cofre Seguro
                vaultDir = SecureVaultHelper.CreateSecureVaultDirectory();
                string sessionId = Path.GetFileName(vaultDir);
                VaultSessionId.Set(context, sessionId);
                vaultFile = Path.Combine(vaultDir, $"{srcTbl}_{DateTime.Now:yyyyMMdd_HHmmss}.vault");

                long extractedCount = 0;

                using (var swVault = new StreamWriter(vaultFile, false, Encoding.UTF8))
                {
                    foreach (var row in queryResults)
                    {
                        var rowObj = new JObject();
                        foreach (var field in row.Schema.Fields)
                        {
                            var val = row[field.Name];
                            rowObj[field.Name] = val != null ? JToken.FromObject(val) : JValue.CreateNull();
                        }

                        int maskedInRow;
                        var maskedObj = maskingEngine.MaskRow(rowObj, out maskedInRow, piiTypeStats);
                        totalMaskedFields += maskedInRow;
                        extractedCount++;

                        swVault.WriteLine(maskedObj.ToString(Formatting.None));
                    }
                }

                GlobalSettings.MessageLogger.LogMessage(
                    "[BigQueryPipeline] Extração e mascaramento concluídos: {0} registros gravados no cofre (Campos PII: {1})",
                    extractedCount, totalMaskedFields);

                if (piiTypeStats.Count > 0)
                {
                    var piiBreakdown = piiTypeStats.Select(kvp => $"{kvp.Key}: {kvp.Value}").ToList();
                    GlobalSettings.MessageLogger.LogMessage(
                        "[BigQueryPipeline] Detalhamento de PII mascarados por tipo/tag: [{0}]", string.Join(", ", piiBreakdown));
                }

                // 3. Carga em NÃO-PROD
                long loadedCount = 0;
                if (!dryRun)
                {
                    var tgtClient = CreateBigQueryClient(tgtProj, tgtCreds);
                    var tableRef = tgtClient.GetTable(tgtDs, tgtTbl);

                    if (truncateTgt)
                    {
                        GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] TruncateTableIfExists=True: Truncando tabela '{0}.{1}.{2}'...", tgtProj, tgtDs, tgtTbl);
                        tgtClient.ExecuteQuery($"TRUNCATE TABLE `{tgtProj}.{tgtDs}.{tgtTbl}`", parameters: null);
                    }

                    using (var srVault = new StreamReader(vaultFile, Encoding.UTF8))
                    {
                        string line;
                        var batchRows = new List<BigQueryInsertRow>();

                        while ((line = srVault.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            var docObj = JObject.Parse(line);
                            var insertRow = new BigQueryInsertRow();
                            foreach (var prop in docObj.Properties())
                            {
                                var val = prop.Value is JValue jv ? jv.Value : prop.Value?.ToString();
                                insertRow.Add(prop.Name, val);
                            }

                            batchRows.Add(insertRow);

                            if (batchRows.Count >= batchSize)
                            {
                                tgtClient.InsertRows(tgtDs, tgtTbl, batchRows);
                                loadedCount += batchRows.Count;
                                batchRows.Clear();
                            }
                        }

                        if (batchRows.Count > 0)
                        {
                            tgtClient.InsertRows(tgtDs, tgtTbl, batchRows);
                            loadedCount += batchRows.Count;
                            batchRows.Clear();
                        }
                    }

                    GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] Inserção concluída no Destino ({0} linhas inseridas)", loadedCount);

                    // Consultas prontas para validação
                    GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] ----------------------------------------------------------");
                    GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] Consultas prontas para validação do Mascaramento no Destino:");
                    GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] [BigQuery SQL Console]:");
                    GlobalSettings.MessageLogger.LogMessage($"[BigQueryPipeline] SELECT * FROM `{tgtProj}.{tgtDs}.{tgtTbl}` LIMIT 50;");
                    GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] ----------------------------------------------------------");
                }
                else
                {
                    GlobalSettings.MessageLogger.LogMessage("[BigQueryPipeline] Modo DRY-RUN ativado: Carga no BigQuery de destino ignorada.");
                }

                swTotal.Stop();

                TotalExtracted.Set(context, extractedCount);
                TotalLoaded.Set(context, loadedCount);
                MaskedFieldsCount.Set(context, totalMaskedFields);

                var summary = new
                {
                    SourceProject = srcProj,
                    SourceDataset = srcDs,
                    SourceTable = srcTbl,
                    TargetProject = tgtProj,
                    TargetDataset = tgtDs,
                    TargetTable = tgtTbl,
                    TotalExtracted = extractedCount,
                    TotalLoaded = loadedCount,
                    MaskedFieldsCount = totalMaskedFields,
                    PiiBreakdown = piiTypeStats,
                    DryRun = dryRun,
                    DurationMs = swTotal.ElapsedMilliseconds
                };

                AuditSummaryJson.Set(context, JsonConvert.SerializeObject(summary, Formatting.Indented));
            }
            finally
            {
                if (autoShred && !string.IsNullOrWhiteSpace(vaultDir))
                {
                    SecureVaultHelper.DestroyVaultDirectory(vaultDir);
                }
            }
        }

        private BigQueryClient CreateBigQueryClient(string projectId, string credsJsonOrPath)
        {
            if (string.IsNullOrWhiteSpace(credsJsonOrPath))
            {
                return BigQueryClient.Create(projectId);
            }

            GoogleCredential credential;
            if (File.Exists(credsJsonOrPath))
            {
                using (var stream = new FileStream(credsJsonOrPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream);
                }
            }
            else if (credsJsonOrPath.TrimStart().StartsWith("{"))
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(credsJsonOrPath)))
                {
                    credential = GoogleCredential.FromStream(stream);
                }
            }
            else
            {
                return BigQueryClient.Create(projectId);
            }

            return BigQueryClient.Create(projectId, credential);
        }
    }
}
