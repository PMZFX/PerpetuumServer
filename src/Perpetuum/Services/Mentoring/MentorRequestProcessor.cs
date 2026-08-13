using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Perpetuum.Log;
using Perpetuum.Threading.Process;

namespace Perpetuum.Services.Mentoring
{
    public sealed class MentorRequestProcessor : IProcess, IMentorRequestDispatcher, IDisposable
    {
        private readonly MentorOptions _options;
        private readonly IMentorProvider _provider;
        private readonly IMentorPlayerContextReader _contextReader;
        private readonly IMentorResponseSink _responseSink;
        private readonly MentorRateLimiter _rateLimiter;
        private readonly BlockingCollection<MentorRequest> _queue;
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly object _lifecycleSync = new object();
        private Task _worker;
        private bool _disposed;

        public MentorRequestProcessor(
            MentorOptions options,
            IMentorProvider provider,
            IMentorPlayerContextReader contextReader,
            IMentorResponseSink responseSink,
            MentorRateLimiter rateLimiter)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
            _responseSink = responseSink ?? throw new ArgumentNullException(nameof(responseSink));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _queue = new BlockingCollection<MentorRequest>(
                new ConcurrentQueue<MentorRequest>(),
                options.QueueCapacity);
        }

        public int PendingCount => _queue.Count;

        public MentorSubmissionStatus Submit(MentorRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                Respond(request, MentorMessages.EmptyQuestion);
                return MentorSubmissionStatus.Invalid;
            }

            if (request.Message.Length > _options.MaximumRequestLength)
            {
                Respond(request, MentorMessages.QuestionTooLong);
                return MentorSubmissionStatus.Invalid;
            }

            if (_stopping.IsCancellationRequested || _queue.IsAddingCompleted)
                return MentorSubmissionStatus.Stopping;

            if (!_rateLimiter.TryAcquire(request.CharacterId, request.AccountId))
            {
                Respond(request, MentorMessages.RateLimited);
                return MentorSubmissionStatus.RateLimited;
            }

            try
            {
                if (!_queue.TryAdd(request))
                {
                    Respond(request, MentorMessages.QueueFull);
                    return MentorSubmissionStatus.QueueFull;
                }
            }
            catch (InvalidOperationException) when (_queue.IsAddingCompleted)
            {
                return MentorSubmissionStatus.Stopping;
            }

            Log(request, "queued", null, 0, 0);
            return MentorSubmissionStatus.Accepted;
        }

        public void Start()
        {
            lock (_lifecycleSync)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(MentorRequestProcessor));
                if (_worker != null)
                    return;

                _worker = Task.Factory.StartNew(
                    ProcessQueueAsync,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
            }
        }

        public void Stop()
        {
            Task worker;
            lock (_lifecycleSync)
            {
                if (!_queue.IsAddingCompleted)
                    _queue.CompleteAdding();
                _stopping.Cancel();
                worker = _worker;
            }

            if (worker == null)
                return;

            try
            {
                worker.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException ex)
            {
                ex.Handle(inner => inner is OperationCanceledException);
            }
        }

        public void Update(TimeSpan time)
        {
            // The bounded queue owns a dedicated worker so provider latency never reaches the game loop.
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            _queue.Dispose();
            _stopping.Dispose();
            _disposed = true;
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                foreach (MentorRequest request in _queue.GetConsumingEnumerable(_stopping.Token))
                {
                    await ProcessRequestAsync(request).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
            }
        }

        private async Task ProcessRequestAsync(MentorRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            MentorPlayerContext context;

            try
            {
                context = _contextReader.Read(request);
            }
            catch (Exception ex)
            {
                Logger.Warning($"mentor request_id={request.RequestId} character_id={request.CharacterId} outcome=context_failure exception={ex.GetType().Name}");
                Respond(request, MentorMessages.ContextUnavailable);
                return;
            }

            using (var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token))
            {
                Task<string> providerTask;
                try
                {
                    providerTask = _provider.GetResponseAsync(
                        new MentorProviderRequest(request, context),
                        providerCancellation.Token);
                    if (providerTask == null)
                        throw new InvalidOperationException("The mentor provider returned no task.");
                }
                catch (Exception ex)
                {
                    LogFailure(request, stopwatch, "provider_failure", ex);
                    Respond(request, MentorMessages.Unavailable);
                    return;
                }

                Task timeoutTask = Task.Delay(_options.ProviderTimeout, _stopping.Token);
                Task completed = await Task.WhenAny(providerTask, timeoutTask).ConfigureAwait(false);

                if (completed != providerTask)
                {
                    providerCancellation.Cancel();
                    ObserveLateFailure(providerTask);

                    if (!_stopping.IsCancellationRequested)
                    {
                        Log(request, "timeout", null, stopwatch.ElapsedMilliseconds, 0);
                        Respond(request, MentorMessages.TimedOut);
                    }
                    return;
                }

                try
                {
                    string response = await providerTask.ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(response))
                        throw new InvalidOperationException("The mentor provider returned an empty response.");

                    if (response.Length > _options.MaximumResponseLength)
                        response = response.Substring(0, _options.MaximumResponseLength);

                    Respond(request, response);
                    Log(request, "completed", null, stopwatch.ElapsedMilliseconds, response.Length);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    LogFailure(request, stopwatch, "provider_failure", ex);
                    Respond(request, MentorMessages.Unavailable);
                }
            }
        }

        private static void ObserveLateFailure(Task providerTask)
        {
            _ = providerTask.ContinueWith(
                task => { _ = task.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void Respond(MentorRequest request, string text)
        {
            try
            {
                _responseSink.Send(new MentorResponse(
                    request.RequestId,
                    request.CharacterId,
                    request.Channel,
                    text));
            }
            catch (Exception ex)
            {
                Logger.Warning($"mentor request_id={request.RequestId} character_id={request.CharacterId} outcome=response_failure exception={ex.GetType().Name}");
            }
        }

        private static void LogFailure(
            MentorRequest request,
            Stopwatch stopwatch,
            string outcome,
            Exception exception)
        {
            Log(request, outcome, exception.GetType().Name, stopwatch.ElapsedMilliseconds, 0);
        }

        private static void Log(
            MentorRequest request,
            string outcome,
            string failureReason,
            long latencyMilliseconds,
            int answerLength)
        {
            Logger.Info(
                $"mentor request_id={request.RequestId} character_id={request.CharacterId} " +
                $"outcome={outcome} latency_ms={latencyMilliseconds} answer_length={answerLength} " +
                $"failure_reason={failureReason ?? "none"}");
        }
    }
}
