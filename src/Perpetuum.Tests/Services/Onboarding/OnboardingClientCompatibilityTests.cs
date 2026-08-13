using System.Collections.Generic;
using Perpetuum.Services.Onboarding;
using Xunit;

namespace Perpetuum.Tests.Services.Onboarding
{
    public class OnboardingClientCompatibilityTests
    {
        [Fact]
        public void ReplacementStateSilentlyCompletesLegacyClientBootstrap()
        {
            var settings = new Dictionary<string, object>();

            bool changed = OnboardingClientCompatibility.ApplyReplacementOnboardingState(settings);

            Assert.True(changed);
            Assert.Equal(1, settings["welcomeScreen"]);
            var triggers = Assert.IsAssignableFrom<IDictionary<string, object>>(
                settings["tutorialTriggers"]);
            Assert.True(triggers.Count >= 100);
            Assert.All(triggers.Values, value => Assert.Equal(1, value));
            Assert.Contains("trigger_deploy", triggers.Keys);
            Assert.Contains("trigger_openmissions", triggers.Keys);
            Assert.Contains("trigger_openrobotcargo", triggers.Keys);
            Assert.Contains("trigger_openfactory", triggers.Keys);
        }

        [Fact]
        public void ReplacementStatePreservesUnrelatedSettingsAndIsIdempotent()
        {
            var settings = new Dictionary<string, object>
            {
                ["windowLayout"] = "player-choice"
            };

            Assert.True(OnboardingClientCompatibility.ApplyReplacementOnboardingState(settings));
            Assert.False(OnboardingClientCompatibility.ApplyReplacementOnboardingState(settings));
            Assert.Equal("player-choice", settings["windowLayout"]);
        }
    }
}
