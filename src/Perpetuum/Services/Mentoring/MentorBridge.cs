using System;

namespace Perpetuum.Services.Mentoring
{
    public sealed class MentorBridge : IMentorChatIngress
    {
        private readonly MentorOptions _options;
        private readonly IMentorClock _clock;
        private readonly IMentorRequestDispatcher _dispatcher;
        private readonly IMentorConversationSink _conversationSink;

        public MentorBridge(
            MentorOptions options,
            IMentorClock clock,
            IMentorRequestDispatcher dispatcher,
            IMentorConversationSink conversationSink)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _conversationSink = conversationSink ??
                throw new ArgumentNullException(nameof(conversationSink));
        }

        public bool TryHandle(string channelName, int characterId, int accountId, string message)
        {
            if (!string.Equals(channelName, _options.ChannelName, StringComparison.OrdinalIgnoreCase))
                return false;

            _conversationSink.EchoQuestion(
                characterId,
                _options.ChannelName,
                message);
            _dispatcher.Submit(new MentorRequest(
                Guid.NewGuid(),
                characterId,
                accountId,
                _options.ChannelName,
                message,
                _clock.UtcNow));

            return true;
        }
    }

    public sealed class SystemMentorClock : IMentorClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
