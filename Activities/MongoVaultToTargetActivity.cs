using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using GridTools.Javelin.Common;
using GridTools.Javelin.MongoDbActivities.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GridTools.Javelin.MongoDbActivities
{
    [DisplayName("MongoDB Vault to Target (Non-Prod)")]
    [Description("Read documents from Secure Vault, apply Data Assistant Tag Template masking rules, load into Target Non-Prod MongoDB, and zero-fill shred the vault")]
    public class MongoVaultToTargetActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [Category("1. Vault Input")]
        [DisplayName("Vault File Path")]
        [Description("Full path to the encrypted .bson.vault file created by MongoExtractToVault")]
        public InArgument<string> VaultFilePath { get; set; }

        [RequiredArgument]
        [Category("2. Target Destination")]
        [DisplayName("Target Connection Profile / URI")]
        [Description("TDM Connection Profile Name (e.g. MONGO_HML_CLUSTER) or Direct MongoDB URI")]
        public InArgument<string> TargetConnectionString { get; set; }

        [RequiredArgument]
        [Category("2. Target Destination")]
        [DisplayName("Target Database Name")]
        public InArgument<string> TargetDatabaseName { get; set; }

        [RequiredArgument]
        [Category("2. Target Destination")]
        [DisplayName("Target Collection Name")]
        public InArgument<string> TargetCollectionName { get; set; }

        [RequiredArgument]
        [Category("3. Data Assistant Tag Template")]
        [DisplayName("Data Assistant Tag Template Name")]
        [Description("Nome do Template de TAG do Data Assistant no TDM Portal (ex: MONGODB_PII_TAG_TEMPLATE). OBRIGATÓRIO.")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("3. Data Assistant Tag Template")]
        [DisplayName("TDM Project Name / ID")]
        public InArgument<string> ProjectIdentifier { get; set; }

        [Category("4. Load Settings")]
        [DisplayName("Write Mode")]
        [Description("Insert (standard bulk insert), Upsert (replace matching doc by key), or Replace")]
        [DefaultValue("Insert")]
        public InArgument<string> WriteMode { get; set; }

        [Category("4. Load Settings")]
        [DisplayName("Upsert Key Field")]
        [Description("Unique key field used when WriteMode is Upsert (default is _id)")]
        [DefaultValue("_id")]
        public InArgument<string> UpsertKeyField { get; set; }

        [Category("4. Load Settings")]
        [DisplayName("Batch Size")]
        [Description("Number of documents per bulk write batch (default: 1000)")]
        [DefaultValue(1000)]
        public InArgument<int?> BatchSize { get; set; }

        [Category("4. Load Settings")]
        [DisplayName("Auto Shred Vault")]
        [Description("Automatically destroy vault file with forensic zero-fill (0x00) after loading")]
        [DefaultValue(true)]
        public InArgument<bool> AutoShredVaultOnComplete { get; set; }

        [Category("5. Output")]
        [DisplayName("Loaded Count")]
        public OutArgument<long> LoadedCount { get; set; }

        [Category("5. Output")]
        [DisplayName("Total PII Fields Masked")]
        public OutArgument<int> TotalPiiFieldsMasked { get; set; }

        [Category("5. Output")]
        [DisplayName("Vault Shredded")]
        public OutArgument<bool> VaultShredded { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            string vaultPath = null;
            bool shouldShred = true;

            try
            {
                base.Execute(context);

                string templateName = DataAssistantTagTemplate.Get(context);
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    throw new InvalidOperationException(
                        "SEGURANÇA TDM: O campo 'DataAssistantTagTemplate' é obrigatório. " +
                        "A execução foi bloqueada para impedir o envio de dados não mascarados ao destino.");
                }

                vaultPath = VaultFilePath.Get(context);
                string targetConnStr = TargetConnectionString.Get(context);
                string targetDbName = TargetDatabaseName.Get(context);
                string targetCollName = TargetCollectionName.Get(context);
                string writeMode = (WriteMode.Get(context) ?? "Insert").Trim();
                string keyField = (UpsertKeyField.Get(context) ?? "_id").Trim();
                int batchSize = BatchSize.Get(context) ?? 1000;
                shouldShred = AutoShredVaultOnComplete.Get(context);
                string projIdStr = ProjectIdentifier.Get(context);
                long? projId = null;
                if (!string.IsNullOrWhiteSpace(projIdStr) && long.TryParse(projIdStr, out long pVal))
                {
                    projId = pVal;
                }

                if (string.IsNullOrWhiteSpace(vaultPath) || !File.Exists(vaultPath))
                {
                    throw new FileNotFoundException("Arquivo do cofre não encontrado: " + vaultPath);
                }

                // 1. Busca e valida as regras do Tag Template no TDM Portal (Trava de Segurança)
                var resolvedRules = TdmDataAssistantTemplateResolver.FetchTemplateRules(
                    templateName, projectId: projId);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoVaultToTarget] Tag Template '{0}' validado ({1} regras ativas). Lendo cofre '{2}' e carregando em NÃO-PROD: {3}.{4}",
                    templateName, resolvedRules.Count, vaultPath, targetDbName, targetCollName);

                var targetCollection = MongoConnectionHelper.GetCollection(targetConnStr, targetDbName, targetCollName);
                var maskingEngine = new TdmDataPainterEngine(resolvedRules);

                long totalLoaded = 0;
                int totalMaskedFields = 0;

                var bulkOps = new List<WriteModel<BsonDocument>>();
                var docsToInsert = new List<BsonDocument>();

                foreach (var rawDoc in SecureVaultHelper.ReadDocumentsFromVault(vaultPath))
                {
                    int maskedCount;
                    var maskedDoc = maskingEngine.MaskDocument(rawDoc, out maskedCount);
                    totalMaskedFields += maskedCount;

                    if (writeMode.Equals("Upsert", StringComparison.OrdinalIgnoreCase))
                    {
                        BsonValue keyValue = maskedDoc.Contains(keyField) ? maskedDoc[keyField] : BsonNull.Value;
                        var filter = Builders<BsonDocument>.Filter.Eq(keyField, keyValue);
                        var replaceModel = new ReplaceOneModel<BsonDocument>(filter, maskedDoc) { IsUpsert = true };
                        bulkOps.Add(replaceModel);
                    }
                    else
                    {
                        docsToInsert.Add(maskedDoc);
                    }

                    if (docsToInsert.Count >= batchSize)
                    {
                        targetCollection.InsertMany(docsToInsert);
                        totalLoaded += docsToInsert.Count;
                        docsToInsert.Clear();
                    }

                    if (bulkOps.Count >= batchSize)
                    {
                        targetCollection.BulkWrite(bulkOps);
                        totalLoaded += bulkOps.Count;
                        bulkOps.Clear();
                    }
                }

                if (docsToInsert.Count > 0)
                {
                    targetCollection.InsertMany(docsToInsert);
                    totalLoaded += docsToInsert.Count;
                    docsToInsert.Clear();
                }

                if (bulkOps.Count > 0)
                {
                    targetCollection.BulkWrite(bulkOps);
                    totalLoaded += bulkOps.Count;
                    bulkOps.Clear();
                }

                LoadedCount.Set(context, totalLoaded);
                TotalPiiFieldsMasked.Set(context, totalMaskedFields);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoVaultToTarget] Carga em NÃO-PROD concluída! Documentos: {0}, Campos PII Mascarados: {1}",
                    totalLoaded, totalMaskedFields);
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
                if (shouldShred && !string.IsNullOrWhiteSpace(vaultPath) && File.Exists(vaultPath))
                {
                    SecureVaultHelper.ForensicsZeroFillAndShred(vaultPath);
                    string vaultDir = Path.GetDirectoryName(vaultPath);
                    SecureVaultHelper.DestroyVaultDirectory(vaultDir);
                    VaultShredded.Set(context, true);
                }
            }
        }
    }
}
