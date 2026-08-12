using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Data;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousMarketMemory
    {
        public AutonomousMarketMemory(
            int characterId,
            long marketEid,
            long dockingBaseEid,
            int zoneId,
            int definition,
            double? bestBuyPrice,
            int? bestBuyQuantity,
            double? bestSellPrice,
            int? bestSellQuantity,
            double? averagePrice,
            DateTime observedAtUtc)
        {
            CharacterId = characterId;
            MarketEid = marketEid;
            DockingBaseEid = dockingBaseEid;
            ZoneId = zoneId;
            Definition = definition;
            BestBuyPrice = bestBuyPrice;
            BestBuyQuantity = bestBuyQuantity;
            BestSellPrice = bestSellPrice;
            BestSellQuantity = bestSellQuantity;
            AveragePrice = averagePrice;
            ObservedAtUtc = observedAtUtc;
        }

        public int CharacterId { get; }
        public long MarketEid { get; }
        public long DockingBaseEid { get; }
        public int ZoneId { get; }
        public int Definition { get; }
        public double? BestBuyPrice { get; }
        public int? BestBuyQuantity { get; }
        public double? BestSellPrice { get; }
        public int? BestSellQuantity { get; }
        public double? AveragePrice { get; }
        public DateTime ObservedAtUtc { get; }

        public static AutonomousMarketMemory FromQuote(int characterId, AutonomousMarketQuote quote)
        {
            if (quote == null)
                throw new ArgumentNullException(nameof(quote));
            return new AutonomousMarketMemory(
                characterId,
                quote.MarketEid,
                quote.DockingBaseEid,
                quote.ZoneId,
                quote.Definition,
                quote.BestBuyPrice,
                quote.BestBuyQuantity,
                quote.BestSellPrice,
                quote.BestSellQuantity,
                quote.AveragePrice,
                quote.ObservedAtUtc);
        }
    }

    public interface IAutonomousMarketMemoryStore
    {
        void Remember(AutonomousMarketMemory memory);
        IReadOnlyList<AutonomousMarketMemory> Recall(int characterId);
    }

    public sealed class DatabaseAutonomousMarketMemoryStore : IAutonomousMarketMemoryStore
    {
        public void Remember(AutonomousMarketMemory memory)
        {
            if (memory == null)
                throw new ArgumentNullException(nameof(memory));

            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_market_memory with (updlock, serializable)
                              set docking_base_eid = @dockingBaseEid,
                                  zone_id = @zoneId,
                                  best_buy_price = @bestBuyPrice,
                                  best_buy_quantity = @bestBuyQuantity,
                                  best_sell_price = @bestSellPrice,
                                  best_sell_quantity = @bestSellQuantity,
                                  average_price = @averagePrice,
                                  observed_at = @observedAt
                              where character_id = @characterId
                                and market_eid = @marketEid
                                and item_definition = @definition;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_market_memory
                                      (character_id, market_eid, docking_base_eid,
                                       zone_id, item_definition,
                                       best_buy_price, best_buy_quantity,
                                       best_sell_price, best_sell_quantity,
                                       average_price, observed_at)
                                  values
                                      (@characterId, @marketEid, @dockingBaseEid,
                                       @zoneId, @definition,
                                       @bestBuyPrice, @bestBuyQuantity,
                                       @bestSellPrice, @bestSellQuantity,
                                       @averagePrice, @observedAt);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", memory.CharacterId)
                .SetParameter("@marketEid", memory.MarketEid)
                .SetParameter("@dockingBaseEid", memory.DockingBaseEid)
                .SetParameter("@zoneId", memory.ZoneId)
                .SetParameter("@definition", memory.Definition)
                .SetParameter("@bestBuyPrice", memory.BestBuyPrice)
                .SetParameter("@bestBuyQuantity", memory.BestBuyQuantity)
                .SetParameter("@bestSellPrice", memory.BestSellPrice)
                .SetParameter("@bestSellQuantity", memory.BestSellQuantity)
                .SetParameter("@averagePrice", memory.AveragePrice)
                .SetParameter("@observedAt", memory.ObservedAtUtc)
                .ExecuteNonQuery();
        }

        public IReadOnlyList<AutonomousMarketMemory> Recall(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));

            return Db.Query()
                .CommandText(@"select character_id, market_eid, docking_base_eid,
                                     zone_id, item_definition,
                                     best_buy_price, best_buy_quantity,
                                     best_sell_price, best_sell_quantity,
                                     average_price, observed_at
                              from dbo.ai_market_memory
                              where character_id = @characterId
                              order by item_definition, market_eid")
                .SetParameter("@characterId", characterId)
                .Execute()
                .Select(record => new AutonomousMarketMemory(
                    record.GetValue<int>("character_id"),
                    record.GetValue<long>("market_eid"),
                    record.GetValue<long>("docking_base_eid"),
                    record.GetValue<int>("zone_id"),
                    record.GetValue<int>("item_definition"),
                    record.GetValue<double?>("best_buy_price"),
                    record.GetValue<int?>("best_buy_quantity"),
                    record.GetValue<double?>("best_sell_price"),
                    record.GetValue<int?>("best_sell_quantity"),
                    record.GetValue<double?>("average_price"),
                    record.GetValue<DateTime>("observed_at")))
                .ToArray();
        }
    }

    public interface IAutonomousRegionalMarketService
    {
        AutonomousMarketMemory ObserveAndRemember(GameActionContext context, int definition);
        IReadOnlyList<AutonomousMarketMemory> Recall(int characterId);
    }

    public sealed class AutonomousRegionalMarketService : IAutonomousRegionalMarketService
    {
        private readonly IAutonomousMarketObservationService _observation;
        private readonly IAutonomousMarketMemoryStore _memory;

        public AutonomousRegionalMarketService(
            IAutonomousMarketObservationService observation,
            IAutonomousMarketMemoryStore memory)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        }

        public AutonomousMarketMemory ObserveAndRemember(GameActionContext context, int definition)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            AutonomousMarketMemory remembered = AutonomousMarketMemory.FromQuote(
                context.Actor.Id,
                _observation.Observe(context, definition));
            _memory.Remember(remembered);
            return remembered;
        }

        public IReadOnlyList<AutonomousMarketMemory> Recall(int characterId)
        {
            return _memory.Recall(characterId);
        }
    }

    public sealed class AutonomousTradeCommodity
    {
        public AutonomousTradeCommodity(int definition, double unitVolume)
        {
            if (definition <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (double.IsNaN(unitVolume) || double.IsInfinity(unitVolume) || unitVolume <= 0)
                throw new ArgumentOutOfRangeException(nameof(unitVolume));
            Definition = definition;
            UnitVolume = unitVolume;
        }

        public int Definition { get; }
        public double UnitVolume { get; }
    }

    public sealed class AutonomousTradePlan
    {
        public AutonomousTradePlan(
            int definition,
            int quantity,
            AutonomousMarketMemory source,
            AutonomousMarketMemory destination,
            double expectedCost,
            double expectedRevenue)
        {
            Definition = definition;
            Quantity = quantity;
            Source = source;
            Destination = destination;
            ExpectedCost = expectedCost;
            ExpectedRevenue = expectedRevenue;
        }

        public int Definition { get; }
        public int Quantity { get; }
        public AutonomousMarketMemory Source { get; }
        public AutonomousMarketMemory Destination { get; }
        public double ExpectedCost { get; }
        public double ExpectedRevenue { get; }
        public double ExpectedGrossProfit => ExpectedRevenue - ExpectedCost;
        public double ExpectedMargin => ExpectedCost <= 0 ? 0 : ExpectedGrossProfit / ExpectedCost;
    }

    public static class AutonomousRegionalTradePolicy
    {
        public static AutonomousTradePlan Select(
            int characterId,
            IEnumerable<AutonomousMarketMemory> memories,
            IEnumerable<AutonomousTradeCommodity> commodities,
            DateTime utcNow,
            TimeSpan maximumObservationAge,
            double minimumUnitProfit,
            double minimumMargin,
            int maximumQuantity,
            double availableBudget,
            double freeCargoVolume)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            if (memories == null)
                throw new ArgumentNullException(nameof(memories));
            if (commodities == null)
                throw new ArgumentNullException(nameof(commodities));
            if (maximumObservationAge <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(maximumObservationAge));
            ValidateNonNegativeFinite(minimumUnitProfit, nameof(minimumUnitProfit));
            ValidateNonNegativeFinite(minimumMargin, nameof(minimumMargin));
            if (maximumQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
            ValidateNonNegativeFinite(availableBudget, nameof(availableBudget));
            ValidateNonNegativeFinite(freeCargoVolume, nameof(freeCargoVolume));

            AutonomousMarketMemory[] fresh = memories
                .Where(memory => memory != null &&
                                 memory.CharacterId == characterId &&
                                 IsFresh(memory, utcNow, maximumObservationAge))
                .ToArray();
            var plans = new List<AutonomousTradePlan>();
            foreach (AutonomousTradeCommodity commodity in commodities
                         .Where(candidate => candidate != null)
                         .OrderBy(candidate => candidate.Definition))
            {
                AutonomousMarketMemory[] quotes = fresh
                    .Where(memory => memory.Definition == commodity.Definition)
                    .ToArray();
                foreach (AutonomousMarketMemory source in quotes.Where(IsBuySource))
                foreach (AutonomousMarketMemory destination in quotes.Where(IsSellDestination))
                {
                    if (source.MarketEid == destination.MarketEid || source.ZoneId == destination.ZoneId)
                        continue;

                    double unitCost = source.BestSellPrice.Value;
                    double unitRevenue = destination.BestBuyPrice.Value;
                    double unitProfit = unitRevenue - unitCost;
                    double margin = unitProfit / unitCost;
                    if (unitProfit < minimumUnitProfit || margin < minimumMargin)
                        continue;

                    int quantity = maximumQuantity;
                    quantity = Math.Min(quantity, source.BestSellQuantity ?? maximumQuantity);
                    quantity = Math.Min(quantity, destination.BestBuyQuantity.Value);
                    quantity = Math.Min(quantity, BoundedFloor(availableBudget / unitCost));
                    quantity = Math.Min(quantity, BoundedFloor(freeCargoVolume / commodity.UnitVolume));
                    if (quantity <= 0)
                        continue;

                    plans.Add(new AutonomousTradePlan(
                        commodity.Definition,
                        quantity,
                        source,
                        destination,
                        unitCost * quantity,
                        unitRevenue * quantity));
                }
            }

            return plans
                .OrderByDescending(plan => plan.ExpectedGrossProfit)
                .ThenByDescending(plan => plan.ExpectedMargin)
                .ThenBy(plan => plan.Definition)
                .ThenBy(plan => plan.Source.MarketEid)
                .ThenBy(plan => plan.Destination.MarketEid)
                .FirstOrDefault();
        }

        private static bool IsFresh(
            AutonomousMarketMemory memory,
            DateTime utcNow,
            TimeSpan maximumObservationAge)
        {
            TimeSpan age = utcNow - memory.ObservedAtUtc;
            return age >= TimeSpan.Zero && age <= maximumObservationAge;
        }

        private static bool IsBuySource(AutonomousMarketMemory memory)
        {
            return Positive(memory.BestSellPrice);
        }

        private static bool IsSellDestination(AutonomousMarketMemory memory)
        {
            return Positive(memory.BestBuyPrice) && memory.BestBuyQuantity > 0;
        }

        private static bool Positive(double? value)
        {
            return value.HasValue && value.Value > 0 &&
                   !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        }

        private static int BoundedFloor(double value)
        {
            if (value <= 0 || double.IsNaN(value))
                return 0;
            return value >= int.MaxValue ? int.MaxValue : (int)Math.Floor(value);
        }

        private static void ValidateNonNegativeFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
