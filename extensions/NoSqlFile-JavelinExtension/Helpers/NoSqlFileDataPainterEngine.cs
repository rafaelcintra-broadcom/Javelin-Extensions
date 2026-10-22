using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GridTools.Javelin.Common;
using Newtonsoft.Json.Linq;

namespace GridTools.Javelin.NoSqlFileActivities.Helpers
{
    public class NoSqlFileDataPainterEngine
    {
        private readonly List<TdmTagRuleDefinition> _rules;
        private readonly TdmGeneratorContextClient _generatorClient;

        public NoSqlFileDataPainterEngine(List<TdmTagRuleDefinition> rules, TdmGeneratorContextClient generatorClient = null)
        {
            _rules = rules ?? new List<TdmTagRuleDefinition>();
            _generatorClient = generatorClient;
        }

        public JObject MaskRow(JObject inputRow, out int maskedCount, Dictionary<string, int> piiStats = null)
        {
            maskedCount = 0;
            if (inputRow == null) return null;

            var result = new JObject();
            foreach (var prop in inputRow.Properties())
            {
                result[prop.Name] = MaskToken(prop.Name, prop.Value, ref maskedCount, piiStats);
            }

            return result;
        }

        private JToken MaskToken(string pathOrKey, JToken token, ref int maskedCount, Dictionary<string, int> piiStats)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return JValue.CreateNull();
            }

            if (token is JObject obj)
            {
                var newObj = new JObject();
                foreach (var child in obj.Properties())
                {
                    string childPath = string.IsNullOrEmpty(pathOrKey) ? child.Name : $"{pathOrKey}.{child.Name}";
                    newObj[child.Name] = MaskToken(childPath, child.Value, ref maskedCount, piiStats);
                }
                return newObj;
            }

            if (token is JArray arr)
            {
                var newArr = new JArray();
                for (int i = 0; i < arr.Count; i++)
                {
                    newArr.Add(MaskToken(pathOrKey, arr[i], ref maskedCount, piiStats));
                }
                return newArr;
            }

            // É valor escalar
            var matchedRule = FindMatchingRule(pathOrKey);
            if (matchedRule != null)
            {
                maskedCount++;
                if (piiStats != null)
                {
                    string tagKey = matchedRule.Tag ?? matchedRule.Name ?? "PII";
                    if (!piiStats.ContainsKey(tagKey)) piiStats[tagKey] = 0;
                    piiStats[tagKey]++;
                }

                object originalVal = (token as JValue)?.Value;
                object generated = EvaluateRule(matchedRule, pathOrKey, originalVal);
                return JToken.FromObject(generated ?? JValue.CreateNull());
            }

            return token.DeepClone();
        }

        private TdmTagRuleDefinition FindMatchingRule(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return null;

            string cleanField = fieldName;
            int lastDot = cleanField.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < cleanField.Length - 1)
            {
                cleanField = cleanField.Substring(lastDot + 1);
            }

            foreach (var rule in _rules)
            {
                if (rule.Pattern != null && (rule.Pattern.IsMatch(fieldName) || rule.Pattern.IsMatch(cleanField)))
                {
                    return rule;
                }
            }

            return null;
        }

        private object EvaluateRule(TdmTagRuleDefinition rule, string fieldName, object originalVal)
        {
            string expr = rule.DataPainterExpr;
            if (string.IsNullOrWhiteSpace(expr))
            {
                expr = "@seedlist(First Name)@";
            }

            if (_generatorClient != null && _generatorClient.IsActive)
            {
                try
                {
                    var resolvedList = _generatorClient.ResolveMultipleExpressions(new List<string> { expr });
                    if (resolvedList != null && resolvedList.Count > 0) return resolvedList[0];
                }
                catch { }
            }

            // Fallback sintético local Data Painter
            return EvaluateDataPainterLocally(expr, originalVal);
        }

        private object EvaluateDataPainterLocally(string expr, object originalVal)
        {
            var rand = new Random(Guid.NewGuid().GetHashCode());

            if (expr.Contains("BRAZIL_VALID_CPF") || expr.Contains("CPF"))
            {
                int[] n = new int[9];
                for (int i = 0; i < 9; i++) n[i] = rand.Next(0, 10);
                int d1 = 0;
                for (int i = 0; i < 9; i++) d1 += n[i] * (10 - i);
                d1 = 11 - (d1 % 11);
                if (d1 >= 10) d1 = 0;
                int d2 = 0;
                for (int i = 0; i < 9; i++) d2 += n[i] * (11 - i);
                d2 += d1 * 2;
                d2 = 11 - (d2 % 11);
                if (d2 >= 10) d2 = 0;
                return $"{n[0]}{n[1]}{n[2]}.{n[3]}{n[4]}{n[5]}.{n[6]}{n[7]}{n[8]}-{d1}{d2}";
            }

            if (expr.Contains("BRAZIL_VALID_CNPJ") || expr.Contains("CNPJ"))
            {
                return $"{rand.Next(10, 99)}.{rand.Next(100, 999)}.{rand.Next(100, 999)}/0001-{rand.Next(10, 99)}";
            }

            if (expr.Contains("First Name"))
            {
                string[] fnames = { "Lucas", "Mariana", "Gabriel", "Beatriz", "Rodrigo", "Camila", "Matheus", "Larissa", "Thiago", "Amanda" };
                return fnames[rand.Next(fnames.Length)];
            }

            if (expr.Contains("Last Name"))
            {
                string[] lnames = { "Silva", "Santos", "Oliveira", "Souza", "Rodrigues", "Ferreira", "Alves", "Pereira", "Lima", "Gomes" };
                return lnames[rand.Next(lnames.Length)];
            }

            if (expr.Contains("FULL_NAME") || expr.Contains("NAME"))
            {
                string[] fnames = { "Lucas", "Mariana", "Gabriel", "Beatriz", "Rodrigo", "Camila", "Matheus", "Larissa", "Thiago", "Amanda" };
                string[] lnames = { "Silva", "Santos", "Oliveira", "Souza", "Rodrigues", "Ferreira", "Alves", "Pereira", "Lima", "Gomes" };
                return $"{fnames[rand.Next(fnames.Length)]} {lnames[rand.Next(lnames.Length)]}";
            }

            if (expr.Contains("example.com") || expr.Contains("@"))
            {
                string[] users = { "lucas", "mariana", "gabriel", "beatriz", "rodrigo", "camila" };
                return $"{users[rand.Next(users.Length)]}.{rand.Next(100, 999)}@example.com";
            }

            if (expr.Contains("randval") || expr.Contains("randdigits"))
            {
                return $"{rand.Next(10000000, 99999999)}";
            }

            return $"Masked_{rand.Next(1000, 9999)}";
        }
    }
}
