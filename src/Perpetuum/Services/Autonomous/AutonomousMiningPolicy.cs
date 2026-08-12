using System;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousMiningResumeDirective
    {
        StartDocked,
        ResumeTarget,
        ReturnToOrigin,
        RecoverToBase
    }

    public static class AutonomousMiningResumePolicy
    {
        public static AutonomousMiningResumeDirective Select(
            bool docked,
            int? currentZoneId,
            MaterialType configuredMaterial,
            AutonomousWorkState state)
        {
            if (docked)
                return AutonomousMiningResumeDirective.StartDocked;
            if (state == null ||
                !string.Equals(state.BehaviorName, "mining", StringComparison.OrdinalIgnoreCase) ||
                state.ZoneId != currentZoneId ||
                state.MaterialType != configuredMaterial ||
                !state.Origin.HasValue ||
                state.DockingBaseEid <= 0)
                return AutonomousMiningResumeDirective.RecoverToBase;

            if (state.Target.HasValue &&
                (EqualsPhase(state, "TravellingToDeposit") ||
                 EqualsPhase(state, "LockingDeposit") ||
                 EqualsPhase(state, "Mining")))
                return AutonomousMiningResumeDirective.ResumeTarget;

            return AutonomousMiningResumeDirective.ReturnToOrigin;
        }

        private static bool EqualsPhase(AutonomousWorkState state, string phase)
        {
            return string.Equals(state.Phase, phase, StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum AutonomousMiningReturnReason
    {
        None,
        CargoThreshold,
        DurationLimit,
        Threat,
        EquipmentUnavailable
    }

    public static class AutonomousMiningReturnPolicy
    {
        public static AutonomousMiningReturnReason Assess(
            double cargoFillRatio,
            double configuredCargoFillRatio,
            TimeSpan miningElapsed,
            TimeSpan maximumMiningTime,
            bool threat,
            bool equipmentAvailable)
        {
            if (threat)
                return AutonomousMiningReturnReason.Threat;
            if (!equipmentAvailable)
                return AutonomousMiningReturnReason.EquipmentUnavailable;
            if (cargoFillRatio >= configuredCargoFillRatio)
                return AutonomousMiningReturnReason.CargoThreshold;
            if (miningElapsed >= maximumMiningTime)
                return AutonomousMiningReturnReason.DurationLimit;
            return AutonomousMiningReturnReason.None;
        }
    }

    public static class AutonomousMiningSurveyPolicy
    {
        private static readonly (int X, int Y)[] Directions =
        {
            (1, 0),
            (1, 1),
            (0, 1),
            (-1, 1),
            (-1, 0),
            (-1, -1),
            (0, -1),
            (1, -1)
        };

        public static Position GetSite(Position origin, int siteIndex, int stepDistance)
        {
            if (siteIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(siteIndex));
            if (stepDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepDistance));

            int ring = siteIndex / Directions.Length + 1;
            (int x, int y) = Directions[siteIndex % Directions.Length];
            return new Position(
                origin.X + x * ring * stepDistance,
                origin.Y + y * ring * stepDistance);
        }
    }
}
