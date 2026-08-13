using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Containers;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class ResearchProductionResult
    {
        public ResearchProductionResult(
            ProductionInProgress production,
            ResearchLab facility,
            PublicContainer sourceContainer)
        {
            Production = production ?? throw new ArgumentNullException(nameof(production));
            Facility = facility ?? throw new ArgumentNullException(nameof(facility));
            SourceContainer = sourceContainer ?? throw new ArgumentNullException(nameof(sourceContainer));
        }

        public ProductionInProgress Production { get; }
        public ResearchLab Facility { get; }
        public PublicContainer SourceContainer { get; }

        public Dictionary<string, object> ToDictionary(Character character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            return new Dictionary<string, object>
            {
                {k.production, Production.ToDictionary()},
                {k.sourceContainer, SourceContainer.ToDictionary()},
                {k.facility, Facility.GetFacilityInfo(character)}
            };
        }
    }
}
