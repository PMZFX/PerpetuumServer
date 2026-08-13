using System;
using System.Collections.Generic;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousPopulationTelemetryTests
    {
        [Fact]
        public void SnapshotAggregatesPopulationWithoutGameplayState()
        {
            var configuration = new AutonomousConfiguration
            {
                PopulationLab = new AutonomousPopulationLabOptions
                {
                    Enabled = true,
                    PersistSnapshots = true,
                    SnapshotIntervalSeconds = 5,
                    SnapshotRetentionHours = 24
                }
            };
            var store = new RecordingStore();
            var telemetry = new AutonomousPopulationTelemetry(configuration, store);
            var registry = new SnapshotRegistry(
                new AutonomousActorSnapshot(1, "mining", AutonomousActorStatus.Active, null),
                new AutonomousActorSnapshot(2, "mining", AutonomousActorStatus.Suspended, "human_session"),
                new AutonomousActorSnapshot(3, "mission", AutonomousActorStatus.Faulted, "test"));

            telemetry.Start(4, registry.Snapshots);
            telemetry.RecordRegistrationFailure();
            telemetry.RecordUpdate(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(2), true);
            telemetry.RecordUpdate(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(7), false);
            telemetry.Tick(TimeSpan.FromSeconds(4), 2, 1, registry);
            Assert.Null(store.Sample);

            telemetry.Tick(TimeSpan.FromSeconds(1), 2, 1, registry);

            AutonomousPopulationSample sample = Assert.IsType<AutonomousPopulationSample>(store.Sample);
            Assert.Equal(4, sample.ConfiguredActors);
            Assert.Equal(3, sample.RegisteredActors);
            Assert.Equal(1, sample.ActiveActors);
            Assert.Equal(1, sample.SuspendedActors);
            Assert.Equal(1, sample.FaultedActors);
            Assert.Equal(2, sample.UpdatesCompleted);
            Assert.Equal(1, sample.UpdateFailures);
            Assert.Equal(1, sample.RegistrationFailures);
            Assert.Equal(7, sample.MaximumUpdateMilliseconds);
            Assert.Equal(3000, sample.MaximumActorElapsedMilliseconds);
            Assert.Equal(1, sample.SchedulerBacklog);
            Assert.Equal("mining:2,mission:1", sample.BehaviorCounts);
            Assert.Equal(24, store.RetentionHours);
        }

        [Fact]
        public void DisabledPopulationTelemetryDoesNotPersist()
        {
            var store = new RecordingStore();
            var telemetry = new AutonomousPopulationTelemetry(new AutonomousConfiguration(), store);
            var registry = new SnapshotRegistry();

            telemetry.Start(0, registry.Snapshots);
            telemetry.RecordUpdate(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), false);
            telemetry.Tick(TimeSpan.FromHours(1), 0, 0, registry);
            telemetry.Stop(registry.Snapshots);

            Assert.Null(store.Sample);
        }

        [Fact]
        public void TelemetryPersistenceFailureCannotInterruptActorScheduling()
        {
            var configuration = new AutonomousConfiguration
            {
                PopulationLab = new AutonomousPopulationLabOptions
                {
                    Enabled = true,
                    PersistSnapshots = true,
                    SnapshotIntervalSeconds = 5
                }
            };
            var telemetry = new AutonomousPopulationTelemetry(configuration, new ThrowingStore());
            var registry = new SnapshotRegistry(
                new AutonomousActorSnapshot(1, "mining", AutonomousActorStatus.Active, null));
            telemetry.Start(1, registry.Snapshots);
            telemetry.RecordUpdate(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1), true);

            Exception error = Record.Exception(() =>
                telemetry.Tick(TimeSpan.FromSeconds(5), 1, 0, registry));

            Assert.Null(error);
        }

        private sealed class RecordingStore : IAutonomousPopulationSampleStore
        {
            public AutonomousPopulationSample Sample { get; private set; }
            public int RetentionHours { get; private set; }

            public void Save(AutonomousPopulationSample sample, int retentionHours)
            {
                Sample = sample;
                RetentionHours = retentionHours;
            }
        }

        private sealed class ThrowingStore : IAutonomousPopulationSampleStore
        {
            public void Save(AutonomousPopulationSample sample, int retentionHours)
            {
                throw new InvalidOperationException("expected telemetry failure");
            }
        }

        private sealed class SnapshotRegistry : IAutonomousActorRegistry
        {
            public SnapshotRegistry(params AutonomousActorSnapshot[] snapshots)
            {
                Snapshots = snapshots;
            }

            public IReadOnlyCollection<IAutonomousActor> Actors { get; } =
                Array.Empty<IAutonomousActor>();
            public IReadOnlyCollection<AutonomousActorSnapshot> Snapshots { get; }

            public void Add(IAutonomousActor actor) => throw new NotSupportedException();

            public bool TryGet(int characterId, out IAutonomousActor actor)
            {
                actor = null;
                return false;
            }

            public void Clear() { }
        }
    }
}
