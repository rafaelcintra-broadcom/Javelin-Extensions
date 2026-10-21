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
        [DisplayName("Filter (JSON or SQL WHERE)")]
        [Description("BSON/JSON query filter (ex: {\"status\": \"ACTIVE\"}) OU cláusula WHERE clássica (ex: status = 'ATIVO' AND idade >= 18). Default is {} for all documents.")]
        [DefaultValue("{}")]
        public InArgument<string> FilterJson { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("WHERE Clause (SQL-Style)")]
        [Description("Cláusula WHERE clássica opcional (ex: status = 'ATIVO' AND uf IN ('SP','RJ')). Tem precedência se preenchida.")]
        public InArgument<string> WhereClause { get; set; }

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

        [RequiredArgument]
        [Category("3. Data Assistant Tag Template")]
        [DisplayName("Data Assistant Tag Template Name")]
        [Description("Nome do Template de TAG do Data Assistant no TDM Portal (ex: MONGODB_PII_TAG_TEMPLATE). OBRIGATÓRIO: os dados são SEMPRE mascarados na memória antes de gravar no cofre.")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("3. Data Assistant Tag Template")]
        [DisplayName("TDM Project Name / ID")]
        [Description("Opcional: ID ou Nome do Projeto no TDM para carregar regras específicas de projeto")]
        public InArgument<string> ProjectIdentifier { get; set; }

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
        [DisplayName("Total PII Fields Masked")]
        public OutArgument<int> TotalPiiFieldsMasked { get; set; }

        [Category("4. Output")]
        [DisplayName("SHA256 Checksum")]
        public OutArgument<string> Sha256Checksum { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string templateName = DataAssistantTagTemplate.Get(context);
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    throw new InvalidOperationException(
                        "SEGURANÇA TDM: O campo 'DataAssistantTagTemplate' é obrigatório. " +
                        "A extração foi bloqueada para impedir o armazenamento de dados brutos não mascarados no cofre.");
                }

                string connStr = ConnectionString.Get(context);
                string dbName = DatabaseName.Get(context);
                string collName = CollectionName.Get(context);
                string whereClause = WhereClause.Get(context);
                string filterStr = FilterJson.Get(context);
                string projStr = ProjectionJson.Get(context);
                string sortStr = SortJson.Get(context);
                int? limit = Limit.Get(context);
                string customVaultDir = VaultDirectory.Get(context);
                string projIdStr = ProjectIdentifier.Get(context);
                long? projId = null;
                if (!string.IsNullOrWhiteSpace(projIdStr) && long.TryParse(projIdStr, out long pVal))
                {
                    projId = pVal;
                }

                string effectiveFilter = !string.IsNullOrWhiteSpace(whereClause) ? whereClause : filterStr;
                if (string.IsNullOrWhiteSpace(effectiveFilter))
                    effectiveFilter = "{}";

                // Busca e valida as regras no TDM Portal (Trava de Segurança fail-closed)
                var rules = TdmDataAssistantTemplateResolver.FetchTemplateRules(templateName, projectId: projId);
                var maskingEngine = new TdmDataPainterEngine(rules);

                var filterDoc = MongoSqlFilterParser.BuildFilter(effectiveFilter);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoExtractToVault] Iniciando extração segura com mascaramento obrigatório de PROD: {0}.{1} (Template: '{2}', {3} regras, Filtro MQL: {4})",
                    dbName, collName, templateName, rules.Count, filterDoc.ToJson());

                // 1. Instancia Cofre Seguro (Barreiras 1 e 2: ACL NTFS + Windows EFS)
                string vaultDir = SecureVaultHelper.CreateSecureVaultDirectory(customVaultDir);
                string sessionId = Path.GetFileName(vaultDir);
                VaultSessionId.Set(context, sessionId);

                // 2. Conecta no MongoDB de Produção (com resolução de profile se aplicável)
                var collection = MongoConnectionHelper.GetCollection(connStr, dbName, collName);
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

                int totalMaskedFields = 0;

                // 3. Executa streaming de documentos: SEMPRE aplica mascaramento na RAM antes de gravar no cofre
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

                long totalExtracted;
                string sha256;
                string vaultFilePath = SecureVaultHelper.WriteDocumentsToVault(
                    vaultDir, collName, DocumentStream(), out totalExtracted, out sha256);

                VaultFilePath.Set(context, vaultFilePath);
                ExtractedCount.Set(context, totalExtracted);
                TotalPiiFieldsMasked.Set(context, totalMaskedFields);
                Sha256Checksum.Set(context, sha256);

                GlobalSettings.MessageLogger.LogMessage(
                    "[MongoExtractToVault] Extração com mascaramento finalizada com sucesso! Documentos: {0}, Campos Mascarados: {1}, Checksum: {2}, Arquivo: {3}",
                    totalExtracted, totalMaskedFields, sha256, vaultFilePath);
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
