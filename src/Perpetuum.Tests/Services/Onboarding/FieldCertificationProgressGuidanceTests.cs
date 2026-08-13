using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.Onboarding;
using Xunit;

namespace Perpetuum.Tests.Services.Onboarding
{
    public class FieldCertificationProgressGuidanceTests
    {
        [Fact]
        public void ReachingPickupExplainsTheInteractionSequence()
        {
            string message = FieldCertificationProgressGuidance.SelectMessage(
                FieldCertificationMissionContract.MissionName,
                MissionTargetType.reach_position,
                true,
                false);

            Assert.Contains("Double-click Item Supply", message);
            Assert.Contains("Stop moving", message);
        }

        [Fact]
        public void LoadingCargoExplainsHowToSubmitIt()
        {
            string message = FieldCertificationProgressGuidance.SelectMessage(
                FieldCertificationMissionContract.MissionName,
                MissionTargetType.use_itemsupply,
                true,
                false);

            Assert.Contains("objective C", message);
            Assert.Contains("Submit items", message);
        }

        [Fact]
        public void MissionCompletionExplainsRewardAndStarterRobotOwnership()
        {
            string message = FieldCertificationProgressGuidance.SelectMessage(
                FieldCertificationMissionContract.MissionName,
                MissionTargetType.submit_item,
                true,
                true);

            Assert.Contains("10,000 NIC", message);
            Assert.Contains("starter robot", message);
            Assert.Contains("Target Acquisition", message);
        }

        [Fact]
        public void ReachingTheRangeUsesTheVerifiedPrimaryLockBinding()
        {
            string message = FieldCertificationProgressGuidance.SelectMessage(
                CombatCertificationMissionContract.MissionName,
                MissionTargetType.reach_position,
                true,
                false);

            Assert.Contains("press R", message);
            Assert.Contains("primary locked target", message);
        }

        [Fact]
        public void LockingTheTargetExplainsTheCombatAction()
        {
            string message = FieldCertificationProgressGuidance.SelectMessage(
                CombatCertificationMissionContract.MissionName,
                MissionTargetType.lock_unit,
                true,
                false);

            Assert.Contains("Primary lock confirmed", message);
            Assert.Contains("autocannon", message);
        }

        [Fact]
        public void CombatCompletionExplainsTheReusableCombatLoop()
        {
            string message = FieldCertificationProgressGuidance.SelectMessage(
                CombatCertificationMissionContract.MissionName,
                MissionTargetType.kill_definition,
                true,
                true);

            Assert.Contains("15,000 NIC", message);
            Assert.Contains("select, lock", message);
        }

        [Theory]
        [InlineData("different_mission", MissionTargetType.reach_position, true)]
        [InlineData(FieldCertificationMissionContract.MissionName, MissionTargetType.reach_position, false)]
        [InlineData(FieldCertificationMissionContract.MissionName, MissionTargetType.lock_unit, true)]
        public void IgnoresUnrelatedOrIncompleteProgress(
            string missionName,
            MissionTargetType targetType,
            bool targetCompleted)
        {
            string message = FieldCertificationProgressGuidance.SelectMessage(
                missionName,
                targetType,
                targetCompleted,
                false);

            Assert.Null(message);
        }
    }
}
