using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.CassandraActivities.Helpers
{
    public class CassandraDataPainterEngine
    {
        private readonly Random _random;
        private readonly List<TdmTagRuleDefinition> _rules;
        private readonly TdmGeneratorContextClient _tdmContextClient;

        private static readonly string[] FirstNames = {
            "Lucas", "Gabriel", "Mateus", "Felipe", "Rodrigo", "Guilherme", "Gustavo", "Leonardo", "Bruno", "Thiago",
            "Larissa", "Juliana", "Mariana", "Beatriz", "Camila", "Fernanda", "Aline", "Leticia", "Amanda", "Patricia",
            "Carlos", "Eduardo", "Andre", "Rafael", "Marcelo", "Renato", "Diego", "Alexandre", "Fabio", "Fernando"
        };

        private static readonly string[] LastNames = {
            "Silva", "Santos", "Oliveira", "Souza", "Rodrigues", "Ferreira", "Alves", "Pereira", "Lima", "Gomes",
            "Costa", "Ribeiro", "Martins", "Carvalho", "Almeida", "Lopes", "Soares", "Fernandes", "Vieira", "Barbosa"
        };

        private static readonly string[] StreetNames = {
            "Avenida Paulista", "Rua Augusta", "Avenida Brigadeiro Faria Lima", "Rua Oscar Freire", "Avenida Copacabana",
            "Rua das Flores", "Avenida Brasil", "Rua XV de Novembro", "Avenida Rio Branco", "Rua Boa Vista"
        };

        public CassandraDataPainterEngine(List<TdmTagRuleDefinition> resolvedTemplateRules, TdmGeneratorContextClient tdmContextClient = null)
        {
            _random = new Random();
            if (resolvedTemplateRules == null || resolvedTemplateRules.Count == 0)
            {
                throw new InvalidOperationException("O motor de mascaramento requer uma lista válida de regras de Tag Template.");
            }
            _rules = resolvedTemplateRules;
            _tdmContextClient = tdmContextClient;
        }

        public Dictionary<string, object> MaskRow(Dictionary<string, object> sourceRow, out int maskedFieldCount, Dictionary<string, int> tagStats = null)
        {
            maskedFieldCount = 0;
            if (sourceRow == null) return null;

            var maskedRow = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in sourceRow)
            {
                string colName = kvp.Key;
                object origVal = kvp.Value;

                TdmTagRuleDefinition matchedRule = null;
                foreach (var rule in _rules)
                {
                    if (rule.Pattern != null && rule.Pattern.IsMatch(colName))
                    {
                        matchedRule = rule;
                        break;
                    }
                }

                if (matchedRule != null)
                {
                    maskedRow[colName] = EvaluateFormulaToObject(matchedRule.DataPainterExpr, colName, origVal);
                    maskedFieldCount++;
                    if (tagStats != null)
                    {
                        string tagKey = matchedRule.Tag ?? matchedRule.Name ?? "PII";
                        tagStats[tagKey] = tagStats.TryGetValue(tagKey, out var c) ? c + 1 : 1;
                    }
                }
                else
                {
                    maskedRow[colName] = origVal;
                }
            }

            return maskedRow;
        }

        private object EvaluateFormulaToObject(string formula, string fieldName, object originalValue)
        {
            if (string.IsNullOrWhiteSpace(formula)) return originalValue;

            if (formula.Contains("@guid()") || formula.Contains("@randhex("))
                return Guid.NewGuid();

            var matchDigits = Regex.Match(formula, @"@randdigits\((\d+)(?:,\s*(\d+))?\)@");
            if (matchDigits.Success)
            {
                int len = int.Parse(matchDigits.Groups[1].Value);
                return GenerateRandomDigits(len);
            }

            var matchRange = Regex.Match(formula, @"@randrange\(([\d\.]+),\s*([\d\.]+)(?:,\s*(\d+))?\)@");
            if (matchRange.Success)
            {
                double min = double.Parse(matchRange.Groups[1].Value, CultureInfo.InvariantCulture);
                double max = double.Parse(matchRange.Groups[2].Value, CultureInfo.InvariantCulture);
                double val = min + _random.NextDouble() * (max - min);
                if (originalValue is int) return (int)val;
                if (originalValue is long) return (long)val;
                return val;
            }

            if (formula.Contains("BRAZIL_VALID_CPF") || formula.Contains("CPF"))
                return GenerateValidCpf(originalValue?.ToString());
            if (formula.Contains("BRAZIL_VALID_CNPJ") || formula.Contains("CNPJ"))
                return GenerateValidCnpj(originalValue?.ToString());
            if (formula.Contains("FULL_NAME") || formula.Contains("Nomes_Brasileiros"))
                return $"{FirstNames[_random.Next(FirstNames.Length)]} {LastNames[_random.Next(LastNames.Length)]}";
            if (formula.Contains("First Name") || formula.Contains("FirstName"))
                return FirstNames[_random.Next(FirstNames.Length)];
            if (formula.Contains("Last Name") || formula.Contains("LastName"))
                return LastNames[_random.Next(LastNames.Length)];
            if (formula.Contains("FULL_ADDRESS") || formula.Contains("Address"))
                return StreetNames[_random.Next(StreetNames.Length)];

            if (formula.Contains("@dob("))
            {
                int age = _random.Next(18, 70);
                return DateTime.Now.AddYears(-age).AddDays(_random.Next(0, 365));
            }

            return formula;
        }

        private string GenerateRandomDigits(int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(_random.Next(0, 10));
            return sb.ToString();
        }

        private string GenerateValidCpf(string originalValue)
        {
            int[] d = new int[11];
            for (int i = 0; i < 9; i++) d[i] = _random.Next(0, 10);
            int sum1 = 0;
            for (int i = 0; i < 9; i++) sum1 += d[i] * (10 - i);
            int rem1 = sum1 % 11;
            d[9] = rem1 < 2 ? 0 : 11 - rem1;
            int sum2 = 0;
            for (int i = 0; i < 10; i++) sum2 += d[i] * (11 - i);
            int rem2 = sum2 % 11;
            d[10] = rem2 < 2 ? 0 : 11 - rem2;
            var sb = new StringBuilder();
            foreach (int digit in d) sb.Append(digit);
            string raw = sb.ToString();
            if (originalValue != null && originalValue.Contains("."))
                return $"{raw.Substring(0, 3)}.{raw.Substring(3, 3)}.{raw.Substring(6, 3)}-{raw.Substring(9, 2)}";
            return raw;
        }

        private string GenerateValidCnpj(string originalValue)
        {
            int[] d = new int[14];
            for (int i = 0; i < 8; i++) d[i] = _random.Next(0, 10);
            d[8] = 0; d[9] = 0; d[10] = 0; d[11] = 1;
            int[] w1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int sum1 = 0;
            for (int i = 0; i < 12; i++) sum1 += d[i] * w1[i];
            int rem1 = sum1 % 11;
            d[12] = rem1 < 2 ? 0 : 11 - rem1;
            int[] w2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            int sum2 = 0;
            for (int i = 0; i < 13; i++) sum2 += d[i] * w2[i];
            int rem2 = sum2 % 11;
            d[13] = rem2 < 2 ? 0 : 11 - rem2;
            var sb = new StringBuilder();
            foreach (int digit in d) sb.Append(digit);
            string raw = sb.ToString();
            if (originalValue != null && originalValue.Contains("."))
                return $"{raw.Substring(0, 2)}.{raw.Substring(2, 3)}.{raw.Substring(5, 3)}/{raw.Substring(8, 4)}-{raw.Substring(12, 2)}";
            return raw;
        }
    }
}
