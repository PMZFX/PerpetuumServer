using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousRobotRecoveryPolicyTests
    {
        [Fact]
        public void SameLiveRobotNeedsNoRecovery()
        {
            Assert.Equal(
                AutonomousRobotRecoveryReason.None,
                AutonomousRobotRecoveryPolicy.Assess(10, 10, false));
        }

        [Fact]
        public void MissingActiveRobotRequiresRecovery()
        {
            Assert.Equal(
                AutonomousRobotRecoveryReason.NoActiveRobot,
                AutonomousRobotRecoveryPolicy.Assess(10, 0, false));
        }

        [Fact]
        public void DeadLiveRobotRequiresRecoveryBeforeDeathTransactionCompletes()
        {
            Assert.Equal(
                AutonomousRobotRecoveryReason.RobotDestroyed,
                AutonomousRobotRecoveryPolicy.Assess(10, 10, true));
        }

        [Fact]
        public void StarterOrReplacementRobotIsNotSilentlyAccepted()
        {
            Assert.Equal(
                AutonomousRobotRecoveryReason.RobotReplaced,
                AutonomousRobotRecoveryPolicy.Assess(10, 20, false));
        }

        [Fact]
        public void RobotAppearingAfterBehaviorStartedWithoutOneRequiresRestart()
        {
            Assert.Equal(
                AutonomousRobotRecoveryReason.RobotReplaced,
                AutonomousRobotRecoveryPolicy.Assess(0, 20, false));
        }
    }
}
