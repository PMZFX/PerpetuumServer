using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Actions
{
    public class ProductionManufacturingActionTests
    {
        [Fact]
        public void ResearchQuotePreservesP31ResponseShape()
        {
            var quote = new ResearchQuote(
                10,
                20,
                3,
                30,
                new ResearchQuoteValues(40, 50, 60, 70),
                new ResearchQuoteValues(80, 90, 100, 110),
                120);

            Dictionary<string, object> data = quote.ToDictionary();

            Assert.Equal(
                new[]
                {
                    k.calibrationProgram, k.facility, k.itemDefinition, k.nominal,
                    k.real, k.researchKitDefinition, k.researchKitLevel
                },
                data.Keys.OrderBy(key => key));
            var real = Assert.IsType<Dictionary<string, object>>(data[k.real]);
            Assert.Equal(new[] {k.materialEfficiency, k.price, k.researchTime, k.timeEfficiency},
                real.Keys.OrderBy(key => key));
            Assert.Equal(40L, real[k.price]);
        }

        [Fact]
        public void CalibrationQuoteAddsLegacyEfficiencyFieldsToMassQuote()
        {
            var quote = new CalibrationProgramQuote(MassQuote(), 12, 13);

            Dictionary<string, object> data = quote.ToDictionary();

            Assert.Equal(12, data[k.materialEfficiency]);
            Assert.Equal(13, data[k.timeEfficiency]);
            Assert.True(data.ContainsKey(k.materials));
            Assert.True(data.ContainsKey(k.targetQuantity));
        }

        [Fact]
        public void MassQuotePreservesMaterialOrderAndLegacyFields()
        {
            Dictionary<string, object> data = MassQuote().ToDictionary();
            var materials = Assert.IsType<Dictionary<string, object>>(data[k.materials]);

            Assert.Equal(new[] {"c0", "c1"}, materials.Keys);
            Assert.Equal(55L, data[k.price]);
            Assert.Equal(2, data[k.targetQuantity]);
            Assert.Equal(21, Assert.IsType<Dictionary<string, object>>(materials["c0"])[k.definition]);
        }

        [Fact]
        public void RoundsPolicyPreservesLegacyNegativeNormalization()
        {
            Assert.Equal(1, ProductionRoundsPolicy.NormalizeRequestedRounds(-1));
            Assert.Equal(0, ProductionRoundsPolicy.NormalizeRequestedRounds(0));
            Assert.Equal(4, ProductionRoundsPolicy.NormalizeRequestedRounds(4));
        }

        private static MassProductionQuote MassQuote()
        {
            return new MassProductionQuote(
                20,
                55,
                66,
                1.25,
                true,
                2,
                new[]
                {
                    new ProductionMaterialQuote(21, 3, 2, false),
                    new ProductionMaterialQuote(22, 1, 1, true)
                });
        }
    }
}
