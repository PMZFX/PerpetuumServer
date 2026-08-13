using System;
using Perpetuum.Data;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousEquipmentGoalState
    {
        public AutonomousEquipmentGoalState(
            int characterId,
            int robotDefinition,
            string phase,
            long robotEid = 0,
            string blockedReason = null,
            int revision = 0)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            if (robotDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(robotDefinition));
            if (string.IsNullOrWhiteSpace(phase))
                throw new ArgumentException("An equipment goal phase is required.", nameof(phase));
            if (robotEid < 0)
                throw new ArgumentOutOfRangeException(nameof(robotEid));
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            CharacterId = characterId;
            RobotDefinition = robotDefinition;
            Phase = phase;
            RobotEid = robotEid;
            BlockedReason = blockedReason;
            Revision = revision;
        }

        public int CharacterId { get; }
        public int RobotDefinition { get; }
        public string Phase { get; }
        public long RobotEid { get; }
        public string BlockedReason { get; }
        public int Revision { get; }

        public AutonomousEquipmentGoalState WithProgress(
            string phase,
            long robotEid = 0,
            string blockedReason = null)
        {
            return new AutonomousEquipmentGoalState(
                CharacterId,
                RobotDefinition,
                phase,
                robotEid,
                blockedReason,
                Revision + 1);
        }
    }

    public interface IAutonomousEquipmentGoalStore
    {
        AutonomousEquipmentGoalState Load(int characterId);
        void Save(AutonomousEquipmentGoalState state);
        void Delete(int characterId);
    }

    public sealed class DatabaseAutonomousEquipmentGoalStore : IAutonomousEquipmentGoalStore
    {
        public AutonomousEquipmentGoalState Load(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));

            var record = Db.Query()
                .CommandText(@"select character_id, robot_definition, phase,
                                     robot_eid, blocked_reason, goal_revision
                              from dbo.ai_equipment_goal
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            return record == null
                ? null
                : new AutonomousEquipmentGoalState(
                    record.GetValue<int>("character_id"),
                    record.GetValue<int>("robot_definition"),
                    record.GetValue<string>("phase"),
                    record.GetValue<long>("robot_eid"),
                    record.GetValue<string>("blocked_reason"),
                    record.GetValue<int>("goal_revision"));
        }

        public void Save(AutonomousEquipmentGoalState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_equipment_goal with (updlock, serializable)
                              set robot_definition = @robotDefinition,
                                  phase = @phase,
                                  robot_eid = @robotEid,
                                  blocked_reason = @blockedReason,
                                  goal_revision = @revision,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_equipment_goal
                                      (character_id, robot_definition, phase, robot_eid,
                                       blocked_reason, goal_revision)
                                  values
                                      (@characterId, @robotDefinition, @phase, @robotEid,
                                       @blockedReason, @revision);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@robotDefinition", state.RobotDefinition)
                .SetParameter("@phase", state.Phase)
                .SetParameter("@robotEid", state.RobotEid > 0 ? (object)state.RobotEid : null)
                .SetParameter("@blockedReason", state.BlockedReason)
                .SetParameter("@revision", state.Revision)
                .ExecuteNonQuery();
        }

        public void Delete(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            Db.Query()
                .CommandText("delete dbo.ai_equipment_goal where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteNonQuery();
        }
    }
}
