using System;
using Perpetuum.Accounting.Characters;
using Perpetuum.Services.Channels;

namespace Perpetuum.Services.Mentoring
{
    public sealed class MentorChannelCatalog : IMentorChannelCatalog
    {
        public const string Topic = "Automated, server-backed help. Mentor replies are visible only to you.";

        private readonly MentorOptions _options;

        public MentorChannelCatalog(MentorOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Channel GetChannel(Character character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            var member = new ChannelMember(character, ChannelMemberRole.Undefined);
            return new Channel(ChannelType.Highlighted, _options.ChannelName, null)
                .SetTopic(Topic)
                .SetMember(member);
        }
    }
}
