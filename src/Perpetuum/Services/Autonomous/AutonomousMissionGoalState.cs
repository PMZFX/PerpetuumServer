using System;
using Perpetuum.Data;
using Perpetuum.Services.MissionEngine;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousMissionGoalState
    {
        public AutonomousMissionGoalState(
            int characterId,
            MissionCategory category,
            int level,
            int targetCount,
            int completedCount,
            string phase,
            long sourceEid,
            Guid? missionGuid = null,
            long targetEid = 0,
            string blockedReason = null,
            int revision = 0)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            if (!Enum.IsDefined(typeof(MissionCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));
            if (level < -1 || level > 9)
                throw new ArgumentOutOfRangeException(nameof(level));
            if (targetCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetCount));
            if (completedCount < 0 || completedCount > targetCount)
                throw new ArgumentOutOfRangeException(nameof(completedCount));
            if (string.IsNullOrWhiteSpace(phase))
                throw new ArgumentException("A mission goal phase is required.", nameof(phase));
            if (sourceEid <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceEid));
            if (targetEid < 0)
                throw new ArgumentOutOfRangeException(nameof(targetEid));
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            CharacterId = characterId;
            Category = category;
            Level = level;
            TargetCount = targetCount;
            CompletedCount = completedCount;
            Phase = phase;
            SourceEid = sourceEid;
            MissionGuid = missionGuid;
            TargetEid = targetEid;
            BlockedReason = blockedReason;
            Revision = revision;
        }

        public int CharacterId { get; }
        public MissionCategory Category { get; }
        public int Level { get; }
        public int TargetCount { get; }
        public int CompletedCount { get; }
        public string Phase { get; }
        public long SourceEid { get; }
        public Guid? MissionGuid { get; }
        public long TargetEid { get; }
        public string BlockedReason { get; }
        public int Revision { get; }
        public bool Complete => CompletedCount >= TargetCount;

        public AutonomousMissionGoalState WithProgress(
            string phase,
            Guid? missionGuid = null,
            long targetEid = 0,
            string blockedReason = null,
            int? completedCount = null)
        {
            return new AutonomousMissionGoalState(
                CharacterId,
                Category,
                Level,
                TargetCount,
                completedCount ?? CompletedCount,
                phase,
                SourceEid,
                missionGuid,
                targetEid,
                blockedReason,
                Revision + 1);
        }
    }

    public interface IAutonomousMissionGoalStore
    {
        AutonomousMissionGoalState Load(int characterId);
        void Save(AutonomousMissionGoalState state);
        void Delete(int characterId);
    }

    public sealed class DatabaseAutonomousMissionGoalStore : IAutonomousMissionGoalStore
    {
        public AutonomousMissionGoalState Load(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            var record = Db.Query()
                .CommandText(@"select character_id, mission_category, mission_level,
                                     target_count, completed_count, phase, mission_guid,
                                     source_eid, target_eid, blocked_reason, goal_revision
                              from dbo.ai_mission_goal
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            return record == null
                ? null
                : new AutonomousMissionGoalState(
                    record.GetValue<int>("character_id"),
                    (MissionCategory)record.GetValue<int>("mission_category"),
                    record.GetValue<int>("mission_level"),
                    record.GetValue<int>("target_count"),
                    record.GetValue<int>("completed_count"),
                    record.GetValue<string>("phase"),
                    record.GetValue<long>("source_eid"),
                    record.GetValue<Guid?>("mission_guid"),
                    record.GetValue<long?>("target_eid") ?? 0,
                    record.GetValue<string>("blocked_reason"),
                    record.GetValue<int>("goal_revision"));
        }

        public void Save(AutonomousMissionGoalState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_mission_goal with (updlock, serializable)
                              set mission_category = @category,
                                  mission_level = @level,
                                  target_count = @targetCount,
                                  completed_count = @completedCount,
                                  phase = @phase,
                                  mission_guid = @missionGuid,
                                  source_eid = @sourceEid,
                                  target_eid = @targetEid,
                                  blocked_reason = @blockedReason,
                                  goal_revision = @revision,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_mission_goal
                                      (character_id, mission_category, mission_level,
                                       target_count, completed_count, phase, mission_guid,
                                       source_eid, target_eid, blocked_reason, goal_revision)
                                  values
                                      (@characterId, @category, @level,
                                       @targetCount, @completedCount, @phase, @missionGuid,
                                       @sourceEid, @targetEid, @blockedReason, @revision);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@category", (int)state.Category)
                .SetParameter("@level", state.Level)
                .SetParameter("@targetCount", state.TargetCount)
                .SetParameter("@completedCount", state.CompletedCount)
                .SetParameter("@phase", state.Phase)
                .SetParameter("@missionGuid", state.MissionGuid)
                .SetParameter("@sourceEid", state.SourceEid)
                .SetParameter("@targetEid", state.TargetEid > 0 ? (object)state.TargetEid : null)
                .SetParameter("@blockedReason", state.BlockedReason)
                .SetParameter("@revision", state.Revision)
                .ExecuteNonQuery();
        }

        public void Delete(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            Db.Query()
                .CommandText("delete dbo.ai_mission_goal where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteNonQuery();
        }
    }
}
