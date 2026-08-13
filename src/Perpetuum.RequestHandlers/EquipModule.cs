using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers
{
    public class EquipModule : IRequestHandler
    {
        private readonly IRobotFittingActionService _fitting;

        public EquipModule(IRobotFittingActionService fitting)
        {
            _fitting = fitting;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new EquipModuleAction(
                request.Data.GetOrDefault<long>(k.containerEID),
                request.Data.GetOrDefault<long>(k.robotEID),
                request.Data.GetOrDefault<long>(k.moduleEID),
                request.Data.GetOrDefault<string>(k.robotComponent).ToEnum<RobotComponentType>(),
                request.Data.GetOrDefault<int>(k.slot));
            RobotFittingResult fittingResult = _fitting.Equip(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                action);

            var result = new Dictionary<string, object>
            {
                {k.robot, fittingResult.Robot.ToDictionary()},
                {k.container, fittingResult.Container.ToDictionary()}
            };
            Message.Builder.FromRequest(request).WithData(result).WrapToResult().Send();
        }
    }
}
