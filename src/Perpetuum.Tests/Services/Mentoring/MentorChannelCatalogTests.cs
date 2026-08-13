using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Services.Channels;
using Perpetuum.Services.Mentoring;
using Xunit;

namespace Perpetuum.Tests.Services.Mentoring
{
    public class MentorChannelCatalogTests
    {
        [Fact]
        public void ChannelDescriptionMatchesTheStockClientListProtocol()
        {
            Character.CharacterFactory = CreateCharacter;
            var character = CreateCharacter(7);
            var catalog = new MentorChannelCatalog(MentorOptions.PhaseOneDefaults);

            Channel channel = catalog.GetChannel(character);
            IDictionary<string, object> data = channel.ToDictionary(character, true);

            Assert.Equal("Mentor", data[k.name]);
            Assert.Equal((int)ChannelType.Highlighted, data[k.type]);
            Assert.Equal(false, data[k.password]);
            Assert.Equal(1, data[k.count]);
            Assert.Equal(MentorChannelCatalog.Topic, data[k.topic]);

            var members = Assert.IsAssignableFrom<IDictionary<string, object>>(data[k.members]);
            IDictionary<string, object> member = Assert.IsAssignableFrom<IDictionary<string, object>>(members["m0"]);
            Assert.Equal(7, member[k.memberID]);
            Assert.Equal((int)ChannelMemberRole.Undefined, member[k.role]);
        }

        private static Character CreateCharacter(int id)
        {
            return new Character(
                id,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }
    }
}
