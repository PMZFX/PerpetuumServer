using Perpetuum.Services.MissionEngine.MissionTargets;
using Xunit;

namespace Perpetuum.Tests.Services.Onboarding
{
    public class ConfiguredLockTargetTests
    {
        [Fact]
        public void MatchesPersistentNpcByDefinitionAndPosition()
        {
            Assert.True(LockUnitZoneTarget.IsConfiguredNpcMatch(
                true,
                5334,
                5334,
                true));
        }

        [Theory]
        [InlineData(false, 5334, 5334, true)]
        [InlineData(true, 5334, 1473, true)]
        [InlineData(true, 5334, 5334, false)]
        public void RejectsUnconfiguredWrongOrDistantNpc(
            bool hasDefinition,
            int targetDefinition,
            int npcDefinition,
            bool positionMatches)
        {
            Assert.False(LockUnitZoneTarget.IsConfiguredNpcMatch(
                hasDefinition,
                targetDefinition,
                npcDefinition,
                positionMatches));
        }
    }
}
