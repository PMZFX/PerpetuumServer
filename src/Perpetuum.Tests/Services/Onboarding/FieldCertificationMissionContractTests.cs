using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.Onboarding;
using Xunit;

namespace Perpetuum.Tests.Services.Onboarding
{
    public class FieldCertificationMissionContractTests
    {
        private readonly FieldCertificationMissionContract _contract =
            new FieldCertificationMissionContract();

        [Fact]
        public void AcceptsCurrentTrainingHubMissionShape()
        {
            bool valid = _contract.TryValidate(
                FieldCertificationMissionContract.MissionName,
                FieldCertificationMissionContract.TrainingZoneId,
                10000,
                ExpectedTargets(),
                out string reason);

            Assert.True(valid);
            Assert.Null(reason);
        }

        [Theory]
        [InlineData("another_mission", 45, 10000, "mission_name_mismatch")]
        [InlineData("mission_tutorialchecklist_transport", 1, 10000, "training_zone_mismatch")]
        [InlineData("mission_tutorialchecklist_transport", 45, 0, "missing_verified_reward")]
        public void RejectsUnsafeContentChanges(
            string name,
            int zoneId,
            double reward,
            string expectedReason)
        {
            bool valid = _contract.TryValidate(
                name,
                zoneId,
                reward,
                ExpectedTargets(),
                out string reason);

            Assert.False(valid);
            Assert.Equal(expectedReason, reason);
        }

        [Fact]
        public void RejectsChangedObjectiveSequence()
        {
            bool valid = _contract.TryValidate(
                FieldCertificationMissionContract.MissionName,
                FieldCertificationMissionContract.TrainingZoneId,
                10000,
                new[]
                {
                    MissionTargetType.reach_position,
                    MissionTargetType.kill_definition
                },
                out string reason);

            Assert.False(valid);
            Assert.Equal("objective_sequence_mismatch", reason);
        }

        private static MissionTargetType[] ExpectedTargets()
        {
            return new[]
            {
                MissionTargetType.reach_position,
                MissionTargetType.use_itemsupply,
                MissionTargetType.submit_item
            };
        }
    }
}
