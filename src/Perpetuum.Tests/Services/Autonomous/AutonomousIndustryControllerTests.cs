using System.Collections.Generic;
using Perpetuum.Services.Autonomous;
using Perpetuum.Services.ProductionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousIndustryControllerTests
    {
        [Fact]
        public void ExistingProductionWinsOverStartingDuplicateAfterRestart()
        {
            Assert.Equal(
                AutonomousManufacturerDirective.WaitForProduction,
                AutonomousManufacturerPolicy.Select(
                    complete: false,
                    matchingProduction: true,
                    hasUsableLine: true,
                    hasMissingMaterials: false));
        }

        [Theory]
        [InlineData(true, false, false, false, AutonomousManufacturerDirective.Complete)]
        [InlineData(false, false, false, false, AutonomousManufacturerDirective.WaitForCalibration)]
        [InlineData(false, false, true, true, AutonomousManufacturerDirective.WaitForMaterials)]
        [InlineData(false, false, true, false, AutonomousManufacturerDirective.StartProduction)]
        public void PolicySelectsNaturalWaitOrStart(
            bool complete,
            bool matchingProduction,
            bool hasUsableLine,
            bool hasMissingMaterials,
            AutonomousManufacturerDirective expected)
        {
            Assert.Equal(expected, AutonomousManufacturerPolicy.Select(
                complete,
                matchingProduction,
                hasUsableLine,
                hasMissingMaterials));
        }

        [Fact]
        public void ExactCharacterQuoteProducesProcurementGoalsWithoutGrantingItems()
        {
            var quote = new MassProductionQuote(
                100,
                10,
                20,
                1.1,
                false,
                1,
                new[]
                {
                    new ProductionMaterialQuote(200, 7, 5, false),
                    new ProductionMaterialQuote(300, 1, 1, true)
                });

            IReadOnlyDictionary<int, long> missing = AutonomousManufacturerPolicy.FindMissingMaterials(
                quote,
                new Dictionary<int, long> {{200, 2}, {300, 1}});

            Assert.Equal(new Dictionary<int, long> {{200, 5}}, missing);
        }

        [Fact]
        public void ProcurementCodecIsDeterministicAndRejectsInvalidState()
        {
            string json = AutonomousIndustryProcurementCodec.Serialize(
                new Dictionary<int, long> {{300, 2}, {100, 4}});

            Assert.Equal("{\"100\":4,\"300\":2}", json);
            Assert.Equal(4, AutonomousIndustryProcurementCodec.Deserialize(json)[100]);
            Assert.Empty(AutonomousIndustryProcurementCodec.Deserialize("{\"0\":2}"));
            Assert.Empty(AutonomousIndustryProcurementCodec.Deserialize("not json"));
        }

        [Fact]
        public void ProgressUpdatesPreservePersistentGoalIdentity()
        {
            var goal = new AutonomousIndustryGoalState(7, 100, 2, 3, 400, "Planning");

            AutonomousIndustryGoalState waiting = goal.WithProgress(
                "WaitingProduction",
                lineId: 5,
                productionId: 6);

            Assert.Equal(100, waiting.TargetDefinition);
            Assert.Equal(2, waiting.TargetQuantity);
            Assert.Equal(3, waiting.InitialInventoryQuantity);
            Assert.Equal(400, waiting.MillFacilityEid);
            Assert.Equal(5, waiting.LineId);
            Assert.Equal(6, waiting.ProductionId);
        }
    }
}
