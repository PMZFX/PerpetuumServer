using System;
using System.Collections.Generic;
using System.Linq;

namespace Perpetuum.Services.ProductionEngine
{
    public sealed class RepairItemQuote
    {
        public RepairItemQuote(long itemEid, int price, double healthRatio)
        {
            if (itemEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(itemEid));
            if (price < 0)
                throw new ArgumentOutOfRangeException(nameof(price));
            if (healthRatio < 0 || healthRatio > 1)
                throw new ArgumentOutOfRangeException(nameof(healthRatio));

            ItemEid = itemEid;
            Price = price;
            HealthRatio = healthRatio;
        }

        public long ItemEid { get; }
        public int Price { get; }
        public double HealthRatio { get; }
    }

    public sealed class RepairQuote
    {
        public RepairQuote(long facilityEid, IEnumerable<RepairItemQuote> items)
        {
            if (facilityEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(facilityEid));
            FacilityEid = facilityEid;
            Items = (items ?? throw new ArgumentNullException(nameof(items))).ToArray();
        }

        public long FacilityEid { get; }
        public IReadOnlyList<RepairItemQuote> Items { get; }
        public long TotalPrice => Items.Sum(item => (long)item.Price);
    }
}
