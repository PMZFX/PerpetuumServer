using System;
using System.Collections.Generic;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Zones.Teleporting;

namespace Perpetuum.Services.Autonomous
{
    public static class AutonomousWorldEntryPolicy
    {
        public static bool ShouldWaitForTeleportEntry(
            bool priorPlayerWasTeleporting,
            TimeSpan missingPlayerElapsed,
            TimeSpan timeout)
        {
            if (missingPlayerElapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(missingPlayerElapsed));
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            return priorPlayerWasTeleporting && missingPlayerElapsed < timeout;
        }
    }

    public enum AutonomousWorldTravelStatus
    {
        Idle,
        Travelling,
        WaitingForTeleportAccess,
        Teleporting,
        Arrived,
        RouteUnavailable,
        Blocked,
        TransitionTimedOut
    }

    public interface IAutonomousWorldTravelService
    {
        AutonomousWorldTravelStatus Status { get; }
        int TargetZoneId { get; }

        bool TryStart(GameActionContext context, int targetZoneId, double throttle);
        AutonomousWorldTravelStatus Update(GameActionContext context, TimeSpan elapsed);
        void Stop(GameActionContext context);
    }

    /// <summary>
    /// Executes a public multi-zone route one normal movement leg and one
    /// audited teleport action at a time. It owns no destination or career
    /// policy, so hauling, exploration, and corporation jobs can share it.
    /// </summary>
    public sealed class AutonomousWorldTravelService : IAutonomousWorldTravelService
    {
        private const double LegDistance = 36;
        private const int MaximumLegsPerHop = 1024;
        private static readonly TimeSpan ZoneTransitionTimeout = TimeSpan.FromSeconds(20);

        private readonly IAutonomousTeleportNetworkService _network;
        private readonly IAutonomousNavigationService _navigation;
        private readonly ITeleportActionService _teleport;
        private AutonomousTeleportLink _link;
        private double _throttle;
        private int _legAttempt;
        private int _legCount;
        private TimeSpan _transitionElapsed;

        public AutonomousWorldTravelService(
            IAutonomousTeleportNetworkService network,
            IAutonomousNavigationService navigation,
            ITeleportActionService teleport)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _teleport = teleport ?? throw new ArgumentNullException(nameof(teleport));
        }

        public AutonomousWorldTravelStatus Status { get; private set; } = AutonomousWorldTravelStatus.Idle;
        public int TargetZoneId { get; private set; }

        public bool TryStart(GameActionContext context, int targetZoneId, double throttle)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (targetZoneId < 0)
                throw new ArgumentOutOfRangeException(nameof(targetZoneId));
            if (double.IsNaN(throttle) || double.IsInfinity(throttle) || throttle <= 0 || throttle > 1)
                throw new ArgumentOutOfRangeException(nameof(throttle));

            _navigation.Stop(context);
            TargetZoneId = targetZoneId;
            _throttle = throttle;
            _link = null;
            _legAttempt = 0;
            _legCount = 0;
            _transitionElapsed = TimeSpan.Zero;
            return BeginCurrentHop(context);
        }

        public AutonomousWorldTravelStatus Update(GameActionContext context, TimeSpan elapsed)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));

            switch (Status)
            {
                case AutonomousWorldTravelStatus.Travelling:
                    UpdateMovement(context, elapsed);
                    break;
                case AutonomousWorldTravelStatus.WaitingForTeleportAccess:
                    TryUseTeleport(context);
                    break;
                case AutonomousWorldTravelStatus.Teleporting:
                    UpdateZoneTransition(context, elapsed);
                    break;
            }

            return Status;
        }

        public void Stop(GameActionContext context)
        {
            _navigation.Stop(context);
            _link = null;
            TargetZoneId = 0;
            _legAttempt = 0;
            _legCount = 0;
            _transitionElapsed = TimeSpan.Zero;
            Status = AutonomousWorldTravelStatus.Idle;
        }

        private bool BeginCurrentHop(GameActionContext context)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null)
            {
                Status = AutonomousWorldTravelStatus.Blocked;
                return false;
            }

            if (player.Zone.Id == TargetZoneId)
            {
                Status = AutonomousWorldTravelStatus.Arrived;
                return true;
            }

            IReadOnlyList<AutonomousTeleportLink> route = AutonomousTeleportRoutePolicy.FindRouteFromPosition(
                player.Zone.Id,
                TargetZoneId,
                player.CurrentPosition,
                _network.Observe());
            if (route.Count == 0)
            {
                Status = AutonomousWorldTravelStatus.RouteUnavailable;
                return false;
            }

            _link = route[0];
            _legAttempt = 0;
            _legCount = 0;
            return TryStartNextLeg(context);
        }

        private bool TryStartNextLeg(GameActionContext context)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null || _link == null || player.Zone.Id != _link.SourceZoneId)
            {
                Status = AutonomousWorldTravelStatus.Blocked;
                return false;
            }

            double interactionRange = GetInteractionRange(_link);
            if (player.CurrentPosition.TotalDistance2D(_link.SourcePosition) <= interactionRange)
            {
                _navigation.Stop(context);
                Status = AutonomousWorldTravelStatus.WaitingForTeleportAccess;
                return true;
            }

            if (_legCount >= MaximumLegsPerHop)
            {
                Status = AutonomousWorldTravelStatus.Blocked;
                return false;
            }

            while (_legAttempt < AutonomousWorldTravelLegPolicy.CandidateCount)
            {
                Position approach = AutonomousWorldTravelLegPolicy.GetApproachPosition(
                    player.CurrentPosition,
                    _link.SourcePosition,
                    interactionRange);
                Position candidate = AutonomousWorldTravelLegPolicy.GetCandidate(
                    player.CurrentPosition,
                    approach,
                    LegDistance,
                    _legAttempt++);
                if (!_navigation.TryStart(context, candidate, _throttle))
                    continue;

                Status = AutonomousWorldTravelStatus.Travelling;
                return true;
            }

            Status = AutonomousWorldTravelStatus.Blocked;
            return false;
        }

        private void UpdateMovement(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousNavigationStatus navigationStatus = _navigation.Update(context, elapsed);
            if (navigationStatus == AutonomousNavigationStatus.Arrived)
            {
                _navigation.Stop(context);
                _legCount++;
                _legAttempt = 0;
                TryStartNextLeg(context);
            }
            else if (navigationStatus == AutonomousNavigationStatus.Blocked ||
                     navigationStatus == AutonomousNavigationStatus.Stuck)
            {
                _navigation.Stop(context);
                TryStartNextLeg(context);
            }
        }

        private void TryUseTeleport(GameActionContext context)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null || _link == null || player.Zone.Id != _link.SourceZoneId)
            {
                Status = AutonomousWorldTravelStatus.Blocked;
                return;
            }

            if (player.CurrentPosition.TotalDistance2D(_link.SourcePosition) > GetInteractionRange(_link))
            {
                _legAttempt = 0;
                TryStartNextLeg(context);
                return;
            }

            if (player.HasTeleportSicknessEffect)
                return;

            _teleport.Execute(context, new TeleportAction(_link.SourceTeleportEid, _link.DescriptionId));
            _transitionElapsed = TimeSpan.Zero;
            Status = AutonomousWorldTravelStatus.Teleporting;
        }

        private void UpdateZoneTransition(GameActionContext context, TimeSpan elapsed)
        {
            _transitionElapsed += elapsed;
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone != null && _link != null && player.Zone.Id == _link.TargetZoneId)
            {
                _link = null;
                _transitionElapsed = TimeSpan.Zero;
                BeginCurrentHop(context);
                return;
            }

            if (_transitionElapsed < ZoneTransitionTimeout)
                return;

            Status = AutonomousWorldTravelStatus.TransitionTimedOut;
        }

        private static double GetInteractionRange(AutonomousTeleportLink link)
        {
            return Math.Max(0.75, Math.Min(link.SourceRange, Teleport.TeleportRange) * 0.8);
        }
    }

    public static class AutonomousWorldTravelLegPolicy
    {
        private static readonly double[] DistanceScales = {1, 2.0 / 3, 1.0 / 3};

        private static readonly double[] DirectionOffsets =
        {
            0,
            1.0 / 16,
            -1.0 / 16,
            2.0 / 16,
            -2.0 / 16,
            3.0 / 16,
            -3.0 / 16,
            4.0 / 16,
            -4.0 / 16,
            5.0 / 16,
            -5.0 / 16,
            6.0 / 16,
            -6.0 / 16,
            7.0 / 16,
            -7.0 / 16,
            8.0 / 16
        };

        public static int CandidateCount => DirectionOffsets.Length * DistanceScales.Length;

        /// <summary>
        /// Stops short of an occupied world object while remaining safely
        /// inside its normal interaction radius.
        /// </summary>
        public static Position GetApproachPosition(
            Position current,
            Position source,
            double interactionRange)
        {
            if (double.IsNaN(interactionRange) || double.IsInfinity(interactionRange) ||
                interactionRange <= 0)
                throw new ArgumentOutOfRangeException(nameof(interactionRange));
            if (current.TotalDistance2D(source) <= interactionRange)
                return current;

            double stopDistance = Math.Max(0.5, interactionRange * 0.65);
            return source.OffsetInDirection(source.DirectionTo(current), stopDistance);
        }

        /// <summary>
        /// Selects a deterministic local leg toward a public destination.
        /// Later attempts fan out around an obstacle, while every candidate
        /// remains inside the normal navigator's local range.
        /// </summary>
        public static Position GetCandidate(
            Position current,
            Position destination,
            double legDistance,
            int attempt)
        {
            if (double.IsNaN(legDistance) || double.IsInfinity(legDistance) ||
                legDistance <= 0 || legDistance > AutonomousNavigationService.MaximumStartDistance)
                throw new ArgumentOutOfRangeException(nameof(legDistance));
            if (attempt < 0 || attempt >= CandidateCount)
                throw new ArgumentOutOfRangeException(nameof(attempt));

            double remaining = current.TotalDistance2D(destination);
            if (attempt == 0 && remaining <= legDistance)
                return destination;

            int directionIndex = attempt % DirectionOffsets.Length;
            int distanceIndex = attempt / DirectionOffsets.Length;
            double fullDistance = Math.Min(legDistance, Math.Max(6, remaining * 0.75));
            double distance = Math.Max(6, fullDistance * DistanceScales[distanceIndex]);
            double direction = NormalizeDirection(
                current.DirectionTo(destination) + DirectionOffsets[directionIndex]);
            return current.OffsetInDirection(direction, distance).Center;
        }

        private static double NormalizeDirection(double direction)
        {
            direction %= 1;
            return direction < 0 ? direction + 1 : direction;
        }
    }
}
