using System;
using System.IO;
using Xunit;

namespace GridTools.Javelin.MongoDbActivities.Helpers.Tests
{
    public class TdmPortalConnectionProfileResolverTests
    {
        [Fact]
        public void ResolveTdmBaseUrl_WithCustomUrl_ReturnsSanitizedUrl()
        {
            var url1 = TdmPortalConnectionProfileResolver.ResolveTdmBaseUrl("https://tdm.mycompany.com:8443/");
            Assert.Equal("https://tdm.mycompany.com:8443", url1);

            var url2 = TdmPortalConnectionProfileResolver.ResolveTdmBaseUrl("http://localhost:8080");
            Assert.Equal("http://localhost:8080", url2);

            var url3 = TdmPortalConnectionProfileResolver.ResolveTdmBaseUrl("tdm-host:8443");
            Assert.Equal("https://tdm-host:8443", url3);
        }

        [Fact]
        public void ResolveMongoConnectionString_DirectMongoUri_ReturnsUriDirectly()
        {
            string dbName;
            var uri = TdmPortalConnectionProfileResolver.ResolveMongoConnectionString(
                "mongodb://myuser:mypass@localhost:27017/customers_prod?authSource=admin",
                out dbName);

            Assert.Equal("mongodb://myuser:mypass@localhost:27017/customers_prod?authSource=admin", uri);
            Assert.Equal("customers_prod", dbName);
        }

        [Fact]
        public void ResolveMongoConnectionString_JdbcMongoUri_StripsJdbcPrefix()
        {
            string dbName;
            var uri = TdmPortalConnectionProfileResolver.ResolveMongoConnectionString(
                "jdbc:mongodb://localhost:27017/orders_db",
                out dbName);

            Assert.Equal("mongodb://localhost:27017/orders_db", uri);
            Assert.Equal("orders_db", dbName);
        }

        [Fact]
        public void ResolveMongoConnectionString_UnknownProfile_ThrowsDescriptiveArgumentException()
        {
            string dbName;
            var ex = Assert.Throws<ArgumentException>(() =>
                TdmPortalConnectionProfileResolver.ResolveMongoConnectionString(
                    "NON_EXISTENT_PROFILE_XYZ",
                    out dbName,
                    customTdmUrl: "https://localhost:9999"));

            Assert.Contains("NON_EXISTENT_PROFILE_XYZ", ex.Message);
            Assert.Contains("https://localhost:9999", ex.Message);
        }

        [Fact]
        public void CryptUtil_DecryptsEncryptedTdmPassword()
        {
            string encrypted = "{cry}KBb6obDI4E0EgSCsHlFJbNq9wwaE2iG1r4+KlAVOMAX8";
            string decrypted = GridTools.Javelin.MongoDbActivities.Helpers.Crypto.CryptUtil.DecryptPasswordIfNeeded(encrypted);
            Assert.Equal("TDM@123", decrypted);
        }

        [Fact]
        public void CryptUtil_DecryptsEncryptedTdmPasswordWithoutCryPrefix()
        {
            // Senhas do Connection Profile retornadas pelo TDM Portal não vêm com o prefixo {cry}
            string rawEncryptedFromProfile = "BdXneRBJbFTXwag2ivDci3zf6V65dpGNDyZ3WLcqN6-k1xzdFHIAszqN3lx0I+jwM52v";
            string decrypted = GridTools.Javelin.MongoDbActivities.Helpers.Crypto.CryptUtil.DecryptPasswordIfNeeded(rawEncryptedFromProfile);
            Assert.NotNull(decrypted);
            Assert.NotEqual(rawEncryptedFromProfile, decrypted);
        }

        [Fact]
        public void CryptUtil_LeavesPlainTextPasswordUntouched()
        {
            string plain = "MyNormalPassword123!";
            string decrypted = GridTools.Javelin.MongoDbActivities.Helpers.Crypto.CryptUtil.DecryptPasswordIfNeeded(plain);
            Assert.Equal(plain, decrypted);
        }
    }
}
