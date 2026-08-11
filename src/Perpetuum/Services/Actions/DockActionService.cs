using Perpetuum.Data;
using Perpetuum.Zones;

namespace Perpetuum.Services.Actions
{
    public sealed class DockAction
    {
        public DockAction(long dockingBaseEid)
        {
            DockingBaseEid = dockingBaseEid;
        }

        public long DockingBaseEid { get; }
    }

    public interface IDockActionService
    {
        void Execute(GameActionContext context, DockAction action);
    }

    public sealed class DockActionService : IDockActionService
    {
        private readonly IZoneManager _zoneManager;
        private readonly IGameActionAudit _audit;

        public DockActionService(IZoneManager zoneManager, IGameActionAudit audit)
        {
            _zoneManager = zoneManager;
            _audit = audit;
        }

        public void Execute(GameActionContext context, DockAction action)
        {
            _audit.Execute(context, "dock", () => ExecuteCore(context, action));
        }

        private void ExecuteCore(GameActionContext context, DockAction action)
        {
            if (action == null)
                throw new System.ArgumentNullException(nameof(action));

            using (var scope = Db.CreateTransaction())
            {
                var player = _zoneManager.GetPlayer(context.Actor)
                    .ThrowIfNull(ErrorCodes.PlayerNotFound);
                player.CheckDockingConditionsAndThrow(action.DockingBaseEid);

                scope.Complete();
            }
        }
    }
}
