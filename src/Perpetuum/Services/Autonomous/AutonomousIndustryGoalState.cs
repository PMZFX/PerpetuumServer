using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Perpetuum.Data;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousIndustryGoalState
    {
        public AutonomousIndustryGoalState(
            int characterId,
            int targetDefinition,
            long targetQuantity,
            long initialInventoryQuantity,
            long millFacilityEid,
            string phase,
            int? lineId = null,
            int? productionId = null,
            IReadOnlyDictionary<int, long> procurement = null,
            string blockedReason = null,
            long researchFacilityEid = 0,
            long prototypeFacilityEid = 0,
            long refineryFacilityEid = 0,
            long committedDemandQuantity = 0)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            if (targetDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetDefinition));
            if (targetQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetQuantity));
            if (initialInventoryQuantity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialInventoryQuantity));
            if (millFacilityEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(millFacilityEid));
            if (researchFacilityEid < 0)
                throw new ArgumentOutOfRangeException(nameof(researchFacilityEid));
            if (prototypeFacilityEid < 0)
                throw new ArgumentOutOfRangeException(nameof(prototypeFacilityEid));
            if (refineryFacilityEid < 0)
                throw new ArgumentOutOfRangeException(nameof(refineryFacilityEid));
            if (committedDemandQuantity < 0 || committedDemandQuantity > targetQuantity)
                throw new ArgumentOutOfRangeException(nameof(committedDemandQuantity));
            if (string.IsNullOrWhiteSpace(phase))
                throw new ArgumentException("An industry goal phase is required.", nameof(phase));
            if (lineId <= 0)
                throw new ArgumentOutOfRangeException(nameof(lineId));
            if (productionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(productionId));

            IReadOnlyDictionary<int, long> normalizedProcurement =
                (procurement ?? new Dictionary<int, long>())
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (normalizedProcurement.Any(pair => pair.Key <= 0 || pair.Value <= 0))
                throw new ArgumentOutOfRangeException(nameof(procurement));

            CharacterId = characterId;
            TargetDefinition = targetDefinition;
            TargetQuantity = targetQuantity;
            InitialInventoryQuantity = initialInventoryQuantity;
            MillFacilityEid = millFacilityEid;
            ResearchFacilityEid = researchFacilityEid;
            PrototypeFacilityEid = prototypeFacilityEid;
            RefineryFacilityEid = refineryFacilityEid;
            CommittedDemandQuantity = committedDemandQuantity;
            Phase = phase;
            LineId = lineId;
            ProductionId = productionId;
            Procurement = normalizedProcurement;
            BlockedReason = blockedReason;
        }

        public int CharacterId { get; }
        public int TargetDefinition { get; }
        public long TargetQuantity { get; }
        public long InitialInventoryQuantity { get; }
        public long MillFacilityEid { get; }
        public long ResearchFacilityEid { get; }
        public long PrototypeFacilityEid { get; }
        public long RefineryFacilityEid { get; }
        public long CommittedDemandQuantity { get; }
        public string Phase { get; }
        public int? LineId { get; }
        public int? ProductionId { get; }
        public IReadOnlyDictionary<int, long> Procurement { get; }
        public string BlockedReason { get; }

        public AutonomousIndustryGoalState WithProgress(
            string phase,
            int? lineId = null,
            int? productionId = null,
            IReadOnlyDictionary<int, long> procurement = null,
            string blockedReason = null)
        {
            return new AutonomousIndustryGoalState(
                CharacterId,
                TargetDefinition,
                TargetQuantity,
                InitialInventoryQuantity,
                MillFacilityEid,
                phase,
                lineId,
                productionId,
                procurement,
                blockedReason,
                ResearchFacilityEid,
                PrototypeFacilityEid,
                RefineryFacilityEid,
                CommittedDemandQuantity);
        }

        public AutonomousIndustryGoalState WithDemand(
            long committedDemandQuantity,
            string phase,
            string blockedReason = null)
        {
            return new AutonomousIndustryGoalState(
                CharacterId,
                TargetDefinition,
                TargetQuantity,
                InitialInventoryQuantity,
                MillFacilityEid,
                phase,
                procurement: Procurement,
                blockedReason: blockedReason,
                researchFacilityEid: ResearchFacilityEid,
                prototypeFacilityEid: PrototypeFacilityEid,
                refineryFacilityEid: RefineryFacilityEid,
                committedDemandQuantity: committedDemandQuantity);
        }
    }

    public interface IAutonomousIndustryGoalStore
    {
        AutonomousIndustryGoalState Load(int characterId);
        void Save(AutonomousIndustryGoalState state);
    }

    public sealed class DatabaseAutonomousIndustryGoalStore : IAutonomousIndustryGoalStore
    {
        public AutonomousIndustryGoalState Load(int characterId)
        {
            var record = Db.Query()
                .CommandText(@"select character_id, target_definition, target_quantity,
                                     initial_inventory_quantity, mill_facility_eid,
                                     research_facility_eid, prototype_facility_eid,
                                     refinery_facility_eid, committed_demand_quantity, phase,
                                     line_id, production_id, procurement_json, blocked_reason
                              from dbo.ai_industry_goal
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            return record == null
                ? null
                : new AutonomousIndustryGoalState(
                    record.GetValue<int>("character_id"),
                    record.GetValue<int>("target_definition"),
                    record.GetValue<long>("target_quantity"),
                    record.GetValue<long>("initial_inventory_quantity"),
                    record.GetValue<long>("mill_facility_eid"),
                    record.GetValue<string>("phase"),
                    record.GetValue<int?>("line_id"),
                    record.GetValue<int?>("production_id"),
                    AutonomousIndustryProcurementCodec.Deserialize(record.GetValue<string>("procurement_json")),
                    record.GetValue<string>("blocked_reason"),
                    record.GetValue<long>("research_facility_eid"),
                    record.GetValue<long>("prototype_facility_eid"),
                    record.GetValue<long>("refinery_facility_eid"),
                    record.GetValue<long>("committed_demand_quantity"));
        }

        public void Save(AutonomousIndustryGoalState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_industry_goal with (updlock, serializable)
                              set target_definition = @targetDefinition,
                                  target_quantity = @targetQuantity,
                                  initial_inventory_quantity = @initialInventoryQuantity,
                                  mill_facility_eid = @millFacilityEid,
                                  research_facility_eid = @researchFacilityEid,
                                  prototype_facility_eid = @prototypeFacilityEid,
                                  refinery_facility_eid = @refineryFacilityEid,
                                  committed_demand_quantity = @committedDemandQuantity,
                                  phase = @phase,
                                  line_id = @lineId,
                                  production_id = @productionId,
                                  procurement_json = @procurementJson,
                                  blocked_reason = @blockedReason,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_industry_goal
                                      (character_id, target_definition, target_quantity,
                                       initial_inventory_quantity, mill_facility_eid, phase,
                                       line_id, production_id, procurement_json, blocked_reason,
                                       research_facility_eid, prototype_facility_eid,
                                       refinery_facility_eid, committed_demand_quantity)
                                  values
                                      (@characterId, @targetDefinition, @targetQuantity,
                                       @initialInventoryQuantity, @millFacilityEid, @phase,
                                       @lineId, @productionId, @procurementJson, @blockedReason,
                                       @researchFacilityEid, @prototypeFacilityEid,
                                       @refineryFacilityEid, @committedDemandQuantity);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@targetDefinition", state.TargetDefinition)
                .SetParameter("@targetQuantity", state.TargetQuantity)
                .SetParameter("@initialInventoryQuantity", state.InitialInventoryQuantity)
                .SetParameter("@millFacilityEid", state.MillFacilityEid)
                .SetParameter("@researchFacilityEid", state.ResearchFacilityEid)
                .SetParameter("@prototypeFacilityEid", state.PrototypeFacilityEid)
                .SetParameter("@refineryFacilityEid", state.RefineryFacilityEid)
                .SetParameter("@committedDemandQuantity", state.CommittedDemandQuantity)
                .SetParameter("@phase", state.Phase)
                .SetParameter("@lineId", state.LineId)
                .SetParameter("@productionId", state.ProductionId)
                .SetParameter("@procurementJson", AutonomousIndustryProcurementCodec.Serialize(state.Procurement))
                .SetParameter("@blockedReason", state.BlockedReason)
                .ExecuteNonQuery();
        }
    }

    public static class AutonomousIndustryProcurementCodec
    {
        public static string Serialize(IReadOnlyDictionary<int, long> procurement)
        {
            if (procurement == null || procurement.Count == 0)
                return null;
            return JsonConvert.SerializeObject(
                procurement.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        public static IReadOnlyDictionary<int, long> Deserialize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new Dictionary<int, long>();
            try
            {
                Dictionary<int, long> result = JsonConvert.DeserializeObject<Dictionary<int, long>>(value);
                return result == null || result.Any(pair => pair.Key <= 0 || pair.Value <= 0)
                    ? new Dictionary<int, long>()
                    : result.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            catch (JsonException)
            {
                return new Dictionary<int, long>();
            }
        }
    }
}
