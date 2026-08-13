using System;
using System.Collections.Generic;
using Perpetuum.Accounting.Characters;
using Perpetuum.Containers;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class CalibrationProgramQuote
    {
        public CalibrationProgramQuote(
            MassProductionQuote production,
            int materialEfficiency,
            int timeEfficiency)
        {
            Production = production ?? throw new ArgumentNullException(nameof(production));
            MaterialEfficiency = materialEfficiency;
            TimeEfficiency = timeEfficiency;
        }

        public MassProductionQuote Production { get; }
        public int MaterialEfficiency { get; }
        public int TimeEfficiency { get; }

        public Dictionary<string, object> ToDictionary()
        {
            Dictionary<string, object> result = Production.ToDictionary();
            result.Add(k.materialEfficiency, MaterialEfficiency);
            result.Add(k.timeEfficiency, TimeEfficiency);
            return result;
        }
    }

    public sealed class CalibrationLineResult
    {
        public CalibrationLineResult(Mill facility, PublicContainer sourceContainer)
        {
            Facility = facility ?? throw new ArgumentNullException(nameof(facility));
            SourceContainer = sourceContainer ?? throw new ArgumentNullException(nameof(sourceContainer));
        }

        public Mill Facility { get; }
        public PublicContainer SourceContainer { get; }

        public Dictionary<string, object> ToDictionary(Character character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            Dictionary<string, object> lines = Facility.GetLinesList(character);
            return new Dictionary<string, object>
            {
                {k.lines, lines},
                {k.lineCount, lines.Count},
                {k.sourceContainer, SourceContainer.ToDictionary()},
                {k.facility, Facility.GetFacilityInfo(character)}
            };
        }
    }
}
