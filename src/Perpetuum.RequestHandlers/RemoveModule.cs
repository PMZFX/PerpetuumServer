using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers
{
    public class RemoveModule : IRequestHandler
    {
        private readonly IRobotFittingActionService _fitting;

        public RemoveModule(IRobotFittingActionService fitting)
        {
            _fitting = fitting;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new RemoveModuleAction(
                request.Data.GetOrDefault<long>(k.containerEID),
                request.Data.GetOrDefault<long>(k.robotEID),
                request.Data.GetOrDefault<long>(k.moduleEID));
            RobotFittingResult fittingResult = _fitting.Remove(
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
