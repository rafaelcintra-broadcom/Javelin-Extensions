using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using GridTools.Javelin.ZoweActivities.Models;

namespace GridTools.Javelin.ZoweActivities.Helpers
{
    public static class ZoweCliHelper
    {
        public static string BuildArguments(string zoweArguments, ZoweProfileOptions options = null)
        {
            var fullArgs = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(zoweArguments))
            {
                fullArgs.Append(zoweArguments);
            }

            if (options != null)
            {
                // Profile options
                if (!string.IsNullOrWhiteSpace(options.ZosmfProfile))
                {
                    fullArgs.Append($" --zosmf-p \"{EscapeForCli(options.ZosmfProfile)}\"");
                }
                if (!string.IsNullOrWhiteSpace(options.BaseProfile))
                {
                    fullArgs.Append($" --base-p \"{EscapeForCli(options.BaseProfile)}\"");
                }

                // Direct Host / Port / Base Path
                if (!string.IsNullOrWhiteSpace(options.Host))
                    fullArgs.Append($" --host \"{EscapeForCli(options.Host)}\"");

                if (options.Port.HasValue && options.Port.Value > 0)
                    fullArgs.Append($" --port {options.Port.Value}");

                if (!string.IsNullOrWhiteSpace(options.BasePath))
                    fullArgs.Append($" --base-path \"{EscapeForCli(options.BasePath)}\"");

                // Authentication: API Token vs User/Password
                if (!string.IsNullOrWhiteSpace(options.TokenValue))
                {
                    fullArgs.Append($" --token-value \"{EscapeForCli(options.TokenValue)}\"");
                    if (!string.IsNullOrWhiteSpace(options.TokenType))
                    {
                        fullArgs.Append($" --token-type \"{EscapeForCli(options.TokenType)}\"");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(options.User))
                        fullArgs.Append($" --user \"{EscapeForCli(options.User)}\"");

                    if (!string.IsNullOrWhiteSpace(options.Password))
                        fullArgs.Append($" --password \"{EscapeForCli(options.Password)}\"");
                }

                // Client Certificate Auth
                if (!string.IsNullOrWhiteSpace(options.CertPath))
                    fullArgs.Append($" --cert-file \"{EscapeForCli(options.CertPath)}\"");

                if (!string.IsNullOrWhiteSpace(options.CertKeyPath))
                    fullArgs.Append($" --cert-key-file \"{EscapeForCli(options.CertKeyPath)}\"");

                // SSL validation
                if (options.RejectUnauthorized.HasValue)
                    fullArgs.Append($" --ru {options.RejectUnauthorized.Value.ToString().ToLower()}");
            }

            return fullArgs.ToString();
        }

        public static ZoweCliExecutionResult ExecuteCommand(
            string zoweArguments,
            ZoweProfileOptions options = null,
            string customZowePath = null,
            int timeoutSeconds = 120,
            string workingDirectory = null)
        {
            var result = new ZoweCliExecutionResult();
            string binary = !string.IsNullOrWhiteSpace(customZowePath) ? customZowePath : "zowe";
            string fullArgs = BuildArguments(zoweArguments, options);

            result.RawCommand = $"{binary} {fullArgs}";

            var psi = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = fullArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = psi;

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                            stdoutBuilder.AppendLine(e.Data);
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                            stderrBuilder.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    bool exited = process.WaitForExit(timeoutSeconds > 0 ? timeoutSeconds * 1000 : 120000);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        result.Success = false;
                        result.ExitCode = -1;
                        result.StdOut = stdoutBuilder.ToString();
                        result.StdErr = stderrBuilder.ToString();
                        result.FormattedError = $"Timeout de execução ({timeoutSeconds}s) excedido para o comando Zowe CLI.";
                        return result;
                    }

                    result.ExitCode = process.ExitCode;
                    result.StdOut = stdoutBuilder.ToString();
                    result.StdErr = stderrBuilder.ToString();
                    result.Success = (process.ExitCode == 0);

                    if (!result.Success)
                    {
                        string errText = !string.IsNullOrWhiteSpace(result.StdErr) ? result.StdErr : result.StdOut;
                        result.FormattedError = errText;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ExitCode = -2;
                result.StdErr = ex.ToString();
                result.FormattedError = ex.Message;
            }

            return result;
        }

        public static string EscapeForCli(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace("\"", "\\\"");
        }
    }
}
