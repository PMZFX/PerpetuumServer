using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Perpetuum.Services.Mentoring
{
    public static class MentorNameFormatter
    {
        private static readonly Regex Separators = new Regex("[^a-zA-Z0-9]+", RegexOptions.Compiled);

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(" ", Separators
                .Split(value.Trim().ToLowerInvariant())
                .Where(token => token.Length > 0 && token != "def"));
        }

        public static string ToDisplayName(string definitionName)
        {
            string[] tokens = Normalize(definitionName)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return definitionName ?? string.Empty;

            var displayTokens = new List<string>(tokens.Length);
            for (int index = 0; index < tokens.Length; index++)
            {
                string token = tokens[index];
                if (token == "bot" && index == tokens.Length - 1)
                    continue;

                switch (token)
                {
                    case "npc":
                        displayTokens.Add("NPC");
                        break;
                    case "mk2":
                        displayTokens.Add("Mk2");
                        break;
                    case "cprg":
                        displayTokens.Add("Calibration Program");
                        break;
                    default:
                        displayTokens.Add(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token));
                        break;
                }
            }

            return string.Join(" ", displayTokens);
        }
    }
}
