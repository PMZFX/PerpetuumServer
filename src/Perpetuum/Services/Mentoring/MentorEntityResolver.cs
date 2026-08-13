using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.EntityFramework;

namespace Perpetuum.Services.Mentoring
{
    public sealed class MentorEntityResolver : IMentorEntityResolver
    {
        private readonly Lazy<IEntityDefaultReader> _entityDefaultReader;
        private readonly IMentorTextCatalog _textCatalog;

        public MentorEntityResolver(Lazy<IEntityDefaultReader> entityDefaultReader)
            : this(entityDefaultReader, FallbackMentorTextCatalog.Instance)
        {
        }

        public MentorEntityResolver(
            Lazy<IEntityDefaultReader> entityDefaultReader,
            IMentorTextCatalog textCatalog)
        {
            _entityDefaultReader = entityDefaultReader ??
                throw new ArgumentNullException(nameof(entityDefaultReader));
            _textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
        }

        public IReadOnlyList<MentorEntityMatch> Resolve(string query, int maximumResults = 5)
        {
            if (maximumResults <= 0 || string.IsNullOrWhiteSpace(query))
                return Array.Empty<MentorEntityMatch>();

            string normalizedQuery = MentorNameFormatter.Normalize(query);
            if (normalizedQuery.Length == 0)
                return Array.Empty<MentorEntityMatch>();

            string[] queryTokens = normalizedQuery.Split(' ');
            bool definitionQuery = int.TryParse(normalizedQuery, out int requestedDefinition);

            return _entityDefaultReader.Value.GetAll()
                .Where(entity => entity != null && entity != EntityDefault.None && !entity._hidden)
                .Select(entity => new
                {
                    Entity = entity,
                    DisplayName = _textCatalog.DisplayName(entity.Name),
                    NormalizedName = MentorNameFormatter.Normalize(entity.Name),
                    NormalizedDisplayName = MentorNameFormatter.Normalize(
                        _textCatalog.DisplayName(entity.Name))
                })
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = Score(
                        candidate.Entity,
                        candidate.NormalizedName,
                        candidate.NormalizedDisplayName,
                        normalizedQuery,
                        queryTokens,
                        definitionQuery,
                        requestedDefinition)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Candidate.Entity.Definition)
                .Take(maximumResults)
                .Select(candidate => new MentorEntityMatch(
                    candidate.Candidate.Entity.Definition,
                    candidate.Candidate.Entity.Name,
                    candidate.Candidate.DisplayName,
                    candidate.Candidate.Entity._descriptionToken,
                    (long)candidate.Candidate.Entity.CategoryFlags,
                    _textCatalog.Description(candidate.Candidate.Entity._descriptionToken)))
                .ToArray();
        }

        private static int Score(
            EntityDefault entity,
            string normalizedName,
            string normalizedDisplayName,
            string normalizedQuery,
            string[] queryTokens,
            bool definitionQuery,
            int requestedDefinition)
        {
            if (definitionQuery && entity.Definition == requestedDefinition)
                return 2000;

            if (!queryTokens.All(token =>
                    normalizedName.Split(' ').Contains(token) ||
                    normalizedDisplayName.Split(' ').Contains(token)))
            {
                return 0;
            }

            int score;
            if (normalizedDisplayName == normalizedQuery)
                score = 1200;
            else if (normalizedName == normalizedQuery)
                score = 1100;
            else if (normalizedDisplayName.StartsWith(normalizedQuery + " ", StringComparison.Ordinal))
                score = 900;
            else
                score = 600 + queryTokens.Length * 20;

            string[] nameTokens = normalizedName.Split(' ');
            if (nameTokens.LastOrDefault() == "bot")
                score += 40;
            if (!queryTokens.Contains("npc") && nameTokens.Contains("npc"))
                score -= 100;
            if (!queryTokens.Contains("cprg") && nameTokens.Contains("cprg"))
                score -= 80;
            if (!queryTokens.Contains("reward") && nameTokens.Any(token => token.StartsWith("reward", StringComparison.Ordinal)))
                score -= 60;

            return score;
        }
    }
}
