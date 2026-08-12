using Perpetuum.Data;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousActorState
    {
        public AutonomousActorState(
            int characterId,
            string behaviorName,
            long expectedRobotEid,
            long observedRobotEid,
            bool recoveryRequired,
            string recoveryReason,
            int recoveryRevision)
        {
            CharacterId = characterId;
            BehaviorName = behaviorName;
            ExpectedRobotEid = expectedRobotEid;
            ObservedRobotEid = observedRobotEid;
            RecoveryRequired = recoveryRequired;
            RecoveryReason = recoveryReason;
            RecoveryRevision = recoveryRevision;
        }

        public int CharacterId { get; }
        public string BehaviorName { get; }
        public long ExpectedRobotEid { get; }
        public long ObservedRobotEid { get; }
        public bool RecoveryRequired { get; }
        public string RecoveryReason { get; }
        public int RecoveryRevision { get; }
    }

    public interface IAutonomousActorStateStore
    {
        AutonomousActorState Load(int characterId);
        void Save(AutonomousActorState state);
    }

    public sealed class DatabaseAutonomousActorStateStore : IAutonomousActorStateStore
    {
        public AutonomousActorState Load(int characterId)
        {
            var record = Db.Query()
                .CommandText(@"select character_id, behavior_name, expected_robot_eid,
                                     observed_robot_eid, recovery_required,
                                     recovery_reason, recovery_revision
                              from dbo.ai_actor_state
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            if (record == null)
                return null;

            return new AutonomousActorState(
                record.GetValue<int>("character_id"),
                record.GetValue<string>("behavior_name"),
                record.GetValue<long>("expected_robot_eid"),
                record.GetValue<long>("observed_robot_eid"),
                record.GetValue<bool>("recovery_required"),
                record.GetValue<string>("recovery_reason"),
                record.GetValue<int>("recovery_revision"));
        }

        public void Save(AutonomousActorState state)
        {
            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_actor_state with (updlock, serializable)
                              set behavior_name = @behaviorName,
                                  expected_robot_eid = @expectedRobotEid,
                                  observed_robot_eid = @observedRobotEid,
                                  recovery_required = @recoveryRequired,
                                  recovery_reason = @recoveryReason,
                                  recovery_revision = @recoveryRevision,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_actor_state
                                      (character_id, behavior_name, expected_robot_eid,
                                       observed_robot_eid, recovery_required,
                                       recovery_reason, recovery_revision)
                                  values
                                      (@characterId, @behaviorName, @expectedRobotEid,
                                       @observedRobotEid, @recoveryRequired,
                                       @recoveryReason, @recoveryRevision);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@behaviorName", state.BehaviorName)
                .SetParameter("@expectedRobotEid", state.ExpectedRobotEid > 0 ? (object)state.ExpectedRobotEid : null)
                .SetParameter("@observedRobotEid", state.ObservedRobotEid > 0 ? (object)state.ObservedRobotEid : null)
                .SetParameter("@recoveryRequired", state.RecoveryRequired)
                .SetParameter("@recoveryReason", state.RecoveryReason)
                .SetParameter("@recoveryRevision", state.RecoveryRevision)
                .ExecuteNonQuery();
        }
    }
}
