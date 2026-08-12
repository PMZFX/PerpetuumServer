using System;

namespace Perpetuum.Services.Autonomous
{
    public static class AutonomousUndockPolicy
    {
        public static bool IsReady(
            TimeSpan dockedFor,
            TimeSpan configuredDwell,
            DateTime nextAvailableUndockTime,
            DateTime now)
        {
            return dockedFor >= configuredDwell && nextAvailableUndockTime <= now;
        }
    }
}
