using System.Collections.Generic;
using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousIndustryProcurementTests
    {
        [Theory]
        [InlineData(250, -1, 100, 5.0, 1000.0, 100)]
        [InlineData(250, 12, 100, 5.0, 1000.0, 12)]
        [InlineData(250, -1, 100, 5.0, 49.0, 9)]
        [InlineData(5, -1, 100, 5.0, 1000.0, 5)]
        [InlineData(5, -1, 100, 5.0, 4.0, 0)]
        [InlineData(0, -1, 100, 5.0, 1000.0, 0)]
        public void PurchaseQuantityHonorsNeedOfferBatchAndWalletReserve(
            long required,
            int offer,
            int maximum,
            double unitPrice,
            double spendable,
            int expected)
        {
            Assert.Equal(
                expected,
                AutonomousIndustryProcurementService.SelectPurchaseQuantity(
                    required,
                    offer,
                    maximum,
                    unitPrice,
                    spendable));
        }

        [Fact]
        public void PurchaseProgressIsDurableInventoryReconciliationDataOnly()
        {
            IReadOnlyDictionary<int, long> remaining =
                AutonomousIndustryProcurementService.SubtractPurchased(
                    new Dictionary<int, long> {{300, 4}, {100, 2}},
                    300,
                    3);

            Assert.Equal(new Dictionary<int, long> {{100, 2}, {300, 1}}, remaining);
            Assert.Empty(AutonomousIndustryProcurementService.SubtractPurchased(
                new Dictionary<int, long> {{300, 2}},
                300,
                5));
        }
    }
}
