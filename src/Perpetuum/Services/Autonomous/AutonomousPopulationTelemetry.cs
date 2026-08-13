using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Data;
using Perpetuum.Log;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousPopulationSample
    {
        public AutonomousPopulationSample(
            DateTime observedAtUtc,
            int configuredActors,
            int registeredActors,
            int activeActors,
            int standbyActors,
            int suspendedActors,
            int faultedActors,
            int updatesCompleted,
            int updateFailures,
            int registrationFailures,
            double maximumUpdateMilliseconds,
            double maximumActorElapsedMilliseconds,
            int schedulerBacklog,
            string behaviorCounts)
        {
            ObservedAtUtc = observedAtUtc;
            ConfiguredActors = configuredActors;
            RegisteredActors = registeredActors;
            ActiveActors = activeActors;
            StandbyActors = standbyActors;
            SuspendedActors = suspendedActors;
            FaultedActors = faultedActors;
            UpdatesCompleted = updatesCompleted;
            UpdateFailures = updateFailures;
            RegistrationFailures = registrationFailures;
            MaximumUpdateMilliseconds = maximumUpdateMilliseconds;
            MaximumActorElapsedMilliseconds = maximumActorElapsedMilliseconds;
            SchedulerBacklog = schedulerBacklog;
            BehaviorCounts = behaviorCounts;
        }

        public DateTime ObservedAtUtc { get; }
        public int ConfiguredActors { get; }
        public int RegisteredActors { get; }
        public int ActiveActors { get; }
        public int StandbyActors { get; }
        public int SuspendedActors { get; }
        public int FaultedActors { get; }
        public int UpdatesCompleted { get; }
        public int UpdateFailures { get; }
        public int RegistrationFailures { get; }
        public double MaximumUpdateMilliseconds { get; }
        public double MaximumActorElapsedMilliseconds { get; }
        public int SchedulerBacklog { get; }
        public string BehaviorCounts { get; }
    }

    public interface IAutonomousPopulationSampleStore
    {
        void Save(AutonomousPopulationSample sample, int retentionHours);
    }

    public sealed class DatabaseAutonomousPopulationSampleStore : IAutonomousPopulationSampleStore
    {
        public void Save(AutonomousPopulationSample sample, int retentionHours)
        {
            if (sample == null)
                throw new ArgumentNullException(nameof(sample));

            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              insert dbo.ai_population_sample
                                  (observed_at, configured_actors, registered_actors,
                                   active_actors, standby_actors, suspended_actors,
                                   faulted_actors, updates_completed, update_failures,
                                   registration_failures, maximum_update_ms,
                                   maximum_actor_elapsed_ms, scheduler_backlog,
                                   behavior_counts)
                              values
                                  (@observedAt, @configuredActors, @registeredActors,
                                   @activeActors, @standbyActors, @suspendedActors,
                                   @faultedActors, @updatesCompleted, @updateFailures,
                                   @registrationFailures, @maximumUpdateMs,
                                   @maximumActorElapsedMs, @schedulerBacklog,
                                   @behaviorCounts);
                              delete dbo.ai_population_sample
                              where observed_at < dateadd(hour, -@retentionHours, sysutcdatetime());
                              commit transaction;")
                .SetParameter("@observedAt", sample.ObservedAtUtc)
                .SetParameter("@configuredActors", sample.ConfiguredActors)
                .SetParameter("@registeredActors", sample.RegisteredActors)
                .SetParameter("@activeActors", sample.ActiveActors)
                .SetParameter("@standbyActors", sample.StandbyActors)
                .SetParameter("@suspendedActors", sample.SuspendedActors)
                .SetParameter("@faultedActors", sample.FaultedActors)
                .SetParameter("@updatesCompleted", sample.UpdatesCompleted)
                .SetParameter("@updateFailures", sample.UpdateFailures)
                .SetParameter("@registrationFailures", sample.RegistrationFailures)
                .SetParameter("@maximumUpdateMs", sample.MaximumUpdateMilliseconds)
                .SetParameter("@maximumActorElapsedMs", sample.MaximumActorElapsedMilliseconds)
                .SetParameter("@schedulerBacklog", sample.SchedulerBacklog)
                .SetParameter("@behaviorCounts", sample.BehaviorCounts)
                .SetParameter("@retentionHours", retentionHours)
                .ExecuteNonQuery();
        }
    }

    public interface IAutonomousPopulationTelemetry
    {
        void Start(int configuredActors, IReadOnlyCollection<AutonomousActorSnapshot> snapshots);
        void RecordRegistrationFailure();
        void RecordUpdate(TimeSpan actorElapsed, TimeSpan executionTime, bool succeeded);
        void Tick(
            TimeSpan elapsed,
            int scheduledActors,
            int schedulerBacklog,
            IAutonomousActorRegistry registry);
        void Stop(IReadOnlyCollection<AutonomousActorSnapshot> snapshots);
    }

    public sealed class AutonomousPopulationTelemetry : IAutonomousPopulationTelemetry
    {
        private readonly AutonomousConfiguration _configuration;
        private readonly IAutonomousPopulationSampleStore _store;
        private TimeSpan _windowElapsed;
        private int _configuredActors;
        private int _updatesCompleted;
        private int _updateFailures;
        private int _registrationFailures;
        private double _maximumUpdateMilliseconds;
        private double _maximumActorElapsedMilliseconds;
        private int _schedulerBacklog;

        public AutonomousPopulationTelemetry(
            AutonomousConfiguration configuration,
            IAutonomousPopulationSampleStore store)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public void Start(int configuredActors, IReadOnlyCollection<AutonomousActorSnapshot> snapshots)
        {
            if (!Enabled)
                return;

            _configuredActors = configuredActors;
            ResetWindow();
            Logger.Info($"[AUTONOMOUS_POPULATION] event=started configured={configuredActors} registered={snapshots.Count}");
        }

        public void RecordRegistrationFailure()
        {
            if (Enabled)
                _registrationFailures++;
        }

        public void RecordUpdate(TimeSpan actorElapsed, TimeSpan executionTime, bool succeeded)
        {
            if (!Enabled)
                return;

            _updatesCompleted++;
            if (!succeeded)
                _updateFailures++;
            _maximumUpdateMilliseconds = Math.Max(_maximumUpdateMilliseconds, executionTime.TotalMilliseconds);
            _maximumActorElapsedMilliseconds = Math.Max(
                _maximumActorElapsedMilliseconds,
                actorElapsed.TotalMilliseconds);
        }

        public void Tick(
            TimeSpan elapsed,
            int scheduledActors,
            int schedulerBacklog,
            IAutonomousActorRegistry registry)
        {
            if (!Enabled)
                return;
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            _windowElapsed += elapsed;
            _schedulerBacklog = schedulerBacklog;
            if (_windowElapsed < TimeSpan.FromSeconds(Options.SnapshotIntervalSeconds))
                return;

            Emit("snapshot", scheduledActors, registry.Snapshots);
        }

        public void Stop(IReadOnlyCollection<AutonomousActorSnapshot> snapshots)
        {
            if (!Enabled)
                return;

            if (_windowElapsed > TimeSpan.Zero || _updatesCompleted > 0 || _registrationFailures > 0)
                Emit("stopped", 0, snapshots);
            Logger.Info($"[AUTONOMOUS_POPULATION] event=host_stopped registered={snapshots.Count}");
        }

        private bool Enabled => _configuration.PopulationLab?.Enabled == true;
        private AutonomousPopulationLabOptions Options => _configuration.PopulationLab;

        private void Emit(
            string eventName,
            int scheduledActors,
            IReadOnlyCollection<AutonomousActorSnapshot> snapshots)
        {
            AutonomousPopulationSample sample = BuildSample(snapshots);
            Logger.Info(
                $"[AUTONOMOUS_POPULATION] event={eventName}" +
                $" configured={sample.ConfiguredActors}" +
                $" registered={sample.RegisteredActors}" +
                $" active={sample.ActiveActors}" +
                $" standby={sample.StandbyActors}" +
                $" suspended={sample.SuspendedActors}" +
                $" faulted={sample.FaultedActors}" +
                $" updates={sample.UpdatesCompleted}" +
                $" failures={sample.UpdateFailures}" +
                $" registration_failures={sample.RegistrationFailures}" +
                $" scheduled={scheduledActors}" +
                $" backlog={sample.SchedulerBacklog}" +
                $" max_update_ms={sample.MaximumUpdateMilliseconds:F3}" +
                $" max_actor_elapsed_ms={sample.MaximumActorElapsedMilliseconds:F3}" +
                $" behaviors={sample.BehaviorCounts}");

            if (Options.PersistSnapshots)
            {
                try
                {
                    _store.Save(sample, Options.SnapshotRetentionHours);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[AUTONOMOUS_POPULATION] event=persistence_failed reason={ex.GetType().Name}");
                }
            }
            ResetWindow();
        }

        private AutonomousPopulationSample BuildSample(
            IReadOnlyCollection<AutonomousActorSnapshot> snapshots)
        {
            string behaviorCounts = string.Join(",", snapshots
                .GroupBy(snapshot => string.IsNullOrWhiteSpace(snapshot.Behavior)
                    ? "unknown"
                    : snapshot.Behavior.Trim().ToLowerInvariant())
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}"));
            return new AutonomousPopulationSample(
                DateTime.UtcNow,
                _configuredActors,
                snapshots.Count,
                snapshots.Count(snapshot => snapshot.Status == AutonomousActorStatus.Active),
                snapshots.Count(snapshot => snapshot.Status == AutonomousActorStatus.Standby),
                snapshots.Count(snapshot => snapshot.Status == AutonomousActorStatus.Suspended),
                snapshots.Count(snapshot => snapshot.Status == AutonomousActorStatus.Faulted),
                _updatesCompleted,
                _updateFailures,
                _registrationFailures,
                _maximumUpdateMilliseconds,
                _maximumActorElapsedMilliseconds,
                _schedulerBacklog,
                behaviorCounts);
        }

        private void ResetWindow()
        {
            _windowElapsed = TimeSpan.Zero;
            _updatesCompleted = 0;
            _updateFailures = 0;
            _registrationFailures = 0;
            _maximumUpdateMilliseconds = 0;
            _maximumActorElapsedMilliseconds = 0;
            _schedulerBacklog = 0;
        }
    }
}
