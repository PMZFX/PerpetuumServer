using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Perpetuum.Services.Mentoring
{
    public sealed class EmbeddedMentorKnowledgeBase : IMentorKnowledgeBase
    {
        private const string ResourceSuffix = ".mentor.md";
        private static readonly HashSet<string> StopWords = new HashSet<string>(
            new[]
            {
                "a", "an", "and", "are", "can", "do", "does", "how", "i", "in", "is",
                "it", "me", "my", "of", "on", "the", "this", "to", "what", "why"
            },
            StringComparer.Ordinal);

        private readonly MentorKnowledgeDocument[] _documents;

        public EmbeddedMentorKnowledgeBase()
            : this(LoadDocuments(typeof(EmbeddedMentorKnowledgeBase).Assembly))
        {
        }

        public EmbeddedMentorKnowledgeBase(IEnumerable<MentorKnowledgeDocument> documents)
        {
            _documents = (documents ?? throw new ArgumentNullException(nameof(documents)))
                .OrderBy(document => document.Id, StringComparer.Ordinal)
                .ToArray();
        }

        public IReadOnlyList<MentorKnowledgeDocument> Documents => _documents;

        public IReadOnlyList<MentorKnowledgeMatch> Search(string query, int maximumResults = 3)
        {
            if (maximumResults <= 0 || string.IsNullOrWhiteSpace(query))
                return Array.Empty<MentorKnowledgeMatch>();

            string normalizedQuery = MentorNameFormatter.Normalize(query);
            string[] queryTokens = Tokens(normalizedQuery).Where(token => !StopWords.Contains(token)).ToArray();
            if (queryTokens.Length == 0)
                return Array.Empty<MentorKnowledgeMatch>();

            return _documents
                .Select(document => new MentorKnowledgeMatch(document, Score(document, normalizedQuery, queryTokens)))
                .Where(match => match.Score >= 12)
                .OrderByDescending(match => match.Score)
                .ThenByDescending(match => match.Document.Source)
                .ThenBy(match => match.Document.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maximumResults)
                .ToArray();
        }

        private static int Score(
            MentorKnowledgeDocument document,
            string normalizedQuery,
            IReadOnlyList<string> queryTokens)
        {
            string title = MentorNameFormatter.Normalize(document.Title);
            string content = MentorNameFormatter.Normalize(document.Content);
            string[] titleTokens = Tokens(title);
            string[] contentTokens = Tokens(content);
            int score = (int)document.Source * 2;

            foreach (string topic in document.Topics)
            {
                string normalizedTopic = MentorNameFormatter.Normalize(topic);
                if (normalizedQuery == normalizedTopic)
                    score += 80;
                else if (normalizedQuery.Contains(normalizedTopic) || normalizedTopic.Contains(normalizedQuery))
                    score += 45;

                string[] topicTokens = Tokens(normalizedTopic);
                score += queryTokens.Count(token => topicTokens.Contains(token)) * 12;
            }

            score += queryTokens.Count(token => titleTokens.Contains(token)) * 8;
            score += queryTokens.Count(token => contentTokens.Contains(token)) * 2;
            if (queryTokens.All(token => titleTokens.Contains(token) || contentTokens.Contains(token)))
                score += 10;

            return score;
        }

        private static string[] Tokens(string value)
        {
            return value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static IEnumerable<MentorKnowledgeDocument> LoadDocuments(Assembly assembly)
        {
            foreach (string resourceName in assembly.GetManifestResourceNames()
                         .Where(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    continue;
                using var reader = new StreamReader(stream);
                yield return Parse(reader.ReadToEnd(), resourceName);
            }
        }

        private static MentorKnowledgeDocument Parse(string markdown, string resourceName)
        {
            string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 4 || lines[0].Trim() != "---")
                throw new InvalidDataException($"Mentor knowledge resource {resourceName} has no metadata header.");

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int index = 1;
            for (; index < lines.Length && lines[index].Trim() != "---"; index++)
            {
                int separator = lines[index].IndexOf(':');
                if (separator <= 0)
                    continue;
                metadata[lines[index].Substring(0, separator).Trim()] =
                    lines[index].Substring(separator + 1).Trim();
            }

            if (index >= lines.Length)
                throw new InvalidDataException($"Mentor knowledge resource {resourceName} has an unterminated metadata header.");

            string content = Regex.Replace(
                string.Join(" ", lines.Skip(index + 1)).Trim(),
                @"\s+",
                " ");
            string[] topics = Required(metadata, "topics", resourceName)
                .Split(',')
                .Select(topic => topic.Trim())
                .Where(topic => topic.Length > 0)
                .ToArray();
            if (topics.Length == 0 || content.Length == 0)
                throw new InvalidDataException($"Mentor knowledge resource {resourceName} is empty.");

            if (!Enum.TryParse(
                    Required(metadata, "source", resourceName),
                    true,
                    out MentorKnowledgeSource source))
            {
                throw new InvalidDataException($"Mentor knowledge resource {resourceName} has an invalid source.");
            }

            return new MentorKnowledgeDocument(
                Required(metadata, "id", resourceName),
                Required(metadata, "title", resourceName),
                topics,
                content,
                source,
                Required(metadata, "version", resourceName));
        }

        private static string Required(
            IReadOnlyDictionary<string, string> metadata,
            string key,
            string resourceName)
        {
            if (!metadata.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Mentor knowledge resource {resourceName} is missing {key}.");
            return value;
        }
    }
}
