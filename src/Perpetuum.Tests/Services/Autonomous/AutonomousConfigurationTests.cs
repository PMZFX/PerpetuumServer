using System;
using Perpetuum.Services.Autonomous;
using Perpetuum.Zones.Terrains.Materials;
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

        [Fact]
        public void DefensiveCombatIsOptIn()
        {
            var options = new AutonomousDefenseOptions();

            options.Validate(9);

            Assert.False(options.Enabled);
            Assert.Equal(60.0, options.ResponseRange);
            Assert.Equal(8, options.LockTimeoutSeconds);
            Assert.Equal(20, options.MaxEngagementSeconds);
        }

        [Fact]
        public void EngagementLimitMustExceedLockTimeout()
        {
            var configuration = new AutonomousConfiguration
            {
                Actors =
                {
                    new AutonomousActorDefinition
                    {
                        CharacterId = 9,
                        Behavior = "patrol",
                        Patrol = new AutonomousPatrolOptions
                        {
                            Defense = new AutonomousDefenseOptions
                            {
                                LockTimeoutSeconds = 10,
                                MaxEngagementSeconds = 10
                            }
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

        [Fact]
        public void NegativeRecoveryRevisionIsRejected()
        {
            var configuration = new AutonomousConfiguration
            {
                Actors =
                {
                    new AutonomousActorDefinition { CharacterId = 10, RecoveryRevision = -1 }
                }
            };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
        }

        [Fact]
        public void MiningMaterialIsHumanReadableAndValidated()
        {
            var options = new AutonomousMiningOptions { Material = "titan" };

            options.Validate(12);

            Assert.Equal(MaterialType.Titan, options.GetMaterialType());
            options.Material = "not-a-mineral";
            Assert.Throws<InvalidOperationException>(() => options.Validate(12));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(1.01)]
        public void InvalidMiningCargoThresholdIsRejected(double threshold)
        {
            var configuration = new AutonomousConfiguration
            {
                Actors =
                {
                    new AutonomousActorDefinition
                    {
                        CharacterId = 12,
                        Behavior = "mining",
                        Mining = new AutonomousMiningOptions { CargoFillRatio = threshold }
                    }
                }
            };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
        }

        [Fact]
        public void InvalidAutonomousMarketSettingsAreRejectedEvenWhileDisabled()
        {
            var options = new AutonomousMiningOptions
            {
                Market = new AutonomousMarketOptions { MinimumUnitPrice = 0 }
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(12));

            options.Market.MinimumUnitPrice = 1;
            options.Market.OrderDurationHours = 0;
            Assert.Throws<InvalidOperationException>(() => options.Validate(12));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(1.01)]
        public void InvalidMiningResupplyThresholdIsRejected(double threshold)
        {
            var options = new AutonomousMiningOptions
            {
                Resupply = new AutonomousMiningResupplyOptions { ReloadBelowRatio = threshold }
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(12));
        }

        [Theory]
        [InlineData(1, 16)]
        [InlineData(18, 17)]
        public void InvalidMiningSurveyBoundsAreRejected(int stepDistance, int maxSites)
        {
            var options = new AutonomousMiningOptions
            {
                SurveyStepDistance = stepDistance,
                MaxSurveySites = maxSites
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(12));
        }
    }
}
