using Perpetuum.Accounting.Characters;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Groups.Corporations;
using Perpetuum.Robots;

namespace Perpetuum.Services.Actions
{
    public sealed class RelocateItemsAction
    {
        public RelocateItemsAction(long sourceContainerEid, long targetContainerEid, long[] itemEids)
        {
            SourceContainerEid = sourceContainerEid;
            TargetContainerEid = targetContainerEid;
            ItemEids = itemEids == null ? null : (long[])itemEids.Clone();
        }

        public long SourceContainerEid { get; }
        public long TargetContainerEid { get; }
        public long[] ItemEids { get; }
    }

    public sealed class RelocateItemsResult
    {
        public static readonly RelocateItemsResult NoChange = new RelocateItemsResult();

        private RelocateItemsResult()
        {
        }

        public RelocateItemsResult(Container sourceContainer, Container targetContainer, bool canListTargetContents)
        {
            SourceContainer = sourceContainer;
            TargetContainer = targetContainer;
            CanListTargetContents = canListTargetContents;
            Changed = true;
        }

        public bool Changed { get; }
        public Container SourceContainer { get; }
        public Container TargetContainer { get; }
        public bool CanListTargetContents { get; }
    }

    public interface IRelocateItemsActionService
    {
        RelocateItemsResult Execute(GameActionContext context, RelocateItemsAction action);
    }

    public sealed class RelocateItemsActionService : IRelocateItemsActionService
    {
        private readonly IGameActionAudit _audit;

        public RelocateItemsActionService(IGameActionAudit audit)
        {
            _audit = audit;
        }

        public RelocateItemsResult Execute(GameActionContext context, RelocateItemsAction action)
        {
            return _audit.Execute(context, "relocateItems", () => ExecuteCore(context, action));
        }

        private static RelocateItemsResult ExecuteCore(GameActionContext context, RelocateItemsAction action)
        {
            if (action == null)
                throw new System.ArgumentNullException(nameof(action));

            if (action.SourceContainerEid == action.TargetContainerEid)
                return RelocateItemsResult.NoChange;

            using (var scope = Db.CreateTransaction())
            {
                var character = context.Actor;
                Container.GetContainersWithItems(
                    character,
                    action.SourceContainerEid,
                    action.TargetContainerEid,
                    out Container sourceContainer,
                    out Container targetContainer);

                if (targetContainer is RobotInventory)
                {
                    CorporateHangar.Contains(targetContainer).ThrowIfTrue(ErrorCodes.AccessDenied);
                }

                sourceContainer.RelocateItems(character, character, action.ItemEids, targetContainer);
                sourceContainer.Save();
                targetContainer.Save();

                bool canListTargetContents = CanListTargetContents(character, targetContainer);
                scope.Complete();

                return new RelocateItemsResult(sourceContainer, targetContainer, canListTargetContents);
            }
        }

        private static bool CanListTargetContents(Character character, Container targetContainer)
        {
            try
            {
                targetContainer.CheckAccessAndThrowIfFailed(character, ContainerAccess.List);
                return true;
            }
            catch (PerpetuumException ex) when (ex.error == ErrorCodes.InsufficientPrivileges)
            {
                return false;
            }
        }
    }
}
