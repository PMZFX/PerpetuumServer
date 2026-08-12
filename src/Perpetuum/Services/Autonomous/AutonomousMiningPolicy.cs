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
        public static double GetMaximumLegDistance(int stepDistance)
        {
            if (stepDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepDistance));
            return stepDistance * Math.Sqrt(2);
        }

        public static Position[] RebuildCandidateTrail(
            Position origin,
            int nextSiteIndex,
            int stepDistance)
        {
            if (nextSiteIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(nextSiteIndex));
            if (stepDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepDistance));

            var trail = new Position[nextSiteIndex];
            for (int siteIndex = 0; siteIndex < nextSiteIndex; siteIndex++)
                trail[siteIndex] = GetSite(origin, siteIndex, stepDistance);
            return trail;
        }

        public static Position[] RestoreTransitRoute(
            Position origin,
            int nextSiteIndex,
            int stepDistance,
            IReadOnlyList<Position> persistedRoute)
        {
            if (nextSiteIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(nextSiteIndex));
            if (persistedRoute != null &&
                persistedRoute.Count > 0 &&
                persistedRoute.Count <= nextSiteIndex)
            {
                Position previous = origin;
                bool valid = true;
                foreach (Position waypoint in persistedRoute)
                {
                    if (!IsFinite(waypoint) ||
                        previous.TotalDistance2D(waypoint) > AutonomousNavigationService.MaximumStartDistance)
                    {
                        valid = false;
                        break;
                    }
                    previous = waypoint;
                }

                if (valid)
                {
                    var restored = new Position[persistedRoute.Count];
                    for (int index = 0; index < restored.Length; index++)
                        restored[index] = persistedRoute[index];
                    return restored;
                }
            }

            return RebuildCandidateTrail(origin, nextSiteIndex, stepDistance);
        }

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

        private static bool IsFinite(Position position)
        {
            return !double.IsNaN(position.X) &&
                   !double.IsInfinity(position.X) &&
                   !double.IsNaN(position.Y) &&
                   !double.IsInfinity(position.Y);
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

    public static class AutonomousTerminalArcPolicy
    {
        public static Position[] Build(
            Position center,
            Position start,
            Position target,
            double minimumRadius,
            double maximumLegDistance)
        {
            if (minimumRadius <= 0 || double.IsNaN(minimumRadius) || double.IsInfinity(minimumRadius))
                throw new ArgumentOutOfRangeException(nameof(minimumRadius));
            if (maximumLegDistance <= 0 || double.IsNaN(maximumLegDistance) || double.IsInfinity(maximumLegDistance))
                throw new ArgumentOutOfRangeException(nameof(maximumLegDistance));

            double radius = Math.Max(minimumRadius,
                Math.Max(center.TotalDistance2D(start), center.TotalDistance2D(target)));
            int directionCount = Math.Max(16, (int)Math.Ceiling(2 * Math.PI * radius / maximumLegDistance));
            int startIndex = DirectionIndex(center.DirectionTo(start), directionCount);
            int targetIndex = DirectionIndex(center.DirectionTo(target), directionCount);
            int clockwise = (targetIndex - startIndex + directionCount) % directionCount;
            int counterClockwise = (startIndex - targetIndex + directionCount) % directionCount;
            int direction = clockwise <= counterClockwise ? 1 : -1;
            int steps = Math.Min(clockwise, counterClockwise);
            var route = new List<Position>(steps + 1);

            for (int step = 1; step <= steps; step++)
            {
                int index = (startIndex + direction * step + directionCount) % directionCount;
                route.Add(center.OffsetInDirection(index / (double)directionCount, radius).Center);
            }
            route.Add(target);
            return route.ToArray();
        }

        private static int DirectionIndex(double direction, int directionCount)
        {
            int index = (int)Math.Round(direction * directionCount) % directionCount;
            return index < 0 ? index + directionCount : index;
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
