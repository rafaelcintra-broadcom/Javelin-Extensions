using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using GridTools.Javelin.Common;
using Renci.SshNet;

namespace GridTools.Javelin.HBaseActivities.Helpers
{
    public static class HBaseSshHelper
    {
        private static readonly ConcurrentDictionary<string, SshClient> SshClientCache =
            new ConcurrentDictionary<string, SshClient>();

        public static SshClient GetOrCreateSshClient(
            string rawHost,
            int port,
            string rawUsername,
            string rawPassword,
            string rawPrivateKeyPath,
            string rawPassphrase,
            int timeoutSeconds)
        {
            var decryptor = new JavelinPasswordDecryptor();
            string host = rawHost != null ? decryptor.SubstitutePasswordsInString(rawHost).Trim() : string.Empty;
            string username = rawUsername != null ? decryptor.SubstitutePasswordsInString(rawUsername).Trim() : string.Empty;
            string password = rawPassword != null ? decryptor.SubstitutePasswordsInString(rawPassword) : string.Empty;
            string keyPath = rawPrivateKeyPath != null ? decryptor.SubstitutePasswordsInString(rawPrivateKeyPath).Trim() : string.Empty;
            string passphrase = rawPassphrase != null ? decryptor.SubstitutePasswordsInString(rawPassphrase) : string.Empty;

            if (port <= 0) port = 22;
            if (timeoutSeconds <= 0) timeoutSeconds = 60;

            string cacheKey = $"{host}:{port}:{username}:{password}:{keyPath}";

            return SshClientCache.GetOrAdd(cacheKey, _ =>
            {
                ConnectionInfo connInfo;

                if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
                {
                    PrivateKeyFile keyFile;
                    if (!string.IsNullOrEmpty(passphrase))
                        keyFile = new PrivateKeyFile(keyPath, passphrase);
                    else
                        keyFile = new PrivateKeyFile(keyPath);

                    var authMethods = new AuthenticationMethod[]
                    {
                        new PrivateKeyAuthenticationMethod(username, keyFile)
                    };
                    connInfo = new ConnectionInfo(host, port, username, authMethods);
                }
                else
                {
                    connInfo = new PasswordConnectionInfo(host, port, username, password);
                }

                connInfo.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                var client = new SshClient(connInfo);
                client.Connect();
                return client;
            });
        }

        /// <summary>
        /// Executes hbase shell non-interactively via echo "command" | hbase shell -n
        /// Handles environment variable sourcing (e.g. Kerberos kinit or HBASE_HOME)
        /// </summary>
        public static SshExecutionResult RunHBaseShellCommand(
            SshClient client,
            string hbaseCommand,
            string hbaseBinPath = "hbase",
            string kinitPrincipal = null,
            string kinitKeytabPath = null,
            int commandTimeoutSec = 120)
        {
            if (!client.IsConnected)
            {
                client.Connect();
            }

            var sbScript = new StringBuilder();

            // Sourcing common profile locations in case non-login SSH has a minimal PATH
            sbScript.Append("[ -f /etc/profile ] && . /etc/profile; ");
            sbScript.Append("[ -f ~/.bash_profile ] && . ~/.bash_profile; ");
            sbScript.Append("[ -f ~/.profile ] && . ~/.profile; ");
            sbScript.Append("[ -f ~/.bashrc ] && . ~/.bashrc; ");

            // Optional Kerberos authentication on bastion host
            if (!string.IsNullOrWhiteSpace(kinitPrincipal) && !string.IsNullOrWhiteSpace(kinitKeytabPath))
            {
                sbScript.Append($"kinit -kt {kinitKeytabPath} {kinitPrincipal} && ");
            }

            // Escape double quotes inside the hbase shell statement
            string escapedHBaseCmd = hbaseCommand.Replace("\"", "\\\"");

            string hbaseBin = string.IsNullOrWhiteSpace(hbaseBinPath) ? "hbase" : hbaseBinPath;

            // Execute non-interactively using -n flag and piping via echo
            sbScript.Append($"echo \"{escapedHBaseCmd}\" | {hbaseBin} shell -n");

            // Wrap inside bash -l -c to load full login environment (JAVA_HOME, HBASE_HOME, PATH)
            string innerCommand = sbScript.ToString().Replace("'", "'\\''");
            string finalBashCommand = $"bash -l -c '{innerCommand}'";

            using (var cmd = client.CreateCommand(finalBashCommand))
            {
                cmd.CommandTimeout = TimeSpan.FromSeconds(commandTimeoutSec > 0 ? commandTimeoutSec : 120);
                string stdout = cmd.Execute();
                string stderr = cmd.Error;
                int exitCode = cmd.ExitStatus;

                // Notice: hbase shell frequently prints Java exceptions to stdout while returning exit code 0 or 1
                bool hasJavaError = (!string.IsNullOrEmpty(stdout) && (stdout.Contains("Exception:") || stdout.Contains("Error:"))) ||
                                   (!string.IsNullOrEmpty(stderr) && (stderr.Contains("Exception:") || stderr.Contains("Error:") || stderr.Contains("not found")));

                bool isSuccess = (exitCode == 0) && !hasJavaError;

                return new SshExecutionResult
                {
                    CommandExecuted = finalBashCommand,
                    Output = stdout,
                    Error = stderr,
                    ExitCode = exitCode,
                    IsSuccess = isSuccess
                };
            }
        }
    }

    public class SshExecutionResult
    {
        public string CommandExecuted { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public int ExitCode { get; set; }
        public bool IsSuccess { get; set; }
    }
}
