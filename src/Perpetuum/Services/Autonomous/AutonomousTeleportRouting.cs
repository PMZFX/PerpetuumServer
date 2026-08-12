using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Zones.Teleporting;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousTeleportLink
    {
        public AutonomousTeleportLink(
            int descriptionId,
            long sourceTeleportEid,
            int sourceZoneId,
            Position sourcePosition,
            int sourceRange,
            int targetZoneId,
            TeleportDescriptionType type,
            bool active,
            bool listable,
            bool valid)
        {
            DescriptionId = descriptionId;
            SourceTeleportEid = sourceTeleportEid;
            SourceZoneId = sourceZoneId;
            SourcePosition = sourcePosition;
            SourceRange = sourceRange;
            TargetZoneId = targetZoneId;
            Type = type;
            Active = active;
            Listable = listable;
            Valid = valid;
        }

        public int DescriptionId { get; }
        public long SourceTeleportEid { get; }
        public int SourceZoneId { get; }
        public Position SourcePosition { get; }
        public int SourceRange { get; }
        public int TargetZoneId { get; }
        public TeleportDescriptionType Type { get; }
        public bool Active { get; }
        public bool Listable { get; }
        public bool Valid { get; }

        public bool IsUsableForWorldRoute =>
            DescriptionId > 0 &&
            SourceTeleportEid > 0 &&
            SourceZoneId >= 0 &&
            SourceRange > 0 &&
            TargetZoneId >= 0 &&
            SourceZoneId != TargetZoneId &&
            Type == TeleportDescriptionType.AnotherZone &&
            Active &&
            Listable &&
            Valid;
    }

    public interface IAutonomousTeleportNetworkService
    {
        IReadOnlyList<AutonomousTeleportLink> Observe();
    }

    /// <summary>
    /// Copies the same listable teleport descriptions exposed by TeleportList.
    /// It adds no hidden terrain, unit, or destination information.
    /// </summary>
    public sealed class AutonomousTeleportNetworkService : IAutonomousTeleportNetworkService
    {
        private readonly ITeleportDescriptionRepository _descriptions;

        public AutonomousTeleportNetworkService(ITeleportDescriptionRepository descriptions)
        {
            _descriptions = descriptions ?? throw new ArgumentNullException(nameof(descriptions));
        }

        public IReadOnlyList<AutonomousTeleportLink> Observe()
        {
            return _descriptions.GetAll()
                .Where(description => description.listable)
                .OrderBy(description => description.id)
                .Select(description => new AutonomousTeleportLink(
                    description.id,
                    description.SourceTeleport?.Eid ?? 0,
                    description.SourceZone?.Id ?? 0,
                    description.SourceTeleport?.CurrentPosition ?? Position.Empty,
                    description.sourceRange ?? Teleport.TeleportRange,
                    description.TargetZone?.Id ?? 0,
                    description.descriptionType,
                    description.active,
                    description.listable,
                    description.IsValid()))
                .ToArray();
        }
    }

    public static class AutonomousTeleportRoutePolicy
    {
        /// <summary>
        /// Finds a deterministic shortest zone route through currently usable
        /// public inter-zone links. Equal-length choices use description ID.
        /// </summary>
        public static IReadOnlyList<AutonomousTeleportLink> FindRoute(
            int sourceZoneId,
            int targetZoneId,
            IEnumerable<AutonomousTeleportLink> links)
        {
            if (sourceZoneId < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceZoneId));
            if (targetZoneId < 0)
                throw new ArgumentOutOfRangeException(nameof(targetZoneId));
            if (links == null)
                throw new ArgumentNullException(nameof(links));
            if (sourceZoneId == targetZoneId)
                return Array.Empty<AutonomousTeleportLink>();

            AutonomousTeleportLink[] usable = links
                .Where(link => link != null && link.IsUsableForWorldRoute)
                .OrderBy(link => link.DescriptionId)
                .ToArray();
            var visited = new HashSet<int> {sourceZoneId};
            var queue = new Queue<RouteNode>();
            queue.Enqueue(new RouteNode(sourceZoneId, Array.Empty<AutonomousTeleportLink>()));

            while (queue.Count > 0)
            {
                RouteNode current = queue.Dequeue();
                foreach (AutonomousTeleportLink link in usable.Where(candidate =>
                             candidate.SourceZoneId == current.ZoneId))
                {
                    if (!visited.Add(link.TargetZoneId))
                        continue;

                    AutonomousTeleportLink[] route = current.Route.Concat(new[] {link}).ToArray();
                    if (link.TargetZoneId == targetZoneId)
                        return route;
                    queue.Enqueue(new RouteNode(link.TargetZoneId, route));
                }
            }

            return Array.Empty<AutonomousTeleportLink>();
        }

        private sealed class RouteNode
        {
            public RouteNode(int zoneId, IReadOnlyList<AutonomousTeleportLink> route)
            {
                ZoneId = zoneId;
                Route = route;
            }

            public int ZoneId { get; }
            public IReadOnlyList<AutonomousTeleportLink> Route { get; }
        }
    }
}
