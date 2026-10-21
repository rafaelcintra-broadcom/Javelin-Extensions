using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using Xunit;

namespace GridTools.Javelin.MongoDbActivities.Helpers.Tests
{
    public class TdmDataPainterFormulasTests
    {
        [Fact]
        public void DataPainter_EvaluatesDobFormula()
        {
            var rules = new List<TdmTagRuleDefinition>
            {
                new TdmTagRuleDefinition
                {
                    Tag = "BIRTH_DATE",
                    Pattern = new Regex(@"nascimento|dob", RegexOptions.IgnoreCase),
                    DataPainterExpr = "@dob(18,70)@"
                }
            };

            var engine = new TdmDataPainterEngine(rules);
            var doc = new BsonDocument { { "data_nascimento", "1990-05-15" } };

            int count;
            var masked = engine.MaskDocument(doc, out count);

            Assert.Equal(1, count);
            string dob = masked["data_nascimento"].AsString;
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", dob);

            var parsed = DateTime.Parse(dob);
            int age = DateTime.Today.Year - parsed.Year;
            Assert.True(age >= 17 && age <= 71);
        }

        [Fact]
        public void DataPainter_EvaluatesRandRangeFormula()
        {
            var rules = new List<TdmTagRuleDefinition>
            {
                new TdmTagRuleDefinition
                {
                    Tag = "SALARY",
                    Pattern = new Regex(@"salario|renda", RegexOptions.IgnoreCase),
                    DataPainterExpr = "@randrange(1500.0, 10000.0, 2)@"
                }
            };

            var engine = new TdmDataPainterEngine(rules);
            var doc = new BsonDocument { { "salario", 5000.0 } };

            int count;
            var masked = engine.MaskDocument(doc, out count);

            Assert.Equal(1, count);
            double val = masked["salario"].AsDouble;
            Assert.True(val >= 1500.0 && val <= 10000.0);
        }

        [Fact]
        public void DataPainter_EvaluatesBrazilValidCnpjFormula()
        {
            var rules = new List<TdmTagRuleDefinition>
            {
                new TdmTagRuleDefinition
                {
                    Tag = "CNPJ",
                    Pattern = new Regex(@"cnpj|empresa_doc", RegexOptions.IgnoreCase),
                    DataPainterExpr = "@seedlist(BRAZIL_VALID_CNPJ)@"
                }
            };

            var engine = new TdmDataPainterEngine(rules);
            var doc = new BsonDocument { { "cnpj", "00.000.000/0001-91" } };

            int count;
            var masked = engine.MaskDocument(doc, out count);

            Assert.Equal(1, count);
            string cnpj = masked["cnpj"].AsString;
            Assert.Matches(@"^\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}$", cnpj);
        }
    }
}
