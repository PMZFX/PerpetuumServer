using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousActorHostTests
    {
        [Fact]
        public void DisabledHostDoesNotCreateActors()
        {
            var registry = new AutonomousActorRegistry();
            int factoryCalls = 0;
            var host = new AutonomousActorHost(
                new AutonomousConfiguration(),
                definition =>
                {
                    factoryCalls++;
                    return new FakeActor(definition.CharacterId);
                },
                registry,
                new RecordingAudit(),
                new RecordingTelemetry());

            host.Start();
            host.Update(TimeSpan.FromSeconds(1));

            Assert.Equal(0, factoryCalls);
            Assert.Empty(registry.Actors);
        }

        [Fact]
        public void FailingActorIsFaultedWithoutStoppingHealthyActor()
        {
            var configuration = new AutonomousConfiguration
            {
                Enabled = true,
                MaxConsecutiveFailures = 2,
                Actors =
                {
                    new AutonomousActorDefinition { CharacterId = 1 },
                    new AutonomousActorDefinition { CharacterId = 2 }
                }
            };
            var actors = new Dictionary<int, FakeActor>
            {
                { 1, new FakeActor(1) { AlwaysFail = true } },
                { 2, new FakeActor(2) }
            };
            var registry = new AutonomousActorRegistry();
            var audit = new RecordingAudit();
            var host = new AutonomousActorHost(
                configuration,
                definition => actors[definition.CharacterId],
                registry,
                audit,
                new RecordingTelemetry());

            host.Start();
            host.Update(TimeSpan.FromMilliseconds(500));
            host.Update(TimeSpan.FromMilliseconds(500));
            host.Update(TimeSpan.FromMilliseconds(500));

            Assert.Equal(AutonomousActorStatus.Faulted, actors[1].Status);
            Assert.Equal(2, actors[1].UpdateCount);
            Assert.Equal(3, actors[2].UpdateCount);
            Assert.Equal(2, audit.Events.FindAll(item => item == "1:update_failed:Active").Count);
        }

        [Fact]
        public void StopStopsActorsAndClearsRegistry()
        {
            var configuration = new AutonomousConfiguration
            {
                Enabled = true,
                Actors = { new AutonomousActorDefinition { CharacterId = 11 } }
            };
            var actor = new FakeActor(11);
            var registry = new AutonomousActorRegistry();
            var host = new AutonomousActorHost(
                configuration,
                definition => actor,
                registry,
                new RecordingAudit(),
                new RecordingTelemetry());

            host.Start();
            host.Stop();

            Assert.Equal(1, actor.StopCount);
            Assert.Empty(registry.Actors);
        }

        [Fact]
        public void RegistrationFailureDoesNotPreventOtherActorsFromStarting()
        {
            var configuration = new AutonomousConfiguration
            {
                Enabled = true,
                Actors =
                {
                    new AutonomousActorDefinition { CharacterId = 20 },
                    new AutonomousActorDefinition { CharacterId = 21 }
                }
            };
            var healthy = new FakeActor(21);
            var registry = new AutonomousActorRegistry();
            var audit = new RecordingAudit();
            var host = new AutonomousActorHost(
                configuration,
                definition => definition.CharacterId == 20
                    ? throw new InvalidOperationException("missing character")
                    : healthy,
                registry,
                audit,
                new RecordingTelemetry());

            host.Start();
            host.Update(TimeSpan.FromMilliseconds(500));

            Assert.Equal(1, healthy.UpdateCount);
            Assert.Single(registry.Actors);
            Assert.Contains("20:registration_failed:Faulted", audit.Events);
        }

        [Fact]
        public void PopulationLabStaggersActorsAndPreservesElapsedTime()
        {
            var configuration = new AutonomousConfiguration
            {
                Enabled = true,
                PopulationLab = new AutonomousPopulationLabOptions
                {
                    Enabled = true,
                    MaximumActors = 10,
                    MaximumActorUpdatesPerTick = 1
                },
                Actors =
                {
                    new AutonomousActorDefinition { CharacterId = 30 },
                    new AutonomousActorDefinition { CharacterId = 31 },
                    new AutonomousActorDefinition { CharacterId = 32 }
                }
            };
            var actors = configuration.Actors.ToDictionary(
                definition => definition.CharacterId,
                definition => new FakeActor(definition.CharacterId));
            var telemetry = new RecordingTelemetry();
            var host = new AutonomousActorHost(
                configuration,
                definition => actors[definition.CharacterId],
                new AutonomousActorRegistry(),
                new RecordingAudit(),
                telemetry);

            host.Start();
            for (int i = 0; i < 6; i++)
                host.Update(TimeSpan.FromMilliseconds(500));

            Assert.All(actors.Values, actor => Assert.Equal(2, actor.UpdateCount));
            Assert.All(actors.Values, actor => Assert.Equal(TimeSpan.FromSeconds(1.5), actor.LastElapsed));
            Assert.Equal(6, telemetry.RecordedUpdates);
            Assert.Equal(2, telemetry.LastBacklog);
        }

        [Fact]
        public void ThousandActorScheduleIsBoundedAndFairAcrossRounds()
        {
            List<AutonomousActorDefinition> definitions = Enumerable.Range(1, 1000)
                .Select(characterId => new AutonomousActorDefinition { CharacterId = characterId })
                .ToList();
            var configuration = new AutonomousConfiguration
            {
                Enabled = true,
                Actors = definitions,
                PopulationLab = new AutonomousPopulationLabOptions
                {
                    Enabled = true,
                    MaximumActors = 1000,
                    MaximumActorUpdatesPerTick = 25
                }
            };
            Dictionary<int, FakeActor> actors = definitions.ToDictionary(
                definition => definition.CharacterId,
                definition => new FakeActor(definition.CharacterId));
            var telemetry = new RecordingTelemetry();
            var host = new AutonomousActorHost(
                configuration,
                definition => actors[definition.CharacterId],
                new AutonomousActorRegistry(),
                new RecordingAudit(),
                telemetry);

            host.Start();
            for (int tick = 0; tick < 80; tick++)
                host.Update(TimeSpan.FromMilliseconds(500));

            Assert.All(actors.Values, actor => Assert.Equal(2, actor.UpdateCount));
            Assert.All(actors.Values, actor => Assert.Equal(TimeSpan.FromSeconds(20), actor.LastElapsed));
            Assert.Equal(2000, telemetry.RecordedUpdates);
            Assert.Equal(975, telemetry.LastBacklog);
            Assert.Equal(25, telemetry.MaximumScheduledActors);
        }

        [Fact]
        public void OwnershipChecksAreNeverDeferredByPopulationBudget()
        {
            var configuration = new AutonomousConfiguration
            {
                Enabled = true,
                Actors =
                {
                    new AutonomousActorDefinition { CharacterId = 41 },
                    new AutonomousActorDefinition { CharacterId = 42 },
                    new AutonomousActorDefinition { CharacterId = 43 }
                },
                PopulationLab = new AutonomousPopulationLabOptions
                {
                    Enabled = true,
                    MaximumActors = 3,
                    MaximumActorUpdatesPerTick = 1
                }
            };
            Dictionary<int, FakeActor> actors = configuration.Actors.ToDictionary(
                definition => definition.CharacterId,
                definition => new FakeActor(definition.CharacterId));
            var host = new AutonomousActorHost(
                configuration,
                definition => actors[definition.CharacterId],
                new AutonomousActorRegistry(),
                new RecordingAudit(),
                new RecordingTelemetry());

            host.Start();
            host.Update(TimeSpan.FromMilliseconds(500));
            actors[43].ControlAvailable = false;
            host.Update(TimeSpan.FromMilliseconds(500));

            Assert.All(actors.Values, actor => Assert.Equal(2, actor.ControlChecks));
            Assert.Equal(0, actors[43].UpdateCount);
        }

        private sealed class FakeActor : IAutonomousActor
        {
            public FakeActor(int characterId)
            {
                CharacterId = characterId;
            }

            public int CharacterId { get; }
            public AutonomousActorStatus Status { get; private set; } = AutonomousActorStatus.Stopped;
            public AutonomousActorSnapshot Snapshot =>
                new AutonomousActorSnapshot(CharacterId, "test", Status, null);
            public bool AlwaysFail { get; set; }
            public bool ControlAvailable { get; set; } = true;
            public int UpdateCount { get; private set; }
            public int StopCount { get; private set; }
            public TimeSpan LastElapsed { get; private set; }
            public int ControlChecks { get; private set; }

            public bool CheckControlOwnership()
            {
                ControlChecks++;
                return ControlAvailable && Status != AutonomousActorStatus.Faulted;
            }

            public void Start()
            {
                Status = AutonomousActorStatus.Active;
            }

            public void Stop()
            {
                StopCount++;
                Status = AutonomousActorStatus.Stopped;
            }

            public void Update(TimeSpan elapsed)
            {
                UpdateCount++;
                LastElapsed = elapsed;
                if (AlwaysFail)
                    throw new InvalidOperationException("expected test failure");
            }

            public void Fault(string reason)
            {
                Status = AutonomousActorStatus.Faulted;
            }
        }

        private sealed class RecordingAudit : IAutonomousActorAudit
        {
            public List<string> Events { get; } = new List<string>();

            public void Write(int characterId, string eventName, AutonomousActorStatus status, string reason = null)
            {
                Events.Add($"{characterId}:{eventName}:{status}");
            }
        }

        private sealed class RecordingTelemetry : IAutonomousPopulationTelemetry
        {
            public int RecordedUpdates { get; private set; }
            public int LastBacklog { get; private set; }
            public int MaximumScheduledActors { get; private set; }

            public void Start(int configuredActors, IReadOnlyCollection<AutonomousActorSnapshot> snapshots) { }
            public void RecordRegistrationFailure() { }

            public void RecordUpdate(TimeSpan actorElapsed, TimeSpan executionTime, bool succeeded)
            {
                RecordedUpdates++;
            }

            public void Tick(
                TimeSpan elapsed,
                int scheduledActors,
                int schedulerBacklog,
                IAutonomousActorRegistry registry)
            {
                LastBacklog = schedulerBacklog;
                MaximumScheduledActors = Math.Max(MaximumScheduledActors, scheduledActors);
            }

            public void Stop(IReadOnlyCollection<AutonomousActorSnapshot> snapshots) { }
        }
    }
}
