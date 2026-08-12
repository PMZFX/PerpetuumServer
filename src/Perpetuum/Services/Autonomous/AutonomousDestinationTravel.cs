using System;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Units.DockingBases;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousDestinationTravelStatus
    {
        Idle,
        WorldTravel,
        SurfaceTravel,
        WaitingToDock,
        Arrived,
        BaseUnavailable,
        RouteUnavailable,
        Blocked,
        TransitionTimedOut
    }

    public interface IAutonomousDestinationTravelService
    {
        AutonomousDestinationTravelStatus Status { get; }
        long TargetBaseEid { get; }
        bool TryStart(GameActionContext context, long targetBaseEid, double throttle);
        AutonomousDestinationTravelStatus Update(GameActionContext context, TimeSpan elapsed);
        void Stop(GameActionContext context);
    }

    /// <summary>
    /// Composes public world travel with ordinary local movement and docking.
    /// The destination base is configured career knowledge; all transit and
    /// docking checks remain authoritative player actions.
    /// </summary>
    public sealed class AutonomousDestinationTravelService : IAutonomousDestinationTravelService
    {
        private const double LegDistance = 36;
        private const int MaximumLegs = 1024;

        private readonly DockingBaseHelper _dockingBases;
        private readonly IAutonomousWorldTravelService _worldTravel;
        private readonly IAutonomousNavigationService _navigation;
        private readonly IDockActionService _dock;
        private DockingBase _target;
        private double _throttle;
        private int _legCount;
        private int _legAttempt;

        public AutonomousDestinationTravelService(
            DockingBaseHelper dockingBases,
            IAutonomousWorldTravelService worldTravel,
            IAutonomousNavigationService navigation,
            IDockActionService dock)
        {
            _dockingBases = dockingBases ?? throw new ArgumentNullException(nameof(dockingBases));
            _worldTravel = worldTravel ?? throw new ArgumentNullException(nameof(worldTravel));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _dock = dock ?? throw new ArgumentNullException(nameof(dock));
        }

        public AutonomousDestinationTravelStatus Status { get; private set; } =
            AutonomousDestinationTravelStatus.Idle;
        public long TargetBaseEid { get; private set; }

        public bool TryStart(GameActionContext context, long targetBaseEid, double throttle)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (targetBaseEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetBaseEid));
            if (throttle <= 0 || throttle > 1 || double.IsNaN(throttle) || double.IsInfinity(throttle))
                throw new ArgumentOutOfRangeException(nameof(throttle));

            Stop(context);
            TargetBaseEid = targetBaseEid;
            _throttle = throttle;
            _target = _dockingBases.GetDockingBase(targetBaseEid);
            if (_target?.Zone == null)
            {
                Status = AutonomousDestinationTravelStatus.BaseUnavailable;
                return false;
            }

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null)
            {
                Status = AutonomousDestinationTravelStatus.Blocked;
                return false;
            }

            return player.Zone.Id == _target.Zone.Id
                ? BeginSurfaceLeg(context)
                : BeginWorldTravel(context);
        }

        public AutonomousDestinationTravelStatus Update(GameActionContext context, TimeSpan elapsed)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));

            switch (Status)
            {
                case AutonomousDestinationTravelStatus.WorldTravel:
                    UpdateWorldTravel(context, elapsed);
                    break;
                case AutonomousDestinationTravelStatus.SurfaceTravel:
                    UpdateSurfaceTravel(context, elapsed);
                    break;
                case AutonomousDestinationTravelStatus.WaitingToDock:
                    TryDock(context);
                    break;
            }
            return Status;
        }

        public void Stop(GameActionContext context)
        {
            _worldTravel.Stop(context);
            _navigation.Stop(context);
            _target = null;
            TargetBaseEid = 0;
            _legCount = 0;
            _legAttempt = 0;
            Status = AutonomousDestinationTravelStatus.Idle;
        }

        private bool BeginWorldTravel(GameActionContext context)
        {
            bool started = _worldTravel.TryStart(context, _target.Zone.Id, _throttle);
            if (_worldTravel.Status == AutonomousWorldTravelStatus.Arrived)
                return BeginSurfaceLeg(context);
            Status = started
                ? AutonomousDestinationTravelStatus.WorldTravel
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
            if (player?.Zone == null || _target?.Zone == null || player.Zone.Id != _target.Zone.Id)
            {
                Status = AutonomousDestinationTravelStatus.Blocked;
                return false;
            }

            if (_target.IsInDockingRange(player))
            {
                _navigation.Stop(context);
                Status = AutonomousDestinationTravelStatus.WaitingToDock;
                return true;
            }
            if (_legCount >= MaximumLegs)
            {
                Status = AutonomousDestinationTravelStatus.Blocked;
                return false;
            }

            while (_legAttempt < AutonomousWorldTravelLegPolicy.CandidateCount)
            {
                double range = Math.Max(0.75, _target.DockingRange * 0.8);
                Position approach = AutonomousWorldTravelLegPolicy.GetApproachPosition(
                    player.CurrentPosition,
                    _target.CurrentPosition,
                    range);
                Position candidate = AutonomousWorldTravelLegPolicy.GetCandidate(
                    player.CurrentPosition,
                    approach,
                    LegDistance,
                    _legAttempt++);
                if (!_navigation.TryStart(context, candidate, _throttle))
                    continue;
                Status = AutonomousDestinationTravelStatus.SurfaceTravel;
                return true;
            }

            Status = AutonomousDestinationTravelStatus.Blocked;
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

        private void TryDock(GameActionContext context)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null || _target == null || player.Zone.Id != _target.Zone.Id)
            {
                Status = AutonomousDestinationTravelStatus.Blocked;
                return;
            }
            if (!_target.IsInDockingRange(player))
            {
                _legAttempt = 0;
                BeginSurfaceLeg(context);
                return;
            }
            if (player.HasTeleportSicknessEffect)
                return;

            _dock.Execute(context, new DockAction(_target.Eid));
            Status = AutonomousDestinationTravelStatus.Arrived;
        }

        private static AutonomousDestinationTravelStatus Map(AutonomousWorldTravelStatus status)
        {
            switch (status)
            {
                case AutonomousWorldTravelStatus.RouteUnavailable:
                    return AutonomousDestinationTravelStatus.RouteUnavailable;
                case AutonomousWorldTravelStatus.TransitionTimedOut:
                    return AutonomousDestinationTravelStatus.TransitionTimedOut;
                default:
                    return AutonomousDestinationTravelStatus.Blocked;
            }
        }
    }
}
