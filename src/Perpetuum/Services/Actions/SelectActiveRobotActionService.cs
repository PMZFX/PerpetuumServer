using System;
using Perpetuum.Containers;
using Perpetuum.Containers.SystemContainers;
using Perpetuum.Data;
using Perpetuum.Groups.Corporations;
using Perpetuum.Items;
using Perpetuum.Robots;

namespace Perpetuum.Services.Actions
{
    public sealed class SelectActiveRobotAction
    {
        public SelectActiveRobotAction(long containerEid, long robotEid)
        {
            ContainerEid = containerEid;
            RobotEid = robotEid;
        }

        public long ContainerEid { get; }
        public long RobotEid { get; }
    }

    public interface ISelectActiveRobotActionService
    {
        Robot Execute(GameActionContext context, SelectActiveRobotAction action);
    }

    public sealed class SelectActiveRobotActionService : ISelectActiveRobotActionService
    {
        private readonly RobotHelper _robotHelper;
        private readonly IGameActionAudit _audit;

        public SelectActiveRobotActionService(RobotHelper robotHelper, IGameActionAudit audit)
        {
            _robotHelper = robotHelper ?? throw new ArgumentNullException(nameof(robotHelper));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public Robot Execute(GameActionContext context, SelectActiveRobotAction action)
        {
            return _audit.Execute(context, "selectActiveRobot", () => ExecuteCore(context, action));
        }

        private Robot ExecuteCore(GameActionContext context, SelectActiveRobotAction action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            using (var scope = Db.CreateTransaction())
            {
                context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
                context.Actor.IsRobotSelectedForOtherCharacter(action.RobotEid)
                    .ThrowIfTrue(ErrorCodes.UnknownError);

                Container container = Container.GetOrThrow(action.ContainerEid);
                container.ThrowIfType<CorporateHangar>(ErrorCodes.AccessDenied);
                container.ThrowIfType<CorporateHangarFolder>(ErrorCodes.AccessDenied);
                container.ThrowIfType<RobotInventory>(ErrorCodes.AccessDenied);
                container.ThrowIfType<DefaultSystemContainer>(ErrorCodes.AccessDenied);
                container.CheckAccessAndThrowIfFailed(context.Actor, ContainerAccess.List);

                Robot robot = _robotHelper.LoadRobotForCharacter(
                    action.RobotEid,
                    context.Actor,
                    true);
                robot.Parent.ThrowIfNotEqual(action.ContainerEid, ErrorCodes.ParentError);
                robot.IsRepackaged.ThrowIfTrue(ErrorCodes.ItemHasToBeUnpacked);
                robot.CheckEnablerExtensionsAndThrowIfFailed(context.Actor);

                context.Actor.SetActiveRobot(robot);
                robot.Save();
                scope.Complete();
                return robot;
            }
        }
    }
}
