using System;
using System.Collections.Generic;
using Perpetuum.Services.Channels;
using Perpetuum.Services.Sessions;

namespace Perpetuum.Services.Mentoring
{
    public sealed class MentorChannelGateway :
        IMentorResponseSink,
        IMentorConversationSink
    {
        private const string SenderLabel = "[Syndicate] ARIA";

        private readonly MentorOptions _options;
        private readonly ISessionManager _sessionManager;

        public MentorChannelGateway(
            MentorOptions options,
            ISessionManager sessionManager)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        public void Send(MentorResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            ISession session = _sessionManager.GetByCharacter(response.CharacterId);
            if (session == null || session.Character == null || session.Character.Id != response.CharacterId)
                return;

            SendChatMessage(
                session,
                response.CharacterId,
                $"{SenderLabel}: {response.Text}");
        }

        public void EchoQuestion(int characterId, string channel, string message)
        {
            if (!string.Equals(channel, _options.ChannelName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ISession session = _sessionManager.GetByCharacter(characterId);
            if (session == null || session.Character == null || session.Character.Id != characterId)
                return;

            SendChatMessage(session, characterId, message);
        }

        private Message CreateNotification(ChannelNotify notify, IDictionary<string, object> data)
        {
            var notification = new Dictionary<string, object>
            {
                { k.channel, _options.ChannelName },
                { k.command, (int)notify },
                { k.data, data }
            };

            return new Message(Commands.ChannelNotification, notification);
        }

        private void SendChatMessage(ISession session, int senderId, string message)
        {
            var messageData = new Dictionary<string, object>
            {
                { k.sender, senderId },
                { k.message, message ?? string.Empty }
            };

            session.SendMessage(CreateNotification(ChannelNotify.Message, messageData));
        }
    }
}
