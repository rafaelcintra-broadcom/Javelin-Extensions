using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.MongoDbActivities.Helpers
{
    /// <summary>
    /// Motor de Mascaramento NoSQL que consome as regras resolvidas do Tag Template do Data Assistant.
    /// Suporta avaliação direta no motor oficial do TDM (ContextGenerators) e motor local de alta velocidade.
    /// </summary>
    public class TdmDataPainterEngine
    {
        private readonly Random _random;
        private readonly List<TdmTagRuleDefinition> _rules;
        private readonly TdmGeneratorContextClient _tdmContextClient;

        private static readonly string[] FirstNames = {
            "Lucas", "Gabriel", "Mateus", "Felipe", "Rodrigo", "Guilherme", "Gustavo", "Leonardo", "Bruno", "Thiago",
            "Larissa", "Juliana", "Mariana", "Beatriz", "Camila", "Fernanda", "Aline", "Leticia", "Amanda", "Patricia",
            "Carlos", "Eduardo", "Andre", "Rafael", "Marcelo", "Renato", "Diego", "Alexandre", "Fabio", "Fernando",
            "Bruna", "Carolina", "Daniela", "Jessica", "Renata", "Vanessa", "Natalia", "Priscila", "Tatiane", "Carla"
        };

        private static readonly string[] LastNames = {
            "Silva", "Santos", "Oliveira", "Souza", "Rodrigues", "Ferreira", "Alves", "Pereira", "Lima", "Gomes",
            "Costa", "Ribeiro", "Martins", "Carvalho", "Almeida", "Lopes", "Soares", "Fernandes", "Vieira", "Barbosa",
            "Rocha", "Dias", "Nascimento", "Andrade", "Moreira", "Nunes", "Marques", "Machado", "Mendes", "Freitas",
            "Cardoso", "Ramos", "Goncalves", "Santana", "Teixeira", "Araujo", "Pinto", "Cavalcanti", "Castro", "Campos"
        };

        private static readonly string[] StreetNames = {
            "Avenida Paulista", "Rua Augusta", "Avenida Brigadeiro Faria Lima", "Rua Oscar Freire", "Avenida Copacabana",
            "Rua das Flores", "Avenida Brasil", "Rua XV de Novembro", "Avenida Rio Branco", "Rua Boa Vista",
            "Avenida Ipiranga", "Rua Sete de Setembro", "Avenida Atlantica", "Rua da Consolacao", "Avenida Morumbi"
        };

        public TdmDataPainterEngine(List<TdmTagRuleDefinition> resolvedTemplateRules, TdmGeneratorContextClient tdmContextClient = null)
        {
            _random = new Random();
            if (resolvedTemplateRules == null || resolvedTemplateRules.Count == 0)
            {
                throw new InvalidOperationException("O motor de mascaramento requer uma lista válida de regras de Tag Template.");
            }
            _rules = resolvedTemplateRules;
            _tdmContextClient = tdmContextClient;
        }

        public BsonDocument MaskDocument(BsonDocument sourceDoc, out int maskedFieldCount)
        {
            maskedFieldCount = 0;
            if (sourceDoc == null) return null;

            var maskedDoc = new BsonDocument();

            foreach (var elem in sourceDoc.Elements)
            {
                string fieldName = elem.Name;

                if (fieldName == "_id" && elem.Value.IsObjectId)
                {
                    maskedDoc.Add(elem);
                    continue;
                }

                TdmTagRuleDefinition matchedRule = null;
                foreach (var rule in _rules)
                {
                    if (rule.Pattern != null && rule.Pattern.IsMatch(fieldName))
                    {
                        matchedRule = rule;
                        break;
                    }
                }

                if (matchedRule != null)
                {
                    maskedDoc.Add(fieldName, EvaluateFormulaToBson(matchedRule.DataPainterExpr, fieldName, elem.Value));
                    maskedFieldCount++;
                }
                else if (elem.Value.IsBsonDocument)
                {
                    int subCount;
                    maskedDoc.Add(fieldName, MaskDocument(elem.Value.AsBsonDocument, out subCount));
                    maskedFieldCount += subCount;
                }
                else if (elem.Value.IsBsonArray)
                {
                    var arr = elem.Value.AsBsonArray;
                    var newArr = new BsonArray();
                    foreach (var item in arr)
                    {
                        if (item.IsBsonDocument)
                        {
                            int subCount;
                            newArr.Add(MaskDocument(item.AsBsonDocument, out subCount));
                            maskedFieldCount += subCount;
                        }
                        else
                        {
                            newArr.Add(item);
                        }
                    }
                    maskedDoc.Add(fieldName, newArr);
                }
                else
                {
                    maskedDoc.Add(elem);
                }
            }

            return maskedDoc;
        }

        /// <summary>
        /// Mascara um bloco/lote de documentos MongoDB.
        /// Quando o cliente de contexto do TDM estiver ativo, envia as fórmulas em lote para resolução oficial.
        /// </summary>
        public List<BsonDocument> MaskDocumentsBatch(List<BsonDocument> sourceBatch, out int batchMaskedFields)
        {
            batchMaskedFields = 0;
            if (sourceBatch == null || sourceBatch.Count == 0) return new List<BsonDocument>();

            // Se o contexto remoto do TDM estiver ativo, tenta resolver as expressões em lote
            if (_tdmContextClient != null && _tdmContextClient.IsActive)
            {
                try
                {
                    var expressionsToResolve = new List<string>();
                    var targets = new List<Tuple<int, string, BsonValue>>(); // docIndex, fieldName, originalVal

                    // Mapeia campos a serem mascarados no lote
                    for (int i = 0; i < sourceBatch.Count; i++)
                    {
                        var doc = sourceBatch[i];
                        foreach (var elem in doc.Elements)
                        {
                            if (elem.Name == "_id" && elem.Value.IsObjectId) continue;

                            foreach (var rule in _rules)
                            {
                                if (rule.Pattern != null && rule.Pattern.IsMatch(elem.Name))
                                {
                                    string expr = PrepareExpressionForTdm(rule.DataPainterExpr, elem.Value);
                                    expressionsToResolve.Add(expr);
                                    targets.Add(Tuple.Create(i, elem.Name, elem.Value));
                                    break;
                                }
                            }
                        }
                    }

                    if (expressionsToResolve.Count > 0)
                    {
                        var resolvedValues = _tdmContextClient.ResolveMultipleExpressions(expressionsToResolve);
                        if (resolvedValues != null && resolvedValues.Count == expressionsToResolve.Count)
                        {
                            var resultDocs = new List<BsonDocument>(sourceBatch.Count);
                            var docMap = new Dictionary<int, Dictionary<string, BsonValue>>();

                            for (int k = 0; k < resolvedValues.Count; k++)
                            {
                                int docIdx = targets[k].Item1;
                                string fName = targets[k].Item2;
                                BsonValue origVal = targets[k].Item3;

                                if (!docMap.TryGetValue(docIdx, out var fields))
                                {
                                    fields = new Dictionary<string, BsonValue>();
                                    docMap[docIdx] = fields;
                                }

                                fields[fName] = ConvertStringToBsonValue(resolvedValues[k], origVal);
                                batchMaskedFields++;
                            }

                            for (int i = 0; i < sourceBatch.Count; i++)
                            {
                                var orig = sourceBatch[i];
                                var masked = new BsonDocument();
                                docMap.TryGetValue(i, out var fieldOverrides);

                                foreach (var elem in orig.Elements)
                                {
                                    if (fieldOverrides != null && fieldOverrides.TryGetValue(elem.Name, out var mVal))
                                    {
                                        masked.Add(elem.Name, mVal);
                                    }
                                    else
                                    {
                                        masked.Add(elem);
                                    }
                                }
                                resultDocs.Add(masked);
                            }

                            return resultDocs;
                        }
                    }
                }
                catch { }
            }

            // Fallback: Avaliação direta pelo motor local em memória
            var fallbackDocs = new List<BsonDocument>(sourceBatch.Count);
            foreach (var doc in sourceBatch)
            {
                int count;
                fallbackDocs.Add(MaskDocument(doc, out count));
                batchMaskedFields += count;
            }
            return fallbackDocs;
        }

        private string PrepareExpressionForTdm(string formula, BsonValue originalValue)
        {
            if (string.IsNullOrWhiteSpace(formula)) return "''";

            string prepared = formula;
            if (originalValue != null)
            {
                string rawStr = originalValue.ToString().Replace("'", "''");
                prepared = prepared.Replace("<clone>", rawStr).Replace("<val>", rawStr);
            }

            return prepared;
        }

        private BsonValue ConvertStringToBsonValue(string resolvedStr, BsonValue originalValue)
        {
            if (resolvedStr == null) return BsonNull.Value;

            if (originalValue != null && originalValue.IsNumeric)
            {
                if (long.TryParse(resolvedStr, out long lVal)) return new BsonInt64(lVal);
                if (double.TryParse(resolvedStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) return new BsonDouble(dVal);
            }

            return new BsonString(resolvedStr);
        }

        private BsonValue EvaluateFormulaToBson(string formula, string fieldName, BsonValue originalValue)
        {
            if (string.IsNullOrWhiteSpace(formula))
                return originalValue;

            // @randdigits(N)@ ou @randdigits(N, N)@
            var matchDigits = Regex.Match(formula, @"@randdigits\((\d+)(?:,\s*(\d+))?\)@");
            if (matchDigits.Success)
            {
                int len = int.Parse(matchDigits.Groups[1].Value);
                return GenerateRandomDigits(len);
            }

            // @randrange(min, max, decimals)@
            var matchRange = Regex.Match(formula, @"@randrange\(([\d\.]+),\s*([\d\.]+)(?:,\s*(\d+))?\)@");
            if (matchRange.Success)
            {
                double min = double.Parse(matchRange.Groups[1].Value, CultureInfo.InvariantCulture);
                double max = double.Parse(matchRange.Groups[2].Value, CultureInfo.InvariantCulture);
                double val = min + _random.NextDouble() * (max - min);
                if (matchRange.Groups[3].Success)
                {
                    int decimals = int.Parse(matchRange.Groups[3].Value);
                    val = Math.Round(val, decimals);
                }
                if (originalValue != null && originalValue.IsNumeric)
                    return new BsonDouble(val);
                return new BsonDouble(val);
            }

            // @randlov(...)@ ou @randval(...)@
            if (formula.Contains("@randlov(") || formula.Contains("@randval("))
            {
                var matches = Regex.Matches(formula, @"[""']([^""']*)[""']");
                if (matches.Count > 0)
                {
                    int idx = _random.Next(matches.Count);
                    return matches[idx].Groups[1].Value;
                }
            }

            // @seedlist(...)@
            if (formula.Contains("BRAZIL_VALID_CPF") || formula.Contains("CPF") || formula.Contains("BRDOC(CPF"))
                return GenerateValidCpf(originalValue);
            if (formula.Contains("BRAZIL_VALID_CNPJ") || formula.Contains("CNPJ") || formula.Contains("BRDOC(CNPJ"))
                return GenerateValidCnpj(originalValue);
            if (formula.Contains("FULL_NAME") || formula.Contains("Nomes_Brasileiros"))
            {
                string fn = FirstNames[_random.Next(FirstNames.Length)];
                string ln = LastNames[_random.Next(LastNames.Length)];
                return $"{fn} {ln}";
            }
            if (formula.Contains("First Name") || formula.Contains("FirstName"))
                return FirstNames[_random.Next(FirstNames.Length)];
            if (formula.Contains("Last Name") || formula.Contains("LastName") || formula.Contains("SURNAMES"))
                return LastNames[_random.Next(LastNames.Length)];
            if (formula.Contains("FULL_ADDRESS") || formula.Contains("Street") || formula.Contains("Address"))
                return StreetNames[_random.Next(StreetNames.Length)];

            if (formula.Contains("@dob("))
            {
                int age = _random.Next(18, 70);
                return DateTime.Now.AddYears(-age).AddDays(_random.Next(0, 365)).ToString("yyyy-MM-dd");
            }

            if (formula.Contains("@formatencrypt(") || formula.Contains("@hash()"))
            {
                using (var sha = SHA256.Create())
                {
                    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(originalValue?.ToString() ?? Guid.NewGuid().ToString()));
                    string hashStr = BitConverter.ToString(bytes).Replace("-", "");
                    if (originalValue != null && originalValue.IsString)
                    {
                        int origLen = originalValue.AsString.Length;
                        return hashStr.Length >= origLen ? hashStr.Substring(0, Math.Max(1, origLen)) : hashStr;
                    }
                    return hashStr.Substring(0, 16);
                }
            }

            return formula;
        }

        private string GenerateRandomDigits(int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append(_random.Next(0, 10));
            }
            return sb.ToString();
        }

        private string GenerateValidCpf(BsonValue originalValue)
        {
            int[] d = new int[11];
            for (int i = 0; i < 9; i++)
                d[i] = _random.Next(0, 10);

            int sum1 = 0;
            for (int i = 0; i < 9; i++)
                sum1 += d[i] * (10 - i);
            int rem1 = sum1 % 11;
            d[9] = rem1 < 2 ? 0 : 11 - rem1;

            int sum2 = 0;
            for (int i = 0; i < 10; i++)
                sum2 += d[i] * (11 - i);
            int rem2 = sum2 % 11;
            d[10] = rem2 < 2 ? 0 : 11 - rem2;

            var sb = new StringBuilder();
            foreach (int digit in d)
                sb.Append(digit);

            string rawCpf = sb.ToString();

            if (originalValue != null && originalValue.IsString && originalValue.AsString.Contains("."))
            {
                return $"{rawCpf.Substring(0, 3)}.{rawCpf.Substring(3, 3)}.{rawCpf.Substring(6, 3)}-{rawCpf.Substring(9, 2)}";
            }

            return rawCpf;
        }

        private string GenerateValidCnpj(BsonValue originalValue)
        {
            int[] d = new int[14];
            for (int i = 0; i < 8; i++)
                d[i] = _random.Next(0, 10);
            d[8] = 0; d[9] = 0; d[10] = 0; d[11] = 1;

            int[] w1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int sum1 = 0;
            for (int i = 0; i < 12; i++)
                sum1 += d[i] * w1[i];
            int rem1 = sum1 % 11;
            d[12] = rem1 < 2 ? 0 : 11 - rem1;

            int[] w2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int sum2 = 0;
            for (int i = 0; i < 13; i++)
                sum2 += d[i] * w2[i];
            int rem2 = sum2 % 11;
            d[13] = rem2 < 2 ? 0 : 11 - rem2;

            var sb = new StringBuilder();
            foreach (int digit in d)
                sb.Append(digit);

            string rawCnpj = sb.ToString();

            if (originalValue != null && originalValue.IsString && originalValue.AsString.Contains("."))
            {
                return $"{rawCnpj.Substring(0, 2)}.{rawCnpj.Substring(2, 3)}.{rawCnpj.Substring(5, 3)}/{rawCnpj.Substring(8, 4)}-{rawCnpj.Substring(12, 2)}";
            }

            return rawCnpj;
        }
    }
}
