using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using GridTools.Javelin.Common;
using GridTools.Javelin.DynamoDbActivities.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.DynamoDbActivities
{
    [DisplayName("DynamoDB Prod To Non-Prod Masking Pipeline")]
    [Description("Executa pipeline in-flight de extração, mascaramento via Tag Template TDM e carga do Amazon DynamoDB de Produção para Não-Produção com Cofre Seguro.")]
    public class DynamoDbProdToNonProdPipelineActivity : CodeActivity
    {
        [Category("1. Source DynamoDB (PROD)")]
        [RequiredArgument]
        [DisplayName("Source AWS Region / Service URL")]
        [Description("Região AWS de Origem (ex: us-east-1, sa-east-1) ou ServiceURL (ex: http://localhost:8000).")]
        public InArgument<string> SourceRegionOrServiceUrl { get; set; }

        [Category("1. Source DynamoDB (PROD)")]
        [DisplayName("Source Access Key ID")]
        public InArgument<string> SourceAccessKey { get; set; }

        [Category("1. Source DynamoDB (PROD)")]
        [DisplayName("Source Secret Access Key")]
        public InArgument<string> SourceSecretKey { get; set; }

        [Category("1. Source DynamoDB (PROD)")]
        [RequiredArgument]
        [DisplayName("Source Table Name")]
        [Description("Nome da Tabela de Origem no DynamoDB.")]
        public InArgument<string> SourceTableName { get; set; }

        [Category("1. Source DynamoDB (PROD)")]
        [DisplayName("Extraction Limit")]
        [Description("Limite máximo de itens a extrair.")]
        public InArgument<int?> Limit { get; set; }

        [Category("2. Target DynamoDB (NON-PROD)")]
        [RequiredArgument]
        [DisplayName("Target AWS Region / Service URL")]
        [Description("Região AWS de Destino (ex: us-east-1, sa-east-1) ou ServiceURL.")]
        public InArgument<string> TargetRegionOrServiceUrl { get; set; }

        [Category("2. Target DynamoDB (NON-PROD)")]
        [DisplayName("Target Access Key ID")]
        public InArgument<string> TargetAccessKey { get; set; }

        [Category("2. Target DynamoDB (NON-PROD)")]
        [DisplayName("Target Secret Access Key")]
        public InArgument<string> TargetSecretKey { get; set; }

        [Category("2. Target DynamoDB (NON-PROD)")]
        [DisplayName("Target Table Name")]
        [Description("Nome da Tabela de Destino (se omitido, usa o mesmo nome da Origem).")]
        public InArgument<string> TargetTableName { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [RequiredArgument]
        [DisplayName("Data Assistant Tag Template")]
        [Description("Nome do Tag Template do Data Assistant no TDM Portal (ex: 'DYNAMODB_PII_TAG_TEMPLATE', 'PII_DEFAULT').")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [DisplayName("Project Identifier")]
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

                string srcRegion = SourceRegionOrServiceUrl.Get(context);
                string srcKey = SourceAccessKey.Get(context);
                string srcSecret = SourceSecretKey.Get(context);
                string srcTbl = SourceTableName.Get(context);
                int? limit = Limit.Get(context);

                string tgtRegion = TargetRegionOrServiceUrl.Get(context);
                string tgtKey = TargetAccessKey.Get(context);
                if (string.IsNullOrWhiteSpace(tgtKey)) tgtKey = srcKey;
                string tgtSecret = TargetSecretKey.Get(context);
                if (string.IsNullOrWhiteSpace(tgtSecret)) tgtSecret = srcSecret;
                string tgtTbl = TargetTableName.Get(context);
                if (string.IsNullOrWhiteSpace(tgtTbl)) tgtTbl = srcTbl;

                int batchSize = BatchSize.Get(context) ?? 25; // Limite padrão DynamoDB BatchWriteItem é 25
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

                GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] ==========================================================");
                GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] Validando Tag Template '{0}' no TDM Portal...", templateName);

                var resolvedRules = TdmDataAssistantTemplateResolver.FetchTemplateRules(
                    templateName, portalUrl, tdmToken, tdmUser, tdmPass, projId, debugLogs);

                GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] Tag Template validado com sucesso! ({0} regras ativas)", resolvedRules.Count);

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

                // 1. Conexão Origem
                var srcClient = CreateDynamoClient(srcRegion, srcKey, srcSecret);
                var srcTable = Table.LoadTable(srcClient, srcTbl);

                var scanFilter = new ScanFilter();
                var scanConfig = new ScanOperationConfig
                {
                    Filter = scanFilter
                };
                if (limit.HasValue && limit.Value > 0)
                {
                    scanConfig.Limit = limit.Value;
                }

                GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] Escaneando itens da tabela Origem '{0}'...", srcTbl);

                var search = srcTable.Scan(scanConfig);
                var maskingEngine = new DynamoDbDataPainterEngine(resolvedRules, generatorContext);
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

                        var sampleSearch = srcTable.Scan(new ScanOperationConfig { Limit = 1 });
                        var sampleItems = sampleSearch.GetNextSet();
                        if (sampleItems.Count > 0)
                        {
                            var sObj = JObject.Parse(sampleItems[0].ToJson());
                            int dummy;
                            var mObj = maskingEngine.MaskRow(sObj, out dummy);

                            TdmNoSqlGeneratorManager.CreateNoSqlGenerator(
                                resolvedTdmUrl, authHeader, effectiveProjId, effectiveVerId, customGenName, tgtTbl, resolvedRules, sObj, mObj, dataMode, debugLogs);
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
                    do
                    {
                        var items = search.GetNextSet();
                        foreach (var doc in items)
                        {
                            string json = doc.ToJson();
                            var jObj = JObject.Parse(json);

                            int maskedInRow;
                            var maskedObj = maskingEngine.MaskRow(jObj, out maskedInRow, piiTypeStats);
                            totalMaskedFields += maskedInRow;
                            extractedCount++;

                            swVault.WriteLine(maskedObj.ToString(Formatting.None));

                            if (limit.HasValue && extractedCount >= limit.Value) break;
                        }

                        if (limit.HasValue && extractedCount >= limit.Value) break;

                    } while (!search.IsDone);
                }

                GlobalSettings.MessageLogger.LogMessage(
                    "[DynamoDbPipeline] Extração e mascaramento concluídos: {0} documentos gravados no cofre (Campos PII: {1})",
                    extractedCount, totalMaskedFields);

                if (piiTypeStats.Count > 0)
                {
                    var piiBreakdown = piiTypeStats.Select(kvp => $"{kvp.Key}: {kvp.Value}").ToList();
                    GlobalSettings.MessageLogger.LogMessage(
                        "[DynamoDbPipeline] Detalhamento de PII mascarados por tipo/tag: [{0}]", string.Join(", ", piiBreakdown));
                }

                // 3. Carga em NÃO-PROD
                long loadedCount = 0;
                if (!dryRun)
                {
                    var tgtClient = CreateDynamoClient(tgtRegion, tgtKey, tgtSecret);
                    var tgtTable = Table.LoadTable(tgtClient, tgtTbl);

                    using (var srVault = new StreamReader(vaultFile, Encoding.UTF8))
                    {
                        string line;
                        var batchWrite = tgtTable.CreateBatchWrite();

                        while ((line = srVault.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            var doc = Document.FromJson(line);
                            batchWrite.AddDocumentToPut(doc);
                            loadedCount++;

                            if (loadedCount % 25 == 0)
                            {
                                batchWrite.Execute();
                                batchWrite = tgtTable.CreateBatchWrite();
                            }
                        }

                        // Executa lote remanescente
                        batchWrite.Execute();
                    }

                    GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] Carga concluída no Destino ({0} itens inseridos)", loadedCount);

                    // Consultas prontas para validação
                    GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] ----------------------------------------------------------");
                    GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] Consultas prontas para validação do Mascaramento no Destino:");
                    GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] [AWS CLI DynamoDB Scan]:");
                    GlobalSettings.MessageLogger.LogMessage($"[DynamoDbPipeline] aws dynamodb scan --table-name {tgtTbl} --max-items 10");
                    GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] ----------------------------------------------------------");
                }
                else
                {
                    GlobalSettings.MessageLogger.LogMessage("[DynamoDbPipeline] Modo DRY-RUN ativado: Carga no DynamoDB de destino ignorada.");
                }

                swTotal.Stop();

                TotalExtracted.Set(context, extractedCount);
                TotalLoaded.Set(context, loadedCount);
                MaskedFieldsCount.Set(context, totalMaskedFields);

                var summary = new
                {
                    SourceRegion = srcRegion,
                    SourceTable = srcTbl,
                    TargetRegion = tgtRegion,
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

        private AmazonDynamoDBClient CreateDynamoClient(string regionOrUrl, string accessKey, string secretKey)
        {
            var config = new AmazonDynamoDBConfig();
            if (!string.IsNullOrWhiteSpace(regionOrUrl) && regionOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                config.ServiceURL = regionOrUrl;
            }
            else if (!string.IsNullOrWhiteSpace(regionOrUrl))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(regionOrUrl);
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.USEast1;
            }

            if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
            {
                return new AmazonDynamoDBClient(accessKey, secretKey, config);
            }

            return new AmazonDynamoDBClient(config);
        }
    }
}
