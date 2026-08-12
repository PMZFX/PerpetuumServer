using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;
using Perpetuum.Zones;

namespace Perpetuum.RequestHandlers.Zone
{
    public class TeleportUse : IRequestHandler<IZoneRequest>
    {
        private readonly ITeleportActionService _teleport;

        public TeleportUse(ITeleportActionService teleport)
        {
            _teleport = teleport;
        }

        public void HandleRequest(IZoneRequest request)
        {
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            var teleportEid = request.Data.GetOrDefault<long>(k.eid);
            var descriptionId = request.Data.GetOrDefault<int>(k.ID);
            var rewardLevel = request.Data.GetOrDefault<int>(k.rewardLevel);
            _teleport.Execute(context, new TeleportAction(teleportEid, descriptionId, rewardLevel));
        }
    }
}
