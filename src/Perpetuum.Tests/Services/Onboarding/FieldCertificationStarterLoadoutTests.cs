using Perpetuum.Services.Onboarding;
using Xunit;

namespace Perpetuum.Tests.Services.Onboarding
{
    public class FieldCertificationStarterLoadoutTests
    {
        [Theory]
        [InlineData(1.4475, 50, 3, 52)]
        [InlineData(0, 50, 100, 100)]
        [InlineData(2, 0, 3, 3)]
        public void CalculatesCapacityForCurrentCargoAndMissionItem(
            double currentLoad,
            double missionVolume,
            double configuredCapacity,
            double expected)
        {
            double actual = FieldCertificationStarterLoadout.CalculateRequiredCapacity(
                currentLoad,
                missionVolume,
                configuredCapacity);

            Assert.Equal(expected, actual);
        }
    }
}
