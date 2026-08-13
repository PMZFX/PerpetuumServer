using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers
{
    public class SelectActiveRobot : IRequestHandler
    {
        private readonly ISelectActiveRobotActionService _selectActiveRobot;

        public SelectActiveRobot(ISelectActiveRobotActionService selectActiveRobot)
        {
            _selectActiveRobot = selectActiveRobot;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new SelectActiveRobotAction(
                request.Data.GetOrDefault<long>(k.containerEID),
                request.Data.GetOrDefault<long>(k.robotEID));
            _selectActiveRobot.Execute(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                action);
            Message.Builder.FromRequest(request).WithOk().Send();
        }
    }
}
