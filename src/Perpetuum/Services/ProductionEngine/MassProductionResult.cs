using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Containers;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class MassProductionResult
    {
        public MassProductionResult(
            ProductionInProgress production,
            Mill facility,
            PublicContainer sourceContainer,
            bool hasBonus)
        {
            Production = production ?? throw new ArgumentNullException(nameof(production));
            Facility = facility ?? throw new ArgumentNullException(nameof(facility));
            SourceContainer = sourceContainer ?? throw new ArgumentNullException(nameof(sourceContainer));
            HasBonus = hasBonus;
        }

        public ProductionInProgress Production { get; }
        public Mill Facility { get; }
        public PublicContainer SourceContainer { get; }
        public bool HasBonus { get; }

        public Dictionary<string, object> ToDictionary(Character character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            Dictionary<string, object> lines = Facility.GetLinesList(character);
            return new Dictionary<string, object>
            {
                {k.lines, lines},
                {k.lineCount, lines.Count},
                {k.production, Production.ToDictionary()},
                {k.sourceContainer, SourceContainer.ToDictionary()},
                {k.facility, Facility.GetFacilityInfo(character)},
                {k.hasBonus, HasBonus}
            };
        }
    }

    public sealed class MassProductionLineQuote
    {
        public MassProductionLineQuote(MassProductionQuote quote, ProductionLine line)
        {
            Quote = quote ?? throw new ArgumentNullException(nameof(quote));
            Line = line ?? throw new ArgumentNullException(nameof(line));
        }

        public MassProductionQuote Quote { get; }
        public ProductionLine Line { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                {
                    "l1", new Dictionary<string, object>
                    {
                        {k.line, Quote.ToDictionary()},
                        {k.data, Line.ToDictionary()}
                    }
                }
            };
        }
    }
}
