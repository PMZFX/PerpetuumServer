using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Items;
using Perpetuum.Modules;
using Perpetuum.Robots;

namespace Perpetuum.Services.Actions
{
    public sealed class EquipModuleAction
    {
        public EquipModuleAction(
            long containerEid,
            long robotEid,
            long moduleEid,
            RobotComponentType componentType,
            int slot)
        {
            ContainerEid = containerEid;
            RobotEid = robotEid;
            ModuleEid = moduleEid;
            ComponentType = componentType;
            Slot = slot;
        }

        public long ContainerEid { get; }
        public long RobotEid { get; }
        public long ModuleEid { get; }
        public RobotComponentType ComponentType { get; }
        public int Slot { get; }
    }

    public sealed class RemoveModuleAction
    {
        public RemoveModuleAction(long containerEid, long robotEid, long moduleEid)
        {
            ContainerEid = containerEid;
            RobotEid = robotEid;
            ModuleEid = moduleEid;
        }

        public long ContainerEid { get; }
        public long RobotEid { get; }
        public long ModuleEid { get; }
    }

    public sealed class RobotFittingResult
    {
        public RobotFittingResult(Robot robot, Container container)
        {
            Robot = robot ?? throw new ArgumentNullException(nameof(robot));
            Container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public Robot Robot { get; }
        public Container Container { get; }
    }

    public interface IRobotFittingActionService
    {
        RobotFittingResult Equip(GameActionContext context, EquipModuleAction action);
        RobotFittingResult Remove(GameActionContext context, RemoveModuleAction action);
    }

    public sealed class RobotFittingActionService : IRobotFittingActionService
    {
        private readonly RobotHelper _robotHelper;
        private readonly IGameActionAudit _audit;

        public RobotFittingActionService(RobotHelper robotHelper, IGameActionAudit audit)
        {
            _robotHelper = robotHelper ?? throw new ArgumentNullException(nameof(robotHelper));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public RobotFittingResult Equip(GameActionContext context, EquipModuleAction action)
        {
            return _audit.Execute(context, "equipModule", () => EquipCore(context, action));
        }

        public RobotFittingResult Remove(GameActionContext context, RemoveModuleAction action)
        {
            return _audit.Execute(context, "removeModule", () => RemoveCore(context, action));
        }

        private RobotFittingResult EquipCore(GameActionContext context, EquipModuleAction action)
        {
            Validate(context, action);
            using (var scope = Db.CreateTransaction())
            {
                context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
                Container container = LoadContainer(context, action.ContainerEid);
                Robot robot = LoadRobot(action.RobotEid);
                robot.Initialize(context.Actor);
                RobotComponent component = robot.GetRobotComponentOrThrow(action.ComponentType);
                component.MakeSlotFree(action.Slot, container);
                var module = (Module)container.GetItemOrThrow(action.ModuleEid).Unstack(1);
                component.EquipModuleOrThrow(module, action.Slot);

                robot.Initialize(context.Actor);
                robot.Save();
                container.Save();
                scope.Complete();
                return new RobotFittingResult(robot, container);
            }
        }

        private RobotFittingResult RemoveCore(GameActionContext context, RemoveModuleAction action)
        {
            Validate(context, action);
            using (var scope = Db.CreateTransaction())
            {
                context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
                Container container = LoadContainer(context, action.ContainerEid);
                container.EnlistTransaction();
                Robot robot = LoadRobot(action.RobotEid);
                robot.EnlistTransaction();
                Module module = robot.GetModule(action.ModuleEid).ThrowIfNull(ErrorCodes.ModuleNotFound);
                module.Owner = context.Actor.Eid;
                module.Unequip(container);

                robot.Initialize(context.Actor);
                robot.Save();
                container.Save();
                scope.Complete();
                return new RobotFittingResult(robot, container);
            }
        }

        private static Container LoadContainer(GameActionContext context, long containerEid)
        {
            Container container = Container.GetWithItems(containerEid, context.Actor)
                .ThrowIfNull(ErrorCodes.ContainerNotFound);
            container.ThrowIfType<VolumeWrapperContainer>(ErrorCodes.AccessDenied);
            return container;
        }

        private Robot LoadRobot(long robotEid)
        {
            Robot robot = _robotHelper.LoadRobotOrThrow(robotEid);
            robot.IsSingleAndUnpacked.ThrowIfFalse(ErrorCodes.RobotMustbeSingleAndNonRepacked);
            return robot;
        }

        private static void Validate(GameActionContext context, object action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (action == null)
                throw new ArgumentNullException(nameof(action));
        }
    }
}
