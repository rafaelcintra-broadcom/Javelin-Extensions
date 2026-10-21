using System;
using System.Activities;
using System.ComponentModel;
using System.IO;
using GridTools.Javelin.Common;
using GridTools.Javelin.MongoDbActivities.Helpers;

namespace GridTools.Javelin.MongoDbActivities
{
    [DisplayName("MongoDB Vault Shred & Destroy")]
    [Description("Forensic Zero-Fill (0x00) byte overwriting and destruction of Secure Vault files or directories")]
    public class MongoVaultShredActivity : JavelinNativeActivity
    {
        [RequiredArgument]
        [DisplayName("Target Path (File or Directory)")]
        [Description("Full path to the vault file (.vault) or directory (Session_*) to shred")]
        public InArgument<string> TargetPath { get; set; }

        [DisplayName("Overwrite Passes")]
        [Description("Number of zero-fill overwriting passes (default: 1 pass with 0x00)")]
        [DefaultValue(1)]
        public InArgument<int?> OverwritePasses { get; set; }

        [DisplayName("Is Success")]
        public OutArgument<bool> IsSuccess { get; set; }

        [DisplayName("Bytes Overwritten")]
        public OutArgument<long> BytesOverwritten { get; set; }

        protected override void Execute(NativeActivityContext context)
        {
            try
            {
                base.Execute(context);

                string path = TargetPath.Get(context);
                int passes = OverwritePasses.Get(context) ?? 1;

                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentException("TargetPath não pode ser vazio.");
                }

                long totalBytes = 0;

                if (File.Exists(path))
                {
                    totalBytes = SecureVaultHelper.ForensicsZeroFillAndShred(path, passes);
                }
                else if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        totalBytes += SecureVaultHelper.ForensicsZeroFillAndShred(f, passes);
                    }
                    Directory.Delete(path, true);
                }
                else
                {
                    GlobalSettings.MessageLogger.LogMessage("[VaultShred] Caminho não encontrado (já destruído): {0}", path);
                }

                BytesOverwritten.Set(context, totalBytes);
                IsSuccess.Set(context, true);

                GlobalSettings.MessageLogger.LogMessage(
                    "[VaultShred] Sanitização forense concluída com sucesso: {0} bytes sobrescritos e destruídos em {1}",
                    totalBytes, path);
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
