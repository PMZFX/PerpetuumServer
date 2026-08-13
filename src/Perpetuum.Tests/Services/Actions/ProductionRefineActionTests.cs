using System;
using System.Linq;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Actions
{
    public class ProductionRefineActionTests
    {
        [Fact]
        public void AmountPolicyPreservesLegacyZeroClamp()
        {
            Assert.Equal(0, ProductionRefineAmountPolicy.Normalize(0));
            Assert.Equal(0, ProductionRefineAmountPolicy.Normalize(-1));
        }

        [Fact]
        public void AmountPolicyPreservesValidAmountsAndCapsLegacyMaximum()
        {
            Assert.Equal(42, ProductionRefineAmountPolicy.Normalize(42));
            Assert.Equal(
                ProductionRefineAmountPolicy.MaximumAmount,
                ProductionRefineAmountPolicy.Normalize(int.MaxValue));
        }

        [Fact]
        public void TypedQuotePreservesComponentOrder()
        {
            var quote = new RefineQuote(
                100,
                2,
                300,
                new[]
                {
                    new RefineComponentQuote(20, 10, 12),
                    new RefineComponentQuote(10, 6, 8)
                });

            Assert.Equal(new[] {20, 10}, quote.Components.Select(component => component.Definition));
            Assert.Equal(2, quote.TargetAmount);
            Assert.Equal(300, quote.FacilityEid);
        }

        [Fact]
        public void TypedQuoteSupportsLegacyEmptyComponentResponse()
        {
            var quote = new RefineQuote(
                100,
                2,
                300,
                Array.Empty<RefineComponentQuote>());

            Assert.Empty(quote.Components);
        }

        [Fact]
        public void TypedQuoteSupportsLegacyZeroAmountQuery()
        {
            var quote = new RefineQuote(
                100,
                0,
                300,
                new[] {new RefineComponentQuote(20, 0, 1)});

            Assert.Equal(0, quote.TargetAmount);
            Assert.Equal(0, quote.Components[0].NominalAmount);
        }

        [Fact]
        public void ProductionMaterialQuotePreservesLegacySingleItemShape()
        {
            var quote = new ProductionMaterialQuote(10, 3, 1, true);

            var data = quote.ToDictionary(3);

            Assert.Equal(10, data[k.definition]);
            Assert.Equal(3, data[k.amount]);
            Assert.Equal(1, data[k.effectiveAmount]);
            Assert.False(data.ContainsKey(k.nominalAmount));
        }

        [Fact]
        public void ProductionMaterialQuotePreservesLegacyStackShape()
        {
            var quote = new ProductionMaterialQuote(20, 17, 5, false);

            var data = quote.ToDictionary(1);

            Assert.Equal(20, data[k.definition]);
            Assert.Equal(17, data[k.effectiveAmount]);
            Assert.Equal(5, data[k.nominalAmount]);
            Assert.False(data.ContainsKey(k.amount));
        }
    }
}
