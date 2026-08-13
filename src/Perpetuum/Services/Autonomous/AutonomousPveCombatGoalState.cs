using System;
using Perpetuum.Data;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousPveCombatGoalState
    {
        public AutonomousPveCombatGoalState(
            int characterId,
            int targetCount,
            int completedCount,
            int maxLosses,
            int lossCount,
            string phase,
            DateTime phaseStartedAt,
            long targetEid = 0,
            long lockId = 0,
            long lastLostRobotEid = 0,
            string blockedReason = null,
            DateTime? engagementStartedAt = null,
            string assignmentKey = null,
            int revision = 0)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            if (targetCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetCount));
            if (completedCount < 0 || completedCount > targetCount)
                throw new ArgumentOutOfRangeException(nameof(completedCount));
            if (maxLosses <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxLosses));
            if (lossCount < 0)
                throw new ArgumentOutOfRangeException(nameof(lossCount));
            if (string.IsNullOrWhiteSpace(phase))
                throw new ArgumentException("A combat goal phase is required.", nameof(phase));
            if (targetEid < 0)
                throw new ArgumentOutOfRangeException(nameof(targetEid));
            if (lockId < 0)
                throw new ArgumentOutOfRangeException(nameof(lockId));
            if (lastLostRobotEid < 0)
                throw new ArgumentOutOfRangeException(nameof(lastLostRobotEid));
            if (assignmentKey != null && assignmentKey.Length > 128)
                throw new ArgumentOutOfRangeException(nameof(assignmentKey));
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            CharacterId = characterId;
            TargetCount = targetCount;
            CompletedCount = completedCount;
            MaxLosses = maxLosses;
            LossCount = lossCount;
            Phase = phase;
            PhaseStartedAt = phaseStartedAt;
            TargetEid = targetEid;
            LockId = lockId;
            LastLostRobotEid = lastLostRobotEid;
            BlockedReason = blockedReason;
            EngagementStartedAt = engagementStartedAt;
            AssignmentKey = assignmentKey;
            Revision = revision;
        }

        public int CharacterId { get; }
        public int TargetCount { get; }
        public int CompletedCount { get; }
        public int MaxLosses { get; }
        public int LossCount { get; }
        public string Phase { get; }
        public DateTime PhaseStartedAt { get; }
        public long TargetEid { get; }
        public long LockId { get; }
        public long LastLostRobotEid { get; }
        public string BlockedReason { get; }
        public DateTime? EngagementStartedAt { get; }
        public string AssignmentKey { get; }
        public int Revision { get; }
        public bool Complete => CompletedCount >= TargetCount;
        public bool LossBudgetReached => LossCount >= MaxLosses;
    }

    public interface IAutonomousPveCombatGoalStore
    {
        AutonomousPveCombatGoalState Load(int characterId);
        void Save(AutonomousPveCombatGoalState state);
        void Delete(int characterId);
    }

    public sealed class DatabaseAutonomousPveCombatGoalStore : IAutonomousPveCombatGoalStore
    {
        public AutonomousPveCombatGoalState Load(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            var record = Db.Query()
                .CommandText(@"select character_id, target_count, completed_count,
                                     max_losses, loss_count, phase, target_eid,
                                     lock_id, last_lost_robot_eid, blocked_reason,
                                     phase_started_at, engagement_started_at,
                                     assignment_key,
                                     goal_revision
                              from dbo.ai_combat_goal
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            return record == null
                ? null
                : new AutonomousPveCombatGoalState(
                    record.GetValue<int>("character_id"),
                    record.GetValue<int>("target_count"),
                    record.GetValue<int>("completed_count"),
                    record.GetValue<int>("max_losses"),
                    record.GetValue<int>("loss_count"),
                    record.GetValue<string>("phase"),
                    record.GetValue<DateTime>("phase_started_at"),
                    record.GetValue<long?>("target_eid") ?? 0,
                    record.GetValue<long?>("lock_id") ?? 0,
                    record.GetValue<long?>("last_lost_robot_eid") ?? 0,
                    record.GetValue<string>("blocked_reason"),
                    record.GetValue<DateTime?>("engagement_started_at"),
                    record.GetValue<string>("assignment_key"),
                    record.GetValue<int>("goal_revision"));
        }

        public void Save(AutonomousPveCombatGoalState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_combat_goal with (updlock, serializable)
                              set target_count = @targetCount,
                                  completed_count = @completedCount,
                                  max_losses = @maxLosses,
                                  loss_count = @lossCount,
                                  phase = @phase,
                                  target_eid = @targetEid,
                                  lock_id = @lockId,
                                  last_lost_robot_eid = @lastLostRobotEid,
                                  blocked_reason = @blockedReason,
                                  phase_started_at = @phaseStartedAt,
                                  engagement_started_at = @engagementStartedAt,
                                  assignment_key = @assignmentKey,
                                  goal_revision = @revision,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_combat_goal
                                      (character_id, target_count, completed_count,
                                       max_losses, loss_count, phase, target_eid,
                                       lock_id, last_lost_robot_eid, blocked_reason,
                                       phase_started_at, engagement_started_at,
                                       assignment_key,
                                       goal_revision)
                                  values
                                      (@characterId, @targetCount, @completedCount,
                                       @maxLosses, @lossCount, @phase, @targetEid,
                                       @lockId, @lastLostRobotEid, @blockedReason,
                                       @phaseStartedAt, @engagementStartedAt,
                                       @assignmentKey,
                                       @revision);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@targetCount", state.TargetCount)
                .SetParameter("@completedCount", state.CompletedCount)
                .SetParameter("@maxLosses", state.MaxLosses)
                .SetParameter("@lossCount", state.LossCount)
                .SetParameter("@phase", state.Phase)
                .SetParameter("@targetEid", state.TargetEid > 0 ? (object)state.TargetEid : null)
                .SetParameter("@lockId", state.LockId > 0 ? (object)state.LockId : null)
                .SetParameter("@lastLostRobotEid", state.LastLostRobotEid > 0 ? (object)state.LastLostRobotEid : null)
                .SetParameter("@blockedReason", state.BlockedReason)
                .SetParameter("@phaseStartedAt", state.PhaseStartedAt)
                .SetParameter("@engagementStartedAt", state.EngagementStartedAt)
                .SetParameter("@assignmentKey", state.AssignmentKey)
                .SetParameter("@revision", state.Revision)
                .ExecuteNonQuery();
        }

        public void Delete(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            Db.Query()
                .CommandText("delete dbo.ai_combat_goal where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteNonQuery();
        }
    }
}
