using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Bson;

namespace GridTools.Javelin.MongoDbActivities.Helpers
{
    /// <summary>
    /// Traduz cláusulas WHERE estilo SQL clássico (ex: status = 'ATIVO' AND idade >= 18)
    /// ou aceita filtros JSON nativos do MongoDB diretamente.
    /// </summary>
    public static class MongoSqlFilterParser
    {
        public static BsonDocument BuildFilter(string filterOrWhereClause)
        {
            if (string.IsNullOrWhiteSpace(filterOrWhereClause))
                return new BsonDocument();

            string trimmed = filterOrWhereClause.Trim();

            // 1. Se já for um documento JSON/BSON válido (começa com '{')
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                return MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(trimmed);
            }

            // 2. Se começar com 'WHERE ', remove o prefixo
            if (trimmed.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(6).Trim();
            }

            if (string.IsNullOrWhiteSpace(trimmed))
                return new BsonDocument();

            return ParseWhereClause(trimmed);
        }

        public static BsonDocument ParseWhereClause(string sqlWhere)
        {
            // Divide por OR (nível superior)
            var orParts = SplitTopLevel(sqlWhere, "OR");
            if (orParts.Count > 1)
            {
                var orArray = new BsonArray();
                foreach (var part in orParts)
                {
                    orArray.Add(ParseWhereClause(part));
                }
                return new BsonDocument("$or", orArray);
            }

            // Divide por AND
            var andParts = SplitTopLevel(sqlWhere, "AND");
            var combinedDoc = new BsonDocument();
            var andArray = new BsonArray();

            foreach (var part in andParts)
            {
                var condDoc = ParseSingleCondition(part.Trim());
                if (condDoc != null && condDoc.ElementCount > 0)
                {
                    andArray.Add(condDoc);
                }
            }

            if (andArray.Count == 0)
                return new BsonDocument();

            if (andArray.Count == 1)
                return andArray[0].AsBsonDocument;

            return new BsonDocument("$and", andArray);
        }

        private static BsonDocument ParseSingleCondition(string condition)
        {
            string cond = condition.Trim();

            // Remove parênteses externos redundantes se houver
            while (cond.StartsWith("(") && cond.EndsWith(")") && IsEnclosedInMatchingParens(cond))
            {
                cond = cond.Substring(1, cond.Length - 2).Trim();
                // Se a expressão interna contiver AND/OR, reprocessa recursivamente
                if (ContainsTopLevelOperator(cond, "AND") || ContainsTopLevelOperator(cond, "OR"))
                {
                    return ParseWhereClause(cond);
                }
            }

            // 1. IS NULL / IS NOT NULL
            var matchIsNull = Regex.Match(cond, @"^([\w\.\$]+)\s+IS\s+NULL$", RegexOptions.IgnoreCase);
            if (matchIsNull.Success)
            {
                string field = matchIsNull.Groups[1].Value.Trim();
                return new BsonDocument(field, BsonNull.Value);
            }

            var matchIsNotNull = Regex.Match(cond, @"^([\w\.\$]+)\s+IS\s+NOT\s+NULL$", RegexOptions.IgnoreCase);
            if (matchIsNotNull.Success)
            {
                string field = matchIsNotNull.Groups[1].Value.Trim();
                return new BsonDocument(field, new BsonDocument("$ne", BsonNull.Value));
            }

            // 2. IN / NOT IN: campo IN ('VAL1', 'VAL2', 123)
            var matchIn = Regex.Match(cond, @"^([\w\.\$]+)\s+(NOT\s+IN|IN)\s*\((.*?)\)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (matchIn.Success)
            {
                string field = matchIn.Groups[1].Value.Trim();
                string op = matchIn.Groups[2].Value.ToUpperInvariant().Replace(" ", "");
                string rawValues = matchIn.Groups[3].Value;

                var bsonValues = ParseCommaSeparatedValues(rawValues);
                string mongoOp = op == "NOTIN" ? "$nin" : "$in";
                return new BsonDocument(field, new BsonDocument(mongoOp, bsonValues));
            }

            // 3. LIKE / NOT LIKE: campo LIKE '%valor%' / 'abc%'
            var matchLike = Regex.Match(cond, @"^([\w\.\$]+)\s+(NOT\s+LIKE|LIKE)\s+(.+)$", RegexOptions.IgnoreCase);
            if (matchLike.Success)
            {
                string field = matchLike.Groups[1].Value.Trim();
                bool notLike = matchLike.Groups[2].Value.StartsWith("NOT", StringComparison.OrdinalIgnoreCase);
                string rawPattern = Unquote(matchLike.Groups[3].Value.Trim());

                // Converte padrão SQL LIKE (%) para Regex
                string regexPattern = "^" + Regex.Escape(rawPattern).Replace("%", ".*").Replace("_", ".") + "$";
                var regexDoc = new BsonDocument("$regex", regexPattern).Add("$options", "i");

                if (notLike)
                {
                    return new BsonDocument(field, new BsonDocument("$not", new BsonRegularExpression(regexPattern, "i")));
                }
                return new BsonDocument(field, regexDoc);
            }

            // 4. Operadores de Comparação Padrão: !=, <>, <=, >=, =, <, >
            var opMatch = Regex.Match(cond, @"^([\w\.\$]+)\s*(!=|<>|<=|>=|=|<|>)\s*(.+)$");
            if (opMatch.Success)
            {
                string field = opMatch.Groups[1].Value.Trim();
                string op = opMatch.Groups[2].Value;
                string rawVal = opMatch.Groups[3].Value.Trim();
                BsonValue bsonVal = ParseLiteralValue(rawVal);

                switch (op)
                {
                    case "=":
                        return new BsonDocument(field, bsonVal);
                    case "!=":
                    case "<>":
                        return new BsonDocument(field, new BsonDocument("$ne", bsonVal));
                    case ">":
                        return new BsonDocument(field, new BsonDocument("$gt", bsonVal));
                    case ">=":
                        return new BsonDocument(field, new BsonDocument("$gte", bsonVal));
                    case "<":
                        return new BsonDocument(field, new BsonDocument("$lt", bsonVal));
                    case "<=":
                        return new BsonDocument(field, new BsonDocument("$lte", bsonVal));
                }
            }

            // Fallback: se não conseguir interpretar como SQL clássico, tenta encapsular como filtro texto
            return new BsonDocument();
        }

        private static BsonValue ParseLiteralValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return BsonNull.Value;

            string val = raw.Trim();

            // Null
            if (val.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                return BsonNull.Value;

            // Booleans
            if (val.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                return BsonBoolean.True;
            if (val.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                return BsonBoolean.False;

            // Strings com aspas simples ou duplas
            if ((val.StartsWith("'") && val.EndsWith("'")) || (val.StartsWith("\"") && val.EndsWith("\"")))
            {
                string unquoted = val.Substring(1, val.Length - 2).Replace("''", "'");

                // Checa se é Data ISO válida (ex: '2026-01-01' ou '2026-01-01T10:00:00Z')
                if (DateTime.TryParse(unquoted, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime dt))
                {
                    // Se tiver formato típico de data, armazena como BsonDateTime para consulta precisa no Mongo
                    if (Regex.IsMatch(unquoted, @"^\d{4}-\d{2}-\d{2}"))
                    {
                        return new BsonDateTime(dt);
                    }
                }

                return new BsonString(unquoted);
            }

            // Números Inteiros
            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lVal))
            {
                if (lVal >= int.MinValue && lVal <= int.MaxValue)
                    return new BsonInt32((int)lVal);
                return new BsonInt64(lVal);
            }

            // Números Decimais
            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
            {
                return new BsonDouble(dVal);
            }

            return new BsonString(val);
        }

        private static BsonArray ParseCommaSeparatedValues(string rawList)
        {
            var arr = new BsonArray();
            var tokens = SplitTopLevel(rawList, ",");
            foreach (var tok in tokens)
            {
                if (!string.IsNullOrWhiteSpace(tok))
                {
                    arr.Add(ParseLiteralValue(tok.Trim()));
                }
            }
            return arr;
        }

        private static string Unquote(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if ((s.StartsWith("'") && s.EndsWith("'")) || (s.StartsWith("\"") && s.EndsWith("\"")))
            {
                return s.Substring(1, s.Length - 2).Replace("''", "'");
            }
            return s;
        }

        private static List<string> SplitTopLevel(string input, string delimiter)
        {
            var results = new List<string>();
            int parenDepth = 0;
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            int lastIdx = 0;

            string pattern = @"\b" + Regex.Escape(delimiter) + @"\b";
            if (delimiter == ",")
                pattern = ",";

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '\'' && !inDoubleQuote) inSingleQuote = !inSingleQuote;
                else if (c == '"' && !inSingleQuote) inDoubleQuote = !inDoubleQuote;
                else if (!inSingleQuote && !inDoubleQuote)
                {
                    if (c == '(') parenDepth++;
                    else if (c == ')') parenDepth--;
                    else if (parenDepth == 0)
                    {
                        var match = Regex.Match(input.Substring(i), "^" + pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            results.Add(input.Substring(lastIdx, i - lastIdx));
                            i += match.Length - 1;
                            lastIdx = i + 1;
                        }
                    }
                }
            }

            if (lastIdx <= input.Length)
            {
                results.Add(input.Substring(lastIdx));
            }

            return results;
        }

        private static bool ContainsTopLevelOperator(string input, string op)
        {
            var parts = SplitTopLevel(input, op);
            return parts.Count > 1;
        }

        private static bool IsEnclosedInMatchingParens(string s)
        {
            if (!s.StartsWith("(") || !s.EndsWith(")")) return false;
            int depth = 0;
            for (int i = 0; i < s.Length - 1; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                if (depth == 0) return false; // Fechou antes do fim
            }
            return depth == 1;
        }
    }
}
