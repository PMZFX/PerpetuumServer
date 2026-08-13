using System;
using System.Collections.Generic;
using System.Linq;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class MassProductionQuote
    {
        public MassProductionQuote(
            int targetDefinition,
            long price,
            int productionTimeSeconds,
            double materialMultiplier,
            bool hasBonus,
            int targetQuantity,
            IEnumerable<ProductionMaterialQuote> materials)
        {
            if (targetDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetDefinition));
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price));
            if (productionTimeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(productionTimeSeconds));
            if (materialMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(materialMultiplier));
            if (targetQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetQuantity));

            TargetDefinition = targetDefinition;
            Price = price;
            ProductionTimeSeconds = productionTimeSeconds;
            MaterialMultiplier = materialMultiplier;
            HasBonus = hasBonus;
            TargetQuantity = targetQuantity;
            Materials = (materials ?? throw new ArgumentNullException(nameof(materials))).ToArray();
        }

        public int TargetDefinition { get; }
        public long Price { get; }
        public int ProductionTimeSeconds { get; }
        public double MaterialMultiplier { get; }
        public bool HasBonus { get; }
        public int TargetQuantity { get; }
        public IReadOnlyList<ProductionMaterialQuote> Materials { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                {k.materials, Materials.ToDictionary("c", material => material.ToDictionary(1))},
                {k.productionTime, ProductionTimeSeconds},
                {k.price, Price},
                {k.definition, TargetDefinition},
                {k.materialMultiplier, MaterialMultiplier},
                {k.hasBonus, HasBonus},
                {k.targetQuantity, TargetQuantity}
            };
        }
    }
}
