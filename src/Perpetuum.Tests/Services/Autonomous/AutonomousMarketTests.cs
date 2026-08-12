using System;
using System.Linq;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousMarketTests
    {
        [Fact]
        public void EligibleBestBidWinsOverHistoricalListingPrice()
        {
            double price = AutonomousMarketPricingPolicy.SelectUnitPrice(10, 0.98, 24, 100);

            Assert.Equal(24, price);
        }

        [Fact]
        public void BidBelowFloorFallsBackToDiscountedAverage()
        {
            double price = AutonomousMarketPricingPolicy.SelectUnitPrice(10, 0.9, 8, 20);

            Assert.Equal(18, price);
        }

        [Fact]
        public void FloorProtectsListingsWithoutUsefulMarketHistory()
        {
            Assert.Equal(10, AutonomousMarketPricingPolicy.SelectUnitPrice(10, 0.9, null, null));
            Assert.Equal(10, AutonomousMarketPricingPolicy.SelectUnitPrice(10, 0.9, null, 5));
        }

        [Fact]
        public void CargoSelectionCanBeRestrictedToConfiguredMineral()
        {
            var cargo = Cargo(
                Item(1, 101, true),
                Item(2, 202, true),
                Item(3, 303, false));

            AutonomousCargoItemSnapshot selected =
                AutonomousCargoDispositionService.SelectNextItem(cargo, 202, false);

            Assert.Equal(2, selected.Eid);
        }

        [Fact]
        public void CargoSelectionIncludesRareRawMaterialsOnlyWhenConfigured()
        {
            var cargo = Cargo(
                Item(4, 404, false),
                Item(2, 202, true),
                Item(1, 101, true));

            AutonomousCargoItemSnapshot selected =
                AutonomousCargoDispositionService.SelectNextItem(cargo, 202, true);

            Assert.Equal(1, selected.Eid);
            Assert.Null(AutonomousCargoDispositionService.SelectNextItem(
                Cargo(Item(4, 404, false)),
                202,
                true));
        }

        [Fact]
        public void RegionalPlanIsBoundedByBudgetCargoAndVisibleOrderDepth()
        {
            DateTime now = new DateTime(2026, 8, 12, 19, 30, 0, DateTimeKind.Utc);
            var plan = AutonomousRegionalTradePolicy.Select(
                1,
                new[]
                {
                    Memory(1, 10, 100, 1, 700, sell: 10, sellQuantity: 100, observed: now),
                    Memory(1, 20, 200, 2, 700, buy: 15, buyQuantity: 6, observed: now),
                    Memory(1, 30, 300, 3, 700, buy: 16, buyQuantity: 2, observed: now)
                },
                new[] {new AutonomousTradeCommodity(700, 2)},
                now,
                TimeSpan.FromHours(2),
                1,
                0.1,
                50,
                100,
                10);

            Assert.NotNull(plan);
            Assert.Equal(20, plan.Destination.MarketEid);
            Assert.Equal(5, plan.Quantity);
            Assert.Equal(50, plan.ExpectedCost);
            Assert.Equal(75, plan.ExpectedRevenue);
        }

        [Fact]
        public void RegionalPlanRejectsStaleAndSameZoneKnowledge()
        {
            DateTime now = new DateTime(2026, 8, 12, 19, 30, 0, DateTimeKind.Utc);
            var memories = new[]
            {
                Memory(1, 10, 100, 1, 700, sell: 10, sellQuantity: 10, observed: now),
                Memory(1, 20, 200, 1, 700, buy: 20, buyQuantity: 10, observed: now),
                Memory(1, 30, 300, 3, 700, buy: 30, buyQuantity: 10,
                    observed: now - TimeSpan.FromHours(3))
            };

            Assert.Null(AutonomousRegionalTradePolicy.Select(
                1,
                memories,
                new[] {new AutonomousTradeCommodity(700, 1)},
                now,
                TimeSpan.FromHours(2),
                1,
                0.1,
                10,
                1000,
                100));
        }

        [Fact]
        public void RegionalPlanNeverCombinesDifferentActorsMemories()
        {
            DateTime now = new DateTime(2026, 8, 12, 19, 30, 0, DateTimeKind.Utc);

            Assert.Null(AutonomousRegionalTradePolicy.Select(
                1,
                new[]
                {
                    Memory(1, 10, 100, 1, 700, sell: 10, sellQuantity: 10, observed: now),
                    Memory(2, 20, 200, 2, 700, buy: 20, buyQuantity: 10, observed: now)
                },
                new[] {new AutonomousTradeCommodity(700, 1)},
                now,
                TimeSpan.FromHours(2),
                1,
                0.1,
                10,
                1000,
                100));
        }

        [Fact]
        public void MarketMemoryCopiesOnlyTheObservedLocalQuote()
        {
            DateTime observed = new DateTime(2026, 8, 12, 19, 30, 0, DateTimeKind.Utc);
            var quote = new AutonomousMarketQuote(
                10,
                100,
                1,
                700,
                15,
                6,
                10,
                8,
                12,
                observed);

            AutonomousMarketMemory memory = AutonomousMarketMemory.FromQuote(4, quote);

            Assert.Equal(4, memory.CharacterId);
            Assert.Equal(10, memory.MarketEid);
            Assert.Equal(100, memory.DockingBaseEid);
            Assert.Equal(1, memory.ZoneId);
            Assert.Equal(observed, memory.ObservedAtUtc);
            Assert.Equal(6, memory.BestBuyQuantity);
            Assert.Equal(8, memory.BestSellQuantity);
        }

        [Fact]
        public void ShipmentSaleFloorPreservesBothAbsoluteAndMarginRequirements()
        {
            Assert.Equal(12, AutonomousTradeSalePolicy.GetMinimumUnitPrice(10, 2, 0.1));
            Assert.Equal(15, AutonomousTradeSalePolicy.GetMinimumUnitPrice(10, 1, 0.5));
        }

        [Fact]
        public void MarketSurveyVisitsMissingThenOldestObservation()
        {
            DateTime now = new DateTime(2026, 8, 12, 19, 30, 0, DateTimeKind.Utc);
            var memories = new[]
            {
                Memory(1, 10, 100, 1, 700, observed: now),
                Memory(1, 20, 200, 2, 700, observed: now - TimeSpan.FromHours(2)),
                Memory(1, 21, 200, 2, 701, observed: now - TimeSpan.FromHours(1)),
                Memory(1, 30, 300, 3, 700, observed: now - TimeSpan.FromHours(3))
            };

            Assert.Equal(300, AutonomousMarketSurveyPolicy.SelectNextBase(
                new long[] {100, 200, 300},
                new[] {700, 701},
                memories,
                100));

            AutonomousMarketMemory[] complete = memories
                .Append(Memory(1, 30, 300, 3, 700, observed: now))
                .Append(Memory(1, 31, 300, 3, 701, observed: now - TimeSpan.FromMinutes(30)))
                .ToArray();
            Assert.Equal(200, AutonomousMarketSurveyPolicy.SelectNextBase(
                new long[] {100, 200, 300},
                new[] {700, 701},
                complete,
                100));
        }

        [Fact]
        public void DurableShipmentRetainsDestinationWhileQuantityChanges()
        {
            DateTime acquired = new DateTime(2026, 8, 12, 19, 30, 0, DateTimeKind.Utc);
            var state = new AutonomousTradeState(
                4, 700, 10, 12, 10, 100, 1, 20, 200, 2, acquired);

            AutonomousTradeState remaining = state.WithRemainingQuantity(3);

            Assert.Equal(3, remaining.QuantityRemaining);
            Assert.Equal(200, remaining.DestinationBaseEid);
            Assert.Equal(12, remaining.UnitCost);
            Assert.Equal(acquired, remaining.AcquiredAtUtc);
        }

        private static AutonomousCargoSnapshot Cargo(params AutonomousCargoItemSnapshot[] items)
        {
            return new AutonomousCargoSnapshot(500, 100, 50, items);
        }

        private static AutonomousCargoItemSnapshot Item(long eid, int definition, bool rawMaterial)
        {
            return new AutonomousCargoItemSnapshot(eid, definition, 5, 1, rawMaterial);
        }

        private static AutonomousMarketMemory Memory(
            int characterId,
            long marketEid,
            long dockingBaseEid,
            int zoneId,
            int definition,
            double? buy = null,
            int? buyQuantity = null,
            double? sell = null,
            int? sellQuantity = null,
            DateTime? observed = null)
        {
            return new AutonomousMarketMemory(
                characterId,
                marketEid,
                dockingBaseEid,
                zoneId,
                definition,
                buy,
                buyQuantity,
                sell,
                sellQuantity,
                null,
                observed ?? DateTime.UtcNow);
        }
    }
}
