using System;
using System.Collections.Generic;

namespace Perpetuum.Services.Mentoring
{
    public enum MentorKnowledgeSource
    {
        Historical = 0,
        GeneratedCurrent = 1,
        CuratedCurrent = 2
    }

    public sealed class MentorKnowledgeDocument
    {
        public MentorKnowledgeDocument(
            string id,
            string title,
            IReadOnlyList<string> topics,
            string content,
            MentorKnowledgeSource source,
            string version)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Topics = topics ?? throw new ArgumentNullException(nameof(topics));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Source = source;
            Version = version ?? throw new ArgumentNullException(nameof(version));
        }

        public string Id { get; }
        public string Title { get; }
        public IReadOnlyList<string> Topics { get; }
        public string Content { get; }
        public MentorKnowledgeSource Source { get; }
        public string Version { get; }
    }

    public sealed class MentorKnowledgeMatch
    {
        public MentorKnowledgeMatch(MentorKnowledgeDocument document, int score)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Score = score;
        }

        public MentorKnowledgeDocument Document { get; }
        public int Score { get; }
    }

    public interface IMentorKnowledgeBase
    {
        IReadOnlyList<MentorKnowledgeDocument> Documents { get; }
        IReadOnlyList<MentorKnowledgeMatch> Search(string query, int maximumResults = 3);
    }
}
