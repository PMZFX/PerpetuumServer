using System;
using System.Collections.Generic;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class ResearchQuoteValues
    {
        public ResearchQuoteValues(long price, int researchTimeSeconds, int materialEfficiency, int timeEfficiency)
        {
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price));
            if (researchTimeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(researchTimeSeconds));

            Price = price;
            ResearchTimeSeconds = researchTimeSeconds;
            MaterialEfficiency = materialEfficiency;
            TimeEfficiency = timeEfficiency;
        }

        public long Price { get; }
        public int ResearchTimeSeconds { get; }
        public int MaterialEfficiency { get; }
        public int TimeEfficiency { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                {k.price, Price},
                {k.researchTime, ResearchTimeSeconds},
                {k.materialEfficiency, MaterialEfficiency},
                {k.timeEfficiency, TimeEfficiency}
            };
        }
    }

    public sealed class ResearchQuote
    {
        public ResearchQuote(
            int researchKitDefinition,
            int itemDefinition,
            int researchKitLevel,
            int calibrationProgramDefinition,
            ResearchQuoteValues real,
            ResearchQuoteValues nominal,
            long facilityEid)
        {
            if (researchKitDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(researchKitDefinition));
            if (itemDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemDefinition));
            if (researchKitLevel <= 0)
                throw new ArgumentOutOfRangeException(nameof(researchKitLevel));
            if (calibrationProgramDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(calibrationProgramDefinition));
            if (facilityEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(facilityEid));

            ResearchKitDefinition = researchKitDefinition;
            ItemDefinition = itemDefinition;
            ResearchKitLevel = researchKitLevel;
            CalibrationProgramDefinition = calibrationProgramDefinition;
            Real = real ?? throw new ArgumentNullException(nameof(real));
            Nominal = nominal ?? throw new ArgumentNullException(nameof(nominal));
            FacilityEid = facilityEid;
        }

        public int ResearchKitDefinition { get; }
        public int ItemDefinition { get; }
        public int ResearchKitLevel { get; }
        public int CalibrationProgramDefinition { get; }
        public ResearchQuoteValues Real { get; }
        public ResearchQuoteValues Nominal { get; }
        public long FacilityEid { get; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                {k.researchKitDefinition, ResearchKitDefinition},
                {k.itemDefinition, ItemDefinition},
                {k.researchKitLevel, ResearchKitLevel},
                {k.calibrationProgram, CalibrationProgramDefinition},
                {k.real, Real.ToDictionary()},
                {k.nominal, Nominal.ToDictionary()},
                {k.facility, FacilityEid}
            };
        }
    }
}
