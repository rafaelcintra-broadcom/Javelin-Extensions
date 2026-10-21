using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MongoDB.Bson;
using Xunit;

namespace GridTools.Javelin.MongoDbActivities.Helpers.Tests
{
    public class SecureVaultHelperTests : IDisposable
    {
        private readonly string _tempTestDir;

        public SecureVaultHelperTests()
        {
            _tempTestDir = Path.Combine(Path.GetTempPath(), "TdmVaultTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTestDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempTestDir))
            {
                try { Directory.Delete(_tempTestDir, true); } catch { }
            }
        }

        [Fact]
        public void WriteAndReadVault_IntegrityCheckWithSha256()
        {
            var testDocs = new List<BsonDocument>
            {
                new BsonDocument { { "_id", 1 }, { "nome", "User A" }, { "valor", 150.75 } },
                new BsonDocument { { "_id", 2 }, { "nome", "User B" }, { "valor", 299.90 } },
                new BsonDocument { { "_id", 3 }, { "nome", "User C" }, { "valor", 1200.00 } }
            };

            long extractedCount;
            string sha256;
            string vaultFile = SecureVaultHelper.WriteDocumentsToVault(
                _tempTestDir, "test_collection", testDocs, out extractedCount, out sha256);

            Assert.True(File.Exists(vaultFile));
            Assert.Equal(3, extractedCount);
            Assert.False(string.IsNullOrWhiteSpace(sha256));

            // Lê de volta do cofre
            var readDocs = SecureVaultHelper.ReadDocumentsFromVault(vaultFile).ToList();
            Assert.Equal(3, readDocs.Count);
            Assert.Equal("User A", readDocs[0]["nome"].AsString);
            Assert.Equal(299.90, readDocs[1]["valor"].AsDouble);
        }

        [Fact]
        public void ForensicsZeroFillAndShred_DestroysFileCompletely()
        {
            var testDocs = new List<BsonDocument>
            {
                new BsonDocument { { "sensivel", "DADO_CONFIDENCIAL_123456" } }
            };

            long count;
            string sha;
            string vaultFile = SecureVaultHelper.WriteDocumentsToVault(
                _tempTestDir, "shred_test", testDocs, out count, out sha);

            Assert.True(File.Exists(vaultFile));

            // Executa destruicao forense com 0x00
            SecureVaultHelper.ForensicsZeroFillAndShred(vaultFile);

            // Arquivo deve ter sido apagado
            Assert.False(File.Exists(vaultFile));
        }
    }
}
