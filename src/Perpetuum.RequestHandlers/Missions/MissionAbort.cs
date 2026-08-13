using System;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Missions
{
    public class MissionAbort : IRequestHandler
    {
        private readonly IMissionActionService _missionActions;

        public MissionAbort(IMissionActionService missionActions)
        {
            _missionActions = missionActions;
        }

        public void HandleRequest(IRequest request)
        {
            string guidString = request.Data.GetOrDefault<string>(k.guid);
            Guid.TryParse(guidString, out Guid missionGuid).ThrowIfFalse(ErrorCodes.SyntaxError);
            _missionActions.Abort(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                new MissionGuidAction(missionGuid));
        }
    }
}
