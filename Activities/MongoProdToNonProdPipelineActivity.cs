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
        [DisplayName("Source Filter (JSON)")]
        [Description("BSON/JSON query filter, e.g. {\"status\": \"ACTIVE\"}. Default is {} for all documents.")]
        [DefaultValue("{}")]
        public InArgument<string> SourceFilterJson { get; set; }

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
        [Category("3. Data Assistant Tag Template")]
        [DisplayName("Data Assistant Tag Template Name")]
        [Description("Nome do Template de TAG do Data Assistant no TDM Portal (ex: MONGODB_PII_TAG_TEMPLATE). OBRIGATÓRIO: O pipeline não roda sem este template.")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("3. Data Assistant Tag Template")]
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
                string filterStr = SourceFilterJson.Get(context);
                int? limit = Limit.Get(context);

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
                string projIdStr = ProjectIdentifier.Get(context);
                long? projId = null;
                if (!string.IsNullOrWhiteSpace(projIdStr) && long.TryParse(projIdStr, out long pVal))
                {
                    projId = pVal;
                }

                if (string.IsNullOrWhiteSpace(filterStr)) filterStr = "{}";

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] ==========================================================");
                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Validando Tag Template '{0}' no TDM Portal antes de acessar Produção...", templateName);

                // 1. Valida e Busca as Regras do Tag Template via API do TDM (Trava de Segurança)
                var resolvedRules = TdmDataAssistantTemplateResolver.FetchTemplateRules(
                    templateName, projectId: projId);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Tag Template validado com sucesso! ({0} regras ativas)", resolvedRules.Count);
                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Extraindo PROD ({0}.{1}) -> COFRE SEGURO -> NÃO-PROD ({2}.{3})",
                    srcDb, srcColl, tgtDb, tgtColl);

                // 2. Instancia Cofre Seguro (Barreiras 1 e 2: ACL NTFS + Windows EFS)
                vaultDir = SecureVaultHelper.CreateSecureVaultDirectory();
                string sessionId = Path.GetFileName(vaultDir);
                VaultSessionId.Set(context, sessionId);

                // 3. Extrai dados de PROD para o Cofre
                var srcCollection = MongoConnectionHelper.GetCollection(srcConn, srcDb, srcColl);
                var filterDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(filterStr);
                var findFluent = srcCollection.Find(filterDoc);
                if (limit.HasValue && limit.Value > 0)
                {
                    findFluent = findFluent.Limit(limit.Value);
                }

                IEnumerable<BsonDocument> DocumentStream()
                {
                    using (var cursor = findFluent.ToCursor())
                    {
                        while (cursor.MoveNext())
                        {
                            foreach (var doc in cursor.Current)
                            {
                                yield return doc;
                            }
                        }
                    }
                }

                long extractedCount;
                string sha256;
                vaultFile = SecureVaultHelper.WriteDocumentsToVault(
                    vaultDir, srcColl, DocumentStream(), out extractedCount, out sha256);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoPipeline] Extração concluída: {0} documentos gravados no cofre protegido (SHA256: {1})",
                    extractedCount, sha256);

                // 4. Aplica Motor de Mascaramento Data Assistant e Carga em NÃO-PROD
                var maskingEngine = new TdmDataPainterEngine(resolvedRules);
                long loadedCount = 0;
                int totalMaskedFields = 0;

                if (!dryRun)
                {
                    var targetDb = MongoConnectionHelper.GetDatabase(tgtConn, tgtDb);
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

                    foreach (var rawDoc in SecureVaultHelper.ReadDocumentsFromVault(vaultFile))
                    {
                        int maskedCount;
                        var maskedDoc = maskingEngine.MaskDocument(rawDoc, out maskedCount);
                        totalMaskedFields += maskedCount;

                        if (writeMode.Equals("Upsert", StringComparison.OrdinalIgnoreCase))
                        {
                            BsonValue keyValue = maskedDoc.Contains(upsertKey) ? maskedDoc[upsertKey] : BsonNull.Value;
                            var filter = Builders<BsonDocument>.Filter.Eq(upsertKey, keyValue);
                            var replaceModel = new ReplaceOneModel<BsonDocument>(filter, maskedDoc) { IsUpsert = true };
                            bulkOps.Add(replaceModel);
                        }
                        else
                        {
                            docsToInsert.Add(maskedDoc);
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
                    foreach (var rawDoc in SecureVaultHelper.ReadDocumentsFromVault(vaultFile))
                    {
                        int maskedCount;
                        maskingEngine.MaskDocument(rawDoc, out maskedCount);
                        totalMaskedFields += maskedCount;
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
