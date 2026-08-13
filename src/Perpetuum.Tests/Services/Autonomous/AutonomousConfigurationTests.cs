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

        [Fact]
        public void MarketProcurementRequiresPriceCapAndReserve()
        {
            var options = new AutonomousMiningOptions
            {
                Resupply = new AutonomousMiningResupplyOptions
                {
                    BuyFromMarket = true,
                    MaximumUnitPrice = 0
                }
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(12));

            options.Resupply.MaximumUnitPrice = 100;
            options.Resupply.TileProbeReserve = 0;
            options.Resupply.MiningChargeReserve = 0;
            Assert.Throws<InvalidOperationException>(() => options.Validate(12));
        }

        [Fact]
        public void TraderRequiresExplicitRegionalMarketsAndCommodities()
        {
            var definition = new AutonomousActorDefinition
            {
                CharacterId = 20,
                Behavior = "trader"
            };
            var configuration = new AutonomousConfiguration { Actors = {definition} };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());

            definition.Trader.MarketBaseEids.AddRange(new long[] {100, 200});
            definition.Trader.Commodities.Add("titan");
            configuration.Validate();
        }

        [Fact]
        public void TraderRejectsDuplicateMarketsAndUnsafeBudgetPolicy()
        {
            var options = new AutonomousTraderOptions
            {
                MarketBaseEids = new System.Collections.Generic.List<long> {100, 100},
                Commodities = new System.Collections.Generic.List<string> {"titan"}
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(20));

            options.MarketBaseEids[1] = 200;
            options.WalletReserve = -1;
            Assert.Throws<InvalidOperationException>(() => options.Validate(20));
        }

        [Fact]
        public void ManufacturerRequiresExplicitPersistentGoalAndFacility()
        {
            var definition = new AutonomousActorDefinition
            {
                CharacterId = 21,
                Behavior = "manufacturer"
            };
            var configuration = new AutonomousConfiguration { Actors = {definition} };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());

            definition.Manufacturer.TargetDefinition = 100;
            definition.Manufacturer.MillFacilityEid = 200;
            configuration.Validate();
        }

        [Fact]
        public void ManufacturerRejectsUnsafeQuantityAndRetry()
        {
            var options = new AutonomousManufacturerOptions
            {
                TargetDefinition = 100,
                MillFacilityEid = 200,
                Quantity = 0
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(21));
            options.Quantity = 1;
            options.RetrySeconds = 1;
            Assert.Throws<InvalidOperationException>(() => options.Validate(21));

            options.RetrySeconds = 30;
            options.ResearchFacilityEid = -1;
            Assert.Throws<InvalidOperationException>(() => options.Validate(21));

            options.ResearchFacilityEid = 0;
            options.PrototypeFacilityEid = -1;
            Assert.Throws<InvalidOperationException>(() => options.Validate(21));
        }

        [Fact]
        public void ManufacturerProcurementRequiresBoundedPriceQuantityAndReserve()
        {
            var options = new AutonomousManufacturerProcurementOptions
            {
                Enabled = true,
                MaximumPurchaseQuantity = 100,
                MaximumUnitPrice = 0,
                WalletReserve = 10000
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(21));

            options.MaximumUnitPrice = 25;
            options.MaximumPurchaseQuantity = 0;
            Assert.Throws<InvalidOperationException>(() => options.Validate(21));

            options.MaximumPurchaseQuantity = 100;
            options.WalletReserve = -1;
            Assert.Throws<InvalidOperationException>(() => options.Validate(21));

            options.WalletReserve = 0;
            options.Validate(21);
        }

        [Theory]
        [InlineData(1, 48)]
        [InlineData(18, 129)]
        public void InvalidMiningSurveyBoundsAreRejected(int stepDistance, int maxSites)
        {
            var options = new AutonomousMiningOptions
            {
                SurveyStepDistance = stepDistance,
                MaxSurveySites = maxSites
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(12));
        }

        [Fact]
        public void WideMiningSurveyIsValidWhenEveryAdjacentLegIsNavigable()
        {
            var options = new AutonomousMiningOptions
            {
                SurveyStepDistance = 24,
                MaxSurveySites = 128
            };

            options.Validate(12);
            Assert.True(
                AutonomousMiningSurveyPolicy.GetMaximumLegDistance(options.SurveyStepDistance) <
                AutonomousNavigationService.MaximumStartDistance);
        }
    }
}
