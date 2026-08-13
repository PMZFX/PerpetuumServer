using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Accounting.Characters;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class PrototypeQuote
    {
        public PrototypeQuote(
            int requestedDefinition,
            int prototypeDefinition,
            long price,
            int productionTimeSeconds,
            double materialMultiplier,
            bool hasBonus,
            Prototyper facility,
            IEnumerable<ProductionMaterialQuote> materials)
        {
            if (requestedDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestedDefinition));
            if (prototypeDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(prototypeDefinition));
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price));
            if (productionTimeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(productionTimeSeconds));
            if (materialMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(materialMultiplier));
            if (facility == null)
                throw new ArgumentNullException(nameof(facility));
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));

            ProductionMaterialQuote[] materialArray = materials.ToArray();
            RequestedDefinition = requestedDefinition;
            PrototypeDefinition = prototypeDefinition;
            Price = price;
            ProductionTimeSeconds = productionTimeSeconds;
            MaterialMultiplier = materialMultiplier;
            HasBonus = hasBonus;
            Facility = facility;
            Materials = materialArray;
        }

        public int RequestedDefinition { get; }
        public int PrototypeDefinition { get; }
        public long Price { get; }
        public int ProductionTimeSeconds { get; }
        public double MaterialMultiplier { get; }
        public bool HasBonus { get; }
        public Prototyper Facility { get; }
        public IReadOnlyList<ProductionMaterialQuote> Materials { get; }

        public Dictionary<string, object> ToDictionary(Character character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            return new Dictionary<string, object>
            {
                {k.materials, Materials.ToDictionary("c", material => material.ToDictionary(1))},
                {k.price, Price},
                {k.productionTime, ProductionTimeSeconds},
                {k.facility, Facility.GetFacilityInfo(character)},
                {k.targetDefinition, PrototypeDefinition},
                {k.materialEfficiency, MaterialMultiplier},
                {k.hasBonus, HasBonus}
            };
        }
    }
}
