using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.EntityFramework;
using Perpetuum.Players;
using Perpetuum.Services.Sessions;
using Perpetuum.Units;
using Perpetuum.Zones;
using Perpetuum.Zones.Locking.Locks;
using Perpetuum.Zones.NpcSystem;
using Perpetuum.Zones.PunchBags;

namespace Perpetuum.Services.Mentoring
{
    public sealed class ReadOnlyMentorPlayerContextReader : IMentorPlayerContextReader
    {
        private readonly ISessionManager _sessionManager;
        private readonly Lazy<IEntityDefaultReader> _entityDefaultReader;
        private readonly IMentorTextCatalog _textCatalog;

        public ReadOnlyMentorPlayerContextReader(
            ISessionManager sessionManager,
            Lazy<IEntityDefaultReader> entityDefaultReader)
            : this(sessionManager, entityDefaultReader, FallbackMentorTextCatalog.Instance)
        {
        }

        public ReadOnlyMentorPlayerContextReader(
            ISessionManager sessionManager,
            Lazy<IEntityDefaultReader> entityDefaultReader,
            IMentorTextCatalog textCatalog)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _entityDefaultReader = entityDefaultReader ??
                throw new ArgumentNullException(nameof(entityDefaultReader));
            _textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
        }

        public MentorPlayerContext Read(MentorRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ISession session = _sessionManager.GetByCharacter(request.CharacterId);
            if (session == null || session.Character == null || session.Character.Id != request.CharacterId)
                throw new InvalidOperationException("The requesting character is no longer online.");
            if (session.AccountId != request.AccountId)
                throw new InvalidOperationException("The request is not scoped to the active character session.");

            var character = session.Character;
            ZoneConfiguration zone = character.GetCurrentZoneConfiguration();
            bool isDocked = character.IsDocked;
            int? zoneId = character.ZoneId;
            long activeRobotEid = character.ActiveRobotEid;
            IZone liveZone = zoneId.HasValue ? session.ZoneMgr.GetZone(zoneId.Value) : null;
            Player livePlayer = !isDocked && liveZone != null && activeRobotEid > 0
                ? liveZone.GetPlayer(activeRobotEid)
                : null;
            Position? position = livePlayer?.CurrentPosition ?? character.ZonePosition;
            EntityDefault activeRobot = activeRobotEid > 0
                ? _entityDefaultReader.Value.GetByEid(activeRobotEid)
                : EntityDefault.None;
            long dockingBaseEid = isDocked ? character.CurrentDockingBaseEid : 0;
            EntityDefault dockingBase = dockingBaseEid > 0
                ? _entityDefaultReader.Value.GetByEid(dockingBaseEid)
                : EntityDefault.None;

            return new MentorPlayerContext(
                character.Id,
                session.AccountId,
                character.Nick,
                isDocked,
                zoneId,
                zone == ZoneConfiguration.None ? null : zone.Name,
                position,
                activeRobotEid,
                activeRobot == EntityDefault.None ? 0 : activeRobot.Definition,
                activeRobot == EntityDefault.None ? null : _textCatalog.DisplayName(activeRobot.Name),
                dockingBaseEid,
                dockingBase == EntityDefault.None ? null : _textCatalog.DisplayName(dockingBase.Name),
                character.IsInTraining(),
                ReadVisibleTargets(livePlayer),
                ReadZoneCombatTargets(livePlayer, liveZone),
                livePlayer != null && liveZone != null);
        }

        private IReadOnlyList<MentorNearbyUnitSnapshot> ReadVisibleTargets(Player player)
        {
            if (player == null)
                return Array.Empty<MentorNearbyUnitSnapshot>();

            return player.GetVisibleUnits()
                .Select(visibility => visibility.Target)
                .Where(IsNpcOrTutorialTarget)
                .OrderBy(player.GetDistance)
                .Take(12)
                .Select(unit => ToSnapshot(player, unit, true))
                .ToArray();
        }

        private IReadOnlyList<MentorNearbyUnitSnapshot> ReadZoneCombatTargets(
            Player player,
            IZone zone)
        {
            if (player == null || zone == null)
                return Array.Empty<MentorNearbyUnitSnapshot>();

            return zone.Units
                .Where(IsNpcOrTutorialTarget)
                .Where(unit => unit.IsAttackable == ErrorCodes.NoError &&
                               !unit.IsInvulnerable &&
                               unit.IsLockable)
                .OrderBy(player.GetDistance)
                .Take(12)
                .Select(unit => ToSnapshot(player, unit, player.IsVisible(unit)))
                .ToArray();
        }

        private MentorNearbyUnitSnapshot ToSnapshot(Player player, Unit unit, bool isVisible)
        {
            UnitLock targetLock = player.GetLockByUnit(unit);
            bool validAttackTarget =
                unit.IsAttackable == ErrorCodes.NoError && !unit.IsInvulnerable;
            return new MentorNearbyUnitSnapshot(
                unit.Eid,
                unit.Definition,
                _textCatalog.DisplayName(unit.ED.Name),
                Math.Round(player.GetDistance(unit), 1),
                unit.IsLockable,
                player.IsInLockingRange(unit),
                validAttackTarget,
                targetLock?.State.ToString() ?? "none",
                targetLock?.Primary ?? false,
                isVisible,
                unit.CurrentPosition);
        }

        private static bool IsNpcOrTutorialTarget(Unit unit)
        {
            return unit is Npc || unit is PunchBag;
        }
    }
}
