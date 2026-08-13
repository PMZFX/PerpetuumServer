using System;
using System.Collections.Generic;
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

        [Fact]
        public void ProactivePveIsOptInAndConservativelyBounded()
        {
            var options = new AutonomousPveOptions();

            options.Validate(9);

            Assert.False(options.Enabled);
            Assert.Equal(75.0, options.AcquisitionRange);
            Assert.Equal(45.0, options.EngagementRange);
            Assert.Equal(0.45, options.RetreatArmorRatio);
            Assert.Equal(1, options.TargetCount);
            Assert.Equal(1, options.MaxLosses);
        }

        [Fact]
        public void PveEngagementRangeCannotExceedVisibleAcquisitionRange()
        {
            var options = new AutonomousPveOptions
            {
                AcquisitionRange = 40,
                EngagementRange = 41
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(9));
        }

        [Fact]
        public void EnabledPveRequiresARepairableConfiguredLoadout()
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
                            Pve = new AutonomousPveOptions {Enabled = true}
                        }
                    }
                }
            };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
        }

        [Fact]
        public void CombatMissionRequiresEnabledPveAndARepairableCombatFitting()
        {
            var actor = new AutonomousActorDefinition
            {
                CharacterId = 19,
                Behavior = "mission",
                Mission = new AutonomousMissionOptions
                {
                    Enabled = true,
                    Category = "Combat",
                    SourceBaseEid = 100
                }
            };
            var configuration = new AutonomousConfiguration {Actors = {actor}};

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());

            actor.Mission.Pve.Enabled = true;
            actor.Equipment = new AutonomousEquipmentOptions
            {
                Enabled = true,
                Robot = "arkhe_empty",
                RepairFacilityEid = 100,
                RepairBelowRatio = actor.Mission.Pve.RetreatArmorRatio,
                Slots = new List<AutonomousEquipmentSlotOptions>
                {
                    new AutonomousEquipmentSlotOptions
                    {
                        Module = "small_laser",
                        Ammo = "small_laser_crystal",
                        Component = "Head",
                        Slot = 0
                    }
                }
            };

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());
            actor.Equipment.RepairBelowRatio = 0.95;
            configuration.Validate();
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

            options.PrototypeFacilityEid = 0;
            options.RefineryFacilityEid = -1;
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

        [Fact]
        public void ManufacturerClosedEconomyIsExplicitAndPriceBounded()
        {
            var options = new AutonomousManufacturerSalesOptions();

            options.Validate(21);
            Assert.False(options.Enabled);
            Assert.Equal(1.0, options.MinimumUnitPrice);
            Assert.Equal(24, options.OrderDurationHours);

            options.Enabled = true;
            options.MinimumUnitPrice = 0;
            Assert.Throws<InvalidOperationException>(() => options.Validate(21));

            options.MinimumUnitPrice = 5;
            options.OrderDurationHours = 721;
            Assert.Throws<InvalidOperationException>(() => options.Validate(21));
        }

        [Fact]
        public void EquipmentBehaviorRequiresExplicitRobotAndRepairFacility()
        {
            var definition = new AutonomousActorDefinition
            {
                CharacterId = 22,
                Behavior = "equipment"
            };
            var configuration = new AutonomousConfiguration {Actors = {definition}};

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());

            definition.Equipment.Robot = "arkhe_empty";
            definition.Equipment.RepairFacilityEid = 200;
            definition.Equipment.Enabled = true;
            definition.Equipment.Slots.Add(new AutonomousEquipmentSlotOptions
            {
                Module = "small_laser",
                Component = "Head",
                Slot = 1
            });
            configuration.Validate();
        }

        [Fact]
        public void EquipmentBehaviorRejectsDuplicateSlotsAndUnsafeRepairPolicy()
        {
            var options = new AutonomousEquipmentOptions
            {
                Enabled = true,
                Robot = "arkhe_empty",
                RepairFacilityEid = 200,
                Slots = new List<AutonomousEquipmentSlotOptions>
                {
                    new AutonomousEquipmentSlotOptions
                    {
                        Module = "module_a",
                        Component = "Head",
                        Slot = 1
                    },
                    new AutonomousEquipmentSlotOptions
                    {
                        Module = "module_b",
                        Component = "Head",
                        Slot = 1
                    }
                }
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(22));
            options.Slots.RemoveAt(1);
            options.RepairBelowRatio = 1.1;
            Assert.Throws<InvalidOperationException>(() => options.Validate(22));
        }

        [Fact]
        public void EquipmentProcurementRequiresPriceCapAndSafeReserve()
        {
            var options = new AutonomousEquipmentOptions
            {
                Enabled = true,
                Robot = "arkhe_empty",
                RepairFacilityEid = 200,
                Procurement = new AutonomousEquipmentProcurementOptions
                {
                    Enabled = true,
                    MaximumUnitPrice = 0
                }
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(22));
            options.Procurement.MaximumUnitPrice = 100;
            options.Procurement.WalletReserve = -1;
            Assert.Throws<InvalidOperationException>(() => options.Validate(22));

            options.Procurement.WalletReserve = 0;
            options.Validate(22);
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

        [Fact]
        public void PlayerRolePlanIsExplicitDistinctAndBounded()
        {
            var options = new AutonomousPlayerOptions();
            Assert.Throws<InvalidOperationException>(() => options.Validate(30));

            options.Roles.Add("mining");
            options.Roles.Add("MINING");
            Assert.Throws<InvalidOperationException>(() => options.Validate(30));

            options.Roles.RemoveAt(1);
            options.MaximumRoleSeconds = options.MinimumRoleSeconds;
            Assert.Throws<InvalidOperationException>(() => options.Validate(30));

            options.MaximumRoleSeconds = 60;
            options.Validate(30);
            Assert.Equal(new[] {"mining"}, options.GetRoles());
        }

        [Fact]
        public void PlayerValidatesEverySelectedRoleBeforeHosting()
        {
            var definition = new AutonomousActorDefinition
            {
                CharacterId = 30,
                Behavior = "player",
                Player = new AutonomousPlayerOptions
                {
                    Roles = new List<string> {"mining", "mission"}
                }
            };
            var configuration = new AutonomousConfiguration {Actors = {definition}};

            Assert.Throws<InvalidOperationException>(() => configuration.Validate());

            definition.Mission.Enabled = true;
            definition.Mission.SourceBaseEid = 100;
            configuration.Validate();
        }

        [Fact]
        public void PlayerCombatMissionCanUseItsOwnOrdinaryLoadout()
        {
            var definition = new AutonomousActorDefinition
            {
                CharacterId = 31,
                Behavior = "player",
                Player = new AutonomousPlayerOptions
                {
                    Roles = new List<string> {"mission", "mining"},
                    Loadouts = new Dictionary<string, AutonomousEquipmentOptions>
                    {
                        {
                            "mission",
                            new AutonomousEquipmentOptions
                            {
                                Enabled = true,
                                Robot = "combat_robot",
                                RepairFacilityEid = 100,
                                Slots = new List<AutonomousEquipmentSlotOptions>
                                {
                                    new AutonomousEquipmentSlotOptions
                                    {
                                        Module = "weapon",
                                        Ammo = "ammo",
                                        Component = "Head",
                                        Slot = 0
                                    }
                                }
                            }
                        },
                        {
                            "mining",
                            new AutonomousEquipmentOptions
                            {
                                Enabled = true,
                                Robot = "mining_robot",
                                RepairFacilityEid = 100
                            }
                        }
                    }
                },
                Mission = new AutonomousMissionOptions
                {
                    Enabled = true,
                    Category = "Combat",
                    SourceBaseEid = 100,
                    Pve = new AutonomousPveOptions {Enabled = true}
                }
            };
            var configuration = new AutonomousConfiguration {Actors = {definition}};

            configuration.Validate();

            Assert.False(definition.Equipment.Enabled);
            Assert.Equal("combat_robot", definition.GetEquipmentOptions("mission").Robot);
            Assert.Equal("mining_robot", definition.GetEquipmentOptions("mining").Robot);
        }

        [Fact]
        public void PlayerRejectsLoadoutOutsideItsEquipmentRolesOrPlan()
        {
            var options = new AutonomousPlayerOptions
            {
                Roles = new List<string> {"mining"},
                Loadouts = new Dictionary<string, AutonomousEquipmentOptions>
                {
                    {"manufacturer", new AutonomousEquipmentOptions {Enabled = true}}
                }
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(31));

            options.Loadouts = new Dictionary<string, AutonomousEquipmentOptions>
            {
                {"trader", new AutonomousEquipmentOptions {Enabled = true}}
            };
            Assert.Throws<InvalidOperationException>(() => options.Validate(31));
        }

        [Fact]
        public void ManufacturerSupplyDemandRequiresOrdinaryMarketProcurement()
        {
            var options = new AutonomousManufacturerOptions
            {
                TargetDefinition = 100,
                MillFacilityEid = 200,
                SupplyDemand = new AutonomousSupplyDemandOptions {Enabled = true}
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(31));

            options.Procurement.Enabled = true;
            options.Procurement.MaximumUnitPrice = 50;
            options.Validate(31);
        }

        [Fact]
        public void MiningSupplyFulfillmentRequiresOrdinaryMarketSales()
        {
            var options = new AutonomousMiningOptions
            {
                SupplyFulfillment = new AutonomousSupplyFulfillmentOptions {Enabled = true}
            };

            Assert.Throws<InvalidOperationException>(() => options.Validate(31));

            options.Market.Enabled = true;
            options.Validate(31);
        }
    }
}
