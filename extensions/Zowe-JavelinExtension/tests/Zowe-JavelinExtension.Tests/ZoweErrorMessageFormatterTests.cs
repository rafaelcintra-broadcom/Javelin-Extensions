using System;
using GridTools.Javelin.ZoweActivities.Helpers;
using Xunit;

namespace GridTools.Javelin.ZoweActivities.Tests
{
    public class ZoweErrorMessageFormatterTests
    {
        [Fact]
        public void FormatErrorMessage_WhenEmptyError_ReturnsDefaultMessage()
        {
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage("", "Zowe Submit Job");
            Assert.Contains("Erro na execução da atividade Zowe Mainframe [Zowe Submit Job]", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenZoweCliNotFound_ReturnsCliNotFoundMessage()
        {
            string rawError = "'zowe' is not recognized as an internal or external command, operable program or batch file.";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Raw CLI Command");

            Assert.Contains("[Zowe CLI Não Encontrado]", formatted);
            Assert.Contains("npm install -g @zowe/cli", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenAuthenticationFailed_ReturnsAuthMessage()
        {
            string rawError = "Command Error: HTTP status 401: Unauthorized - Invalid credentials or password expired";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Authenticate (API ML Login)");

            Assert.Contains("[Falha de Autenticação z/OSMF / RACF / ACF2]", formatted);
            Assert.Contains("credenciais informadas", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenSslCertError_ReturnsSslCertificateMessage()
        {
            string rawError = "Error: UNABLE_TO_VERIFY_LEAF_SIGNATURE: self signed certificate in certificate chain";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Dataset List");

            Assert.Contains("[Erro de Certificado SSL / TLS z/OSMF]", formatted);
            Assert.Contains("RejectUnauthorized = false", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenConnectionRefused_ReturnsConnectionMessage()
        {
            string rawError = "Error: connect ECONNREFUSED 10.0.0.1:10443";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Submit Job");

            Assert.Contains("[Falha de Conexão com Mainframe]", formatted);
            Assert.Contains("z/OSMF está ativo na LPAR", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenDatasetNotFound_ReturnsDatasetNotFoundMessage()
        {
            string rawError = "IKJ56228I DATA SET TEST.NONEXIST.DATA NOT IN CATALOG OR CATALOG CANNOT BE ACCESSED";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Dataset Download", "TEST.NONEXIST.DATA");

            Assert.Contains("[Data Set Não Encontrado no Mainframe]", formatted);
            Assert.Contains("TEST.NONEXIST.DATA", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenMemberNotFound_ReturnsMemberNotFoundMessage()
        {
            string rawError = "IKJ56702I MEMBER PROG99 NOT FOUND IN TEST.SOURCE.PDS";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Dataset Download", "PROG99");

            Assert.Contains("[Membro de PDS Não Encontrado]", formatted);
            Assert.Contains("PROG99", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenJobNotFound_ReturnsJobNotFoundMessage()
        {
            string rawError = "Job not found on JES spool for JOB99999";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Job Status", "JOB99999");

            Assert.Contains("[Job z/OS / JES Não Encontrado]", formatted);
            Assert.Contains("JOB99999", formatted);
        }

        [Fact]
        public void FormatErrorMessage_WhenGenericError_ReturnsFormattedGenericError()
        {
            string rawError = "Some unexpected mainframe internal system error code 500";
            string formatted = ZoweErrorMessageFormatter.FormatErrorMessage(rawError, "Zowe Custom Activity");

            Assert.Contains("[Erro Zowe Mainframe - Zowe Custom Activity]", formatted);
            Assert.Contains("Some unexpected mainframe internal system error code 500", formatted);
        }
    }
}
