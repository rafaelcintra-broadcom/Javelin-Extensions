using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Cassandra;
using GridTools.Javelin.Common;
using GridTools.Javelin.CassandraActivities.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.CassandraActivities
{
    [DisplayName("Cassandra Prod to Non-Prod Pipeline")]
    [Description("Direct NoSQL Migration: Extract from Cassandra Prod -> Encrypted Secure Vault -> Data Assistant Tag Template Masking -> Load to Non-Prod -> Forensics Zero-Fill Shred")]
    public class CassandraProdToNonProdPipelineActivity : JavelinNativeActivity
    {
        // --- Conexão de Origem (Prod) ---
        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Source Connection Profile / Host")]
        [Description("TDM Connection Profile Name (e.g. CASSANDRA_PROD) or Host:Port")]
        public InArgument<string> SourceConnectionString { get; set; }

        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Source Keyspace Name")]
        public InArgument<string> SourceKeyspaceName { get; set; }

        [RequiredArgument]
        [Category("1. Source (Production)")]
        [DisplayName("Source Table / ColumnFamily")]
        public InArgument<string> SourceTableName { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Source CQL WHERE Clause")]
        [Description("Cláusula WHERE CQL opcional (ex: status = 'ACTIVE' ALLOW FILTERING)")]
        public InArgument<string> SourceWhereClause { get; set; }

        [Category("1. Source (Production)")]
        [DisplayName("Extraction Limit")]
        [Description("Maximum rows to extract (0 or null for all)")]
        public InArgument<int?> Limit { get; set; }

        // --- Conexão de Destino (Não-Prod / Target) ---
        [RequiredArgument]
        [Category("2. Target (Non-Production)")]
        [DisplayName("Target Connection Profile / Host")]
        [Description("TDM Connection Profile Name (e.g. CASSANDRA_HML) or Host:Port")]
        public InArgument<string> TargetConnectionString { get; set; }

        [RequiredArgument]
        [Category("2. Target (Non-Production)")]
        [DisplayName("Target Keyspace Name")]
        public InArgument<string> TargetKeyspaceName { get; set; }

        [RequiredArgument]
        [Category("2. Target (Non-Production)")]
        [DisplayName("Target Table Name")]
        public InArgument<string> TargetTableName { get; set; }

        [Category("2. Target (Non-Production)")]
        [DisplayName("Truncate Target Table If Exists")]
        [Description("Se True, executa TRUNCATE na tabela de destino antes da carga")]
        [DefaultValue(false)]
        public InArgument<bool> TruncateTableIfExists { get; set; }

        // --- Data Assistant Tag Template (Obrigatório para Garantia de Mascaramento) ---
        [RequiredArgument]
        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("Data Assistant Tag Template Name")]
        [Description("Nome do Template de TAG do Data Assistant no TDM Portal (ex: CASSANDRA_PII_TAG_TEMPLATE). OBRIGATÓRIO: O pipeline não roda sem este template.")]
        public InArgument<string> DataAssistantTagTemplate { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Portal URL")]
        [Description("URL do TDM Portal (ex: https://tdm-server:8443). Se em branco, localiza automaticamente o application.properties.")]
        public InArgument<string> TdmPortalUrl { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Username")]
        public InArgument<string> TdmUsername { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Password")]
        public InArgument<string> TdmPassword { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Auth Token (JWT)")]
        public InArgument<string> TdmAuthToken { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Project Name / ID")]
        public InArgument<string> ProjectIdentifier { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Version ID")]
        public InArgument<long?> VersionIdentifier { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("Create TDM NoSQL Generator")]
        [Description("Se habilitado, registra e publica um NoSQL Data Generator permanente no TDM Portal com as regras e esquema mascarados")]
        [DefaultValue(false)]
        public InArgument<bool> CreateTdmNoSqlGenerator { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("Generator Generation Mode")]
        [Description("Formulas (fórmulas generativas autônomas do Data Painter) ou ReplayMaskedData (grava os valores literais mascarados da extração para replay rápido)")]
        [DefaultValue("Formulas")]
        public InArgument<string> GeneratorMode { get; set; }

        [Category("3. Data Assistant Tag Template & TDM Portal")]
        [DisplayName("TDM Generator Name")]
        [Description("Nome personalizado para o NoSQL Generator criado no TDM Portal (opcional)")]
        public InArgument<string> TdmGeneratorName { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Batch Size")]
        [DefaultValue(1000)]
        public InArgument<int?> BatchSize { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Dry Run")]
        [DefaultValue(false)]
        public InArgument<bool> DryRun { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Enable Debug Logs")]
        [DefaultValue(false)]
        public InArgument<bool> EnableDebugLogs { get; set; }

        [Category("4. Security Vault & Performance")]
        [DisplayName("Auto Shred Vault")]
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
                    throw new InvalidOperationException("SEGURANÇA TDM: O campo 'DataAssistantTagTemplate' é obrigatório.");
                }

                string srcConn = SourceConnectionString.Get(context);
                string srcKs = SourceKeyspaceName.Get(context);
                string srcTbl = SourceTableName.Get(context);
                string whereClause = SourceWhereClause.Get(context);
                int? limit = Limit.Get(context);

                string tgtConn = TargetConnectionString.Get(context);
                string tgtKs = TargetKeyspaceName.Get(context);
                string tgtTbl = TargetTableName.Get(context);
                if (string.IsNullOrWhiteSpace(tgtTbl)) tgtTbl = srcTbl;

                bool truncateTgt = TruncateTableIfExists.Get(context);
                int batchSize = BatchSize.Get(context) ?? 1000;
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

                GlobalSettings.MessageLogger.LogMessage("[CassandraPipeline] ==========================================================");
                GlobalSettings.MessageLogger.LogMessage("[CassandraPipeline] Validando Tag Template '{0}' no TDM Portal...", templateName);

                var resolvedRules = TdmDataAssistantTemplateResolver.FetchTemplateRules(
                    templateName, portalUrl, tdmToken, tdmUser, tdmPass, projId, debugLogs);

                GlobalSettings.MessageLogger.LogMessage("[CassandraPipeline] Tag Template validado com sucesso! ({0} regras ativas)", resolvedRules.Count);

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

                // 1. Conexão com Cassandra de Origem
                var srcSession = CassandraConnectionHelper.GetSession(srcConn, srcKs, portalUrl, tdmToken, tdmUser, tdmPass, projId, debugLogs);

                string cqlQuery = $"SELECT * FROM {srcKs}.{srcTbl}";
                if (!string.IsNullOrWhiteSpace(whereClause))
                {
                    string cleanWhere = whereClause.Trim();
                    if (!cleanWhere.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase)) cleanWhere = "WHERE " + cleanWhere;
                    cqlQuery += " " + cleanWhere;
                }
                if (limit.HasValue && limit.Value > 0)
                {
                    cqlQuery += $" LIMIT {limit.Value}";
                }

                GlobalSettings.MessageLogger.LogMessage("[CassandraPipeline] Extraindo CQL: {0}", cqlQuery);

                var rowSet = srcSession.Execute(cqlQuery);
                var maskingEngine = new CassandraDataPainterEngine(resolvedRules, generatorContext);
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

                        var sampleRow = rowSet.FirstOrDefault();
                        if (sampleRow != null)
                        {
                            var sRowDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (var col in rowSet.Columns)
                            {
                                sRowDict[col.Name] = sampleRow.GetValue<object>(col.Name);
                            }
                            var sObj = JObject.FromObject(sRowDict);
                            int dummy;
                            var mDict = maskingEngine.MaskRow(sRowDict, out dummy);
                            var mObj = JObject.FromObject(mDict);

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
                var columns = rowSet.Columns.Select(c => c.Name).ToList();

                using (var swVault = new StreamWriter(vaultFile, false, Encoding.UTF8))
                {
                    foreach (var row in rowSet)
                    {
                        var rowDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var col in columns)
                        {
                            rowDict[col] = row.GetValue<object>(col);
                        }

                        int maskedInRow;
                        var maskedRow = maskingEngine.MaskRow(rowDict, out maskedInRow, piiTypeStats);
                        totalMaskedFields += maskedInRow;
                        extractedCount++;

                        swVault.WriteLine(JsonConvert.SerializeObject(maskedRow));
                    }
                }

                GlobalSettings.MessageLogger.LogMessage(
                    "[CassandraPipeline] Extração e mascaramento concluídos: {0} registros gravados no cofre (Campos PII: {1})",
                    extractedCount, totalMaskedFields);

                if (piiTypeStats.Count > 0)
                {
                    var piiBreakdown = piiTypeStats.Select(kvp => $"{kvp.Key}: {kvp.Value}").ToList();
                    GlobalSettings.MessageLogger.LogMessage(
                        "[CassandraPipeline] Detalhamento de PII mascarados por tipo/tag: [{0}]", string.Join(", ", piiBreakdown));
                }

                // 3. Carga em NÃO-PROD
                long loadedCount = 0;
                if (!dryRun)
                {
                    var tgtSession = CassandraConnectionHelper.GetSession(tgtConn, tgtKs, portalUrl, tdmToken, tdmUser, tdmPass, projId, debugLogs);
                    if (truncateTgt)
                    {
                        GlobalSettings.MessageLogger.LogMessage("[CassandraPipeline] TruncateTableIfExists=True: Truncando tabela '{0}.{1}'...", tgtKs, tgtTbl);
                        tgtSession.Execute($"TRUNCATE {tgtKs}.{tgtTbl}");
                    }

                    using (var srVault = new StreamReader(vaultFile, Encoding.UTF8))
                    {
                        string line;
                        while ((line = srVault.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var rowDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(line);

                            var colNames = string.Join(", ", rowDict.Keys);
                            var paramPlaceholders = string.Join(", ", rowDict.Keys.Select(k => "?"));
                            var insertCql = $"INSERT INTO {tgtKs}.{tgtTbl} ({colNames}) VALUES ({paramPlaceholders})";

                            var ps = tgtSession.Prepare(insertCql);
                            var bs = ps.Bind(rowDict.Values.ToArray());
                            tgtSession.Execute(bs);
                            loadedCount++;
                        }
                    }
                }
                else
                {
                    loadedCount = extractedCount;
                    GlobalSettings.MessageLogger.LogMessage("[CassandraPipeline] DryRun ativo: gravação em destino suprimida.");
                }

                sw.Stop();
                long elapsedMs = sw.ElapsedMilliseconds;

                TotalProcessedCount.Set(context, loadedCount);
                TotalPiiFieldsMasked.Set(context, totalMaskedFields);
                ExecutionTimeMs.Set(context, elapsedMs);

                var auditObj = new
                {
                    pipeline = "Cassandra Prod to Non-Prod",
                    tagTemplateUsed = templateName,
                    source = $"{srcKs}.{srcTbl}",
                    target = $"{tgtKs}.{tgtTbl}",
                    totalDocuments = loadedCount,
                    totalPiiFieldsMasked = totalMaskedFields,
                    piiMaskedBreakdown = piiTypeStats,
                    dryRun = dryRun,
                    executionTimeMs = elapsedMs,
                    targetVerification = new
                    {
                        keyspace = tgtKs,
                        table = tgtTbl,
                        cqlQuery = $"SELECT * FROM {tgtKs}.{tgtTbl} LIMIT 5;"
                    },
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                AuditSummaryJson.Set(context, JsonConvert.SerializeObject(auditObj, Formatting.Indented));

                GlobalSettings.MessageLogger.LogMessage(
                    "[CassandraPipeline] Pipeline concluído com sucesso em {0}ms! Processados: {1}, PII Mascarados: {2}",
                    elapsedMs, loadedCount, totalMaskedFields);

                if (!dryRun)
                {
                    GlobalSettings.MessageLogger.LogMessage(
                        "[CassandraPipeline] Consulta para validar no Cassandra Destino (cqlsh):\n" +
                        "  SELECT * FROM {0}.{1} LIMIT 5;\n" +
                        "  SELECT count(*) FROM {0}.{1};",
                        tgtKs, tgtTbl);
                }
            }
            catch (Exception ex)
            {
                var bex = new BusinessException(ex.Message, DisplayName, ex.InnerException);
                if (!continueOnErrorInExecMode) throw bex;
            }
            finally
            {
                if (autoShred && !string.IsNullOrWhiteSpace(vaultDir) && Directory.Exists(vaultDir))
                {
                    SecureVaultHelper.DestroyVaultDirectory(vaultDir);
                }
            }
        }
    }
}
