using System;
using Perpetuum.Host.Requests;
using Perpetuum.Services.MissionEngine;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Missions
{
    public class MissionStart : IRequestHandler
    {
        private readonly IMissionActionService _missionActions;

        public MissionStart(IMissionActionService missionActions)
        {
            _missionActions = missionActions;
        }

        public void HandleRequest(IRequest request)
        {
            long locationEid = request.Session.Character.IsDocked
                ? 0
                : request.Data.GetOrDefault<long>(k.location);
            MissionStartResult result = _missionActions.Start(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                new MissionStartAction(
                    (MissionCategory)request.Data.GetOrDefault<int>(k.missionCategory),
                    request.Data.GetOrDefault<int>(k.missionLevel),
                    locationEid));
            Message.Builder.FromRequest(request).WithData(result.Payload).Send();
        }
    }
}
