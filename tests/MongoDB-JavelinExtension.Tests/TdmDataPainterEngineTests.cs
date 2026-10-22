using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using Xunit;

namespace GridTools.Javelin.MongoDbActivities.Helpers.Tests
{
    public class TdmDataPainterEngineTests
    {
        private List<TdmTagRuleDefinition> CreateSampleRules()
        {
            return new List<TdmTagRuleDefinition>
            {
                new TdmTagRuleDefinition
                {
                    Tag = "CPF",
                    Pattern = new Regex(@"cpf|documento", RegexOptions.IgnoreCase),
                    DataPainterExpr = "@seedlist(BRAZIL_VALID_CPF)@"
                },
                new TdmTagRuleDefinition
                {
                    Tag = "EMAIL",
                    Pattern = new Regex(@"email|correio", RegexOptions.IgnoreCase),
                    DataPainterExpr = "@randemail()@"
                },
                new TdmTagRuleDefinition
                {
                    Tag = "CREDIT_CARD",
                    Pattern = new Regex(@"cartao|card_number|pan", RegexOptions.IgnoreCase),
                    DataPainterExpr = "@randdigits(16)@"
                },
                new TdmTagRuleDefinition
                {
                    Tag = "FIRST_NAME",
                    Pattern = new Regex(@"primeiro_nome|nome_titular", RegexOptions.IgnoreCase),
                    DataPainterExpr = "@seedlist(FIRST_NAME)@"
                }
            };
        }

        [Fact]
        public void MaskDocument_ConstructorThrowsIfNoRules()
        {
            Assert.Throws<InvalidOperationException>(() => new TdmDataPainterEngine(null));
            Assert.Throws<InvalidOperationException>(() => new TdmDataPainterEngine(new List<TdmTagRuleDefinition>()));
        }

        [Fact]
        public void MaskDocument_GeneratesValidMod11Cpf()
        {
            var engine = new TdmDataPainterEngine(CreateSampleRules());
            var doc = new BsonDocument
            {
                { "cpf", "111.222.333-44" },
                { "nome_titular", "Joao da Silva" }
            };

            int maskedCount;
            var masked = engine.MaskDocument(doc, out maskedCount);

            Assert.Equal(2, maskedCount);
            string maskedCpf = masked["cpf"].AsString;
            
            // Verifica formato 000.000.000-00
            Assert.Matches(@"^\d{3}\.\d{3}\.\d{3}-\d{2}$", maskedCpf);

            // Valida Modulo 11 matematico do CPF gerado
            string digitsOnly = Regex.Replace(maskedCpf, @"\D", "");
            Assert.Equal(11, digitsOnly.Length);
            Assert.True(IsValidCpfMod11(digitsOnly));
        }

        [Fact]
        public void MaskDocument_GeneratesValid16DigitPan()
        {
            var engine = new TdmDataPainterEngine(CreateSampleRules());
            var doc = new BsonDocument
            {
                { "cartao", "4111111111111111" }
            };

            int maskedCount;
            var masked = engine.MaskDocument(doc, out maskedCount);

            Assert.Equal(1, maskedCount);
            string maskedCard = masked["cartao"].AsString;
            Assert.Equal(16, maskedCard.Length);
            Assert.Matches(@"^\d{16}$", maskedCard);
        }

        [Fact]
        public void MaskDocument_MasksNestedBsonDocuments()
        {
            var engine = new TdmDataPainterEngine(CreateSampleRules());
            var doc = new BsonDocument
            {
                { "_id", ObjectId.GenerateNewId() },
                { "titular", new BsonDocument
                    {
                        { "nome_titular", "Carlos Eduardo" },
                        { "email", "carlos@original.com" }
                    }
                }
            };

            int maskedCount;
            var masked = engine.MaskDocument(doc, out maskedCount);

            Assert.Equal(2, maskedCount);
            Assert.NotEqual("carlos@original.com", masked["titular"]["email"].AsString);
            Assert.Contains("@", masked["titular"]["email"].AsString);
        }

        [Fact]
        public void MaskDocumentsBatch_ProcessesBatchCorrectly()
        {
            var engine = new TdmDataPainterEngine(CreateSampleRules());
            var batch = new List<BsonDocument>
            {
                new BsonDocument { { "cpf", "111.222.333-44" }, { "nome_titular", "Alice" } },
                new BsonDocument { { "cpf", "555.666.777-88" }, { "email", "alice@example.com" } }
            };

            int batchMasked;
            var maskedBatch = engine.MaskDocumentsBatch(batch, out batchMasked);

            Assert.Equal(2, maskedBatch.Count);
            Assert.Equal(4, batchMasked);
            Assert.NotEqual("111.222.333-44", maskedBatch[0]["cpf"].AsString);
            Assert.NotEqual("alice@example.com", maskedBatch[1]["email"].AsString);
        }

        [Fact]
        public void TdmGeneratorContextClient_InitializesWithDummyPropertiesWithoutCrashing()
        {
            var client = new TdmGeneratorContextClient("https://invalid-host:9999", "dummy-token", enableDebugLogs: false);
            Assert.False(client.IsActive);
            bool success = client.InitializeDummyContext(projectId: 0, versionId: 0, levelId: 0);
            Assert.False(success);
            Assert.Null(client.ResolveMultipleExpressions(new List<string> { "@seedlist(FirstName)@" }));
            client.Dispose();
        }

        private bool IsValidCpfMod11(string cpf)
        {
            int[] mult1 = { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
            int[] mult2 = { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

            string tempCpf = cpf.Substring(0, 9);
            int sum = 0;
            for (int i = 0; i < 9; i++)
                sum += int.Parse(tempCpf[i].ToString()) * mult1[i];

            int rem = sum % 11;
            int d1 = rem < 2 ? 0 : 11 - rem;

            tempCpf += d1;
            sum = 0;
            for (int i = 0; i < 10; i++)
                sum += int.Parse(tempCpf[i].ToString()) * mult2[i];

            rem = sum % 11;
            int d2 = rem < 2 ? 0 : 11 - rem;

            return cpf.EndsWith(d1.ToString() + d2.ToString());
        }
    }
}
