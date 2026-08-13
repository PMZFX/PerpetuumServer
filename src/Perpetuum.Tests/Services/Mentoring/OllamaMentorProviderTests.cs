using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Perpetuum.Services.Mentoring;
using Xunit;

namespace Perpetuum.Tests.Services.Mentoring
{
    public class ModelEndpointMentorProviderTests
    {
        [Fact]
        public async Task SendsOnlyReadOnlySnapshotsAndCurrentGuidesToModel()
        {
            var handler = new RecordingHandler("Use your active objective and lock the Arkhe.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            string answer = await Ask(provider, "I am lost. What now?");

            Assert.Equal("Use your active objective and lock the Arkhe.", answer);
            Assert.Contains("VERIFIED PLAYER SNAPSHOT", handler.LastRequest);
            Assert.Contains("VERIFIED ACTIVE MISSIONS", handler.LastRequest);
            Assert.Contains("CURRENT CURATED MENTOR GUIDES", handler.LastRequest);
            Assert.Contains("I am lost. What now?", handler.LastRequest);
            Assert.DoesNotContain("password", handler.LastRequest, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task MarksEverySnapshotAsFreshAndDoesNotTreatTrainingAsCombatRestriction()
        {
            var handler = new RecordingHandler("Check the target and module state.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "Can I attack these drones?");

            Assert.Contains("refreshed for this question", handler.LastRequest);
            Assert.Contains("not the current area and not a combat restriction", handler.LastRequest);
            Assert.Contains("VERIFIED VISIBLE TARGET SNAPSHOT", handler.LastRequest);
            Assert.Contains("Never invent keyboard or mouse controls", handler.LastRequest);
        }

        [Fact]
        public async Task SuppliesVerifiedVisibleNpcAttackAndLockState()
        {
            var handler = new RecordingHandler("That visible NPC is a valid target.");
            using ModelEndpointMentorProvider provider = Provider(handler);
            var npc = new MentorNearbyUnitSnapshot(
                900,
                77,
                "def_npc_dummy_decoy",
                12.5,
                true,
                true,
                true,
                "Locked",
                true);
            var context = new MentorPlayerContext(
                7,
                11,
                "Tester",
                false,
                45,
                "Training Island",
                new Position(40, 60),
                88,
                42,
                "def_arkhe_bot",
                0,
                null,
                true,
                new[] { npc });

            await Ask(provider, "Can I attack this drone?", context);

            Assert.Contains("NPC Dummy Decoy", handler.LastRequest);
            Assert.Contains("within_current_locking_range=yes", handler.LastRequest);
            Assert.Contains("valid_attack_target=yes", handler.LastRequest);
            Assert.Contains("lock_state=Locked", handler.LastRequest);
            Assert.Contains("primary_lock=yes", handler.LastRequest);
        }

        [Fact]
        public async Task SuppliesNearestZoneCombatTargetsWhenNoneAreCurrentlyVisible()
        {
            var handler = new RecordingHandler("There are attackable drones elsewhere in this zone.");
            using ModelEndpointMentorProvider provider = Provider(handler);
            var zoneTarget = new MentorNearbyUnitSnapshot(
                901,
                1473,
                "Malfunctioning Arkhe",
                238.4,
                true,
                false,
                true,
                "none",
                false,
                false,
                new Position(1078, 940));
            var context = new MentorPlayerContext(
                7,
                11,
                "Tester",
                false,
                45,
                "Training Island",
                new Position(900, 800),
                88,
                5,
                "Arkhe",
                0,
                null,
                true,
                Array.Empty<MentorNearbyUnitSnapshot>(),
                new[] { zoneTarget },
                true);

            await Ask(provider, "Where are the closest enemies I can fight?", context);

            Assert.Contains("VERIFIED ZONE COMBAT OPPORTUNITIES", handler.LastRequest);
            Assert.Contains("Malfunctioning Arkhe", handler.LastRequest);
            Assert.Contains("visible=no", handler.LastRequest);
            Assert.Contains("position=1078,940", handler.LastRequest);
            Assert.Contains("never interpret an empty visible snapshot", handler.LastRequest);
        }

        [Fact]
        public async Task SelectsAuthoritativeTrainingRewardsForDecisionQuestions()
        {
            var handler = new RecordingHandler("Finish it for the additional cumulative rewards.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "What tutorial rewards do I lose if I leave early, and is it worth finishing?");

            Assert.Contains("Exit NIC formula: 500000 base NIC + 125000", handler.LastRequest);
            Assert.Contains("Waspish fitted robot", handler.LastRequest);
            Assert.Contains("rewards are cumulative", handler.LastRequest);
            Assert.Contains("current completed reward level is client-side", handler.LastRequest);
            Assert.Contains("never substitute a generic introduction", handler.LastRequest);
        }

        [Fact]
        public async Task DoesNotAddTrainingRewardRowsToUnrelatedQuestions()
        {
            var handler = new RecordingHandler("Check your active objective.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "What should I do next?");

            Assert.Contains("VERIFIED TRAINING-EXIT REWARD CATALOG", handler.LastRequest);
            Assert.Contains("Not selected for this question", handler.LastRequest);
            Assert.DoesNotContain("Waspish fitted robot", handler.LastRequest);
        }

        [Fact]
        public async Task CarriesTrainingSubjectIntoShortRewardFollowUp()
        {
            var handler = new RecordingHandler(
                "The tutorial has cumulative exit rewards.",
                "The level-four track includes a fitted robot.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "Can I leave the tutorial early?");
            await Ask(provider, "What are the potential rewards, though?");

            Assert.Contains("Exit NIC formula: 500000 base NIC + 125000", handler.LastRequest);
            Assert.Contains("Waspish fitted robot", handler.LastRequest);
        }

        [Fact]
        public async Task DoesNotConfuseMissionRewardsWithTrainingExitRewards()
        {
            var handler = new RecordingHandler("Read the active mission's reward panel.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "What is the reward for my active mission?");

            Assert.Contains("Not selected for this question", handler.LastRequest);
            Assert.DoesNotContain("Waspish fitted robot", handler.LastRequest);
        }

        [Fact]
        public async Task RecentConversationIsBoundedAndCanBeClearedWithoutCallingModel()
        {
            var handler = new RecordingHandler("first answer", "second answer");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "first question");
            await Ask(provider, "follow up");
            Assert.Contains("Player: first question", handler.LastRequest);
            Assert.Contains("ARIA: first answer", handler.LastRequest);

            int requestsBeforeClear = handler.RequestCount;
            string answer = await Ask(provider, "mentor clear");

            Assert.Contains("cleared", answer, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(requestsBeforeClear, handler.RequestCount);
        }

        [Fact]
        public async Task FeedbackRatesLastAnswerWithoutCallingModel()
        {
            var handler = new RecordingHandler("Use the live objective.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "What should I do next?");
            int requestsBeforeFeedback = handler.RequestCount;

            string response = await Ask(provider, "mentor -");
            string duplicate = await Ask(provider, "mentor -");

            Assert.Contains("recorded", response, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("already marked unhelpful", duplicate, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(requestsBeforeFeedback, handler.RequestCount);
        }

        [Fact]
        public async Task FeedbackCanRateDeterministicLiveAnswer()
        {
            var handler = new RecordingHandler(Array.Empty<string>());
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "Where am I?");
            string response = await Ask(provider, "mentor +");

            Assert.Contains("helpful", response, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task FeedbackRequiresAnAnswerAndClearRemovesRateableHistory()
        {
            var handler = new RecordingHandler("Use the live objective.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            string empty = await Ask(provider, "mentor +");
            await Ask(provider, "What next?");
            await Ask(provider, "mentor clear");
            string cleared = await Ask(provider, "mentor -");

            Assert.Contains("isn't a recent", empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("isn't a recent", cleared, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task MentorHelpDocumentsFeedbackAndClearCommandsWithoutCallingModel()
        {
            var handler = new RecordingHandler(Array.Empty<string>());
            using ModelEndpointMentorProvider provider = Provider(handler);

            string answer = await Ask(provider, "mentor help");

            Assert.Contains("mentor +", answer);
            Assert.Contains("mentor -", answer);
            Assert.Contains("mentor clear", answer);
            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task ModelFailureUsesVerifiedDeterministicFallback()
        {
            var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
            using ModelEndpointMentorProvider provider = Provider(handler);

            string answer = await Ask(provider, "Why won't my weapon fire?");

            Assert.Contains("lock the intended target", answer, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Current mentor guide", answer);
        }

        [Fact]
        public async Task OpenAiCompatibleProtocolUsesExpectedRoutePayloadAndResponse()
        {
            var handler = RecordingHandler.OpenAi("Use the live objective.");
            using ModelEndpointMentorProvider provider = Provider(
                handler,
                openAiCompatible: true);

            string answer = await Ask(provider, "What next?");

            Assert.Equal("Use the live objective.", answer);
            Assert.Equal("http://mentor.test/v1/chat/completions", handler.LastUri.ToString());
            Assert.Contains("\"max_tokens\":700", handler.LastRequest);
            Assert.DoesNotContain("num_predict", handler.LastRequest);
        }

        [Fact]
        public async Task DirectLocationQuestionUsesFreshSnapshotWithoutModelInterpretation()
        {
            var handler = new RecordingHandler(Array.Empty<string>());
            using ModelEndpointMentorProvider provider = Provider(handler);
            var context = new MentorPlayerContext(
                7,
                11,
                "Tester",
                false,
                45,
                "zone_training",
                new Position(464, 584),
                88,
                42,
                "def_arkhe_bot",
                0,
                null,
                true);

            string answer = await Ask(provider, "Where am I right now?", context);

            Assert.Equal(
                "You're undocked in Zone Training near (464, 584). [Live server state]",
                answer);
            Assert.Equal(0, handler.RequestCount);
            Assert.DoesNotContain("combat", answer, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LockHotkeyQuestionUsesDocumentedDefaultsWithoutCallingModel()
        {
            var handler = new RecordingHandler(Array.Empty<string>());
            using ModelEndpointMentorProvider provider = Provider(handler);

            string answer = await Ask(provider, "Is there a hot key to lock onto a target?");

            Assert.Equal(0, handler.RequestCount);
            Assert.Contains("press F to lock", answer);
            Assert.Contains("R to lock it and make it your primary target", answer);
            Assert.Contains("keyboard settings", answer);
            Assert.DoesNotContain("middle mouse", answer, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task InventedGuideCitationFallsBackToCurrentCuratedAnswer()
        {
            var handler = new RecordingHandler(
                "Press T to lock it. [Guide: Combat Basics v1.0]");
            using ModelEndpointMentorProvider provider = Provider(handler);

            string answer = await Ask(provider, "Why won't my weapon fire?");

            Assert.Contains("lock the intended target", answer, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Combat Basics", answer);
            Assert.DoesNotContain("Press T", answer);
        }

        [Fact]
        public async Task SendsOnlyGuidesRelevantToCurrentQuestion()
        {
            var handler = new RecordingHandler("Check locking and range.");
            using ModelEndpointMentorProvider provider = Provider(handler);

            await Ask(provider, "How do I lock a target?");

            Assert.Contains("Target locking and range", handler.LastRequest);
            Assert.DoesNotContain("Manufacturing and production", handler.LastRequest);
            Assert.DoesNotContain("Mining basics", handler.LastRequest);
        }

        private static ModelEndpointMentorProvider Provider(
            HttpMessageHandler handler,
            bool openAiCompatible = false)
        {
            IMentorMissionStateReader missions = new StaticMissionReader();
            IMentorTrainingRewardReader rewards = new StaticTrainingRewardReader();
            IMentorEntityResolver entities = new EmptyEntityResolver();
            IMentorKnowledgeBase knowledge = new EmbeddedMentorKnowledgeBase();
            var fallback = new DiagnosticMentorProvider(missions, entities, knowledge);
            return new ModelEndpointMentorProvider(
                missions,
                new Lazy<IMentorTrainingRewardReader>(() => rewards),
                entities,
                knowledge,
                fallback,
                new HttpClient(handler),
                new Uri("http://mentor.test/"),
                "test-model",
                openAiCompatible);
        }

        private sealed class StaticTrainingRewardReader : IMentorTrainingRewardReader
        {
            public MentorTrainingRewardCatalog Read()
            {
                return new MentorTrainingRewardCatalog(
                    500000,
                    125000,
                    4,
                    new[]
                    {
                        new MentorTrainingRewardSnapshot(
                            4,
                            1,
                            0,
                            null,
                            0,
                            201,
                            "Waspish",
                            "waspish_starterpack")
                    });
            }
        }

        private static async Task<string> Ask(
            IMentorProvider provider,
            string message,
            MentorPlayerContext context = null)
        {
            var request = new MentorRequest(
                Guid.NewGuid(),
                7,
                11,
                "Mentor",
                message,
                DateTime.UtcNow);
            context ??= new MentorPlayerContext(
                7,
                11,
                "Tester",
                false,
                5,
                "New Virginia",
                new Position(40, 60),
                88,
                42,
                "def_arkhe_bot",
                0,
                null,
                true);
            return await provider.GetResponseAsync(
                new MentorProviderRequest(request, context),
                TestContext.Current.CancellationToken);
        }

        private sealed class StaticMissionReader : IMentorMissionStateReader
        {
            public MentorMissionState Read(MentorRequest request)
            {
                var objective = new MentorObjectiveSnapshot(
                    1,
                    "lock_unit",
                    true,
                    false,
                    false,
                    0,
                    1,
                    42,
                    "def_arkhe_bot",
                    5,
                    new Position(80, 90),
                    10,
                    null);
                var mission = new MentorMissionSnapshot(
                    9,
                    Guid.NewGuid(),
                    "Target practice",
                    "training",
                    1,
                    DateTime.UtcNow,
                    DateTime.UtcNow.AddHours(1),
                    new[] { objective });
                return new MentorMissionState(new[] { mission });
            }
        }

        private sealed class EmptyEntityResolver : IMentorEntityResolver
        {
            public IReadOnlyList<MentorEntityMatch> Resolve(string query, int maximumResults = 5)
            {
                return Array.Empty<MentorEntityMatch>();
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;

            public RecordingHandler(params string[] answers)
            {
                _responses = new Queue<HttpResponseMessage>(answers.Select(answer =>
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $"{{\"message\":{{\"content\":\"{answer}\"}}}}",
                            Encoding.UTF8,
                            "application/json")
                    }));
            }

            public RecordingHandler(HttpStatusCode statusCode)
            {
                _responses = new Queue<HttpResponseMessage>(new[]
                {
                    new HttpResponseMessage(statusCode)
                });
            }

            private RecordingHandler(IEnumerable<HttpResponseMessage> responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            public static RecordingHandler OpenAi(params string[] answers)
            {
                return new RecordingHandler(answers.Select(answer =>
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $"{{\"choices\":[{{\"message\":{{\"content\":\"{answer}\"}}}}]}}",
                            Encoding.UTF8,
                            "application/json")
                    }));
            }

            public int RequestCount { get; private set; }
            public string LastRequest { get; private set; }
            public Uri LastUri { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestCount++;
                LastUri = request.RequestUri;
                LastRequest = await request.Content.ReadAsStringAsync(cancellationToken);
                return _responses.Dequeue();
            }
        }
    }
}
