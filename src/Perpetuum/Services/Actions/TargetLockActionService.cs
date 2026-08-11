using System;
using Perpetuum.Players;
using Perpetuum.Zones;
using Perpetuum.Zones.Locking.Locks;

namespace Perpetuum.Services.Actions
{
    public sealed class UnitTargetLockAction
    {
        public UnitTargetLockAction(long targetEid, bool primary)
        {
            TargetEid = targetEid;
            Primary = primary;
        }

        public long TargetEid { get; }
        public bool Primary { get; }
    }

    public sealed class TerrainTargetLockAction
    {
        public TerrainTargetLockAction(int x, int y, bool primary)
        {
            X = x;
            Y = y;
            Primary = primary;
        }

        public int X { get; }
        public int Y { get; }
        public bool Primary { get; }
    }

    public interface ITargetLockActionService
    {
        void LockUnit(GameActionContext context, UnitTargetLockAction action);
        void LockTerrain(GameActionContext context, TerrainTargetLockAction action);
        void SetPrimary(GameActionContext context, long lockId);
        void Cancel(GameActionContext context, long lockId);
    }

    /// <summary>
    /// Submits targeting requests to the actor's real lock handler. Lock count,
    /// range, lockability, target state, and locking time remain enforced by the
    /// normal asynchronous lock update.
    /// </summary>
    public sealed class TargetLockActionService : ITargetLockActionService
    {
        private readonly IGameActionAudit _audit;

        public TargetLockActionService(IGameActionAudit audit)
        {
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void LockUnit(GameActionContext context, UnitTargetLockAction action)
        {
            _audit.Execute(context, "lockUnit", () =>
            {
                if (action == null)
                    throw new ArgumentNullException(nameof(action));

                var player = GetPlayer(context);
                var target = player.Zone?.GetUnit(action.TargetEid);
                if (target == null || !player.IsVisible(target))
                    return;

                player.AddLock(target, action.Primary);
            });
        }

        public void LockTerrain(GameActionContext context, TerrainTargetLockAction action)
        {
            _audit.Execute(context, "lockTerrain", () =>
            {
                if (action == null)
                    throw new ArgumentNullException(nameof(action));

                var player = GetPlayer(context);
                var zone = player.Zone.ThrowIfNull(ErrorCodes.PlayerNotFound);
                double z = zone.GetZ(action.X, action.Y);
                var location = new Position(action.X + 0.5, action.Y + 0.5, z);
                player.AddLock(new TerrainLock(player, location) { Primary = action.Primary });
            });
        }

        public void SetPrimary(GameActionContext context, long lockId)
        {
            _audit.Execute(context, "setPrimaryLock", () => GetPlayer(context).SetPrimaryLock(lockId));
        }

        public void Cancel(GameActionContext context, long lockId)
        {
            _audit.Execute(context, "removeLock", () => GetPlayer(context).CancelLock(lockId));
        }

        private static Player GetPlayer(GameActionContext context)
        {
            return context.Actor.GetPlayerRobotFromZone().ThrowIfNull(ErrorCodes.PlayerNotFound);
        }
    }
}
