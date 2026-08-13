using Perpetuum.Accounting.Characters;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousEquipmentRecoveryCoordinatorTests
    {
        [Fact]
        public void ReadyEquipmentAcknowledgesAuthoritativeReplacement()
        {
            var store = new RecordingStateStore
            {
                State = State(recoveryRequired: true)
            };
            var tracker = new AutonomousRobotRecoveryTracker(7, "mining", store);
            tracker.Start(20, 0);
            var equipment = new RecordingEquipmentController(AutonomousEquipmentUpdateResult.Ready);
            var coordinator = new AutonomousEquipmentRecoveryCoordinator(equipment, new RecordingAudit());

            Assert.True(coordinator.PrepareDocked(
                Context(),
                EnabledOptions(),
                tracker,
                "mining",
                20));
            Assert.False(tracker.RecoveryRequired);
            Assert.Equal(20, tracker.ExpectedRobotEid);
            Assert.Equal(1, equipment.Calls);
        }

        [Fact]
        public void IncompleteEquipmentCannotAcknowledgeReplacement()
        {
            var store = new RecordingStateStore
            {
                State = State(recoveryRequired: true)
            };
            var tracker = new AutonomousRobotRecoveryTracker(7, "trader", store);
            tracker.Start(20, 0);
            var equipment = new RecordingEquipmentController(AutonomousEquipmentUpdateResult.Acted);
            var coordinator = new AutonomousEquipmentRecoveryCoordinator(equipment, new RecordingAudit());

            Assert.False(coordinator.PrepareDocked(
                Context(),
                EnabledOptions(),
                tracker,
                "trader",
                20));
            Assert.True(tracker.RecoveryRequired);
            Assert.Equal(10, tracker.ExpectedRobotEid);
        }

        [Fact]
        public void DisabledEquipmentPreservesManualRecoveryPolicy()
        {
            var store = new RecordingStateStore
            {
                State = State(recoveryRequired: true)
            };
            var tracker = new AutonomousRobotRecoveryTracker(7, "patrol", store);
            tracker.Start(20, 0);
            var equipment = new RecordingEquipmentController(AutonomousEquipmentUpdateResult.Ready);
            var coordinator = new AutonomousEquipmentRecoveryCoordinator(equipment, new RecordingAudit());

            Assert.False(coordinator.PrepareDocked(
                Context(),
                new AutonomousEquipmentOptions(),
                tracker,
                "patrol",
                20));
            Assert.True(tracker.RecoveryRequired);
            Assert.Equal(0, equipment.Calls);
        }

        private static AutonomousEquipmentOptions EnabledOptions()
        {
            return new AutonomousEquipmentOptions
            {
                Enabled = true,
                Robot = "robot",
                RepairFacilityEid = 400
            };
        }

        private static GameActionContext Context()
        {
            var actor = new Character(
                7,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
            return new GameActionContext(actor, GameActionSource.Autonomous);
        }

        private static AutonomousActorState State(bool recoveryRequired)
        {
            return new AutonomousActorState(
                7,
                "mining",
                10,
                20,
                recoveryRequired,
                recoveryRequired ? "RobotDestroyed" : null,
                0);
        }

        private sealed class RecordingEquipmentController : IAutonomousEquipmentController
        {
            private readonly AutonomousEquipmentUpdateResult _result;

            public RecordingEquipmentController(AutonomousEquipmentUpdateResult result)
            {
                _result = result;
            }

            public int Calls { get; private set; }

            public AutonomousEquipmentUpdateResult Update(
                GameActionContext context,
                AutonomousEquipmentOptions options)
            {
                Calls++;
                return _result;
            }
        }

        private sealed class RecordingStateStore : IAutonomousActorStateStore
        {
            public AutonomousActorState State { get; set; }
            public AutonomousActorState Load(int characterId) => State;
            public void Save(AutonomousActorState state) => State = state;
        }

        private sealed class RecordingAudit : IAutonomousActorAudit
        {
            public void Write(int characterId, string eventName, AutonomousActorStatus status, string reason = null)
            {
            }
        }
    }
}
