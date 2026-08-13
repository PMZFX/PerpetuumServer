using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MissionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Actions
{
    public class MissionActionTests
    {
        [Fact]
        public void OptionProjectionPreservesConfiguredAndRandomAvailability()
        {
            var configured = new Dictionary<string, object>
            {
                {k.missionCategory, (int)MissionCategory.Transport},
                {k.missionLevel, 2},
                {"availableCount", 3},
                {"standingBlocked", false}
            };
            var random = new Dictionary<string, object>
            {
                {k.missionCategory, (int)MissionCategory.Mining},
                {k.missionLevel, 1},
                {k.oke, 1}
            };
            var payload = new Dictionary<string, object>
            {
                {k.locationID, 9},
                {k.options, new Dictionary<string, object> {{"c0", configured}}},
                {"randomMissions", new Dictionary<string, object> {{"r0", random}}}
            };

            IReadOnlyList<MissionAvailability> result = MissionOptionProjection.Parse(payload);

            Assert.Equal(2, result.Count);
            MissionAvailability config = result.Single(option => !option.Random);
            Assert.Equal(MissionCategory.Transport, config.Category);
            Assert.Equal(2, config.Level);
            Assert.Equal(3, config.AvailableCount);
            Assert.True(config.Available);
            Assert.False(config.StandingBlocked);
            MissionAvailability randomResult = result.Single(option => option.Random);
            Assert.Equal(MissionCategory.Mining, randomResult.Category);
            Assert.Equal(1, randomResult.AvailableCount);
        }

        [Fact]
        public void OptionProjectionDoesNotMutateLegacyPayload()
        {
            var options = new Dictionary<string, object>();
            var random = new Dictionary<string, object>();
            var payload = new Dictionary<string, object>
            {
                {k.locationID, 4},
                {k.options, options},
                {"randomMissions", random}
            };

            MissionOptionProjection.Parse(payload);

            Assert.Same(options, payload[k.options]);
            Assert.Same(random, payload["randomMissions"]);
            Assert.Equal(new[] {k.locationID, k.options, "randomMissions"}, payload.Keys);
        }

        [Fact]
        public void StartResultCarriesTypedGuidWithoutChangingClientPayload()
        {
            Guid guid = Guid.NewGuid();
            var payload = new Dictionary<string, object>
            {
                {k.mission, new Dictionary<string, object> {{k.guid, guid.ToString()}}}
            };

            var result = new MissionStartResult(guid, payload);

            Assert.Equal(guid, result.MissionGuid);
            Assert.Same(payload, result.Payload);
        }

        [Fact]
        public void ExtensionQuoteSeparatesAffordabilityFromExecution()
        {
            var quote = new ExtensionTrainingQuote(10, 1, 2, 200, 150, 0, 50, false);

            Assert.False(quote.CanAffordPoints);
            Assert.True(quote.CanAffordCredits);
            Assert.Equal(2, quote.NextLevel);
        }
    }
}
