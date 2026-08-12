using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousRobotRecoveryTrackerTests
    {
        [Fact]
        public void FirstStartPersistsTheExpectedRobot()
        {
            var store = new RecordingStateStore();
            var tracker = NewTracker(store);

            AutonomousRecoveryStartDisposition disposition = tracker.Start(10, 0);

            Assert.Equal(AutonomousRecoveryStartDisposition.Ready, disposition);
            Assert.Equal(10, store.State.ExpectedRobotEid);
            Assert.False(store.State.RecoveryRequired);
        }

        [Fact]
        public void ReplacementRemainsBlockedAcrossTrackerRestart()
        {
            var store = new RecordingStateStore();
            var first = NewTracker(store);
            first.Start(10, 0);
            Assert.True(first.RequireRecovery(AutonomousRobotRecoveryReason.RobotDestroyed, 10));

            var restarted = NewTracker(store);
            AutonomousRecoveryStartDisposition disposition = restarted.Start(20, 0);

            Assert.Equal(AutonomousRecoveryStartDisposition.RecoveryRequired, disposition);
            Assert.True(restarted.RecoveryRequired);
            Assert.Equal(10, restarted.ExpectedRobotEid);
            Assert.Equal("RobotDestroyed", restarted.RecoveryReason);
        }

        [Fact]
        public void SameRevisionCannotAcknowledgeRecovery()
        {
            var store = new RecordingStateStore
            {
                State = State(expectedRobotEid: 10, observedRobotEid: 20, recoveryRequired: true, revision: 3)
            };
            var tracker = NewTracker(store);

            AutonomousRecoveryStartDisposition disposition = tracker.Start(20, 3);

            Assert.Equal(AutonomousRecoveryStartDisposition.RecoveryRequired, disposition);
            Assert.True(store.State.RecoveryRequired);
        }

        [Fact]
        public void HigherRevisionAcknowledgesTheSelectedReplacement()
        {
            var store = new RecordingStateStore
            {
                State = State(expectedRobotEid: 10, observedRobotEid: 20, recoveryRequired: true, revision: 3)
            };
            var tracker = NewTracker(store);

            AutonomousRecoveryStartDisposition disposition = tracker.Start(20, 4);

            Assert.Equal(AutonomousRecoveryStartDisposition.RecoveryAcknowledged, disposition);
            Assert.False(store.State.RecoveryRequired);
            Assert.Equal(20, store.State.ExpectedRobotEid);
            Assert.Equal(4, store.State.RecoveryRevision);
        }

        [Fact]
        public void HigherRevisionCannotAcknowledgeWithoutAnActiveRobot()
        {
            var store = new RecordingStateStore
            {
                State = State(expectedRobotEid: 10, observedRobotEid: 0, recoveryRequired: true, revision: 1)
            };
            var tracker = NewTracker(store);

            AutonomousRecoveryStartDisposition disposition = tracker.Start(0, 2);

            Assert.Equal(AutonomousRecoveryStartDisposition.RecoveryRequired, disposition);
            Assert.True(store.State.RecoveryRequired);
            Assert.Equal(1, store.State.RecoveryRevision);
        }

        [Fact]
        public void ChangedRobotOnCleanRestartCreatesDurableRecovery()
        {
            var store = new RecordingStateStore
            {
                State = State(expectedRobotEid: 10, observedRobotEid: 10, recoveryRequired: false, revision: 0)
            };
            var tracker = NewTracker(store);

            AutonomousRecoveryStartDisposition disposition = tracker.Start(20, 0);

            Assert.Equal(AutonomousRecoveryStartDisposition.RecoveryRequired, disposition);
            Assert.True(store.State.RecoveryRequired);
            Assert.Equal("RobotReplaced", store.State.RecoveryReason);
            Assert.Equal(20, store.State.ObservedRobotEid);
        }

        [Fact]
        public void HigherRevisionWhileHealthyIsConsumedAsAWatermark()
        {
            var store = new RecordingStateStore
            {
                State = State(expectedRobotEid: 10, observedRobotEid: 10, recoveryRequired: false, revision: 0)
            };
            var tracker = NewTracker(store);

            tracker.Start(10, 2);
            tracker.RequireRecovery(AutonomousRobotRecoveryReason.RobotDestroyed, 10);
            var restarted = NewTracker(store);

            Assert.Equal(
                AutonomousRecoveryStartDisposition.RecoveryRequired,
                restarted.Start(20, 2));
            Assert.Equal(2, store.State.RecoveryRevision);
        }

        private static AutonomousRobotRecoveryTracker NewTracker(IAutonomousActorStateStore store)
        {
            return new AutonomousRobotRecoveryTracker(7, "patrol", store);
        }

        private static AutonomousActorState State(
            long expectedRobotEid,
            long observedRobotEid,
            bool recoveryRequired,
            int revision)
        {
            return new AutonomousActorState(
                7,
                "patrol",
                expectedRobotEid,
                observedRobotEid,
                recoveryRequired,
                recoveryRequired ? "RobotDestroyed" : null,
                revision);
        }

        private sealed class RecordingStateStore : IAutonomousActorStateStore
        {
            public AutonomousActorState State { get; set; }

            public AutonomousActorState Load(int characterId)
            {
                return State;
            }

            public void Save(AutonomousActorState state)
            {
                State = state;
            }
        }
    }
}
