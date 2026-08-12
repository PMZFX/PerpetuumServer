using System;
using Perpetuum.Data;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousTradeState
    {
        public AutonomousTradeState(
            int characterId,
            int definition,
            int quantityRemaining,
            double unitCost,
            long sourceMarketEid,
            long sourceBaseEid,
            int sourceZoneId,
            long destinationMarketEid,
            long destinationBaseEid,
            int destinationZoneId,
            DateTime acquiredAtUtc)
        {
            CharacterId = characterId;
            Definition = definition;
            QuantityRemaining = quantityRemaining;
            UnitCost = unitCost;
            SourceMarketEid = sourceMarketEid;
            SourceBaseEid = sourceBaseEid;
            SourceZoneId = sourceZoneId;
            DestinationMarketEid = destinationMarketEid;
            DestinationBaseEid = destinationBaseEid;
            DestinationZoneId = destinationZoneId;
            AcquiredAtUtc = acquiredAtUtc;
        }

        public int CharacterId { get; }
        public int Definition { get; }
        public int QuantityRemaining { get; }
        public double UnitCost { get; }
        public long SourceMarketEid { get; }
        public long SourceBaseEid { get; }
        public int SourceZoneId { get; }
        public long DestinationMarketEid { get; }
        public long DestinationBaseEid { get; }
        public int DestinationZoneId { get; }
        public DateTime AcquiredAtUtc { get; }

        public AutonomousTradeState WithRemainingQuantity(int quantity)
        {
            return new AutonomousTradeState(
                CharacterId,
                Definition,
                quantity,
                UnitCost,
                SourceMarketEid,
                SourceBaseEid,
                SourceZoneId,
                DestinationMarketEid,
                DestinationBaseEid,
                DestinationZoneId,
                AcquiredAtUtc);
        }
    }

    public interface IAutonomousTradeStateStore
    {
        AutonomousTradeState Load(int characterId);
        void Save(AutonomousTradeState state);
        void Delete(int characterId);
    }

    public sealed class DatabaseAutonomousTradeStateStore : IAutonomousTradeStateStore
    {
        public AutonomousTradeState Load(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));

            var record = Db.Query()
                .CommandText(@"select character_id, item_definition, quantity_remaining,
                                     unit_cost, source_market_eid, source_base_eid,
                                     source_zone_id, destination_market_eid,
                                     destination_base_eid, destination_zone_id, acquired_at
                              from dbo.ai_trade_state
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            return record == null
                ? null
                : new AutonomousTradeState(
                    record.GetValue<int>("character_id"),
                    record.GetValue<int>("item_definition"),
                    record.GetValue<int>("quantity_remaining"),
                    record.GetValue<double>("unit_cost"),
                    record.GetValue<long>("source_market_eid"),
                    record.GetValue<long>("source_base_eid"),
                    record.GetValue<int>("source_zone_id"),
                    record.GetValue<long>("destination_market_eid"),
                    record.GetValue<long>("destination_base_eid"),
                    record.GetValue<int>("destination_zone_id"),
                    record.GetValue<DateTime>("acquired_at"));
        }

        public void Save(AutonomousTradeState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (state.CharacterId <= 0 || state.Definition <= 0 || state.QuantityRemaining <= 0 ||
                state.UnitCost <= 0 || double.IsNaN(state.UnitCost) || double.IsInfinity(state.UnitCost))
                throw new ArgumentOutOfRangeException(nameof(state));

            Db.Query()
                .CommandText(@"update dbo.ai_trade_state
                              set item_definition = @definition,
                                  quantity_remaining = @quantity,
                                  unit_cost = @unitCost,
                                  source_market_eid = @sourceMarketEid,
                                  source_base_eid = @sourceBaseEid,
                                  source_zone_id = @sourceZoneId,
                                  destination_market_eid = @destinationMarketEid,
                                  destination_base_eid = @destinationBaseEid,
                                  destination_zone_id = @destinationZoneId,
                                  acquired_at = @acquiredAt,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_trade_state
                                      (character_id, item_definition, quantity_remaining,
                                       unit_cost, source_market_eid, source_base_eid,
                                       source_zone_id, destination_market_eid,
                                       destination_base_eid, destination_zone_id, acquired_at)
                                  values
                                      (@characterId, @definition, @quantity,
                                       @unitCost, @sourceMarketEid, @sourceBaseEid,
                                       @sourceZoneId, @destinationMarketEid,
                                       @destinationBaseEid, @destinationZoneId, @acquiredAt);
                              end;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@definition", state.Definition)
                .SetParameter("@quantity", state.QuantityRemaining)
                .SetParameter("@unitCost", state.UnitCost)
                .SetParameter("@sourceMarketEid", state.SourceMarketEid)
                .SetParameter("@sourceBaseEid", state.SourceBaseEid)
                .SetParameter("@sourceZoneId", state.SourceZoneId)
                .SetParameter("@destinationMarketEid", state.DestinationMarketEid)
                .SetParameter("@destinationBaseEid", state.DestinationBaseEid)
                .SetParameter("@destinationZoneId", state.DestinationZoneId)
                .SetParameter("@acquiredAt", state.AcquiredAtUtc)
                .ExecuteNonQuery();
        }

        public void Delete(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            Db.Query()
                .CommandText("delete dbo.ai_trade_state where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteNonQuery();
        }
    }
}
