using System;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using Xunit;

namespace GridTools.Javelin.MongoDbActivities.Helpers.Tests
{
    public class MongoSqlFilterParserTests
    {
        [Fact]
        public void BuildFilter_WithEmptyOrNull_ReturnsEmptyDocument()
        {
            var res1 = MongoSqlFilterParser.BuildFilter(null);
            var res2 = MongoSqlFilterParser.BuildFilter("");
            var res3 = MongoSqlFilterParser.BuildFilter("   ");

            Assert.Equal(new BsonDocument(), res1);
            Assert.Equal(new BsonDocument(), res2);
            Assert.Equal(new BsonDocument(), res3);
        }

        [Fact]
        public void BuildFilter_WithNativeJson_PassesThroughDirectly()
        {
            string json = "{\"status\": \"ACTIVE\", \"age\": {\"$gte\": 18}}";
            var doc = MongoSqlFilterParser.BuildFilter(json);

            Assert.Equal("ACTIVE", doc["status"].AsString);
            Assert.Equal(18, doc["age"]["$gte"].AsInt32);
        }

        [Fact]
        public void ParseWhereClause_SimpleEqualityAndInequality()
        {
            var docEqual = MongoSqlFilterParser.BuildFilter("status = 'ATIVO'");
            Assert.Equal("ATIVO", docEqual["status"].AsString);

            var docNotEqual = MongoSqlFilterParser.BuildFilter("status != 'INATIVO'");
            Assert.Equal("INATIVO", docNotEqual["status"]["$ne"].AsString);

            var docDiamond = MongoSqlFilterParser.BuildFilter("status <> 'CANCELADO'");
            Assert.Equal("CANCELADO", docDiamond["status"]["$ne"].AsString);
        }

        [Fact]
        public void ParseWhereClause_NumericComparisons()
        {
            var doc = MongoSqlFilterParser.BuildFilter("idade >= 18 AND saldo < 50000.50 AND score > 700");
            
            Assert.True(doc.Contains("$and"));
            var andArray = doc["$and"].AsBsonArray;
            Assert.Equal(3, andArray.Count);

            Assert.Equal(18, andArray[0]["idade"]["$gte"].AsInt32);
            Assert.Equal(50000.50, andArray[1]["saldo"]["$lt"].AsDouble);
            Assert.Equal(700, andArray[2]["score"]["$gt"].AsInt32);
        }

        [Fact]
        public void ParseWhereClause_InAndNotInOperators()
        {
            var docIn = MongoSqlFilterParser.BuildFilter("uf IN ('SP', 'RJ', 'MG')");
            Assert.True(docIn["uf"]["$in"].IsBsonArray);
            var inArr = docIn["uf"]["$in"].AsBsonArray;
            Assert.Equal(3, inArr.Count);
            Assert.Equal("SP", inArr[0].AsString);
            Assert.Equal("RJ", inArr[1].AsString);
            Assert.Equal("MG", inArr[2].AsString);

            var docNotIn = MongoSqlFilterParser.BuildFilter("codigo NOT IN (10, 20, 30)");
            Assert.True(docNotIn["codigo"]["$nin"].IsBsonArray);
            var notInArr = docNotIn["codigo"]["$nin"].AsBsonArray;
            Assert.Equal(3, notInArr.Count);
            Assert.Equal(10, notInArr[0].AsInt32);
            Assert.Equal(20, notInArr[1].AsInt32);
            Assert.Equal(30, notInArr[2].AsInt32);
        }

        [Fact]
        public void ParseWhereClause_LikeAndNotLikeOperators()
        {
            var docLike = MongoSqlFilterParser.BuildFilter("nome LIKE 'SILVA%'");
            Assert.Equal("^SILVA.*$", docLike["nome"]["$regex"].AsString);
            Assert.Equal("i", docLike["nome"]["$options"].AsString);

            var docEmail = MongoSqlFilterParser.BuildFilter("email LIKE '%@empresa.com'");
            Assert.Equal("^.*@empresa\\.com$", docEmail["email"]["$regex"].AsString);

            var docNotLike = MongoSqlFilterParser.BuildFilter("nome NOT LIKE 'TESTE%'");
            Assert.True(docNotLike["nome"]["$not"].IsBsonRegularExpression);
        }

        [Fact]
        public void ParseWhereClause_NullChecks()
        {
            var docNull = MongoSqlFilterParser.BuildFilter("cpf IS NULL");
            Assert.True(docNull["cpf"].IsBsonNull);

            var docNotNull = MongoSqlFilterParser.BuildFilter("cpf IS NOT NULL");
            Assert.True(docNotNull["cpf"]["$ne"].IsBsonNull);
        }

        [Fact]
        public void ParseWhereClause_ComplexOrCondition()
        {
            var docOr = MongoSqlFilterParser.BuildFilter("status = 'ATIVO' OR saldo > 1000");
            Assert.True(docOr.Contains("$or"));
            var orArr = docOr["$or"].AsBsonArray;
            Assert.Equal(2, orArr.Count);
            Assert.Equal("ATIVO", orArr[0]["status"].AsString);
            Assert.Equal(1000, orArr[1]["saldo"]["$gt"].AsInt32);
        }

        [Fact]
        public void ParseWhereClause_NestedFieldsAndIsoDates()
        {
            var doc = MongoSqlFilterParser.BuildFilter("cliente.endereco.cidade = 'Sao Paulo' AND dataCriacao >= '2026-01-01'");
            var andArr = doc["$and"].AsBsonArray;
            
            Assert.Equal("Sao Paulo", andArr[0]["cliente.endereco.cidade"].AsString);
            Assert.True(andArr[1]["dataCriacao"]["$gte"].IsBsonDateTime);
        }
    }
}
