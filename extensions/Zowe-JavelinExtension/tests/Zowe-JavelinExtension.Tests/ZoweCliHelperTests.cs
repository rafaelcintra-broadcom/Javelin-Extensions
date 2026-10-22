using System;
using GridTools.Javelin.ZoweActivities.Helpers;
using GridTools.Javelin.ZoweActivities.Models;
using Xunit;

namespace GridTools.Javelin.ZoweActivities.Tests
{
    public class ZoweCliHelperTests
    {
        [Fact]
        public void BuildArguments_WithProfileName_AppendsZosmfProfileFlag()
        {
            var options = new ZoweProfileOptions
            {
                ZosmfProfile = "zosmf_prod",
                BaseProfile = "base_apiml"
            };

            string args = ZoweCliHelper.BuildArguments("zos-jobs list jobs", options);

            Assert.Contains("zos-jobs list jobs", args);
            Assert.Contains("--zosmf-p \"zosmf_prod\"", args);
            Assert.Contains("--base-p \"base_apiml\"", args);
        }

        [Fact]
        public void BuildArguments_WithApiToken_PrefersTokenOverUserAndPassword()
        {
            var options = new ZoweProfileOptions
            {
                Host = "apiml.company.com",
                Port = 10010,
                TokenValue = "eyJh...sample_token...123",
                TokenType = "apimlAuthenticationToken",
                User = "IBMUSER",
                Password = "Password123"
            };

            string args = ZoweCliHelper.BuildArguments("zos-files list ds HLQ.*", options);

            Assert.Contains("--host \"apiml.company.com\"", args);
            Assert.Contains("--port 10010", args);
            Assert.Contains("--token-value \"eyJh...sample_token...123\"", args);
            Assert.Contains("--token-type \"apimlAuthenticationToken\"", args);

            // User and password should NOT be present when TokenValue is provided
            Assert.DoesNotContain("--user", args);
            Assert.DoesNotContain("--password", args);
        }

        [Fact]
        public void BuildArguments_WithDirectUserPassword_AppendsAuthFlags()
        {
            var options = new ZoweProfileOptions
            {
                Host = "zosmf.company.com",
                Port = 10443,
                User = "SYSADM",
                Password = "SecretPassword!",
                RejectUnauthorized = false,
                BasePath = "zosmf/api/v1"
            };

            string args = ZoweCliHelper.BuildArguments("zos-jobs submit data-set \"PROD.JCL(JOB1)\"", options);

            Assert.Contains("--host \"zosmf.company.com\"", args);
            Assert.Contains("--port 10443", args);
            Assert.Contains("--user \"SYSADM\"", args);
            Assert.Contains("--password \"SecretPassword!\"", args);
            Assert.Contains("--ru false", args);
            Assert.Contains("--base-path \"zosmf/api/v1\"", args);
        }

        [Fact]
        public void BuildArguments_WithClientCertificates_AppendsCertFlags()
        {
            var options = new ZoweProfileOptions
            {
                Host = "zosmf.company.com",
                Port = 10443,
                CertPath = "C:\\certs\\client.pem",
                CertKeyPath = "C:\\certs\\client.key"
            };

            string args = ZoweCliHelper.BuildArguments("zos-files list ds HLQ.*", options);

            Assert.Contains("--cert-file \"C:\\certs\\client.pem\"", args);
            Assert.Contains("--cert-key-file \"C:\\certs\\client.key\"", args);
        }

        [Fact]
        public void EscapeForCli_WithQuotes_EscapesProperly()
        {
            string input = "value with \"quotes\" inside";
            string escaped = ZoweCliHelper.EscapeForCli(input);

            Assert.Equal("value with \\\"quotes\\\" inside", escaped);
        }

        [Fact]
        public void EscapeForCli_WhenNullOrEmpty_ReturnsEmptyString()
        {
            Assert.Equal("", ZoweCliHelper.EscapeForCli(null));
            Assert.Equal("", ZoweCliHelper.EscapeForCli(""));
        }
    }
}
