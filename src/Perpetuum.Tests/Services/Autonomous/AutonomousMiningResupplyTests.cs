using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousMiningResupplyTests
    {
        [Fact]
        public void MissingOrDifferentAmmoRequestsReload()
        {
            Assert.True(AutonomousMiningResupplyService.NeedsReload(100, 0, 0, 20, 0.5));
            Assert.True(AutonomousMiningResupplyService.NeedsReload(100, 200, 20, 20, 0.5));
        }

        [Theory]
        [InlineData(9, true)]
        [InlineData(10, false)]
        [InlineData(20, false)]
        public void MatchingAmmoUsesConfiguredCapacityThreshold(int quantity, bool expected)
        {
            Assert.Equal(
                expected,
                AutonomousMiningResupplyService.NeedsReload(100, 100, quantity, 20, 0.5));
        }

        [Theory]
        [InlineData(0, 20)]
        [InlineData(100, 0)]
        public void InvalidDesiredAmmoOrCapacityCannotReload(int desiredDefinition, int capacity)
        {
            Assert.False(
                AutonomousMiningResupplyService.NeedsReload(
                    desiredDefinition,
                    0,
                    0,
                    capacity,
                    0.5));
        }

        [Theory]
        [InlineData(64, 40, -1, 500, 24)]
        [InlineData(64, 40, 10, 500, 10)]
        [InlineData(1000, 0, -1, 500, 500)]
        [InlineData(64, 64, -1, 500, 0)]
        [InlineData(64, 0, 0, 500, 0)]
        public void ProcurementBoundsPurchasesByNeedOfferAndBatch(
            int reserve,
            int cargo,
            int offer,
            int maximumBatch,
            int expected)
        {
            Assert.Equal(expected,
                AutonomousMiningProcurementService.SelectPurchaseQuantity(
                    reserve,
                    cargo,
                    offer,
                    maximumBatch));
        }

        [Fact]
        public void ProcurementCarriesPriceCeilingIntoMarketTransaction()
        {
            var action = new MarketBuyAction(42, false, 24, 90);

            Assert.Equal(90, action.MaximumUnitPrice);
        }

        [Theory]
        [InlineData(64, 40, 24, true)]
        [InlineData(64, 40, 25, false)]
        [InlineData(64, 64, 1, false)]
        [InlineData(64, 40, 0, false)]
        public void PendingTerminalStackCannotExceedCargoReserve(
            int reserve,
            int cargo,
            int pending,
            bool expected)
        {
            Assert.Equal(expected,
                AutonomousMiningProcurementService.CanTransferPending(
                    reserve,
                    cargo,
                    pending));
        }
    }
}
