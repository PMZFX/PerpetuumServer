using System;
using System.Collections.Generic;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class ProductionMaterialQuote
    {
        public ProductionMaterialQuote(
            int definition,
            int requiredAmount,
            int nominalAmount,
            bool isSingle)
        {
            if (definition <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (requiredAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(requiredAmount));
            if (nominalAmount <= 0)
                throw new ArgumentOutOfRangeException(nameof(nominalAmount));

            Definition = definition;
            RequiredAmount = requiredAmount;
            NominalAmount = nominalAmount;
            IsSingle = isSingle;
        }

        public int Definition { get; }
        public int RequiredAmount { get; }
        public int NominalAmount { get; }
        public bool IsSingle { get; }

        public Dictionary<string, object> ToDictionary(int targetAmount)
        {
            var result = new Dictionary<string, object>
            {
                {k.definition, Definition}
            };

            if (IsSingle)
            {
                result.Add(k.amount, targetAmount);
                result.Add(k.effectiveAmount, 1);
            }
            else
            {
                result.Add(k.effectiveAmount, RequiredAmount);
                result.Add(k.nominalAmount, NominalAmount);
            }

            return result;
        }
    }
}
