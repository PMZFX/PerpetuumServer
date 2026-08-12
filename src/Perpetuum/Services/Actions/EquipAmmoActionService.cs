using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Items;
using Perpetuum.Items.Ammos;
using Perpetuum.Modules;
using Perpetuum.Robots;

namespace Perpetuum.Services.Actions
{
    public sealed class EquipAmmoAction
    {
        public EquipAmmoAction(long containerEid, long robotEid, long moduleEid, long ammoEid)
        {
            ContainerEid = containerEid;
            RobotEid = robotEid;
            ModuleEid = moduleEid;
            AmmoEid = ammoEid;
        }

        public long ContainerEid { get; }
        public long RobotEid { get; }
        public long ModuleEid { get; }
        public long AmmoEid { get; }
    }

    public sealed class EquipAmmoResult
    {
        public EquipAmmoResult(Robot robot, Container container)
        {
            Robot = robot;
            Container = container;
        }

        public Robot Robot { get; }
        public Container Container { get; }
    }

    public interface IEquipAmmoActionService
    {
        EquipAmmoResult Execute(GameActionContext context, EquipAmmoAction action);
    }

    /// <summary>
    /// Shared docked ammunition action used by both the client request and
    /// autonomous actors. Existing ownership, fitting, ammo compatibility,
    /// capacity, container access, and transaction rules remain authoritative.
    /// </summary>
    public sealed class EquipAmmoActionService : IEquipAmmoActionService
    {
        private readonly RobotHelper _robotHelper;
        private readonly IGameActionAudit _audit;

        public EquipAmmoActionService(RobotHelper robotHelper, IGameActionAudit audit)
        {
            _robotHelper = robotHelper ?? throw new ArgumentNullException(nameof(robotHelper));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public EquipAmmoResult Execute(GameActionContext context, EquipAmmoAction action)
        {
            return _audit.Execute(context, "equipAmmo", () => ExecuteCore(context, action));
        }

        private EquipAmmoResult ExecuteCore(GameActionContext context, EquipAmmoAction action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            using (var scope = Db.CreateTransaction())
            {
                var character = context.Actor;
                character.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);

                Container container = Container.GetWithItems(action.ContainerEid, character);
                container.ThrowIfType<VolumeWrapperContainer>(ErrorCodes.AccessDenied);

                Robot robot = _robotHelper.LoadRobotOrThrow(action.RobotEid);
                robot.IsSingleAndUnpacked.ThrowIfFalse(ErrorCodes.RobotMustbeSingleAndNonRepacked);

                ActiveModule module = robot.GetModule(action.ModuleEid)
                    .ThrowIfNotType<ActiveModule>(ErrorCodes.ModuleNotFound);
                module.IsAmmoable.ThrowIfFalse(ErrorCodes.AmmoNotRequired);

                var ammo = (Ammo)container.GetItemOrThrow(action.AmmoEid);
                module.CheckLoadableAmmo(ammo.Definition).ThrowIfFalse(ErrorCodes.InvalidAmmoDefinition);
                module.UnequipAmmoToContainer(container);

                ammo = (Ammo)ammo.Unstack(module.AmmoCapacity);
                module.SetAmmo(ammo);

                robot.Initialize(character);
                module.Save();
                container.Save();
                scope.Complete();

                return new EquipAmmoResult(robot, container);
            }
        }
    }
}
