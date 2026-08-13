using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Units;
using Perpetuum.Zones.NpcSystem;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousVisibleUnitKind
    {
        Other,
        Player,
        Npc
    }

    public sealed class AutonomousVisibleUnitSnapshot
    {
        public AutonomousVisibleUnitSnapshot(
            long eid,
            AutonomousVisibleUnitKind kind,
            Position position,
            double distance,
            bool hostile)
            : this(eid, kind, 0, position, distance, hostile)
        {
        }

        public AutonomousVisibleUnitSnapshot(
            long eid,
            AutonomousVisibleUnitKind kind,
            int definition,
            Position position,
            double distance,
            bool hostile)
        {
            Eid = eid;
            Kind = kind;
            Definition = definition;
            Position = position;
            Distance = distance;
            Hostile = hostile;
        }

        public long Eid { get; }
        public AutonomousVisibleUnitKind Kind { get; }
        public int Definition { get; }
        public Position Position { get; }
        public double Distance { get; }
        public bool Hostile { get; }
    }

    public sealed class AutonomousPerceptionSnapshot
    {
        public AutonomousPerceptionSnapshot(
            bool docked,
            int? zoneId,
            Position? position,
            IEnumerable<AutonomousVisibleUnitSnapshot> visibleUnits)
        {
            Docked = docked;
            ZoneId = zoneId;
            Position = position;
            VisibleUnits = (visibleUnits ?? Enumerable.Empty<AutonomousVisibleUnitSnapshot>())
                .OrderBy(unit => unit.Distance)
                .ThenBy(unit => unit.Eid)
                .ToArray();
        }

        public bool Docked { get; }
        public int? ZoneId { get; }
        public Position? Position { get; }
        public IReadOnlyList<AutonomousVisibleUnitSnapshot> VisibleUnits { get; }
    }

    public sealed class AutonomousThreatAssessment
    {
        private AutonomousThreatAssessment(
            IReadOnlyList<AutonomousVisibleUnitSnapshot> threats,
            AutonomousVisibleUnitSnapshot nearest)
        {
            Threats = threats;
            Nearest = nearest;
        }

        public IReadOnlyList<AutonomousVisibleUnitSnapshot> Threats { get; }
        public AutonomousVisibleUnitSnapshot Nearest { get; }
        public bool HasThreat => Nearest != null;

        public static AutonomousThreatAssessment From(
            AutonomousPerceptionSnapshot snapshot,
            double responseRange,
            long excludedEid = 0)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (double.IsNaN(responseRange) || double.IsInfinity(responseRange) || responseRange < 0)
                throw new ArgumentOutOfRangeException(nameof(responseRange));

            AutonomousVisibleUnitSnapshot[] threats = snapshot.VisibleUnits
                .Where(unit => unit.Eid != excludedEid && unit.Hostile && unit.Distance <= responseRange)
                .ToArray();
            return new AutonomousThreatAssessment(threats, threats.FirstOrDefault());
        }
    }

    public enum AutonomousFieldActivity
    {
        Other,
        Deploying,
        TravellingOutbound,
        Dwelling,
        Returning,
        Docking
    }

    public enum AutonomousThreatDirective
    {
        None,
        Dock,
        Return
    }

    public static class AutonomousThreatResponsePolicy
    {
        public static AutonomousThreatDirective Select(
            AutonomousThreatAssessment assessment,
            AutonomousFieldActivity activity)
        {
            if (assessment == null)
                throw new ArgumentNullException(nameof(assessment));
            if (!assessment.HasThreat)
                return AutonomousThreatDirective.None;

            switch (activity)
            {
                case AutonomousFieldActivity.Deploying:
                    return AutonomousThreatDirective.Dock;
                case AutonomousFieldActivity.TravellingOutbound:
                case AutonomousFieldActivity.Dwelling:
                    return AutonomousThreatDirective.Return;
                default:
                    return AutonomousThreatDirective.None;
            }
        }
    }

    public interface IAutonomousPerceptionService
    {
        AutonomousPerceptionSnapshot Observe(GameActionContext context);
    }

    /// <summary>
    /// Projects the real player's existing visibility set. That set is also the
    /// source of client enter/exit packets and already applies detection,
    /// stealth, gang visibility, and GM stealth rules.
    /// </summary>
    public sealed class AutonomousPerceptionService : IAutonomousPerceptionService
    {
        public AutonomousPerceptionSnapshot Observe(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null)
            {
                return new AutonomousPerceptionSnapshot(
                    context.Actor.IsDocked,
                    context.Actor.ZoneId,
                    context.Actor.ZonePosition,
                    null);
            }

            AutonomousVisibleUnitSnapshot[] visibleUnits = player.GetVisibleUnits()
                .Select(visibility => visibility.Target)
                .Where(target => target != null && target.InZone && target.Zone == player.Zone)
                .Select(target => new AutonomousVisibleUnitSnapshot(
                    target.Eid,
                    GetKind(target),
                    target.Definition,
                    target.CurrentPosition,
                    Distance2D(player.CurrentPosition, target.CurrentPosition),
                    target.IsHostile(player)))
                .ToArray();

            return new AutonomousPerceptionSnapshot(
                false,
                player.Zone.Id,
                player.CurrentPosition,
                visibleUnits);
        }

        private static AutonomousVisibleUnitKind GetKind(Unit unit)
        {
            if (unit is Player)
                return AutonomousVisibleUnitKind.Player;
            if (unit is Npc)
                return AutonomousVisibleUnitKind.Npc;
            return AutonomousVisibleUnitKind.Other;
        }

        private static double Distance2D(Position first, Position second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
