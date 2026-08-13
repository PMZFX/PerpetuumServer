using Perpetuum.Host.Requests;
using Perpetuum.Services.Channels;
using Perpetuum.Services.Mentoring;

namespace Perpetuum.RequestHandlers.Channels
{
    public class ChannelList : IRequestHandler
    {
        private readonly IChannelManager _channelManager;
        private readonly IMentorChannelCatalog _mentorChannelCatalog;

        public ChannelList(
            IChannelManager channelManager,
            IMentorChannelCatalog mentorChannelCatalog)
        {
            _channelManager = channelManager;
            _mentorChannelCatalog = mentorChannelCatalog;
        }

        public void HandleRequest(IRequest request)
        {
            var character = request.Session.Character;
            var channels = _channelManager.GetPublicChannels()
                .ConcatSingle(_mentorChannelCatalog.GetChannel(character));
            var result = channels.ToDictionary("c", c => c.ToDictionary(character, false));
            Message.Builder.FromRequest(request).WithData(result).WithEmpty().Send();
        }
    }
}
