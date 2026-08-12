using System;
using System.Collections.Generic;
using System.Drawing;
using Perpetuum.PathFinders;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Zones;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousNavigationStatus
    {
        Idle,
        Moving,
        Arrived,
        Blocked,
        Stuck
    }

    public readonly struct AutonomousNavigationStep
    {
        public AutonomousNavigationStep(AutonomousNavigationStatus status, MovementInput input)
        {
            Status = status;
            Input = input;
        }

        public AutonomousNavigationStatus Status { get; }
        public MovementInput Input { get; }
    }

    public interface IAutonomousNavigationService
    {
        bool TryStart(GameActionContext context, Position destination, double throttle);
        AutonomousNavigationStatus Update(GameActionContext context, TimeSpan elapsed);
        void Stop(GameActionContext context);
        void Reset();
    }

    /// <summary>
    /// Follows adjacent A* cells by producing only player steering input. It does
    /// not move the unit directly, so the normal movement simulation remains the
    /// authority for speed, slope, collision, and position.
    /// </summary>
    public sealed class AutonomousPathFollower
    {
        private const double WaypointTolerance = 0.4;
        private const double DestinationTolerance = 0.75;
        private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(3);

        private readonly Point[] _path;
        private readonly Position _destination;
        private readonly double _requestedThrottle;
        private int _waypointIndex;
        private Position? _lastPosition;
        private TimeSpan _withoutProgress;

        public AutonomousPathFollower(Point[] path, Position destination, double requestedThrottle)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            if (_path.Length == 0)
                throw new ArgumentException("A navigation path cannot be empty.", nameof(path));
            if (requestedThrottle <= 0 || requestedThrottle > 1)
                throw new ArgumentOutOfRangeException(nameof(requestedThrottle));

            _destination = destination;
            _requestedThrottle = requestedThrottle;
            _waypointIndex = Math.Min(1, _path.Length - 1);
        }

        public AutonomousNavigationStep Update(Position current, double maxSpeed, TimeSpan elapsed)
        {
            if (Distance2D(current, _destination) <= DestinationTolerance)
                return Stopped(AutonomousNavigationStatus.Arrived);

            TrackProgress(current, elapsed);
            if (_withoutProgress >= StuckAfter)
                return Stopped(AutonomousNavigationStatus.Stuck);

            while (_waypointIndex < _path.Length - 1 &&
                   Distance2D(current, ToCenter(_path[_waypointIndex])) <= WaypointTolerance)
            {
                _waypointIndex++;
            }

            Position target = _waypointIndex == _path.Length - 1
                ? _destination
                : ToCenter(_path[_waypointIndex]);
            double distance = Distance2D(current, target);
            double seconds = Math.Max(0.1, elapsed.TotalSeconds);
            double safeThrottle = maxSpeed > 0
                ? Math.Min(_requestedThrottle, Math.Max(0.08, distance / (maxSpeed * seconds) * 0.65))
                : _requestedThrottle;

            return new AutonomousNavigationStep(
                AutonomousNavigationStatus.Moving,
                new MovementInput(current.DirectionTo(target), safeThrottle));
        }

        private void TrackProgress(Position current, TimeSpan elapsed)
        {
            if (_lastPosition.HasValue)
            {
                if (Distance2D(_lastPosition.Value, current) >= 0.03)
                    _withoutProgress = TimeSpan.Zero;
                else
                    _withoutProgress += elapsed;
            }

            _lastPosition = current;
        }

        private static AutonomousNavigationStep Stopped(AutonomousNavigationStatus status)
        {
            return new AutonomousNavigationStep(status, new MovementInput(0, 0));
        }

        private static Position ToCenter(Point point)
        {
            return new Position(point.X + 0.5, point.Y + 0.5);
        }

        private static double Distance2D(Position first, Position second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public sealed class AutonomousNavigationService : IAutonomousNavigationService
    {
        public const double MaximumStartDistance = 48;

        private const int SearchMargin = 8;
        private const int MaxPathLength = 256;
        private const int MaxReplans = 2;

        private readonly IMovementInputService _movementInputService;
        private Position _destination;
        private double _throttle;
        private int _zoneId;
        private int _replans;
        private AutonomousPathFollower _follower;

        public AutonomousNavigationService(IMovementInputService movementInputService)
        {
            _movementInputService = movementInputService;
        }

        public bool TryStart(GameActionContext context, Position destination, double throttle)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (throttle <= 0 || throttle > 1 || double.IsNaN(throttle) || double.IsInfinity(throttle))
                throw new ArgumentOutOfRangeException(nameof(throttle));

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null || Distance2D(player.CurrentPosition, destination) > MaximumStartDistance)
                return false;

            _destination = player.Zone.FixZ(destination);
            _throttle = throttle;
            _zoneId = player.Zone.Id;
            _replans = 0;
            return TryBuildPath(player);
        }

        public AutonomousNavigationStatus Update(GameActionContext context, TimeSpan elapsed)
        {
            if (_follower == null)
                return AutonomousNavigationStatus.Idle;

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null || player.Zone.Id != _zoneId)
                return StopWith(context, AutonomousNavigationStatus.Blocked);

            AutonomousNavigationStep step = _follower.Update(player.CurrentPosition, player.MaxSpeed, elapsed);
            if (step.Status == AutonomousNavigationStatus.Stuck)
            {
                _movementInputService.Apply(context, new MovementInput(player.Direction, 0));
                if (_replans < MaxReplans)
                {
                    _replans++;
                    if (TryBuildPath(player))
                        return AutonomousNavigationStatus.Moving;
                }

                return StopWith(context, AutonomousNavigationStatus.Stuck);
            }

            _movementInputService.Apply(context, step.Input);
            if (step.Status == AutonomousNavigationStatus.Arrived)
                _follower = null;

            return step.Status;
        }

        public void Stop(GameActionContext context)
        {
            _follower = null;
            Player player = context?.Actor.GetPlayerRobotFromZone();
            if (player != null)
                _movementInputService.Apply(context, new MovementInput(player.Direction, 0));
        }

        public void Reset()
        {
            _follower = null;
        }

        private AutonomousNavigationStatus StopWith(GameActionContext context, AutonomousNavigationStatus status)
        {
            Stop(context);
            return status;
        }

        private bool TryBuildPath(Player player)
        {
            Point start = new Point(player.CurrentPosition.intX, player.CurrentPosition.intY);
            Point end = new Point(_destination.intX, _destination.intY);
            int minX = Math.Max(0, Math.Min(start.X, end.X) - SearchMargin);
            int minY = Math.Max(0, Math.Min(start.Y, end.Y) - SearchMargin);
            int maxX = Math.Min(player.Zone.Size.Width - 1, Math.Max(start.X, end.X) + SearchMargin);
            int maxY = Math.Min(player.Zone.Size.Height - 1, Math.Max(start.Y, end.Y) + SearchMargin);

            var finder = new AStarFinder(Heuristic.Chebyshev, (x, y) =>
                x >= minX && x <= maxX && y >= minY && y <= maxY && player.IsWalkable(x, y));
            Point[] path = finder.FindPath(start, end);
            if (path != null && path.Length == 0 && start == end)
                path = new[] { start };
            if (path == null || path.Length == 0 || path.Length > MaxPathLength)
            {
                _follower = null;
                return false;
            }

            _follower = new AutonomousPathFollower(path, _destination, _throttle);
            return true;
        }

        private static double Distance2D(Position first, Position second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
