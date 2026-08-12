using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousMiningResupplyTests
    {
        [Fact]
        public void MissingOrDifferentAmmoRequestsReload()
        {
            Assert.True(AutonomousMiningResupplyService.NeedsReload(100, 0, 0, 20, 0.5));
            Assert.True(AutonomousMiningResupplyService.NeedsReload(100, 200, 20, 20, 0.5));
        }

        [Theory]
        [InlineData(9, true)]
        [InlineData(10, false)]
        [InlineData(20, false)]
        public void MatchingAmmoUsesConfiguredCapacityThreshold(int quantity, bool expected)
        {
            Assert.Equal(
                expected,
                AutonomousMiningResupplyService.NeedsReload(100, 100, quantity, 20, 0.5));
        }

        [Theory]
        [InlineData(0, 20)]
        [InlineData(100, 0)]
        public void InvalidDesiredAmmoOrCapacityCannotReload(int desiredDefinition, int capacity)
        {
            Assert.False(
                AutonomousMiningResupplyService.NeedsReload(
                    desiredDefinition,
                    0,
                    0,
                    capacity,
                    0.5));
        }
    }
}
