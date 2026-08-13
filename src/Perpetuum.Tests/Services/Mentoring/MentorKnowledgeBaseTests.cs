using System;
using System.Linq;
using Perpetuum.Services.Mentoring;
using Xunit;

namespace Perpetuum.Tests.Services.Mentoring
{
    public class MentorKnowledgeBaseTests
    {
        [Fact]
        public void EmbeddedCurrentGuidesAreLoadedAndRankExactTopicsFirst()
        {
            var knowledgeBase = new EmbeddedMentorKnowledgeBase();

            MentorKnowledgeMatch match = knowledgeBase
                .Search("my weapon is not firing")
                .FirstOrDefault();

            Assert.NotNull(match);
            Assert.Equal("combat-module-troubleshooting", match.Document.Id);
            Assert.Equal(MentorKnowledgeSource.CuratedCurrent, match.Document.Source);
            Assert.Equal("1", match.Document.Version);
        }

        [Fact]
        public void UnrelatedOrStopWordOnlyQuestionsDoNotProduceFalseMatches()
        {
            var knowledgeBase = new EmbeddedMentorKnowledgeBase();

            Assert.Empty(knowledgeBase.Search("what is this"));
            Assert.Empty(knowledgeBase.Search("purple dancing clouds"));
        }

        [Fact]
        public void SourcePriorityBreaksOtherwiseEqualScores()
        {
            MentorKnowledgeDocument historical = Document(
                "old",
                MentorKnowledgeSource.Historical);
            MentorKnowledgeDocument current = Document(
                "current",
                MentorKnowledgeSource.CuratedCurrent);
            var knowledgeBase = new EmbeddedMentorKnowledgeBase(new[] { historical, current });

            MentorKnowledgeMatch best = knowledgeBase.Search("accumulator").First();

            Assert.Equal("current", best.Document.Id);
        }

        private static MentorKnowledgeDocument Document(
            string id,
            MentorKnowledgeSource source)
        {
            return new MentorKnowledgeDocument(
                id,
                "Accumulator",
                new[] { "accumulator" },
                "Accumulator reference.",
                source,
                "1");
        }
    }
}
