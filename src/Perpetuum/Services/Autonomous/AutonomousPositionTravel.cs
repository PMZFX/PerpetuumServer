using System;
using Perpetuum.Players;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousPositionTravelStatus
    {
        Idle,
        WorldTravel,
        SurfaceTravel,
        Arrived,
        RouteUnavailable,
        Blocked,
        TransitionTimedOut
    }

    public interface IAutonomousPositionTravelService
    {
        AutonomousPositionTravelStatus Status { get; }
        int TargetZoneId { get; }
        Position TargetPosition { get; }

        bool TryStart(
            GameActionContext context,
            int targetZoneId,
            Position targetPosition,
            double targetRange,
            double throttle);
        AutonomousPositionTravelStatus Update(GameActionContext context, TimeSpan elapsed);
        void Stop(GameActionContext context);
    }

    /// <summary>
    /// Composes public teleport routing and normal steering into bounded legs
    /// toward a player-visible map objective. It neither teleports directly nor
    /// completes the objective; ordinary zone simulation remains authoritative.
    /// </summary>
    public sealed class AutonomousPositionTravelService : IAutonomousPositionTravelService
    {
        private const double LegDistance = 36;
        private const int MaximumLegs = 1024;

        private readonly IAutonomousWorldTravelService _worldTravel;
        private readonly IAutonomousNavigationService _navigation;
        private double _targetRange;
        private double _throttle;
        private int _legCount;
        private int _legAttempt;

        public AutonomousPositionTravelService(
            IAutonomousWorldTravelService worldTravel,
            IAutonomousNavigationService navigation)
        {
            _worldTravel = worldTravel ?? throw new ArgumentNullException(nameof(worldTravel));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        }

        public AutonomousPositionTravelStatus Status { get; private set; } =
            AutonomousPositionTravelStatus.Idle;
        public int TargetZoneId { get; private set; }
        public Position TargetPosition { get; private set; }

        public bool TryStart(
            GameActionContext context,
            int targetZoneId,
            Position targetPosition,
            double targetRange,
            double throttle)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (targetZoneId < 0)
                throw new ArgumentOutOfRangeException(nameof(targetZoneId));
            if (double.IsNaN(targetRange) || double.IsInfinity(targetRange) || targetRange <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetRange));
            if (double.IsNaN(throttle) || double.IsInfinity(throttle) || throttle <= 0 || throttle > 1)
                throw new ArgumentOutOfRangeException(nameof(throttle));

            Stop(context);
            TargetZoneId = targetZoneId;
            TargetPosition = targetPosition;
            _targetRange = Math.Max(0.75, targetRange * 0.8);
            _throttle = throttle;

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null)
            {
                Status = AutonomousPositionTravelStatus.Blocked;
                return false;
            }
            return player.Zone.Id == TargetZoneId
                ? BeginSurfaceLeg(context)
                : BeginWorldTravel(context);
        }

        public AutonomousPositionTravelStatus Update(GameActionContext context, TimeSpan elapsed)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));

            if (Status == AutonomousPositionTravelStatus.WorldTravel)
                UpdateWorldTravel(context, elapsed);
            else if (Status == AutonomousPositionTravelStatus.SurfaceTravel)
                UpdateSurfaceTravel(context, elapsed);
            return Status;
        }

        public void Stop(GameActionContext context)
        {
            _worldTravel.Stop(context);
            _navigation.Stop(context);
            TargetZoneId = 0;
            TargetPosition = default(Position);
            _targetRange = 0;
            _legCount = 0;
            _legAttempt = 0;
            Status = AutonomousPositionTravelStatus.Idle;
        }

        private bool BeginWorldTravel(GameActionContext context)
        {
            bool started = _worldTravel.TryStart(context, TargetZoneId, _throttle);
            if (_worldTravel.Status == AutonomousWorldTravelStatus.Arrived)
                return BeginSurfaceLeg(context);
            Status = started
                ? AutonomousPositionTravelStatus.WorldTravel
                : Map(_worldTravel.Status);
            return started;
        }

        private void UpdateWorldTravel(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousWorldTravelStatus status = _worldTravel.Update(context, elapsed);
            if (status == AutonomousWorldTravelStatus.Arrived)
            {
                _worldTravel.Stop(context);
                BeginSurfaceLeg(context);
            }
            else if (status != AutonomousWorldTravelStatus.Travelling &&
                     status != AutonomousWorldTravelStatus.WaitingForTeleportAccess &&
                     status != AutonomousWorldTravelStatus.Teleporting)
            {
                Status = Map(status);
            }
        }

        private bool BeginSurfaceLeg(GameActionContext context)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null || player.Zone.Id != TargetZoneId)
            {
                Status = AutonomousPositionTravelStatus.Blocked;
                return false;
            }
            if (player.CurrentPosition.TotalDistance2D(TargetPosition) <= _targetRange)
            {
                _navigation.Stop(context);
                Status = AutonomousPositionTravelStatus.Arrived;
                return true;
            }
            if (_legCount >= MaximumLegs)
            {
                Status = AutonomousPositionTravelStatus.Blocked;
                return false;
            }

            while (_legAttempt < AutonomousWorldTravelLegPolicy.CandidateCount)
            {
                Position approach = AutonomousWorldTravelLegPolicy.GetApproachPosition(
                    player.CurrentPosition,
                    TargetPosition,
                    _targetRange);
                Position candidate = AutonomousWorldTravelLegPolicy.GetCandidate(
                    player.CurrentPosition,
                    approach,
                    LegDistance,
                    _legAttempt++);
                if (!_navigation.TryStart(context, candidate, _throttle))
                    continue;
                Status = AutonomousPositionTravelStatus.SurfaceTravel;
                return true;
            }

            Status = AutonomousPositionTravelStatus.Blocked;
            return false;
        }

        private void UpdateSurfaceTravel(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousNavigationStatus status = _navigation.Update(context, elapsed);
            if (status == AutonomousNavigationStatus.Arrived)
            {
                _navigation.Stop(context);
                _legCount++;
                _legAttempt = 0;
                BeginSurfaceLeg(context);
            }
            else if (status == AutonomousNavigationStatus.Blocked ||
                     status == AutonomousNavigationStatus.Stuck)
            {
                _navigation.Stop(context);
                BeginSurfaceLeg(context);
            }
        }

        private static AutonomousPositionTravelStatus Map(AutonomousWorldTravelStatus status)
        {
            switch (status)
            {
                case AutonomousWorldTravelStatus.RouteUnavailable:
                    return AutonomousPositionTravelStatus.RouteUnavailable;
                case AutonomousWorldTravelStatus.TransitionTimedOut:
                    return AutonomousPositionTravelStatus.TransitionTimedOut;
                default:
                    return AutonomousPositionTravelStatus.Blocked;
            }
        }
    }
}
