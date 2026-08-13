using System;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousActorRegistryTests
    {
        [Fact]
        public void DuplicateActorsAreRejected()
        {
            var registry = new AutonomousActorRegistry();
            registry.Add(new FakeActor(7));

            Assert.Throws<InvalidOperationException>(() => registry.Add(new FakeActor(7)));
        }

        [Fact]
        public void SnapshotsAreOrderedByCharacterId()
        {
            var registry = new AutonomousActorRegistry();
            registry.Add(new FakeActor(9));
            registry.Add(new FakeActor(3));

            Assert.Collection(
                registry.Snapshots,
                actor => Assert.Equal(3, actor.CharacterId),
                actor => Assert.Equal(9, actor.CharacterId));
        }

        private sealed class FakeActor : IAutonomousActor
        {
            public FakeActor(int characterId)
            {
                CharacterId = characterId;
            }

            public int CharacterId { get; }
            public AutonomousActorStatus Status => AutonomousActorStatus.Stopped;
            public AutonomousActorSnapshot Snapshot =>
                new AutonomousActorSnapshot(CharacterId, "test", Status, null);

            public bool CheckControlOwnership() => true;
            public void Start() { }
            public void Stop() { }
            public void Update(TimeSpan elapsed) { }
            public void Fault(string reason) { }
        }
    }
}
