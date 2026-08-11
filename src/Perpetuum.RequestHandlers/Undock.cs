using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers
{
    public class Undock : IRequestHandler
    {
        private readonly IUndockActionService _undockActionService;

        public Undock(IUndockActionService undockActionService)
        {
            _undockActionService = undockActionService;
        }

        public void HandleRequest(IRequest request)
        {
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            _undockActionService.Execute(context);
        }
    }
}
