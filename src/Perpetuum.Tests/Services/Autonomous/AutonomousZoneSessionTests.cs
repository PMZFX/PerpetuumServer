using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousZoneSessionTests
    {
        [Fact]
        public void SessionsHaveUniqueNonClientIdsAndNormalAccess()
        {
            var first = new AutonomousZoneSession();
            var second = new AutonomousZoneSession();

            Assert.True(first.Id < 0);
            Assert.True(second.Id < 0);
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(AccessLevel.normal, first.AccessLevel);
        }
    }
}
