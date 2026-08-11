using Perpetuum.Accounting.Characters;
using Perpetuum.Items;

namespace Perpetuum.Services.Actions
{
    public interface IUndockActionService
    {
        void Execute(GameActionContext context);
    }

    public sealed class UndockActionService : IUndockActionService
    {
        private readonly IGameActionAudit _audit;

        public UndockActionService(IGameActionAudit audit)
        {
            _audit = audit;
        }

        public void Execute(GameActionContext context)
        {
            _audit.Execute(context, "undock", () => ExecuteCore(context.Actor));
        }

        private static void ExecuteCore(Character character)
        {
            if (!character.AccessLevel.IsAdminOrGm())
            {
                CheckUndockConditionsAndThrowIfFailed(character);
            }

            var dockingBase = character.GetCurrentDockingBase();
            if (dockingBase == null)
                throw new PerpetuumException(ErrorCodes.DockingBaseNotFound);

            if (dockingBase.Zone == null)
                throw new PerpetuumException(ErrorCodes.ItemNotFound);

            dockingBase.Zone.Enter(character, Commands.Undock);
        }

        private static void CheckUndockConditionsAndThrowIfFailed(Character character)
        {
            character.CheckNextAvailableUndockTimeAndThrowIfFailed();

            var activeRobot = character.GetActiveRobot().ThrowIfNull(ErrorCodes.ARobotMustBeSelected);
            activeRobot.CheckEnablerExtensionsAndThrowIfFailed(character);
            activeRobot.CheckEnergySystemAndThrowIfFailed();
            var container = activeRobot.GetContainer().ThrowIfNull(ErrorCodes.WTFErrorMedicalAttentionSuggested);
            container.CheckCapacityAndThrowIfFailed();
        }
    }
}
