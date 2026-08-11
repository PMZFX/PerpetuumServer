using System;
using System.Collections.Generic;
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
                new RecordingAudit());

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
            var host = new AutonomousActorHost(configuration, definition => actors[definition.CharacterId], registry, audit);

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
            var host = new AutonomousActorHost(configuration, definition => actor, registry, new RecordingAudit());

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
                audit);

            host.Start();
            host.Update(TimeSpan.FromMilliseconds(500));

            Assert.Equal(1, healthy.UpdateCount);
            Assert.Single(registry.Actors);
            Assert.Contains("20:registration_failed:Faulted", audit.Events);
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
            public int UpdateCount { get; private set; }
            public int StopCount { get; private set; }

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
    }
}
