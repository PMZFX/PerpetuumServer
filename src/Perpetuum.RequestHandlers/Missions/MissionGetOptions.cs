using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Missions
{
    public class MissionGetOptions : IRequestHandler
    {
        private readonly IMissionActionService _missionActions;

        public MissionGetOptions(IMissionActionService missionActions)
        {
            _missionActions = missionActions;
        }

        public void HandleRequest(IRequest request)
        {
            long locationEid = request.Session.Character.IsDocked
                ? 0
                : request.Data.GetOrDefault<long>(k.eid);
            MissionOptionsResult result = _missionActions.ObserveOptions(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                new MissionLocationAction(locationEid));
            Message.Builder
                .WithData(result.Payload)
                .ToCharacter(request.Session.Character)
                .SetCommand(Commands.MissionGetOptions)
                .Send();
        }
    }
}
