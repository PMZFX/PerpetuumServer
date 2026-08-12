using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers
{
    public class EquipAmmo : IRequestHandler
    {
        private readonly IEquipAmmoActionService _equipAmmoActionService;

        public EquipAmmo(IEquipAmmoActionService equipAmmoActionService)
        {
            _equipAmmoActionService = equipAmmoActionService;
        }

        public void HandleRequest(IRequest request)
        {
            var character = request.Session.Character;
            var action = new EquipAmmoAction(
                request.Data.GetOrDefault<long>(k.containerEID),
                request.Data.GetOrDefault<long>(k.robotEID),
                request.Data.GetOrDefault<long>(k.moduleEID),
                request.Data.GetOrDefault<long>(k.ammoEID));
            var context = new GameActionContext(character, GameActionSource.Client);
            EquipAmmoResult actionResult = _equipAmmoActionService.Execute(context, action);

            var result = new Dictionary<string, object>
            {
                {k.robot, actionResult.Robot.ToDictionary()},
                {k.container, actionResult.Container.ToDictionary()}
            };
            Message.Builder.FromRequest(request).WithData(result).WrapToResult().Send();
        }
    }
}
