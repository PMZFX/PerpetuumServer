using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Perpetuum.EntityFramework;
using Perpetuum.ExportedTypes;
using Perpetuum.Services.Mentoring;
using Xunit;

namespace Perpetuum.Tests.Services.Mentoring
{
    public class DiagnosticMentorTests
    {
        [Theory]
        [InlineData("def_mesmer_bot", "Mesmer")]
        [InlineData("def_mesmer_mk2_bot", "Mesmer Mk2")]
        [InlineData("def_npc_mesmer_guard", "NPC Mesmer Guard")]
        [InlineData("def_mesmer_chassis_cprg", "Mesmer Chassis Calibration Program")]
        public void DefinitionNamesBecomeReadableWithoutInventingData(
            string definitionName,
            string expected)
        {
            Assert.Equal(expected, MentorNameFormatter.ToDisplayName(definitionName));
        }

        [Fact]
        public void EntityResolverPrioritizesCanonicalRobotAndExcludesHiddenDefinitions()
        {
            var resolver = new MentorEntityResolver(new Lazy<IEntityDefaultReader>(() => new EntityReader(
                Entity(1, "def_mesmer_chassis"),
                Entity(2, "def_mesmer_mk2_bot"),
                Entity(3, "def_mesmer_bot"),
                Entity(4, "def_npc_mesmer_guard", hidden: true))));

            IReadOnlyList<MentorEntityMatch> matches = resolver.Resolve("Mesmer");

            Assert.Equal(3, matches.Count);
            Assert.Equal("def_mesmer_bot", matches[0].DefinitionName);
            Assert.DoesNotContain(matches, match => match.Definition == 4);
        }

        [Fact]
        public void EntityReaderInitializationIsDeferredUntilAQuestionNeedsIt()
        {
            bool initialized = false;
            var resolver = new MentorEntityResolver(new Lazy<IEntityDefaultReader>(() =>
            {
                initialized = true;
                return new EntityReader();
            }));

            Assert.False(initialized);
            Assert.Empty(resolver.Resolve("Mesmer"));
            Assert.True(initialized);
        }

        [Fact]
        public async Task LocationAnswerUsesOnlyTheProvidedSnapshot()
        {
            DiagnosticMentorProvider provider = Provider();
            MentorPlayerContext context = Context(
                isDocked: true,
                zoneName: "New Virginia",
                position: new Position(120.4, 88.8),
                dockingBaseEid: 900,
                dockingBaseName: "def_tma_terminal");

            string answer = await Ask(provider, "Where am I?", context);

            Assert.Contains("Tma Terminal", answer);
            Assert.Contains("New Virginia", answer);
            Assert.Contains("(120, 89)", answer);
        }

        [Fact]
        public async Task RobotAnswerReportsCanonicalServerDefinition()
        {
            DiagnosticMentorProvider provider = Provider();
            MentorPlayerContext context = Context(
                activeRobotEid: 800,
                activeRobotDefinition: 42,
                activeRobotName: "def_mesmer_mk2_bot");

            string answer = await Ask(provider, "What robot am I using?", context);

            Assert.Contains("Mesmer Mk2", answer);
            Assert.Contains("def_mesmer_mk2_bot", answer);
            Assert.Contains("definition 42", answer);
        }

        [Fact]
        public async Task ObjectiveAnswerUsesActiveMissionTargetAndProgress()
        {
            var objective = new MentorObjectiveSnapshot(
                12,
                "kill_definition",
                true,
                false,
                false,
                1,
                3,
                77,
                "def_arkhe_bot",
                5,
                new Position(40, 60),
                10,
                null);
            var mission = new MentorMissionSnapshot(
                9,
                Guid.NewGuid(),
                "Target Acquisition",
                "combat_training",
                1,
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1),
                new[] { objective });
            DiagnosticMentorProvider provider = Provider(
                new StaticMissionReader(new MentorMissionState(new[] { mission })));

            string answer = await Ask(provider, "What is my current objective?", Context());

            Assert.Contains("Target Acquisition", answer);
            Assert.Contains("Destroy 3 Arkhe", answer);
            Assert.Contains("1/3 complete", answer);
            Assert.Contains("zone 5 near (40, 60)", answer);
        }

        [Theory]
        [InlineData(
            "use_itemsupply",
            "activate the marked Item Supply and remain still until",
            "is loaded into cargo")]
        [InlineData(
            "submit_item",
            "open the marked submission point and move",
            "from cargo into Submit items")]
        public async Task TransportObjectivesExplainTheRequiredInteraction(
            string objectiveType,
            string expectedStart,
            string expectedEnd)
        {
            var objective = new MentorObjectiveSnapshot(
                12,
                objectiveType,
                true,
                false,
                false,
                0,
                1,
                5333,
                "def_missionitem_general_container",
                45,
                new Position(40, 60),
                10,
                null);
            var mission = new MentorMissionSnapshot(
                1150,
                Guid.NewGuid(),
                "Transport training",
                "training",
                0,
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1),
                new[] { objective });
            DiagnosticMentorProvider provider = Provider(
                new StaticMissionReader(new MentorMissionState(new[] { mission })));

            string answer = await Ask(provider, "What is my current objective?", Context());

            Assert.Contains(expectedStart, answer, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedEnd, answer, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task NoMissionStateIsReportedWithoutGuessing()
        {
            DiagnosticMentorProvider provider = Provider();

            string answer = await Ask(provider, "What mission am I on?", Context());

            Assert.Equal("You do not currently have an active mission.", answer);
        }

        [Fact]
        public async Task NamedEntityQuestionReturnsCurrentDefinitionMatches()
        {
            var resolver = new StaticEntityResolver(new[]
            {
                new MentorEntityMatch(3, "def_mesmer_bot", "Mesmer", "mesmer_desc", 10),
                new MentorEntityMatch(4, "def_mesmer_mk2_bot", "Mesmer Mk2", "mesmer_mk2_desc", 10)
            });
            DiagnosticMentorProvider provider = Provider(entityResolver: resolver);

            string answer = await Ask(provider, "What is a Mesmer?", Context());

            Assert.Contains("Mesmer (def_mesmer_bot, definition 3)", answer);
            Assert.Contains("Related matches: Mesmer Mk2", answer);
            Assert.Equal("mesmer", resolver.LastQuery);
        }

        [Fact]
        public async Task PurchaseQuestionRejectsANonexistentItemBeforeGeneralMarketRetrieval()
        {
            var resolver = new StaticEntityResolver(Array.Empty<MentorEntityMatch>());
            DiagnosticMentorProvider provider = Provider(
                entityResolver: resolver,
                knowledgeBase: new EmbeddedMentorKnowledgeBase());

            string answer = await Ask(
                provider,
                "Where can I buy the Quantum Dominator Mk4?",
                Context());

            Assert.Contains("couldn't find a current server item", answer);
            Assert.Contains("quantum dominator mk4", resolver.LastQuery);
            Assert.DoesNotContain("Current mentor guide", answer);
        }

        [Fact]
        public async Task PurchaseQuestionDoesNotInventLiveMarketAvailability()
        {
            var resolver = new StaticEntityResolver(new[]
            {
                new MentorEntityMatch(3, "def_mesmer_bot", "Mesmer", "mesmer_desc", 10)
            });
            DiagnosticMentorProvider provider = Provider(
                entityResolver: resolver,
                knowledgeBase: new EmbeddedMentorKnowledgeBase());

            string answer = await Ask(provider, "Where do I buy a Mesmer?", Context());

            Assert.Contains("I found Mesmer", answer);
            Assert.Contains("don't yet have a live market-offer query", answer);
        }

        [Fact]
        public async Task UnsupportedQuestionStatesTheAvailableVerifiedTools()
        {
            DiagnosticMentorProvider provider = Provider();

            string answer = await Ask(provider, "Write me a poem", Context());

            Assert.Contains("couldn't verify", answer);
            Assert.Contains("location", answer);
            Assert.Contains("mission", answer);
        }

        [Theory]
        [InlineData("Why won't my weapon fire?", "lock the intended target")]
        [InlineData("What does accumulator recharge do?", "energy reserve")]
        [InlineData("Why can't I fit this module?", "CPU and powergrid")]
        [InlineData("How do market buy orders work?", "offers NIC to sellers")]
        public async Task CommonConceptsUseCurrentCuratedKnowledge(
            string question,
            string expected)
        {
            DiagnosticMentorProvider provider = Provider(
                knowledgeBase: new EmbeddedMentorKnowledgeBase());

            string answer = await Ask(provider, question, Context());

            Assert.Contains(expected, answer, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Current mentor guide", answer);
        }

        [Fact]
        public async Task LiveObjectiveIntentTakesPriorityOverGeneralMissionKnowledge()
        {
            var objective = new MentorObjectiveSnapshot(
                12,
                "use_switch",
                true,
                false,
                false,
                0,
                1,
                0,
                null,
                null,
                null,
                0,
                null);
            var mission = new MentorMissionSnapshot(
                9,
                Guid.NewGuid(),
                "Switch Training",
                "combat_training",
                1,
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1),
                new[] { objective });
            DiagnosticMentorProvider provider = Provider(
                new StaticMissionReader(new MentorMissionState(new[] { mission })),
                knowledgeBase: new EmbeddedMentorKnowledgeBase());

            string answer = await Ask(provider, "What should I do next?", Context());

            Assert.Contains("Switch Training", answer);
            Assert.Contains("activate the required switch", answer, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Current mentor guide", answer);
        }

        private static DiagnosticMentorProvider Provider(
            IMentorMissionStateReader missionReader = null,
            IMentorEntityResolver entityResolver = null,
            IMentorKnowledgeBase knowledgeBase = null)
        {
            return new DiagnosticMentorProvider(
                missionReader ?? new StaticMissionReader(
                    new MentorMissionState(Array.Empty<MentorMissionSnapshot>())),
                entityResolver ?? new StaticEntityResolver(Array.Empty<MentorEntityMatch>()),
                knowledgeBase ?? new EmbeddedMentorKnowledgeBase(Array.Empty<MentorKnowledgeDocument>()));
        }

        private static async Task<string> Ask(
            IMentorProvider provider,
            string message,
            MentorPlayerContext context)
        {
            MentorRequest request = new MentorRequest(
                Guid.NewGuid(),
                context.CharacterId,
                context.AccountId,
                "Mentor",
                message,
                DateTime.UtcNow);
            return await provider.GetResponseAsync(
                new MentorProviderRequest(request, context),
                TestContext.Current.CancellationToken);
        }

        private static MentorPlayerContext Context(
            bool isDocked = false,
            string zoneName = "Alpha",
            Position? position = null,
            long activeRobotEid = 0,
            int activeRobotDefinition = 0,
            string activeRobotName = null,
            long dockingBaseEid = 0,
            string dockingBaseName = null)
        {
            return new MentorPlayerContext(
                7,
                11,
                "Tester",
                isDocked,
                1,
                zoneName,
                position,
                activeRobotEid,
                activeRobotDefinition,
                activeRobotName,
                dockingBaseEid,
                dockingBaseName,
                false);
        }

        private static EntityDefault Entity(int definition, string name, bool hidden = false)
        {
            return new EntityDefault
            {
                Definition = definition,
                Name = name,
                CategoryFlags = CategoryFlags.cf_robots,
                _descriptionToken = name + "_description",
                _hidden = hidden
            };
        }

        private sealed class StaticMissionReader : IMentorMissionStateReader
        {
            private readonly MentorMissionState _state;

            public StaticMissionReader(MentorMissionState state)
            {
                _state = state;
            }

            public MentorMissionState Read(MentorRequest request)
            {
                return _state;
            }
        }

        private sealed class StaticEntityResolver : IMentorEntityResolver
        {
            private readonly IReadOnlyList<MentorEntityMatch> _matches;

            public StaticEntityResolver(IReadOnlyList<MentorEntityMatch> matches)
            {
                _matches = matches;
            }

            public string LastQuery { get; private set; }

            public IReadOnlyList<MentorEntityMatch> Resolve(string query, int maximumResults = 5)
            {
                LastQuery = query;
                return _matches.Take(maximumResults).ToArray();
            }
        }

        private sealed class EntityReader : IEntityDefaultReader
        {
            private readonly EntityDefault[] _entities;

            public EntityReader(params EntityDefault[] entities)
            {
                _entities = entities;
            }

            public bool Exists(int definition)
            {
                return _entities.Any(entity => entity.Definition == definition);
            }

            public EntityDefault Get(int definition)
            {
                return _entities.FirstOrDefault(entity => entity.Definition == definition) ?? EntityDefault.None;
            }

            public EntityDefault GetByEid(long eid)
            {
                return EntityDefault.None;
            }

            public bool TryGet(int definition, out EntityDefault entityDefault)
            {
                entityDefault = Get(definition);
                return entityDefault != EntityDefault.None;
            }

            public IEnumerable<EntityDefault> GetAll()
            {
                return _entities;
            }
        }
    }
}
