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

        private static AutonomousCargoSnapshot Cargo(params AutonomousCargoItemSnapshot[] items)
        {
            return new AutonomousCargoSnapshot(500, 100, 50, items);
        }

        private static AutonomousCargoItemSnapshot Item(long eid, int definition, bool rawMaterial)
        {
            return new AutonomousCargoItemSnapshot(eid, definition, 5, 1, rawMaterial);
        }
    }
}
