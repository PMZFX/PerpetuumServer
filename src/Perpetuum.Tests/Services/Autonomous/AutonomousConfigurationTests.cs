using System;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousConfigurationTests
    {
        [Fact]
        public void DefaultsAreSafeAndDisabled()
        {
            var configuration = new AutonomousConfiguration();

            configuration.Validate();

            Assert.False(configuration.Enabled);
            Assert.Equal(500, configuration.TickIntervalMilliseconds);
            Assert.Equal(3, configuration.MaxConsecutiveFailures);
            Assert.Empty(configuration.Actors);
        }

        [Fact]
        public void DuplicateCharacterIdsAreRejected()
        {
            var configuration = new AutonomousConfiguration
            {
                Actors =
                {
                    new AutonomousActorDefinition { CharacterId = 42 },
                    new AutonomousActorDefinition { CharacterId = 42 }
                }
            };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
        }

        [Fact]
        public void InvalidPatrolOptionsAreRejected()
        {
            var configuration = new AutonomousConfiguration
            {
                Enabled = true,
                Actors =
                {
                    new AutonomousActorDefinition
                    {
                        CharacterId = 7,
                        Behavior = "patrol",
                        Patrol = new AutonomousPatrolOptions { Radius = 2 }
                    }
                }
            };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
        }

        [Fact]
        public void InvalidThreatOptionsAreRejected()
        {
            var configuration = new AutonomousConfiguration
            {
                Actors =
                {
                    new AutonomousActorDefinition
                    {
                        CharacterId = 8,
                        Behavior = "patrol",
                        Patrol = new AutonomousPatrolOptions
                        {
                            Threat = new AutonomousThreatOptions { ResponseRange = 500 }
                        }
                    }
                }
            };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
        }

        [Theory]
        [InlineData(99)]
        [InlineData(60001)]
        public void UnsafeTickIntervalsAreRejected(int interval)
        {
            var configuration = new AutonomousConfiguration { TickIntervalMilliseconds = interval };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
        }
    }
}
