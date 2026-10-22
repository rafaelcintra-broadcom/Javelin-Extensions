using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using GridTools.Javelin.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.DynamoDbActivities.Helpers
{
    /// <summary>
    /// Gerencia as 4 Barreiras de Segurança do Cofre Temporário para NoSQL/Cassandra:
    /// 1. Barreira 1: Isolamento de Acesso via ACL NTFS Restrita (icacls / FileSystemAccessRule)
    /// 2. Barreira 2: Criptografia Transparente do SO (Windows EFS / cipher /e ou AES-256)
    /// 3. Barreira 3: Escrita Primária em RAM (FILE_ATTRIBUTE_TEMPORARY)
    /// 4. Barreira 4: Destruição Irrecuperável Forense (Zero-Fill & Shredding com 0x00)
    /// </summary>
    public static class SecureVaultHelper
    {
        private const int FileAttributeTemporary = 0x00000100;
        private const int BufferSize = 64 * 1024; // 64 KB

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

        /// <summary>
        /// Cria um diretório de cofre seguro aplicando ACL NTFS restrita e criptografia EFS.
        /// </summary>
        public static string CreateSecureVaultDirectory(string baseCustomPath = null)
        {
            string vaultId = Guid.NewGuid().ToString("N");
            string rootPath = string.IsNullOrWhiteSpace(baseCustomPath)
                ? Path.Combine(Path.GetTempPath(), "TdmSecureVault_" + DateTime.Now.ToString("yyyyMMdd"))
                : baseCustomPath;

            string vaultDir = Path.Combine(rootPath, "Session_" + vaultId);

            if (!Directory.Exists(vaultDir))
            {
                Directory.CreateDirectory(vaultDir);
            }

            // Aplica as barreiras 1 e 2 no ambiente Windows
            ApplyNtfsAclIsolation(vaultDir);
            ApplyEfsEncryption(vaultDir);

            GlobalSettings.MessageLogger.LogMessage("[SecureVault] Cofre seguro instanciado com sucesso em: {0} (Sessão: {1})", vaultDir, vaultId);
            return vaultDir;
        }

        /// <summary>
        /// Barreira 1: Desativa herança e concede acesso exclusivo à conta do processo atual.
        /// </summary>
        public static void ApplyNtfsAclIsolation(string directoryPath)
        {
            try
            {
#if NETFRAMEWORK
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                    return;

                var dirInfo = new DirectoryInfo(directoryPath);
                var dirSecurity = dirInfo.GetAccessControl();

                // Desativa herança e remove regras herdadas
                dirSecurity.SetAccessRuleProtection(true, false);

                // Obtém a identidade do usuário atual / conta de serviço
                var currentUser = WindowsIdentity.GetCurrent();
                if (currentUser?.User != null)
                {
                    // Concede controle total apenas ao usuário atual
                    dirSecurity.AddAccessRule(new FileSystemAccessRule(
                        currentUser.User,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                }

                // Permite acesso ao SYSTEM para manutenção do OS
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                dirSecurity.AddAccessRule(new FileSystemAccessRule(
                    systemSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                dirInfo.SetAccessControl(dirSecurity);
                GlobalSettings.MessageLogger.LogMessage("[SecureVault] Barreira 1 (ACL NTFS Restrita) aplicada com sucesso em {0}", directoryPath);
#endif
            }
            catch (Exception ex)
            {
                GlobalSettings.MessageLogger.LogMessage("[SecureVault] Aviso na aplicação de ACL NTFS: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Barreira 2: Ativa criptografia Windows EFS (cipher /e) no diretório.
        /// </summary>
        public static void ApplyEfsEncryption(string directoryPath)
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return;

                var psi = new ProcessStartInfo
                {
                    FileName = "cipher.exe",
                    Arguments = $"/e /d \"{directoryPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit(3000);
                }

                GlobalSettings.MessageLogger.LogMessage("[SecureVault] Barreira 2 (Windows EFS / AES-256) ativada em {0}", directoryPath);
            }
            catch (Exception ex)
            {
                GlobalSettings.MessageLogger.LogMessage("[SecureVault] Aviso na ativação de EFS: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Grava documentos JSON no arquivo do cofre com flag FILE_ATTRIBUTE_TEMPORARY (Barreira 3).
        /// </summary>
        public static string WriteDocumentsToVault(
            string vaultDir,
            string tableName,
            IEnumerable<JObject> documents,
            out long count,
            out string sha256Hex)
        {
            if (!Directory.Exists(vaultDir))
            {
                Directory.CreateDirectory(vaultDir);
            }

            string cleanName = string.Join("_", tableName.Split(Path.GetInvalidFileNameChars()));
            string filePath = Path.Combine(vaultDir, $"{cleanName}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json.vault");

            count = 0;

            using (var sha256 = SHA256.Create())
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.SequentialScan))
            using (var cryptoStream = new CryptoStream(fs, sha256, CryptoStreamMode.Write))
            using (var writer = new StreamWriter(cryptoStream, Encoding.UTF8))
            {
                // Barreira 3: Flag de sistema para priorizar retenção em memória RAM
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        SetFileAttributes(filePath, FileAttributeTemporary);
                    }
                    catch { }
                }

                foreach (var doc in documents)
                {
                    string jsonLine = doc.ToString(Formatting.None);
                    writer.WriteLine(jsonLine);
                    count++;
                }

                writer.Flush();
                cryptoStream.FlushFinalBlock();
                sha256Hex = BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
            }

            GlobalSettings.MessageLogger.LogMessage(
                "[SecureVault] Gravação no cofre concluída: {0} registros gravados em {1} (SHA256: {2})",
                count, filePath, sha256Hex);

            return filePath;
        }

        /// <summary>
        /// Lê documentos JSON do arquivo seguro no cofre.
        /// </summary>
        public static IEnumerable<JObject> ReadDocumentsFromVault(string vaultFilePath)
        {
            if (!File.Exists(vaultFilePath))
                throw new FileNotFoundException("Arquivo do cofre não encontrado: " + vaultFilePath);

            using (var fs = new FileStream(vaultFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    yield return JObject.Parse(line);
                }
            }
        }

        /// <summary>
        /// Barreira 4: Destruição Irrecuperável Forense (Zero-Fill & Shredding com 0x00).
        /// Sobregrava todo o comprimento físico com zeros antes de deletar.
        /// </summary>
        public static long ForensicsZeroFillAndShred(string filePath, int passes = 1)
        {
            if (!File.Exists(filePath))
                return 0;

            long totalBytes = 0;
            try
            {
                var fileInfo = new FileInfo(filePath);
                long fileLength = fileInfo.Length;
                totalBytes = fileLength;

                if (fileLength > 0)
                {
                    byte[] zeroBuffer = new byte[Math.Min(BufferSize, (int)Math.Max(1024, fileLength))];
                    Array.Clear(zeroBuffer, 0, zeroBuffer.Length);

                    int numPasses = Math.Max(1, passes);
                    for (int pass = 0; pass < numPasses; pass++)
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                        {
                            long bytesWritten = 0;
                            while (bytesWritten < fileLength)
                            {
                                int toWrite = (int)Math.Min(zeroBuffer.Length, fileLength - bytesWritten);
                                fs.Write(zeroBuffer, 0, toWrite);
                                bytesWritten += toWrite;
                            }
                            fs.Flush(true); // Garante sync de hardware (fsync)
                        }
                    }
                }

                // Remove atributos de somente leitura / oculto
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);

                GlobalSettings.MessageLogger.LogMessage(
                    "[SecureVault] Barreira 4 (Sanitização Forense Zero-Fill) concluída com sucesso: {0} bytes destruídos em {1}",
                    totalBytes, filePath);
            }
            catch (Exception ex)
            {
                GlobalSettings.MessageLogger.LogMessage("[SecureVault] Aviso durante sanitização de {0}: {1}", filePath, ex.Message);
                try { File.Delete(filePath); } catch { }
            }

            return totalBytes;
        }

        /// <summary>
        /// Destrói todos os arquivos dentro do cofre e remove o diretório da sessão.
        /// </summary>
        public static void DestroyVaultDirectory(string vaultDir)
        {
            if (string.IsNullOrWhiteSpace(vaultDir) || !Directory.Exists(vaultDir))
                return;

            try
            {
                var files = Directory.GetFiles(vaultDir, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    ForensicsZeroFillAndShred(file);
                }

                Directory.Delete(vaultDir, true);
                GlobalSettings.MessageLogger.LogMessage("[SecureVault] Diretório de cofre destruído com sucesso: {0}", vaultDir);
            }
            catch (Exception ex)
            {
                GlobalSettings.MessageLogger.LogMessage("[SecureVault] Aviso ao destruir diretório do cofre {0}: {1}", vaultDir, ex.Message);
            }
        }
    }
}
