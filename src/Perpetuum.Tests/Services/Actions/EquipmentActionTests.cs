using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Actions
{
    public class EquipmentActionTests
    {
        [Fact]
        public void RepairQuotePreservesItemOrderAndTotal()
        {
            var quote = new RepairQuote(
                400,
                new[]
                {
                    new RepairItemQuote(10, 25, 0.5),
                    new RepairItemQuote(20, 75, 0.25)
                });

            Assert.Equal(400, quote.FacilityEid);
            Assert.Equal(new long[] {10, 20}, new[] {quote.Items[0].ItemEid, quote.Items[1].ItemEid});
            Assert.Equal(100, quote.TotalPrice);
        }

        [Fact]
        public void RepairActionCopiesProtocolTargetArray()
        {
            var targets = new long[] {10, 20};
            var action = new ProductionRepairAction(400, targets, true);

            targets[0] = 99;

            Assert.Equal(new long[] {10, 20}, action.TargetEids);
            Assert.True(action.UseCorporationWallet);
        }

        [Fact]
        public void FittingActionsCarryOnlyNormalClientInputs()
        {
            var equip = new EquipModuleAction(
                100,
                200,
                300,
                RobotComponentType.Head,
                2);
            var remove = new RemoveModuleAction(100, 200, 300);

            Assert.Equal(100, equip.ContainerEid);
            Assert.Equal(200, equip.RobotEid);
            Assert.Equal(300, equip.ModuleEid);
            Assert.Equal(RobotComponentType.Head, equip.ComponentType);
            Assert.Equal(2, equip.Slot);
            Assert.Equal(300, remove.ModuleEid);
        }

        [Fact]
        public void ActiveRobotActionCarriesContainerAndRobotIdentity()
        {
            var action = new SelectActiveRobotAction(100, 200);

            Assert.Equal(100, action.ContainerEid);
            Assert.Equal(200, action.RobotEid);
        }
    }
}
