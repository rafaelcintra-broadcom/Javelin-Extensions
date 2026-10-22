using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GridTools.Javelin.Common;
using GridTools.Javelin.NoSqlFileActivities.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.NoSqlFileActivities
{
    [DisplayName("NoSQL File Prod To Non-Prod Masking Pipeline")]
    [Description("Executa pipeline in-flight de extração de arquivos NoSQL (JSON, NDJSON, JSON Lines), mascaramento via Tag Template TDM e gravação segura em Não-Produção com Cofre Seguro.")]
    public class NoSqlFileProdToNonProdPipelineActivity : CodeActivity
    {
        [Category("1. Source Files (PROD)")]
        [RequiredArgument]
        [DisplayName("Source File / Directory Path")]
        [Description("Caminho do arquivo de origem (.json, .ndjson, .jsonl) ou diretório contendo arquivos de Produção.")]
        public InArgument<string> SourcePath { get; set; }

        [Category("1. Source Files (PROD)")]
        [DisplayName("Search Pattern")]
        [Description("Padrão de busca de arquivos caso SourcePath seja um diretório (ex: *.json, *.ndjson).")]
        public InArgument<string> SearchPattern { get; set; }

        [Category("1. Source Files (PROD)")]
        [DisplayName("Extraction Limit")]
        [Description("Limite máximo de registros/documentos a processar.")]
        public InArgument<int?> Limit { get; set; }

        [Category("2. Target Files (NON-PROD)")]
        [RequiredArgument]
        [DisplayName("Target File / Directory Path")]
        [Description("Caminho do arquivo ou diretório de destino em Não-Produção.")]
        public InArgument<string> TargetPath { get; set; }

        [Category("2. Target Files (NON-PROD)")]
        [DisplayName("Overwrite Target If Exists")]
        [Description("Se True, sobrescreve o arquivo de destino caso já exista.")]
        public InArgument<bool> OverwriteTargetIfExists { get; set; }

        [Category("3. TDM Tag Template / Data Assistant")]
        [RequiredArgument]
        [DisplayName("Data Assistant Tag Template")]
        [Description("Nome do Tag Template do Data Assistant no TDM Portal (ex: 'NOSQL_PII_TAG_TEMPLATE', 'PII_DEFAULT').")]
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

                string srcPath = SourcePath.Get(context);
                string searchPattern = SearchPattern.Get(context) ?? "*.json";
                int? limit = Limit.Get(context);

                string tgtPath = TargetPath.Get(context);
                bool overwrite = OverwriteTargetIfExists.Get(context);
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

                GlobalSettings.MessageLogger.LogMessage("[NoSqlFilePipeline] ==========================================================");
                GlobalSettings.MessageLogger.LogMessage("[NoSqlFilePipeline] Validando Tag Template '{0}' no TDM Portal...", templateName);

                var resolvedRules = TdmDataAssistantTemplateResolver.FetchTemplateRules(
                    templateName, portalUrl, tdmToken, tdmUser, tdmPass, projId, debugLogs);

                GlobalSettings.MessageLogger.LogMessage("[NoSqlFilePipeline] Tag Template validado com sucesso! ({0} regras ativas)", resolvedRules.Count);

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

                // Localiza arquivos de origem
                List<string> filesToProcess = new List<string>();
                if (File.Exists(srcPath))
                {
                    filesToProcess.Add(srcPath);
                }
                else if (Directory.Exists(srcPath))
                {
                    filesToProcess.AddRange(Directory.GetFiles(srcPath, searchPattern, SearchOption.TopDirectoryOnly));
                }
                else
                {
                    throw new FileNotFoundException("Arquivo ou diretório de origem não encontrado: " + srcPath);
                }

                GlobalSettings.MessageLogger.LogMessage("[NoSqlFilePipeline] Encontrado(s) {0} arquivo(s) para processamento.", filesToProcess.Count);

                var maskingEngine = new NoSqlFileDataPainterEngine(resolvedRules, generatorContext);
                int totalMaskedFields = 0;
                var piiTypeStats = new Dictionary<string, int>();

                // Opcional: Criar NoSQL Data Generator permanente no TDM Portal (Formulas ou Replay)
                if (createNoSqlGen && filesToProcess.Count > 0)
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

                        string firstContent = File.ReadAllText(filesToProcess[0], Encoding.UTF8).Trim();
                        JObject sampleObj = null;
                        if (firstContent.StartsWith("["))
                        {
                            var arr = JArray.Parse(firstContent);
                            if (arr.Count > 0 && arr[0] is JObject jo) sampleObj = jo;
                        }
                        else
                        {
                            using (var sr = new StringReader(firstContent))
                            {
                                string l = sr.ReadLine();
                                if (!string.IsNullOrWhiteSpace(l)) sampleObj = JObject.Parse(l);
                            }
                        }

                        if (sampleObj != null)
                        {
                            int dummy;
                            var mObj = maskingEngine.MaskRow(sampleObj, out dummy);
                            string genCollName = Path.GetFileNameWithoutExtension(filesToProcess[0]);

                            TdmNoSqlGeneratorManager.CreateNoSqlGenerator(
                                resolvedTdmUrl, authHeader, effectiveProjId, effectiveVerId, customGenName, genCollName, resolvedRules, sampleObj, mObj, dataMode, debugLogs);
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
                vaultFile = Path.Combine(vaultDir, $"NoSqlFiles_{DateTime.Now:yyyyMMdd_HHmmss}.vault");

                long extractedCount = 0;

                using (var swVault = new StreamWriter(vaultFile, false, Encoding.UTF8))
                {
                    foreach (var file in filesToProcess)
                    {
                        GlobalSettings.MessageLogger.LogMessage("[NoSqlFilePipeline] Lendo e mascarando arquivo: {0}", file);
                        string content = File.ReadAllText(file, Encoding.UTF8).Trim();

                        if (content.StartsWith("["))
                        {
                            // Array JSON
                            var arr = JArray.Parse(content);
                            foreach (var elem in arr)
                            {
                                if (elem is JObject obj)
                                {
                                    int maskedInDoc;
                                    var maskedObj = maskingEngine.MaskRow(obj, out maskedInDoc, piiTypeStats);
                                    totalMaskedFields += maskedInDoc;
                                    extractedCount++;
                                    swVault.WriteLine(maskedObj.ToString(Formatting.None));
                                }

                                if (limit.HasValue && extractedCount >= limit.Value) break;
                            }
                        }
                        else
                        {
                            // NDJSON / JSON Lines ou único objeto JSON
                            using (var sr = new StringReader(content))
                            {
                                string line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (string.IsNullOrWhiteSpace(line)) continue;
                                    var obj = JObject.Parse(line);

                                    int maskedInDoc;
                                    var maskedObj = maskingEngine.MaskRow(obj, out maskedInDoc, piiTypeStats);
                                    totalMaskedFields += maskedInDoc;
                                    extractedCount++;
                                    swVault.WriteLine(maskedObj.ToString(Formatting.None));

                                    if (limit.HasValue && extractedCount >= limit.Value) break;
                                }
                            }
                        }

                        if (limit.HasValue && extractedCount >= limit.Value) break;
                    }
                }

                GlobalSettings.MessageLogger.LogMessage(
                    "[NoSqlFilePipeline] Extração e mascaramento concluídos: {0} documentos gravados no cofre (Campos PII: {1})",
                    extractedCount, totalMaskedFields);

                if (piiTypeStats.Count > 0)
                {
                    var piiBreakdown = piiTypeStats.Select(kvp => $"{kvp.Key}: {kvp.Value}").ToList();
                    GlobalSettings.MessageLogger.LogMessage(
                        "[NoSqlFilePipeline] Detalhamento de PII mascarados por tipo/tag: [{0}]", string.Join(", ", piiBreakdown));
                }

                // 3. Carga / Gravação em NÃO-PROD
                long loadedCount = 0;
                if (!dryRun)
                {
                    string targetFile = tgtPath;
                    if (Directory.Exists(tgtPath) || (!Path.HasExtension(tgtPath) && !File.Exists(tgtPath)))
                    {
                        if (!Directory.Exists(tgtPath)) Directory.CreateDirectory(tgtPath);
                        targetFile = Path.Combine(tgtPath, "Masked_NoSqlData.json");
                    }

                    if (File.Exists(targetFile) && !overwrite)
                    {
                        throw new InvalidOperationException($"Arquivo de destino '{targetFile}' já existe e OverwriteTargetIfExists=False.");
                    }

                    using (var srVault = new StreamReader(vaultFile, Encoding.UTF8))
                    using (var swTarget = new StreamWriter(targetFile, false, Encoding.UTF8))
                    {
                        swTarget.WriteLine("[");
                        bool first = true;
                        string line;
                        while ((line = srVault.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (!first) swTarget.WriteLine(",");
                            swTarget.Write("  " + line);
                            first = false;
                            loadedCount++;
                        }
                        swTarget.WriteLine();
                        swTarget.WriteLine("]");
                    }

                    GlobalSettings.MessageLogger.LogMessage("[NoSqlFilePipeline] Gravação concluída no Destino: {0} ({1} documentos)", targetFile, loadedCount);
                }
                else
                {
                    GlobalSettings.MessageLogger.LogMessage("[NoSqlFilePipeline] Modo DRY-RUN ativado: Gravação em Não-Prod ignorada.");
                }

                swTotal.Stop();

                TotalExtracted.Set(context, extractedCount);
                TotalLoaded.Set(context, loadedCount);
                MaskedFieldsCount.Set(context, totalMaskedFields);

                var summary = new
                {
                    Source = srcPath,
                    Target = tgtPath,
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
    }
}
