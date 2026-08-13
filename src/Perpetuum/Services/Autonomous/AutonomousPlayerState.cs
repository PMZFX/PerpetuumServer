using System;
using Perpetuum.Data;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousPlayerState
    {
        public AutonomousPlayerState(
            int characterId,
            string planKey,
            int roleIndex,
            string activeRole,
            DateTime roleStartedAtUtc,
            bool observedWorldWork = false,
            int completedRoles = 0,
            int completedCycles = 0,
            string lastTransitionReason = null,
            int revision = 0)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));
            if (string.IsNullOrWhiteSpace(planKey))
                throw new ArgumentException("A player role plan key is required.", nameof(planKey));
            if (roleIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(roleIndex));
            if (string.IsNullOrWhiteSpace(activeRole))
                throw new ArgumentException("An active player role is required.", nameof(activeRole));
            if (roleStartedAtUtc == default)
                throw new ArgumentOutOfRangeException(nameof(roleStartedAtUtc));
            if (completedRoles < 0)
                throw new ArgumentOutOfRangeException(nameof(completedRoles));
            if (completedCycles < 0)
                throw new ArgumentOutOfRangeException(nameof(completedCycles));
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            CharacterId = characterId;
            PlanKey = planKey;
            RoleIndex = roleIndex;
            ActiveRole = activeRole;
            RoleStartedAtUtc = DateTime.SpecifyKind(roleStartedAtUtc, DateTimeKind.Utc);
            ObservedWorldWork = observedWorldWork;
            CompletedRoles = completedRoles;
            CompletedCycles = completedCycles;
            LastTransitionReason = lastTransitionReason;
            Revision = revision;
        }

        public int CharacterId { get; }
        public string PlanKey { get; }
        public int RoleIndex { get; }
        public string ActiveRole { get; }
        public DateTime RoleStartedAtUtc { get; }
        public bool ObservedWorldWork { get; }
        public int CompletedRoles { get; }
        public int CompletedCycles { get; }
        public string LastTransitionReason { get; }
        public int Revision { get; }

        public AutonomousPlayerState WithWorldWorkObserved()
        {
            if (ObservedWorldWork)
                return this;
            return new AutonomousPlayerState(
                CharacterId,
                PlanKey,
                RoleIndex,
                ActiveRole,
                RoleStartedAtUtc,
                true,
                CompletedRoles,
                CompletedCycles,
                LastTransitionReason,
                Revision + 1);
        }

        public AutonomousPlayerState BeginRole(
            int roleIndex,
            string activeRole,
            DateTime startedAtUtc,
            bool completed,
            bool completedCycle,
            string reason)
        {
            return new AutonomousPlayerState(
                CharacterId,
                PlanKey,
                roleIndex,
                activeRole,
                startedAtUtc,
                completedRoles: CompletedRoles + (completed ? 1 : 0),
                completedCycles: CompletedCycles + (completedCycle ? 1 : 0),
                lastTransitionReason: reason,
                revision: Revision + 1);
        }
    }

    public interface IAutonomousPlayerStateStore
    {
        AutonomousPlayerState Load(int characterId);
        void Save(AutonomousPlayerState state);
    }

    public sealed class DatabaseAutonomousPlayerStateStore : IAutonomousPlayerStateStore
    {
        public AutonomousPlayerState Load(int characterId)
        {
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId));

            var record = Db.Query()
                .CommandText(@"select character_id, plan_key, role_index, active_role,
                                     role_started_at, observed_world_work, completed_roles,
                                     completed_cycles, last_transition_reason, state_revision
                              from dbo.ai_player_state
                              where character_id = @characterId")
                .SetParameter("@characterId", characterId)
                .ExecuteSingleRow();
            return record == null
                ? null
                : new AutonomousPlayerState(
                    record.GetValue<int>("character_id"),
                    record.GetValue<string>("plan_key"),
                    record.GetValue<int>("role_index"),
                    record.GetValue<string>("active_role"),
                    record.GetValue<DateTime>("role_started_at"),
                    record.GetValue<bool>("observed_world_work"),
                    record.GetValue<int>("completed_roles"),
                    record.GetValue<int>("completed_cycles"),
                    record.GetValue<string>("last_transition_reason"),
                    record.GetValue<int>("state_revision"));
        }

        public void Save(AutonomousPlayerState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            Db.Query()
                .CommandText(@"set xact_abort on;
                              begin transaction;
                              update dbo.ai_player_state with (updlock, serializable)
                              set plan_key = @planKey,
                                  role_index = @roleIndex,
                                  active_role = @activeRole,
                                  role_started_at = @roleStartedAt,
                                  observed_world_work = @observedWorldWork,
                                  completed_roles = @completedRoles,
                                  completed_cycles = @completedCycles,
                                  last_transition_reason = @lastTransitionReason,
                                  state_revision = @revision,
                                  updated_at = sysutcdatetime()
                              where character_id = @characterId;
                              if @@rowcount = 0
                              begin
                                  insert dbo.ai_player_state
                                      (character_id, plan_key, role_index, active_role,
                                       role_started_at, observed_world_work, completed_roles,
                                       completed_cycles, last_transition_reason, state_revision)
                                  values
                                      (@characterId, @planKey, @roleIndex, @activeRole,
                                       @roleStartedAt, @observedWorldWork, @completedRoles,
                                       @completedCycles, @lastTransitionReason, @revision);
                              end;
                              commit transaction;")
                .SetParameter("@characterId", state.CharacterId)
                .SetParameter("@planKey", state.PlanKey)
                .SetParameter("@roleIndex", state.RoleIndex)
                .SetParameter("@activeRole", state.ActiveRole)
                .SetParameter("@roleStartedAt", state.RoleStartedAtUtc)
                .SetParameter("@observedWorldWork", state.ObservedWorldWork)
                .SetParameter("@completedRoles", state.CompletedRoles)
                .SetParameter("@completedCycles", state.CompletedCycles)
                .SetParameter("@lastTransitionReason", state.LastTransitionReason)
                .SetParameter("@revision", state.Revision)
                .ExecuteNonQuery();
        }
    }
}
