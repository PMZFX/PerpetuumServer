using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.Onboarding;
using Xunit;

namespace Perpetuum.Tests.Services.Onboarding
{
    public class CombatCertificationMissionContractTests
    {
        private readonly CombatCertificationMissionContract _contract =
            new CombatCertificationMissionContract();

        [Fact]
        public void AcceptsTargetAcquisitionMissionShape()
        {
            bool valid = _contract.TryValidate(
                CombatCertificationMissionContract.MissionName,
                FieldCertificationMissionContract.TrainingZoneId,
                15000,
                ExpectedTargets(),
                out string reason);

            Assert.True(valid);
            Assert.Null(reason);
        }

        [Theory]
        [InlineData("different_mission", 45, 15000, "mission_name_mismatch")]
        [InlineData("mission_syndicate_field_certification_combat", 1, 15000, "training_zone_mismatch")]
        [InlineData("mission_syndicate_field_certification_combat", 45, 0, "missing_verified_reward")]
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
        public void RejectsMissingLockObjective()
        {
            bool valid = _contract.TryValidate(
                CombatCertificationMissionContract.MissionName,
                FieldCertificationMissionContract.TrainingZoneId,
                15000,
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
                MissionTargetType.lock_unit,
                MissionTargetType.kill_definition
            };
        }
    }
}
