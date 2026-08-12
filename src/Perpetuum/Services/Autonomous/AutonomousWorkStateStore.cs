using Perpetuum.Data;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousWorkState
    {
        public AutonomousWorkState(
            int characterId,
            string behaviorName,
            string phase,
            long dockingBaseEid,
            int? zoneId,
            Position? origin,
            Position? target,
            MaterialType materialType,
            int surveySiteIndex = 0)
        {
            CharacterId = characterId;
            BehaviorName = behaviorName;
            Phase = phase;
            DockingBaseEid = dockingBaseEid;
            ZoneId = zoneId;
            Origin = origin;
            Target = target;
            MaterialType = materialType;
            SurveySiteIndex = surveySiteIndex;
        }

        public int CharacterId { get; }
        public string BehaviorName { get; }
        public string Phase { get; }
        public long DockingBaseEid { get; }
        public int? ZoneId { get; }
        public Position? Origin { get; }
        public Position? Target { get; }
        public MaterialType MaterialType { get; }
        public int SurveySiteIndex { get; }
    }

    public interface IAutonomousWorkStateStore
    {
        AutonomousWorkState Load(int characterId);
        void Save(AutonomousWorkState state);
    }

    public sealed class DatabaseAutonomousWorkStateStore : IAutonomousWorkStateStore
    {
        public AutonomousWorkState Load(int characterId)
        {
            var record = Db.Query()
                .CommandText(@"select character_id, behavior_name, phase,
                                     docking_base_eid, zone_id,
                                     origin_x, origin_y, target_x, target_y,
                                     material_type, survey_site_index
                              from dbo.ai_actor_work_state
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            if (record == null)
                return null;

            double? originX = record.GetValue<double?>("origin_x");
            double? originY = record.GetValue<double?>("origin_y");
            double? targetX = record.GetValue<double?>("target_x");
            double? targetY = record.GetValue<double?>("target_y");
            return new AutonomousWorkState(
                record.GetValue<int>("character_id"),
                record.GetValue<string>("behavior_name"),
                record.GetValue<string>("phase"),
                record.GetValue<long?>("docking_base_eid") ?? 0,
                record.GetValue<int?>("zone_id"),
                ToPosition(originX, originY),
                ToPosition(targetX, targetY),
                (MaterialType)(record.GetValue<int?>("material_type") ?? 0),
                record.GetValue<int?>("survey_site_index") ?? 0);
        }

        public void Save(AutonomousWorkState state)
        {
            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_actor_work_state with (updlock, serializable)
                              set behavior_name = @behaviorName,
                                  phase = @phase,
                                  docking_base_eid = @dockingBaseEid,
                                  zone_id = @zoneId,
                                  origin_x = @originX,
                                  origin_y = @originY,
                                  target_x = @targetX,
                                  target_y = @targetY,
                                  material_type = @materialType,
                                  survey_site_index = @surveySiteIndex,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_actor_work_state
                                      (character_id, behavior_name, phase,
                                       docking_base_eid, zone_id,
                                       origin_x, origin_y, target_x, target_y,
                                       material_type, survey_site_index)
                                  values
                                      (@characterId, @behaviorName, @phase,
                                       @dockingBaseEid, @zoneId,
                                       @originX, @originY, @targetX, @targetY,
                                       @materialType, @surveySiteIndex);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@behaviorName", state.BehaviorName)
                .SetParameter("@phase", state.Phase)
                .SetParameter("@dockingBaseEid", NullablePositive(state.DockingBaseEid))
                .SetParameter("@zoneId", state.ZoneId)
                .SetParameter("@originX", state.Origin.HasValue ? (object)state.Origin.Value.X : null)
                .SetParameter("@originY", state.Origin.HasValue ? (object)state.Origin.Value.Y : null)
                .SetParameter("@targetX", state.Target.HasValue ? (object)state.Target.Value.X : null)
                .SetParameter("@targetY", state.Target.HasValue ? (object)state.Target.Value.Y : null)
                .SetParameter("@materialType", state.MaterialType == MaterialType.Undefined
                    ? null
                    : (object)(int)state.MaterialType)
                .SetParameter("@surveySiteIndex", state.SurveySiteIndex)
                .ExecuteNonQuery();
        }

        private static Position? ToPosition(double? x, double? y)
        {
            return x.HasValue && y.HasValue ? new Position(x.Value, y.Value) : (Position?)null;
        }

        private static object NullablePositive(long value)
        {
            return value > 0 ? (object)value : null;
        }
    }
}
