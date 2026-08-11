using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Zone
{
    public class Dock : IRequestHandler<IZoneRequest>
    {
        private readonly IDockActionService _dockActionService;

        public Dock(IDockActionService dockActionService)
        {
            _dockActionService = dockActionService;
        }

        public void HandleRequest(IZoneRequest request)
        {
            var action = new DockAction(request.Data.GetOrDefault<long>(k.baseEID));
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            _dockActionService.Execute(context, action);
        }
    }
}
