using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.Actions;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousPlayerRoleDisposition
    {
        Continue,
        Completed,
        Yielded
    }

    public static class AutonomousPlayerPolicy
    {
        public static AutonomousPlayerRoleDisposition Evaluate(
            bool isDocked,
            bool hasOutstandingCommitment,
            bool completed,
            TimeSpan roleElapsed,
            int minimumRoleSeconds,
            int maximumRoleSeconds)
        {
            if (!isDocked || hasOutstandingCommitment ||
                roleElapsed < TimeSpan.FromSeconds(minimumRoleSeconds))
                return AutonomousPlayerRoleDisposition.Continue;
            if (completed)
                return AutonomousPlayerRoleDisposition.Completed;
            return roleElapsed >= TimeSpan.FromSeconds(maximumRoleSeconds)
                ? AutonomousPlayerRoleDisposition.Yielded
                : AutonomousPlayerRoleDisposition.Continue;
        }
    }

    public delegate IAutonomousActorBehavior AutonomousPlayerRoleBehaviorFactory(
        string role,
        AutonomousActorDefinition definition);

    public interface IAutonomousPlayerCharacterObservationService
    {
        bool IsDocked(GameActionContext context);
    }

    public sealed class AutonomousPlayerCharacterObservationService :
        IAutonomousPlayerCharacterObservationService
    {
        public bool IsDocked(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            return context.Actor.IsDocked;
        }
    }

    /// <summary>
    /// Runs the existing authoritative autonomous role behaviors as one durable
    /// breadth-first player loop. This coordinator schedules policy only. Its
    /// children remain solely responsible for observing, quoting and executing
    /// ordinary character-bound gameplay actions.
    /// </summary>
    public sealed class PlayerAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private readonly AutonomousActorDefinition _definition;
        private readonly AutonomousPlayerRoleBehaviorFactory _roles;
        private readonly IAutonomousPlayerStateStore _states;
        private readonly IAutonomousMissionGoalStore _missionGoals;
        private readonly IAutonomousEquipmentGoalStore _equipmentGoals;
        private readonly IAutonomousIndustryGoalStore _industryGoals;
        private readonly IAutonomousTradeStateStore _tradeStates;
        private readonly IAutonomousPlayerCharacterObservationService _character;
        private readonly IAutonomousActorAudit _audit;
        private IReadOnlyList<string> _plan;
        private AutonomousPlayerState _state;
        private IAutonomousActorBehavior _active;

        public PlayerAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            AutonomousPlayerRoleBehaviorFactory roles,
            IAutonomousPlayerStateStore states,
            IAutonomousMissionGoalStore missionGoals,
            IAutonomousEquipmentGoalStore equipmentGoals,
            IAutonomousIndustryGoalStore industryGoals,
            IAutonomousTradeStateStore tradeStates,
            IAutonomousPlayerCharacterObservationService character,
            IAutonomousActorAudit audit)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _roles = roles ?? throw new ArgumentNullException(nameof(roles));
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _missionGoals = missionGoals ?? throw new ArgumentNullException(nameof(missionGoals));
            _equipmentGoals = equipmentGoals ?? throw new ArgumentNullException(nameof(equipmentGoals));
            _industryGoals = industryGoals ?? throw new ArgumentNullException(nameof(industryGoals));
            _tradeStates = tradeStates ?? throw new ArgumentNullException(nameof(tradeStates));
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public string Name => "player";

        public void Start(GameActionContext context)
        {
            _definition.Player.Validate(_definition.CharacterId);
            _plan = _definition.Player.GetRoles();
            string planKey = string.Join(",", _plan);
            _state = _states.Load(context.Actor.Id);
            if (_state == null)
            {
                _state = new AutonomousPlayerState(
                    context.Actor.Id,
                    planKey,
                    0,
                    _plan[0],
                    DateTime.UtcNow);
                _states.Save(_state);
            }
            else if (!string.Equals(_state.PlanKey, planKey, StringComparison.Ordinal) ||
                     _state.RoleIndex >= _plan.Count ||
                     !string.Equals(_state.ActiveRole, _plan[_state.RoleIndex], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Persistent player role plan for character {context.Actor.Id} does not match configuration.");
            }

            StartActive(context);
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (_active == null || _state == null)
                throw new InvalidOperationException("Player behavior has not been started.");

            _active.Update(context, elapsed);
            bool isDocked = _character.IsDocked(context);
            if (!isDocked && !_state.ObservedWorldWork)
            {
                _state = _state.WithWorldWorkObserved();
                _states.Save(_state);
            }

            bool completed = IsCompleted(context.Actor.Id, _state.ActiveRole);
            bool commitment = HasOutstandingCommitment(context.Actor.Id, _state.ActiveRole);
            AutonomousPlayerRoleDisposition disposition = AutonomousPlayerPolicy.Evaluate(
                isDocked,
                commitment,
                completed,
                DateTime.UtcNow - _state.RoleStartedAtUtc,
                _definition.Player.MinimumRoleSeconds,
                _definition.Player.MaximumRoleSeconds);
            if (disposition == AutonomousPlayerRoleDisposition.Continue)
                return;

            Transition(context, disposition);
        }

        public void Stop(GameActionContext context)
        {
            _active?.Stop(context);
            _active = null;
        }

        private bool IsCompleted(int characterId, string role)
        {
            switch (role)
            {
                case "equipment":
                    return string.Equals(
                        _equipmentGoals.Load(characterId)?.Phase,
                        "ready",
                        StringComparison.OrdinalIgnoreCase);
                case "mission":
                    string missionPhase = _missionGoals.Load(characterId)?.Phase;
                    return string.Equals(missionPhase, "complete", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(missionPhase, "progression_complete", StringComparison.OrdinalIgnoreCase);
                case "mining":
                    return _state.ObservedWorldWork;
                case "trader":
                    return _state.ObservedWorldWork && _tradeStates.Load(characterId) == null;
                case "manufacturer":
                    string phase = _industryGoals.Load(characterId)?.Phase;
                    return string.Equals(phase, "Complete", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(phase, "WaitingDemand", StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private bool HasOutstandingCommitment(int characterId, string role)
        {
            switch (role)
            {
                case "mission":
                    AutonomousMissionGoalState mission = _missionGoals.Load(characterId);
                    return mission?.MissionGuid.HasValue == true && !mission.Complete;
                case "trader":
                    return _tradeStates.Load(characterId) != null;
                default:
                    return false;
            }
        }

        private void Transition(
            GameActionContext context,
            AutonomousPlayerRoleDisposition disposition)
        {
            string previousRole = _state.ActiveRole;
            _active.Stop(context);

            int nextIndex = (_state.RoleIndex + 1) % _plan.Count;
            bool completed = disposition == AutonomousPlayerRoleDisposition.Completed;
            string reason = completed ? "role_completed" : "role_yielded";
            _state = _state.BeginRole(
                nextIndex,
                _plan[nextIndex],
                DateTime.UtcNow,
                completed,
                completed && nextIndex == 0,
                $"{previousRole}_{reason}");
            _states.Save(_state);

            if (completed && previousRole == "mission" && _definition.Player.RepeatCompletedMissions)
                _missionGoals.Delete(context.Actor.Id);

            _audit.Write(
                context.Actor.Id,
                reason,
                AutonomousActorStatus.Active,
                $"{previousRole}_to_{_state.ActiveRole}");
            StartActive(context);
        }

        private void StartActive(GameActionContext context)
        {
            _active = _roles(_state.ActiveRole, _definition);
            _active.Start(context);
            _audit.Write(
                context.Actor.Id,
                "role_started",
                AutonomousActorStatus.Active,
                _state.ActiveRole);
        }
    }
}
