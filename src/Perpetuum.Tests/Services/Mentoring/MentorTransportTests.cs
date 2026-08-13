using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Perpetuum.Services.Mentoring;
using Xunit;

namespace Perpetuum.Tests.Services.Mentoring
{
    public class MentorTransportTests
    {
        [Fact]
        public async Task DeterministicProviderEchoesTheQuestion()
        {
            var provider = new DeterministicMentorProvider();

            string response = await provider.GetResponseAsync(
                new MentorProviderRequest(Request("How do I mine?"), Context()),
                TestContext.Current.CancellationToken);

            Assert.Equal("Mentor test received: How do I mine?", response);
        }

        [Fact]
        public void BridgeInterceptsOnlyTheMentorChannel()
        {
            var dispatcher = new RecordingDispatcher();
            var transcript = new RecordingConversationSink();
            var bridge = new MentorBridge(Options(), new FixedClock(), dispatcher, transcript);

            Assert.False(bridge.TryHandle("Help", 7, 11, "hello"));
            Assert.True(bridge.TryHandle("mentor", 7, 11, "Where am I?"));

            Assert.Single(dispatcher.Requests);
            MentorRequest request = dispatcher.Requests.TryPeek(out MentorRequest queued) ? queued : null;
            Assert.NotNull(request);
            Assert.Equal(7, request.CharacterId);
            Assert.Equal(11, request.AccountId);
            Assert.Equal("Mentor", request.Channel);
            Assert.Equal("Where am I?", request.Message);
            Assert.Single(transcript.Questions);
            Assert.True(transcript.Questions.TryPeek(out RecordedQuestion question));
            Assert.Equal(7, question.CharacterId);
            Assert.Equal("Mentor", question.Channel);
            Assert.Equal("Where am I?", question.Message);
        }

        [Fact]
        public async Task AcceptedRequestRunsOffQueueAndTargetsTheRequester()
        {
            var sink = new RecordingSink();
            using var processor = Processor(
                Options(),
                new DeterministicMentorProvider(),
                new StaticContextReader(),
                sink);
            processor.Start();

            MentorSubmissionStatus status = processor.Submit(Request("How do I mine?"));
            MentorResponse response = await sink.Next.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.Equal(MentorSubmissionStatus.Accepted, status);
            Assert.Equal(7, response.CharacterId);
            Assert.Equal("Mentor", response.Channel);
            Assert.Equal("Mentor test received: How do I mine?", response.Text);
        }

        [Fact]
        public void QueueIsBoundedAndRejectsWithoutBlocking()
        {
            MentorOptions options = Options(queueCapacity: 1);
            var sink = new RecordingSink();
            using var processor = Processor(
                options,
                new DeterministicMentorProvider(),
                new StaticContextReader(),
                sink);

            Assert.Equal(MentorSubmissionStatus.Accepted, processor.Submit(Request("first")));
            Assert.Equal(MentorSubmissionStatus.QueueFull, processor.Submit(Request("second")));
            Assert.True(sink.Responses.TryPeek(out MentorResponse response));
            Assert.Equal(MentorMessages.QueueFull, response.Text);
        }

        [Fact]
        public void RateLimitAppliesToBothCharacterAndAccount()
        {
            MentorOptions options = Options(
                queueCapacity: 10,
                requestsPerCharacter: 1,
                requestsPerAccount: 2);
            var sink = new RecordingSink();
            using var processor = Processor(
                options,
                new DeterministicMentorProvider(),
                new StaticContextReader(),
                sink);

            Assert.Equal(MentorSubmissionStatus.Accepted, processor.Submit(Request("one", 7, 11)));
            Assert.Equal(MentorSubmissionStatus.RateLimited, processor.Submit(Request("two", 7, 11)));
            Assert.Equal(MentorSubmissionStatus.Accepted, processor.Submit(Request("three", 8, 11)));
            Assert.Equal(MentorSubmissionStatus.RateLimited, processor.Submit(Request("four", 9, 11)));
            Assert.Equal(2, sink.Responses.Count);
        }

        [Fact]
        public async Task ProviderTimeoutReturnsSafeMessageAndQueueContinues()
        {
            MentorOptions options = Options(providerTimeout: TimeSpan.FromMilliseconds(30));
            var sink = new RecordingSink();
            var provider = new DelegateProvider((request, cancellationToken) =>
            {
                if (request.Request.Message == "hang")
                    return new TaskCompletionSource<string>().Task;
                return Task.FromResult("recovered");
            });
            using var processor = Processor(options, provider, new StaticContextReader(), sink);
            processor.Start();

            processor.Submit(Request("hang"));
            MentorResponse timeout = await sink.Next.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            sink.ResetNext();
            processor.Submit(Request("next"));
            MentorResponse recovered = await sink.Next.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.Equal(MentorMessages.TimedOut, timeout.Text);
            Assert.Equal("recovered", recovered.Text);
        }

        [Fact]
        public async Task ProviderFailureDoesNotExposeExceptionDetails()
        {
            var sink = new RecordingSink();
            var provider = new DelegateProvider((request, cancellationToken) =>
                Task.FromException<string>(new InvalidOperationException("secret provider detail")));
            using var processor = Processor(Options(), provider, new StaticContextReader(), sink);
            processor.Start();

            processor.Submit(Request("fail"));
            MentorResponse response = await sink.Next.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.Equal(MentorMessages.Unavailable, response.Text);
            Assert.DoesNotContain("secret", response.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task MissingPlayerContextDoesNotCallProvider()
        {
            var sink = new RecordingSink();
            var provider = new CountingProvider();
            using var processor = Processor(
                Options(),
                provider,
                new ThrowingContextReader(),
                sink);
            processor.Start();

            processor.Submit(Request("Where am I?"));
            MentorResponse response = await sink.Next.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.Equal(MentorMessages.ContextUnavailable, response.Text);
            Assert.Equal(0, provider.CallCount);
        }

        [Fact]
        public async Task ProviderResponsesAreBoundedBeforeDelivery()
        {
            MentorOptions options = Options(maximumResponseLength: 8);
            var sink = new RecordingSink();
            var provider = new DelegateProvider((request, cancellationToken) =>
                Task.FromResult("123456789"));
            using var processor = Processor(options, provider, new StaticContextReader(), sink);
            processor.Start();

            processor.Submit(Request("answer"));
            MentorResponse response = await sink.Next.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.Equal("12345678", response.Text);
        }

        private static MentorRequestProcessor Processor(
            MentorOptions options,
            IMentorProvider provider,
            IMentorPlayerContextReader contextReader,
            RecordingSink sink)
        {
            var clock = new FixedClock();
            return new MentorRequestProcessor(
                options,
                provider,
                contextReader,
                sink,
                new MentorRateLimiter(options, clock));
        }

        private static MentorOptions Options(
            int queueCapacity = 8,
            TimeSpan? providerTimeout = null,
            int requestsPerCharacter = 20,
            int requestsPerAccount = 40,
            int maximumResponseLength = 2000)
        {
            return new MentorOptions(
                "Mentor",
                queueCapacity,
                providerTimeout ?? TimeSpan.FromSeconds(1),
                TimeSpan.FromMinutes(1),
                requestsPerCharacter,
                requestsPerAccount,
                1000,
                maximumResponseLength);
        }

        private static MentorRequest Request(
            string message,
            int characterId = 7,
            int accountId = 11)
        {
            return new MentorRequest(
                Guid.NewGuid(),
                characterId,
                accountId,
                "Mentor",
                message,
                DateTime.UtcNow);
        }

        private static MentorPlayerContext Context()
        {
            return new MentorPlayerContext(7, 11, "Tester", true, 1, "Alpha", null, 0);
        }

        private sealed class FixedClock : IMentorClock
        {
            public DateTime UtcNow { get; set; } = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        }

        private sealed class RecordingDispatcher : IMentorRequestDispatcher
        {
            public ConcurrentQueue<MentorRequest> Requests { get; } = new ConcurrentQueue<MentorRequest>();

            public MentorSubmissionStatus Submit(MentorRequest request)
            {
                Requests.Enqueue(request);
                return MentorSubmissionStatus.Accepted;
            }
        }

        private sealed class RecordingConversationSink : IMentorConversationSink
        {
            public ConcurrentQueue<RecordedQuestion> Questions { get; } =
                new ConcurrentQueue<RecordedQuestion>();

            public void EchoQuestion(int characterId, string channel, string message)
            {
                Questions.Enqueue(new RecordedQuestion(characterId, channel, message));
            }
        }

        private sealed class RecordedQuestion
        {
            public RecordedQuestion(int characterId, string channel, string message)
            {
                CharacterId = characterId;
                Channel = channel;
                Message = message;
            }

            public int CharacterId { get; }
            public string Channel { get; }
            public string Message { get; }
        }

        private sealed class RecordingSink : IMentorResponseSink
        {
            public ConcurrentQueue<MentorResponse> Responses { get; } = new ConcurrentQueue<MentorResponse>();
            public TaskCompletionSource<MentorResponse> Next { get; private set; } = NewCompletion();

            public void Send(MentorResponse response)
            {
                Responses.Enqueue(response);
                Next.TrySetResult(response);
            }

            public void ResetNext()
            {
                Next = NewCompletion();
            }

            private static TaskCompletionSource<MentorResponse> NewCompletion()
            {
                return new TaskCompletionSource<MentorResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private sealed class StaticContextReader : IMentorPlayerContextReader
        {
            public MentorPlayerContext Read(MentorRequest request)
            {
                return new MentorPlayerContext(
                    request.CharacterId,
                    request.AccountId,
                    "Tester",
                    true,
                    1,
                    "Alpha",
                    null,
                    0);
            }
        }

        private sealed class ThrowingContextReader : IMentorPlayerContextReader
        {
            public MentorPlayerContext Read(MentorRequest request)
            {
                throw new InvalidOperationException("no context");
            }
        }

        private sealed class DelegateProvider : IMentorProvider
        {
            private readonly Func<MentorProviderRequest, CancellationToken, Task<string>> _handler;

            public DelegateProvider(
                Func<MentorProviderRequest, CancellationToken, Task<string>> handler)
            {
                _handler = handler;
            }

            public Task<string> GetResponseAsync(
                MentorProviderRequest request,
                CancellationToken cancellationToken)
            {
                return _handler(request, cancellationToken);
            }
        }

        private sealed class CountingProvider : IMentorProvider
        {
            public int CallCount { get; private set; }

            public Task<string> GetResponseAsync(
                MentorProviderRequest request,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult("unexpected");
            }
        }
    }
}
