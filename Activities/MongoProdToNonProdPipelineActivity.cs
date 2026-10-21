using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using GridTools.Javelin.Common;
using GridTools.Javelin.MongoDbActivities.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace GridTools.Javelin.MongoDbActivities
{
    [DisplayName("MongoDB Prod to Non-Prod Pipeline")]
    [Description("Direct NoSQL Migration: Extract from Prod -> Encrypted Secure Vault -> Data Assistant Tag Template Masking -> Load to Non-Prod -> Forensics Zero-Fill Shred")]
    public class MongoProdToNonProdPipelineActivity : JavelinNativeActivity
    {
        // --- Conexão de Origem (Prod) ---
        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Source Connection Profile / URI")]
        [Description("TDM Connection Profile Name (e.g. MONGO_PROD_CLUSTER) or Direct MongoDB URI")]
        public InArgument<string> SourceConnectionString { get; set; }

        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Source Database Name")]
        public InArgument<string> SourceDatabaseName { get; set; }

        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Source Collection Name")]
        public InArgument<string> SourceCollectionName { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Source Filter (JSON or SQL WHERE)")]
        [Description("Filtro BSON/JSON (ex: {\"status\": \"ACTIVE\"}) OU cláusula WHERE clássica (ex: status = 'ATIVO' AND idade >= 18 AND uf IN ('SP','RJ')).")]
        [DefaultValue("{}")]
        public InArgument<string> SourceFilterJson { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Source WHERE Clause (SQL-Style)")]
        [Description("Cláusula WHERE clássica opcional (ex: status = 'ATIVO' AND saldo > 1000). Tem precedência se preenchida.")]
        public InArgument<string> SourceWhereClause { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Extraction Limit")]
        [Description("Maximum documents to migrate (0 or null for all matching documents)")]
        public InArgument<int?> Limit { get; set; }

        // --- Conexão de Destino (Não-Prod / Target) ---
        [RequiredArgument]
        [Category("2. Target (Non-Production)")]
        [DisplayName("Target Connection Profile / URI")]
        [Description("TDM Connection Profile Name (e.g. MONGO_HML_CLUSTER) or Direct MongoDB URI")]
        public InArgument<string> TargetConnectionString { get; set; }

        [RequiredArgument]
        [Category("2. Target (Non-Production)")]
        [DisplayName("Target Database Name")]
        public InArgument<string> TargetDatabaseName { get; set; }

        [RequiredArgument]
        [Category("2. Target (Non-Production)")]
        [DisplayName("Target Collection Name")]
        [Description("Destination collection name (defaults to source collection if left empty)")]
        public InArgument<string> TargetCollectionName { get; set; }

        [Category("2. Target (Non-Production)")]
        [DisplayName("Write Mode")]
        [Description("Insert (fast bulk insert), Upsert (replace matching doc), or Replace")]
        [DefaultValue("Insert")]
        public InArgument<string> WriteMode { get; set; }

        [Category("2. Target (Non-Production)")]
        [DisplayName("Upsert Key Field")]
        [Description("Field used for matching during Upsert (default is _id)")]
        [DefaultValue("_id")]
        public InArgument<string> UpsertKeyField { get; set; }

        [Category("2. Target (Non-Production)")]
        [DisplayName("Drop Collection If Exists")]
        [Description("If true, drops/cleans the target collection before loading data")]
        [DefaultValue(false)]
        public InArgument<bool> DropCollectionIfExists { get; set; }

        // --- Data Assistant Tag Template (Obrigatório para Garantia de Mascaramento) ---
        [RequiredArgument]
        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("Data Assistant Tag Template Name")]
        [Description("Nome do Template de TAG do Data Assistant no TDM Portal (ex: MONGODB_PII_TAG_TEMPLATE). OBRIGATÓRIO: O pipeline não roda sem este template.")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Portal URL")]
        [Description("URL do TDM Portal (ex: https://tdm-server:8443). Se em branco, localiza automaticamente o application.properties na máquina local.")]
        public InArgument<string> TdmPortalUrl { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Username")]
        [Description("Usuário do TDM Portal (opcional - se vazio, lê do application.properties ou usa integrator)")]
        public InArgument<string> TdmUsername { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Password")]
        [Description("Senha do usuário TDM (opcional - se vazio, lê do application.properties)")]
        public InArgument<string> TdmPassword { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Auth Token (JWT)")]
        [Description("Token JWT pré-gerado do TDM Portal (opcional - se informado, ignora login)")]
        public InArgument<string> TdmAuthToken { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Project Name / ID")]
        [Description("Opcional: ID ou Nome do Projeto no TDM para carregar regras específicas de projeto")]
        public InArgument<string> ProjectIdentifier { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Batch Size")]
        [Description("Commit batch size for extraction and bulk insert (default: 2000)")]
        [DefaultValue(2000)]
        public InArgument<int?> BatchSize { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Dry Run")]
        [Description("If true, simulates extraction, vault encryption and masking without writing to destination")]
        [DefaultValue(false)]
        public InArgument<bool> DryRun { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Enable Debug Logs")]
        [Description("Se habilitado, imprime no log do Javelin o detalhe de todas as chamadas HTTP e respostas para o TDM Portal")]
        [DefaultValue(false)]
        public InArgument<bool> EnableDebugLogs { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Auto Shred Vault")]
        [Description("Automatically destroy vault file with forensic zero-fill (0x00) after loading")]
        [DefaultValue(true)]
        public InArgument<bool> AutoShredVault { get; set; }

        // --- Outputs & Auditoria GTREP / TDM ---
        [Category("5. Output & Audit")]
        [DisplayName("Total Processed Count")]
        public OutArgument<long> TotalProcessedCount { get; set; }

        [Category("5. Output & Audit")]
        [DisplayName("Total PII Fields Masked")]
        public OutArgument<int> TotalPiiFieldsMasked { get; set; }

        [Category("5. Output & Audit")]
        [DisplayName("Vault Session ID")]
        public OutArgument<string> VaultSessionId { get; set; }

        [Category("5. Output & Audit")]
        [DisplayName("Execution Time (ms)")]
        public OutArgument<long> ExecutionTimeMs { get; set; }

        [Category("5. Output & Audit")]
        [DisplayName("Audit Summary (JSON)")]
        public OutArgument<string> AuditSummaryJson { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            var sw = Stopwatch.StartNew();
            string vaultDir = null;
            string vaultFile = null;
            bool autoShred = true;

            try
            {
                base.Execute(context);

                string templateName = DataAssistantTagTemplate.Get(context);
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    throw new InvalidOperationException(
                        "SEGURANÇA TDM: O campo 'DataAssistantTagTemplate' é obrigatório. " +
                        "Informe o nome do Template de TAG do Data Assistant para aplicar o mascaramento.");
                }

                string srcConn = SourceConnectionString.Get(context);
                string srcDb = SourceDatabaseName.Get(context);
                string srcColl = SourceCollectionName.Get(context);
                string whereClause = SourceWhereClause.Get(context);
                string filterStr = SourceFilterJson.Get(context);
                int? limit = Limit.Get(context);

                string effectiveFilter = !string.IsNullOrWhiteSpace(whereClause) ? whereClause : filterStr;
                if (string.IsNullOrWhiteSpace(effectiveFilter)) effectiveFilter = "{}";

                string tgtConn = TargetConnectionString.Get(context);
                string tgtDb = TargetDatabaseName.Get(context);
                string tgtColl = TargetCollectionName.Get(context);
                if (string.IsNullOrWhiteSpace(tgtColl)) tgtColl = srcColl;

                string writeMode = (WriteMode.Get(context) ?? "Insert").Trim();
                string upsertKey = (UpsertKeyField.Get(context) ?? "_id").Trim();
                bool dropCollection = DropCollectionIfExists.Get(context);
                int batchSize = BatchSize.Get(context) ?? 2000;
                bool dryRun = DryRun.Get(context);
                autoShred = AutoShredVault.Get(context);
                bool debugLogs = EnableDebugLogs.Get(context);
                string portalUrl = TdmPortalUrl.Get(context);
                string tdmUser = TdmUsername.Get(context);
                string tdmPass = TdmPassword.Get(context);
                string tdmToken = TdmAuthToken.Get(context);
                string projIdStr = ProjectIdentifier.Get(context);
                long? projId = null;
                if (!string.IsNullOrWhiteSpace(projIdStr) && long.TryParse(projIdStr, out long pVal))
                {
                    projId = pVal;
                }

                // 3. Extrai dados de PROD para o Cofre com suporte a SQL WHERE ou JSON
                var srcCollection = MongoConnectionHelper.GetCollection(
                    srcConn, srcDb, srcColl, portalUrl, tdmToken, tdmUser, tdmPass, debugLogs);
                var filterDoc = MongoSqlFilterParser.BuildFilter(effectiveFilter);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] ==========================================================");
                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Validando Tag Template '{0}' no TDM Portal antes de acessar Produção...", templateName);

                // 1. Valida e Busca as Regras do Tag Template via API do TDM (Trava de Segurança)
                var resolvedRules = TdmDataAssistantTemplateResolver.FetchTemplateRules(
                    templateName, tdmUrl: portalUrl, tdmToken: tdmToken, tdmUser: tdmUser, tdmPass: tdmPass, projectId: projId, enableDebugLogs: debugLogs);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Tag Template validado com sucesso! ({0} regras ativas)", resolvedRules.Count);
                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Extraindo PROD ({0}.{1}) -> COFRE SEGURO -> NÃO-PROD ({2}.{3}) [Filtro MQL: {4}]",
                    srcDb, srcColl, tgtDb, tgtColl, filterDoc.ToJson());

                // 2. Instancia Cofre Seguro (Barreiras 1 e 2: ACL NTFS + Windows EFS)
                vaultDir = SecureVaultHelper.CreateSecureVaultDirectory();
                string sessionId = Path.GetFileName(vaultDir);
                VaultSessionId.Set(context, sessionId);

                var findFluent = srcCollection.Find(filterDoc);
                if (limit.HasValue && limit.Value > 0)
                {
                    findFluent = findFluent.Limit(limit.Value);
                }

                var maskingEngine = new TdmDataPainterEngine(resolvedRules);
                int totalMaskedFields = 0;

                // 3. Executa streaming de documentos de PROD: Mascara na RAM antes de persistir no cofre
                IEnumerable<BsonDocument> DocumentStream()
                {
                    using (var cursor = findFluent.ToCursor())
                    {
                        while (cursor.MoveNext())
                        {
                            foreach (var doc in cursor.Current)
                            {
                                int count;
                                var masked = maskingEngine.MaskDocument(doc, out count);
                                totalMaskedFields += count;
                                yield return masked;
                            }
                        }
                    }
                }

                long extractedCount;
                string sha256;
                vaultFile = SecureVaultHelper.WriteDocumentsToVault(
                    vaultDir, srcColl, DocumentStream(), out extractedCount, out sha256);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Extração e mascaramento concluídos: {0} documentos gravados no cofre (Campos PII mascarados: {1}, SHA256: {2})",
                    extractedCount, totalMaskedFields, sha256);

                // 4. Carga em NÃO-PROD (os documentos no cofre já estão 100% mascarados e seguros)
                long loadedCount = 0;

                if (!dryRun)
                {
                    var targetDb = MongoConnectionHelper.GetDatabase(
                        tgtConn, tgtDb, portalUrl, tdmToken, tdmUser, tdmPass, debugLogs);
                    if (dropCollection)
                    {
                        GlobalSettings.MessageLogger.LogMessage(
                            "[MongoPipeline] DropCollectionIfExists=true: Removendo collection '{0}' no destino antes da carga...",
                            tgtColl);
                        targetDb.DropCollection(tgtColl);
                    }

                    var tgtCollection = targetDb.GetCollection<BsonDocument>(tgtColl);
                    var docsToInsert = new List<BsonDocument>();
                    var bulkOps = new List<WriteModel<BsonDocument>>();

                    foreach (var doc in SecureVaultHelper.ReadDocumentsFromVault(vaultFile))
                    {
                        if (writeMode.Equals("Upsert", StringComparison.OrdinalIgnoreCase))
                        {
                            BsonValue keyValue = doc.Contains(upsertKey) ? doc[upsertKey] : BsonNull.Value;
                            var filter = Builders<BsonDocument>.Filter.Eq(upsertKey, keyValue);
                            var replaceModel = new ReplaceOneModel<BsonDocument>(filter, doc) { IsUpsert = true };
                            bulkOps.Add(replaceModel);
                        }
                        else
                        {
                            docsToInsert.Add(doc);
                        }

                        if (docsToInsert.Count >= batchSize)
                        {
                            tgtCollection.InsertMany(docsToInsert);
                            loadedCount += docsToInsert.Count;
                            docsToInsert.Clear();
                        }

                        if (bulkOps.Count >= batchSize)
                        {
                            tgtCollection.BulkWrite(bulkOps);
                            loadedCount += bulkOps.Count;
                            bulkOps.Clear();
                        }
                    }

                    if (docsToInsert.Count > 0)
                    {
                        tgtCollection.InsertMany(docsToInsert);
                        loadedCount += docsToInsert.Count;
                        docsToInsert.Clear();
                    }

                    if (bulkOps.Count > 0)
                    {
                        tgtCollection.BulkWrite(bulkOps);
                        loadedCount += bulkOps.Count;
                        bulkOps.Clear();
                    }
                }
                else
                {
                    foreach (var doc in SecureVaultHelper.ReadDocumentsFromVault(vaultFile))
                    {
                        loadedCount++;
                    }
                    GlobalSettings.MessageLogger.LogMessage("[MongoPipeline] DryRun ativo: gravação em destino suprimida.");
                }

                sw.Stop();
                long elapsedMs = sw.ElapsedMilliseconds;

                TotalProcessedCount.Set(context, loadedCount);
                TotalPiiFieldsMasked.Set(context, totalMaskedFields);
                ExecutionTimeMs.Set(context, elapsedMs);

                var auditObj = new
                {
                    pipeline = "MongoDB Prod to Non-Prod",
                    tagTemplateUsed = templateName,
                    source = $"{srcDb}.{srcColl}",
                    target = $"{tgtDb}.{tgtColl}",
                    totalDocuments = loadedCount,
                    totalPiiFieldsMasked = totalMaskedFields,
                    sha256Checksum = sha256,
                    vaultSessionId = sessionId,
                    dryRun = dryRun,
                    executionTimeMs = elapsedMs,
                    securitySafeguards = new[]
                    {
                        "Tag Template Enforcement (Fail-Closed)",
                        "NTFS ACL Isolation (icacls)",
                        "Windows EFS Encryption (cipher /e)",
                        "RAM Cache (FILE_ATTRIBUTE_TEMPORARY)",
                        "Forensic Zero-Fill Shredding (0x00)"
                    },
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                string auditJson = JsonConvert.SerializeObject(auditObj, Formatting.Indented);
                AuditSummaryJson.Set(context, auditJson);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Pipeline concluído com sucesso em {0}ms! Processados: {1}, PII Mascarados: {2}",
                    elapsedMs, loadedCount, totalMaskedFields);
            }
            catch (Exception ex)
            {
                var bex = new BusinessException(ex.Message, DisplayName, ex.InnerException);
                if (!continueOnErrorInExecMode)
                {
                    throw bex;
                }
            }
            finally
            {
                // Barreira 4: Sanitização Forense Zero-Fill e destruição do cofre
                if (autoShred && !string.IsNullOrWhiteSpace(vaultDir) && Directory.Exists(vaultDir))
                {
                    SecureVaultHelper.DestroyVaultDirectory(vaultDir);
                }
            }
        }
    }
}
