using System;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousUndockPolicyTests
    {
        [Fact]
        public void ServerCooldownExtendsAShortConfiguredDwell()
        {
            DateTime now = new DateTime(2026, 8, 12, 0, 38, 24, DateTimeKind.Utc);

            bool ready = AutonomousUndockPolicy.IsReady(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                now.AddSeconds(2),
                now);

            Assert.False(ready);
        }

        [Fact]
        public void ActorIsReadyOnlyAfterDwellAndServerCooldown()
        {
            DateTime now = new DateTime(2026, 8, 12, 0, 38, 27, DateTimeKind.Utc);

            Assert.False(AutonomousUndockPolicy.IsReady(
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(5),
                now,
                now));
            Assert.True(AutonomousUndockPolicy.IsReady(
                TimeSpan.FromSeconds(7),
                TimeSpan.FromSeconds(5),
                now,
                now));
        }
    }
}
