using System.Collections.Generic;
using Perpetuum.Services.Mentoring;
using Xunit;

namespace Perpetuum.Tests.Services.Mentoring
{
    public class MentorTextCatalogTests
    {
        [Fact]
        public void UsesClientDisplayNameAndDescriptionWhenAvailable()
        {
            var catalog = new MentorTextCatalog(new StaticDictionary(new Dictionary<string, object>
            {
                ["def_noob_bot"] = "Arkhe",
                ["def_noob_bot_desc"] = "Universal service and training robot."
            }));

            Assert.Equal("Arkhe", catalog.DisplayName("def_noob_bot"));
            Assert.Equal(
                "Universal service and training robot.",
                catalog.Description("def_noob_bot_desc"));
        }

        [Fact]
        public void FallsBackToReadableDefinitionName()
        {
            var catalog = new MentorTextCatalog(new StaticDictionary(null));

            Assert.Equal("Noob", catalog.DisplayName("def_noob_bot"));
            Assert.Null(catalog.Description("def_noob_bot_desc"));
        }

        private sealed class StaticDictionary : ICustomDictionary
        {
            private readonly Dictionary<string, object> _values;

            public StaticDictionary(Dictionary<string, object> values)
            {
                _values = values;
            }

            public Dictionary<string, object> GetDictionary(int language)
            {
                return _values;
            }
        }
    }
}
