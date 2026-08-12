using System;
using System.Collections.Generic;
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
        public static int SelectResumeSite(
            MaterialType configuredMaterial,
            int maxSites,
            AutonomousWorkState state)
        {
            if (maxSites < 0)
                throw new ArgumentOutOfRangeException(nameof(maxSites));
            if (state == null ||
                !string.Equals(state.BehaviorName, "mining", StringComparison.OrdinalIgnoreCase) ||
                state.MaterialType != configuredMaterial ||
                state.SurveySiteIndex < 0 ||
                state.SurveySiteIndex >= maxSites)
                return 0;

            return state.SurveySiteIndex;
        }

        public static int GetRingCount(int siteCount)
        {
            if (siteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(siteCount));

            int ring = 0;
            int sitesThroughRing = 0;
            while (sitesThroughRing < siteCount)
            {
                ring++;
                sitesThroughRing += 8 * ring;
            }

            return ring;
        }

        public static Position GetSite(Position origin, int siteIndex, int stepDistance)
        {
            if (siteIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(siteIndex));
            if (stepDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepDistance));

            int ring = 1;
            int indexInRing = siteIndex;
            while (indexInRing >= 8 * ring)
            {
                indexInRing -= 8 * ring;
                ring++;
            }

            (int x, int y) = GetRingOffset(ring, indexInRing);
            return new Position(
                origin.X + x * stepDistance,
                origin.Y + y * stepDistance);
        }

        private static (int X, int Y) GetRingOffset(int ring, int index)
        {
            int eastUpperLength = ring + 1;
            if (index < eastUpperLength)
                return (ring, index);
            index -= eastUpperLength;

            int edgeLength = ring * 2;
            if (index < edgeLength)
                return (ring - 1 - index, ring);
            index -= edgeLength;

            if (index < edgeLength)
                return (-ring, ring - 1 - index);
            index -= edgeLength;

            if (index < edgeLength)
                return (-ring + 1 + index, -ring);
            index -= edgeLength;

            return (ring, -ring + 1 + index);
        }
    }

    public static class AutonomousDockingRecoveryPolicy
    {
        private const int DirectionCount = 16;
        private const int AttemptStride = 5;

        public static int GetDirectionIndex(int preferredDirectionIndex, int attempt, int candidateIndex)
        {
            if (preferredDirectionIndex < 0 || preferredDirectionIndex >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(preferredDirectionIndex));
            if (attempt < 0)
                throw new ArgumentOutOfRangeException(nameof(attempt));
            if (candidateIndex < 0 || candidateIndex >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(candidateIndex));

            return (int)((preferredDirectionIndex + (long)attempt * AttemptStride + candidateIndex) % DirectionCount);
        }
    }

    public static class AutonomousMiningReturnRoutePolicy
    {
        public static Position[] Build(Position origin, IReadOnlyList<Position> traversedSurveySites)
        {
            if (traversedSurveySites == null)
                throw new ArgumentNullException(nameof(traversedSurveySites));

            var route = new Position[traversedSurveySites.Count + 1];
            for (int index = 0; index < traversedSurveySites.Count; index++)
                route[index] = traversedSurveySites[traversedSurveySites.Count - index - 1];
            route[route.Length - 1] = origin;
            return route;
        }
    }
}
