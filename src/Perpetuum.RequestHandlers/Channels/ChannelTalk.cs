using Perpetuum.Host.Requests;
using Perpetuum.Services.Channels;
using Perpetuum.Services.Mentoring;

namespace Perpetuum.RequestHandlers.Channels
{
    public class ChannelTalk : IRequestHandler
    {
        private readonly IChannelManager _channelManager;
        private readonly IMentorChatIngress _mentorChatIngress;

        public ChannelTalk(IChannelManager channelManager, IMentorChatIngress mentorChatIngress)
        {
            _channelManager = channelManager;
            _mentorChatIngress = mentorChatIngress;
        }

        public void HandleRequest(IRequest request)
        {
            var channelName = request.Data.GetOrDefault<string>(k.channel);
            var message = request.Data.GetOrDefault<string>(k.message);

            var character = request.Session.Character;
            if (!_mentorChatIngress.TryHandle(
                    channelName,
                    character.Id,
                    request.Session.AccountId,
                    message))
            {
                _channelManager.Talk(channelName, character, message, request);
            }
            Message.Builder.FromRequest(request).WithOk().Send();
        }
    }
}
