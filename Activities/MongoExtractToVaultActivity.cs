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
    [DisplayName("MongoDB Extract to Vault")]
    [Description("Extract documents from MongoDB Production into an encrypted, isolated Secure Vault with Windows EFS/ACL protection")]
    public class MongoExtractToVaultActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Connection Profile / URI (Prod)")]
        [Description("TDM Connection Profile Name (e.g. MONGO_PROD_CLUSTER) or Direct MongoDB URI")]
        public InArgument<string> ConnectionString { get; set; }

        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Database Name")]
        public InArgument<string> DatabaseName { get; set; }

        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Collection Name")]
        public InArgument<string> CollectionName { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Filter (JSON)")]
        [Description("BSON/JSON query filter, e.g. {\"status\": \"ACTIVE\"}. Default is {} for all documents.")]
        [DefaultValue("{}")]
        public InArgument<string> FilterJson { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Projection (JSON)")]
        [Description("Optional fields to include/exclude")]
        public InArgument<string> ProjectionJson { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Sort (JSON)")]
        [Description("Optional sorting criteria")]
        public InArgument<string> SortJson { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Limit")]
        [Description("Maximum number of documents to extract (0 or empty for no limit)")]
        public InArgument<int?> Limit { get; set; }

        [Category("2. Secure Vault Settings")]
        [DisplayName("Vault Directory")]
        [Description("Optional custom directory for the secure vault. If empty, creates an ephemeral encrypted vault in %TEMP%")]
        public InArgument<string> VaultDirectory { get; set; }

        [Category("3. Data Assistant Tag Template (Optional for In-Flight Masking)")]
        [DisplayName("Apply Masking In-Flight")]
        [Description("If true, masks data before storing into the vault; requires DataAssistantTagTemplate")]
        [DefaultValue(false)]
        public InArgument<bool> ApplyMaskingInFlight { get; set; }

        [Category("3. Data Assistant Tag Template (Optional for In-Flight Masking)")]
        [DisplayName("Data Assistant Tag Template Name")]
        [Description("Nome do Template de TAG do Data Assistant caso ApplyMaskingInFlight seja true.")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("4. Output")]
        [DisplayName("Vault File Path")]
        [Description("Full path to the secure vault file containing extracted documents")]
        public OutArgument<string> VaultFilePath { get; set; }

        [Category("4. Output")]
        [DisplayName("Vault Session ID")]
        public OutArgument<string> VaultSessionId { get; set; }

        [Category("4. Output")]
        [DisplayName("Extracted Count")]
        public OutArgument<long> ExtractedCount { get; set; }

        [Category("4. Output")]
        [DisplayName("SHA256 Checksum")]
        public OutArgument<string> Sha256Checksum { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string connStr = ConnectionString.Get(context);
                string dbName = DatabaseName.Get(context);
                string collName = CollectionName.Get(context);
                string filterStr = FilterJson.Get(context);
                string projStr = ProjectionJson.Get(context);
                string sortStr = SortJson.Get(context);
                int? limit = Limit.Get(context);
                string customVaultDir = VaultDirectory.Get(context);
                bool applyMasking = ApplyMaskingInFlight.Get(context);
                string templateName = DataAssistantTagTemplate.Get(context);

                if (string.IsNullOrWhiteSpace(filterStr))
                    filterStr = "{}";

                TdmDataPainterEngine maskingEngine = null;
                if (applyMasking)
                {
                    if (string.IsNullOrWhiteSpace(templateName))
                    {
                        throw new InvalidOperationException(
                            "SEGURANÇA TDM: 'ApplyMaskingInFlight' está ativo, mas o campo 'DataAssistantTagTemplate' não foi informado. " +
                            "Informe o nome do Template de TAG do Data Assistant para extração com mascaramento.");
                    }
                    var rules = TdmDataAssistantTemplateResolver.FetchTemplateRules(templateName);
                    maskingEngine = new TdmDataPainterEngine(rules);
                }

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoExtractToVault] Iniciando extração segura de PROD: {0}.{1} (Filtro: {2})",
                    dbName, collName, filterStr);

                // 1. Instancia Cofre Seguro (Barreiras 1 e 2: ACL NTFS + Windows EFS)
                string vaultDir = SecureVaultHelper.CreateSecureVaultDirectory(customVaultDir);
                string sessionId = Path.GetFileName(vaultDir);
                VaultSessionId.Set(context, sessionId);

                // 2. Conecta no MongoDB de Produção (com resolução de profile se aplicável)
                var collection = MongoConnectionHelper.GetCollection(connStr, dbName, collName);
                var filterDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(filterStr);
                var findFluent = collection.Find(filterDoc);

                if (!string.IsNullOrWhiteSpace(projStr))
                {
                    var projDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(projStr);
                    findFluent = findFluent.Project(projDoc);
                }

                if (!string.IsNullOrWhiteSpace(sortStr))
                {
                    var sortDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(sortStr);
                    findFluent = findFluent.Sort(sortDoc);
                }

                if (limit.HasValue && limit.Value > 0)
                {
                    findFluent = findFluent.Limit(limit.Value);
                }

                // 3. Executa streaming de documentos e grava no cofre com EFS/ACL/RAM Temporary (Barreira 3)
                IEnumerable<BsonDocument> DocumentStream()
                {
                    using (var cursor = findFluent.ToCursor())
                    {
                        while (cursor.MoveNext())
                        {
                            foreach (var doc in cursor.Current)
                            {
                                if (applyMasking && maskingEngine != null)
                                {
                                    int count;
                                    yield return maskingEngine.MaskDocument(doc, out count);
                                }
                                else
                                {
                                    yield return doc;
                                }
                            }
                        }
                    }
                }

                long totalExtracted;
                string sha256;
                string vaultFilePath = SecureVaultHelper.WriteDocumentsToVault(
                    vaultDir, collName, DocumentStream(), out totalExtracted, out sha256);

                VaultFilePath.Set(context, vaultFilePath);
                ExtractedCount.Set(context, totalExtracted);
                Sha256Checksum.Set(context, sha256);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoExtractToVault] Extração para o cofre finalizada com sucesso! Documentos: {0}, Checksum: {1}, Arquivo: {2}",
                    totalExtracted, sha256, vaultFilePath);
            }
            catch (Exception ex)
            {
                var bex = new BusinessException(ex.Message, DisplayName, ex.InnerException);
                if (!continueOnErrorInExecMode)
                {
                    throw bex;
                }
            }
        }
    }
}
