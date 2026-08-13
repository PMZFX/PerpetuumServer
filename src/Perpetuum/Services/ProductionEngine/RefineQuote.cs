using System;
using System.Collections.Generic;
using System.Linq;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class RefineComponentQuote
    {
        public RefineComponentQuote(int definition, int nominalAmount, int effectiveAmount)
        {
            if (definition <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (nominalAmount < 0)
                throw new ArgumentOutOfRangeException(nameof(nominalAmount));
            if (effectiveAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(effectiveAmount));

            Definition = definition;
            NominalAmount = nominalAmount;
            EffectiveAmount = effectiveAmount;
        }

        public int Definition { get; }
        public int NominalAmount { get; }
        public int EffectiveAmount { get; }
    }

    public sealed class RefineQuote
    {
        public RefineQuote(
            int targetDefinition,
            int targetAmount,
            long facilityEid,
            IEnumerable<RefineComponentQuote> components)
        {
            if (targetDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetDefinition));
            if (targetAmount < 0)
                throw new ArgumentOutOfRangeException(nameof(targetAmount));
            if (facilityEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(facilityEid));
            if (components == null)
                throw new ArgumentNullException(nameof(components));

            RefineComponentQuote[] componentArray = components.ToArray();
            TargetDefinition = targetDefinition;
            TargetAmount = targetAmount;
            FacilityEid = facilityEid;
            Components = componentArray;
        }

        public int TargetDefinition { get; }
        public int TargetAmount { get; }
        public long FacilityEid { get; }
        public IReadOnlyList<RefineComponentQuote> Components { get; }
    }
}
