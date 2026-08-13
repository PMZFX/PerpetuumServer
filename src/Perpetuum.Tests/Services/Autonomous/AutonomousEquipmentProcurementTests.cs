using Perpetuum.Services.Autonomous;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousEquipmentProcurementTests
    {
        [Theory]
        [InlineData(100, 25, 75, true)]
        [InlineData(100, 25, 75.01, false)]
        [InlineData(100, 100, 1, false)]
        [InlineData(100, 0, 100, true)]
        [InlineData(100, 0, 0, false)]
        [InlineData(-1, 0, 1, false)]
        public void PurchaseRespectsWalletReserve(
            double balance,
            double reserve,
            double price,
            bool expected)
        {
            Assert.Equal(
                expected,
                AutonomousEquipmentProcurementService.CanPurchaseOne(balance, reserve, price));
        }
    }
}
