using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Containers;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class PrototypeProductionResult
    {
        public PrototypeProductionResult(
            ProductionInProgress production,
            Prototyper facility,
            PublicContainer sourceContainer,
            bool hasBonus)
        {
            Production = production ?? throw new ArgumentNullException(nameof(production));
            Facility = facility ?? throw new ArgumentNullException(nameof(facility));
            SourceContainer = sourceContainer ?? throw new ArgumentNullException(nameof(sourceContainer));
            HasBonus = hasBonus;
        }

        public ProductionInProgress Production { get; }
        public Prototyper Facility { get; }
        public PublicContainer SourceContainer { get; }
        public bool HasBonus { get; }

        public Dictionary<string, object> ToDictionary(Character character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            return new Dictionary<string, object>
            {
                {k.production, Production.ToDictionary()},
                {k.facility, Facility.GetFacilityInfo(character)},
                {k.sourceContainer, SourceContainer.ToDictionary()},
                {k.hasBonus, HasBonus}
            };
        }
    }
}
