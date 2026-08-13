using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MissionEngine;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousMissionUpdateResult
    {
        Waiting,
        Travelling,
        Acted,
        Blocked,
        Complete
    }

    public static class AutonomousMissionPolicy
    {
        public static MissionAvailability SelectAvailability(
            IEnumerable<MissionAvailability> options,
            MissionCategory category,
            int level,
            bool allowRandom = false)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            return options
                .Where(option => (allowRandom || !option.Random) &&
                                 option.Category == category &&
                                 option.Level == level &&
                                 option.Available &&
                                 !option.StandingBlocked)
                .OrderByDescending(option => option.AvailableCount)
                .FirstOrDefault();
        }

        public static bool IsSupported(AutonomousMissionTargetSnapshot target)
        {
            return IsTransportSupported(target) || IsFieldSupported(target);
        }

        public static bool IsTransportSupported(AutonomousMissionTargetSnapshot target)
        {
            return target != null && target.Type == MissionTargetType.fetch_item &&
                   target.Definition > 0 &&
                   target.RemainingQuantity > 0 &&
                   target.DestinationEid > 0;
        }

        public static bool IsFieldSupported(AutonomousMissionTargetSnapshot target)
        {
            if (target == null || !target.HasMapTarget)
                return false;
            if (target.Type == MissionTargetType.reach_position ||
                target.Type == MissionTargetType.pop_npc)
                return true;
            return target.Type == MissionTargetType.kill_definition &&
                   target.Definition > 0 && target.RemainingQuantity > 0;
        }
    }

    public interface IAutonomousMissionController
    {
        void Start(GameActionContext context, AutonomousMissionOptions options);
        AutonomousMissionUpdateResult Update(
            GameActionContext context,
            AutonomousMissionOptions options,
            TimeSpan elapsed);
        void Stop(GameActionContext context);
    }

    public sealed class AutonomousMissionCharacterSnapshot
    {
        public AutonomousMissionCharacterSnapshot(
            bool isDocked,
            long dockingBaseEid,
            int progressionExtensionLevel)
        {
            IsDocked = isDocked;
            DockingBaseEid = dockingBaseEid;
            ProgressionExtensionLevel = progressionExtensionLevel;
        }

        public bool IsDocked { get; }
        public long DockingBaseEid { get; }
        public int ProgressionExtensionLevel { get; }
    }

    public interface IAutonomousMissionCharacterObservationService
    {
        AutonomousMissionCharacterSnapshot Observe(
            GameActionContext context,
            int progressionExtensionId);
    }

    public sealed class AutonomousMissionCharacterObservationService :
        IAutonomousMissionCharacterObservationService
    {
        public AutonomousMissionCharacterSnapshot Observe(
            GameActionContext context,
            int progressionExtensionId)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            return new AutonomousMissionCharacterSnapshot(
                context.Actor.IsDocked,
                context.Actor.IsDocked ? context.Actor.CurrentDockingBaseEid : 0,
                progressionExtensionId > 0
                    ? context.Actor.GetExtensionLevel(progressionExtensionId)
                    : 0);
        }
    }

    /// <summary>
    /// Executes a conservative, durable transport-mission loop. Every update
    /// re-observes authoritative mission and inventory state and performs at
    /// most one ordinary gameplay action. Unsupported target types wait in a
    /// durable blocked phase; they are never auto-completed or aborted.
    /// </summary>
    public sealed class AutonomousMissionController : IAutonomousMissionController
    {
        private readonly IMissionActionService _missions;
        private readonly IAutonomousMissionObservationService _observations;
        private readonly IAutonomousMissionGoalStore _goals;
        private readonly IAutonomousMissionCharacterObservationService _character;
        private readonly IAutonomousEquipmentObservationService _equipment;
        private readonly IAutonomousCargoService _cargo;
        private readonly IRelocateItemsActionService _relocate;
        private readonly IUndockActionService _undock;
        private readonly IAutonomousDestinationTravelService _travel;
        private readonly IAutonomousPositionTravelService _positionTravel;
        private readonly IAutonomousPerceptionService _perception;
        private readonly IAutonomousPveCombatController _combat;
        private readonly IExtensionTrainingActionService _extensions;
        private readonly IAutonomousActorAudit _audit;
        private TimeSpan _dockedElapsed;
        private TimeSpan _retryElapsed;

        public AutonomousMissionController(
            IMissionActionService missions,
            IAutonomousMissionObservationService observations,
            IAutonomousMissionGoalStore goals,
            IAutonomousMissionCharacterObservationService character,
            IAutonomousEquipmentObservationService equipment,
            IAutonomousCargoService cargo,
            IRelocateItemsActionService relocate,
            IUndockActionService undock,
            IAutonomousDestinationTravelService travel,
            IAutonomousPositionTravelService positionTravel,
            IAutonomousPerceptionService perception,
            IAutonomousPveCombatController combat,
            IExtensionTrainingActionService extensions,
            IAutonomousActorAudit audit)
        {
            _missions = missions ?? throw new ArgumentNullException(nameof(missions));
            _observations = observations ?? throw new ArgumentNullException(nameof(observations));
            _goals = goals ?? throw new ArgumentNullException(nameof(goals));
            _character = character ?? throw new ArgumentNullException(nameof(character));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
            _relocate = relocate ?? throw new ArgumentNullException(nameof(relocate));
            _undock = undock ?? throw new ArgumentNullException(nameof(undock));
            _travel = travel ?? throw new ArgumentNullException(nameof(travel));
            _positionTravel = positionTravel ?? throw new ArgumentNullException(nameof(positionTravel));
            _perception = perception ?? throw new ArgumentNullException(nameof(perception));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void Start(GameActionContext context, AutonomousMissionOptions options)
        {
            Validate(context, options);
            _travel.Stop(context);
            _positionTravel.Stop(context);
            _dockedElapsed = TimeSpan.FromSeconds(options.DockedDwellSeconds);
            _retryElapsed = TimeSpan.FromSeconds(options.RetrySeconds);
            LoadOrCreate(context, options);
        }

        public void Stop(GameActionContext context)
        {
            _travel.Stop(context);
            _positionTravel.Stop(context);
            _combat.Stop(context);
            _dockedElapsed = TimeSpan.Zero;
            _retryElapsed = TimeSpan.Zero;
        }

        public AutonomousMissionUpdateResult Update(
            GameActionContext context,
            AutonomousMissionOptions options,
            TimeSpan elapsed)
        {
            Validate(context, options);
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            AutonomousMissionCharacterSnapshot character = _character.Observe(
                context,
                options.ProgressionExtensionId);
            _retryElapsed += elapsed;
            _dockedElapsed = character.IsDocked ? _dockedElapsed + elapsed : TimeSpan.Zero;
            if (character.IsDocked && _travel.Status != AutonomousDestinationTravelStatus.Idle)
                _travel.Stop(context);

            AutonomousMissionGoalState state = LoadOrCreate(context, options);
            try
            {
                IReadOnlyList<AutonomousMissionSnapshot> running = _observations.ObserveRunning(context);
                state = Reconcile(context, state, running);
                if (state.Complete)
                    return HandleProgression(context, options, character, state, elapsed);

                AutonomousMissionSnapshot mission = state.MissionGuid.HasValue
                    ? running.FirstOrDefault(item => item.MissionGuid == state.MissionGuid.Value)
                    : null;
                if (mission == null)
                {
                    if (state.MissionGuid.HasValue)
                        return Wait(ref state,
                            "waiting_for_mission_state",
                            "accepted_mission_not_yet_observable");
                    if (running.Count > 0)
                        return Wait(ref state, "waiting_existing_mission", "another_mission_is_running");
                    return MoveToSourceOrStart(context, options, character, ref state, elapsed);
                }

                AutonomousMissionTargetSnapshot target = mission.CurrentTarget;
                if (!AutonomousMissionPolicy.IsSupported(target))
                {
                    string reason = target == null
                        ? "waiting_for_target_progress"
                        : $"unsupported_target_{target.Type}";
                    return Wait(ref state, "mission_blocked", reason, AutonomousMissionUpdateResult.Blocked);
                }

                long observedTarget = AutonomousMissionPolicy.IsTransportSupported(target)
                    ? target.DestinationEid
                    : target.TargetId;
                bool returningFromCombat = state.Phase.StartsWith(
                    "combat_returning",
                    StringComparison.Ordinal);
                if (state.TargetEid != observedTarget && !returningFromCombat)
                    Write(ref state, state.WithProgress(
                        "mission_observed",
                        mission.MissionGuid,
                        observedTarget));
                return AutonomousMissionPolicy.IsTransportSupported(target)
                    ? MoveCargoTravelOrDeliver(
                        context,
                        options,
                        character,
                        mission,
                        target,
                        ref state,
                        elapsed)
                    : MoveFieldMission(
                        context,
                        options,
                        character,
                        mission,
                        target,
                        ref state,
                        elapsed);
            }
            catch (PerpetuumException exception)
            {
                Write(ref state, state.WithProgress(
                    "mission_action_blocked",
                    state.MissionGuid,
                    state.TargetEid,
                    exception.error.ToString()));
                _audit.Write(context.Actor.Id, "mission_action_blocked", AutonomousActorStatus.Active,
                    exception.error.ToString());
                return AutonomousMissionUpdateResult.Blocked;
            }
        }

        private AutonomousMissionGoalState Reconcile(
            GameActionContext context,
            AutonomousMissionGoalState state,
            IReadOnlyList<AutonomousMissionSnapshot> running)
        {
            if (!state.MissionGuid.HasValue)
            {
                AutonomousMissionSnapshot adopt = running.FirstOrDefault(mission =>
                    mission.Category == state.Category && mission.Level == state.Level);
                if (adopt != null)
                    Write(ref state, state.WithProgress("mission_adopted", adopt.MissionGuid));
                return state;
            }

            if (running.Any(mission => mission.MissionGuid == state.MissionGuid.Value))
                return state;
            AutonomousMissionCompletion completion = _observations.ObserveCompletion(
                context,
                state.MissionGuid.Value);
            if (completion == null || !completion.Finished)
                return state;
            if (!completion.Succeeded)
            {
                Write(ref state, state.WithProgress(
                    "mission_failed",
                    blockedReason: "mission_finished_unsuccessfully"));
                return state;
            }

            int completed = Math.Min(state.TargetCount, state.CompletedCount + 1);
            Write(ref state, state.WithProgress(
                completed >= state.TargetCount ? "mission_goal_complete" : "ready_for_mission",
                completedCount: completed));
            _audit.Write(context.Actor.Id, "mission_completed", AutonomousActorStatus.Active,
                $"completed_{completed}_of_{state.TargetCount}");
            return state;
        }

        private AutonomousMissionUpdateResult MoveToSourceOrStart(
            GameActionContext context,
            AutonomousMissionOptions options,
            AutonomousMissionCharacterSnapshot character,
            ref AutonomousMissionGoalState state,
            TimeSpan elapsed)
        {
            if (!character.IsDocked)
                return Travel(context, options, ref state, state.SourceEid, elapsed);
            if (character.DockingBaseEid != state.SourceEid)
                return LeaveDock(context, options, ref state, state.SourceEid, "travelling_to_mission_source");
            if (!DelayPassed(options.DockedDwellSeconds))
                return AutonomousMissionUpdateResult.Waiting;

            MissionOptionsResult optionsResult = _missions.ObserveOptions(
                context,
                new MissionLocationAction());
            MissionAvailability selected = AutonomousMissionPolicy.SelectAvailability(
                optionsResult.Options,
                state.Category,
                state.Level,
                options.AllowRandom);
            if (selected == null)
                return Wait(ref state, "waiting_for_mission", "mission_unavailable");
            MissionStartResult started = _missions.Start(
                context,
                new MissionStartAction(state.Category, state.Level));
            Write(ref state, state.WithProgress("mission_started", started.MissionGuid));
            ResetDelay();
            _audit.Write(context.Actor.Id, "mission_started", AutonomousActorStatus.Active,
                started.MissionGuid.ToString());
            return AutonomousMissionUpdateResult.Acted;
        }

        private AutonomousMissionUpdateResult MoveFieldMission(
            GameActionContext context,
            AutonomousMissionOptions options,
            AutonomousMissionCharacterSnapshot character,
            AutonomousMissionSnapshot mission,
            AutonomousMissionTargetSnapshot target,
            ref AutonomousMissionGoalState state,
            TimeSpan elapsed)
        {
            if (character.IsDocked)
            {
                _positionTravel.Stop(context);
                if (target.Type == MissionTargetType.kill_definition)
                {
                    _combat.AcknowledgeDockedPreparation(context);
                    _combat.UpdateObjective(
                        context,
                        options.Pve,
                        CreateCombatObjective(options, mission, target));
                    if (!_combat.ShouldDeploy(context.Actor.Id))
                        return Wait(
                            ref state,
                            "combat_deployment_blocked",
                            "combat_goal_not_deployable");
                }
                if (character.DockingBaseEid != state.SourceEid)
                    return LeaveDock(context, options, ref state, state.SourceEid, "combat_returning_to_source");
                return LeaveDock(context, options, ref state, target.TargetId, "combat_deploying");
            }

            if (state.Phase.StartsWith("combat_returning", StringComparison.Ordinal))
                return Travel(context, options, ref state, state.SourceEid, elapsed);

            if (_positionTravel.Status == AutonomousPositionTravelStatus.WorldTravel ||
                _positionTravel.Status == AutonomousPositionTravelStatus.SurfaceTravel)
            {
                AutonomousPositionTravelStatus travelStatus = _positionTravel.Update(context, elapsed);
                if (travelStatus == AutonomousPositionTravelStatus.WorldTravel ||
                    travelStatus == AutonomousPositionTravelStatus.SurfaceTravel)
                    return AutonomousMissionUpdateResult.Travelling;
                if (IsTerminal(travelStatus) && travelStatus != AutonomousPositionTravelStatus.Arrived)
                    return ReturnFromCombat(context, options, ref state, elapsed, travelStatus.ToString());
                _positionTravel.Stop(context);
            }

            AutonomousPerceptionSnapshot world = _perception.Observe(context);
            if (world.Docked || !world.ZoneId.HasValue || !world.Position.HasValue)
                return Wait(ref state, "combat_waiting_for_world", "player_world_unavailable");

            double stagingRange = target.Type == MissionTargetType.kill_definition
                ? Math.Max(target.TargetPositionRange, options.Pve.AcquisitionRange * 0.75)
                : target.TargetPositionRange;
            if (world.ZoneId.Value != target.ZoneId ||
                world.Position.Value.TotalDistance2D(target.TargetPosition.Value) > stagingRange)
            {
                if (!_positionTravel.TryStart(
                        context,
                        target.ZoneId,
                        target.TargetPosition.Value,
                        stagingRange,
                        options.Throttle))
                    return ReturnFromCombat(
                        context,
                        options,
                        ref state,
                        elapsed,
                        _positionTravel.Status.ToString());
                Write(ref state, state.WithProgress(
                    "combat_travelling_to_objective",
                    mission.MissionGuid,
                    target.TargetId));
                return AutonomousMissionUpdateResult.Travelling;
            }

            if (target.Type == MissionTargetType.reach_position ||
                target.Type == MissionTargetType.pop_npc)
                return Wait(ref state, "combat_waiting_for_mission_progress", "position_reached");

            AutonomousPveCombatObjective objective = CreateCombatObjective(options, mission, target);
            AutonomousPveCombatUpdate combat = _combat.UpdateObjective(context, options.Pve, objective);
            switch (combat.Result)
            {
                case AutonomousPveCombatUpdateResult.TargetSelected:
                case AutonomousPveCombatUpdateResult.Approach:
                    if (combat.TargetPosition.HasValue &&
                        _positionTravel.TryStart(
                            context,
                            target.ZoneId,
                            combat.TargetPosition.Value,
                            options.Pve.EngagementRange,
                            options.Throttle))
                    {
                        Write(ref state, state.WithProgress(
                            "combat_approaching_target",
                            mission.MissionGuid,
                            target.TargetId));
                        return AutonomousMissionUpdateResult.Travelling;
                    }
                    return combat.Result == AutonomousPveCombatUpdateResult.TargetSelected
                        ? AutonomousMissionUpdateResult.Acted
                        : AutonomousMissionUpdateResult.Waiting;

                case AutonomousPveCombatUpdateResult.Acted:
                case AutonomousPveCombatUpdateResult.Engaging:
                    Write(ref state, state.WithProgress(
                        "combat_engaging",
                        mission.MissionGuid,
                        target.TargetId));
                    return combat.Result == AutonomousPveCombatUpdateResult.Acted
                        ? AutonomousMissionUpdateResult.Acted
                        : AutonomousMissionUpdateResult.Waiting;

                case AutonomousPveCombatUpdateResult.Complete:
                case AutonomousPveCombatUpdateResult.TargetCompleted:
                    return Wait(
                        ref state,
                        "combat_waiting_for_mission_credit",
                        "authoritative_mission_progress_pending");

                case AutonomousPveCombatUpdateResult.Retreat:
                case AutonomousPveCombatUpdateResult.Resupply:
                case AutonomousPveCombatUpdateResult.LossBudgetReached:
                case AutonomousPveCombatUpdateResult.Blocked:
                    return ReturnFromCombat(
                        context,
                        options,
                        ref state,
                        elapsed,
                        combat.Reason ?? combat.Result.ToString());

                default:
                    return AutonomousMissionUpdateResult.Waiting;
            }
        }

        private static AutonomousPveCombatObjective CreateCombatObjective(
            AutonomousMissionOptions options,
            AutonomousMissionSnapshot mission,
            AutonomousMissionTargetSnapshot target)
        {
            string assignmentKey = string.Concat(
                mission.MissionGuid.ToString("N"),
                ":",
                target.TargetId,
                ":",
                target.Progress);
            return new AutonomousPveCombatObjective(
                assignmentKey,
                target.Definition,
                target.TargetPosition,
                target.TargetPositionRange + options.Pve.AcquisitionRange);
        }

        private AutonomousMissionUpdateResult ReturnFromCombat(
            GameActionContext context,
            AutonomousMissionOptions options,
            ref AutonomousMissionGoalState state,
            TimeSpan elapsed,
            string reason)
        {
            _positionTravel.Stop(context);
            _combat.Stop(context);
            Write(ref state, state.WithProgress(
                "combat_returning",
                state.MissionGuid,
                state.SourceEid,
                reason));
            return Travel(context, options, ref state, state.SourceEid, elapsed);
        }

        private AutonomousMissionUpdateResult MoveCargoTravelOrDeliver(
            GameActionContext context,
            AutonomousMissionOptions options,
            AutonomousMissionCharacterSnapshot character,
            AutonomousMissionSnapshot mission,
            AutonomousMissionTargetSnapshot target,
            ref AutonomousMissionGoalState state,
            TimeSpan elapsed)
        {
            if (!character.IsDocked)
                return Travel(context, options, ref state, target.DestinationEid, elapsed);
            if (character.DockingBaseEid == target.DestinationEid)
            {
                if (!DelayPassed(options.DockedDwellSeconds))
                    return AutonomousMissionUpdateResult.Waiting;
                _missions.Deliver(context, new MissionGuidAction(mission.MissionGuid));
                Write(ref state, state.WithProgress(
                    "mission_delivering",
                    mission.MissionGuid,
                    target.DestinationEid));
                ResetDelay();
                return AutonomousMissionUpdateResult.Acted;
            }

            AutonomousCargoSnapshot cargo = _cargo.Observe(context);
            int cargoQuantity = cargo.Items
                .Where(item => item.Definition == target.Definition)
                .Sum(item => item.Quantity);
            if (cargoQuantity < target.RemainingQuantity)
            {
                AutonomousEquipmentSnapshot equipment = _equipment.Observe(context);
                int missing = target.RemainingQuantity - cargoQuantity;
                long[] items = SelectItems(equipment.LooseItems, target.Definition, missing);
                if (items.Length == 0)
                    return Wait(ref state, "waiting_for_mission_cargo", "mission_cargo_missing");
                _relocate.Execute(
                    context,
                    new RelocateItemsAction(equipment.PublicContainerEid, cargo.ContainerEid, items));
                Write(ref state, state.WithProgress(
                    "mission_cargo_loaded",
                    mission.MissionGuid,
                    target.DestinationEid));
                ResetDelay();
                return AutonomousMissionUpdateResult.Acted;
            }

            return LeaveDock(
                context,
                options,
                ref state,
                target.DestinationEid,
                "travelling_to_mission_target");
        }

        private AutonomousMissionUpdateResult HandleProgression(
            GameActionContext context,
            AutonomousMissionOptions options,
            AutonomousMissionCharacterSnapshot character,
            AutonomousMissionGoalState state,
            TimeSpan elapsed)
        {
            if (options.ProgressionExtensionId <= 0)
            {
                Write(ref state, state.WithProgress("complete"));
                return AutonomousMissionUpdateResult.Complete;
            }
            if (character.ProgressionExtensionLevel >= options.ProgressionExtensionLevel)
            {
                Write(ref state, state.WithProgress("progression_complete"));
                return AutonomousMissionUpdateResult.Complete;
            }
            if (!character.IsDocked)
                return Travel(context, options, ref state, state.SourceEid, elapsed);
            if (!DelayPassed(options.RetrySeconds))
                return AutonomousMissionUpdateResult.Waiting;

            var action = new ExtensionTrainingAction(options.ProgressionExtensionId);
            ExtensionTrainingQuote quote = _extensions.Quote(context, action);
            if (!quote.CanAffordPoints)
                return Wait(ref state, "waiting_for_progression", "not_enough_extension_points");
            if (!quote.CanAffordCredits)
                return Wait(ref state, "waiting_for_progression", "not_enough_credits");
            _extensions.Execute(context, action);
            Write(ref state, state.WithProgress("progression_trained"));
            ResetDelay();
            return AutonomousMissionUpdateResult.Acted;
        }

        private AutonomousMissionUpdateResult LeaveDock(
            GameActionContext context,
            AutonomousMissionOptions options,
            ref AutonomousMissionGoalState state,
            long destination,
            string phase)
        {
            if (!DelayPassed(options.DockedDwellSeconds))
                return AutonomousMissionUpdateResult.Waiting;
            _undock.Execute(context);
            Write(ref state, state.WithProgress(phase, state.MissionGuid, destination));
            ResetDelay();
            return AutonomousMissionUpdateResult.Acted;
        }

        private AutonomousMissionUpdateResult Travel(
            GameActionContext context,
            AutonomousMissionOptions options,
            ref AutonomousMissionGoalState state,
            long destination,
            TimeSpan elapsed)
        {
            if (_travel.TargetBaseEid != destination ||
                _travel.Status == AutonomousDestinationTravelStatus.Idle ||
                IsTerminal(_travel.Status))
            {
                if (IsTerminal(_travel.Status) && _retryElapsed < TimeSpan.FromSeconds(options.RetrySeconds))
                    return AutonomousMissionUpdateResult.Waiting;
                _retryElapsed = TimeSpan.Zero;
                if (!_travel.TryStart(context, destination, options.Throttle))
                {
                    Write(ref state, state.WithProgress(
                        "mission_travel_blocked",
                        state.MissionGuid,
                        destination,
                        _travel.Status.ToString()));
                    return AutonomousMissionUpdateResult.Blocked;
                }
            }
            else
            {
                AutonomousDestinationTravelStatus status = _travel.Update(context, elapsed);
                if (IsTerminal(status) && status != AutonomousDestinationTravelStatus.Arrived)
                {
                    Write(ref state, state.WithProgress(
                        "mission_travel_blocked",
                        state.MissionGuid,
                        destination,
                        status.ToString()));
                    return AutonomousMissionUpdateResult.Blocked;
                }
            }
            return AutonomousMissionUpdateResult.Travelling;
        }

        private AutonomousMissionUpdateResult Wait(
            ref AutonomousMissionGoalState state,
            string phase,
            string reason,
            AutonomousMissionUpdateResult result = AutonomousMissionUpdateResult.Waiting)
        {
            Write(ref state, state.WithProgress(
                phase,
                state.MissionGuid,
                state.TargetEid,
                reason));
            return result;
        }

        private AutonomousMissionGoalState LoadOrCreate(
            GameActionContext context,
            AutonomousMissionOptions options)
        {
            AutonomousMissionGoalState state = _goals.Load(context.Actor.Id);
            MissionCategory category = options.GetCategory();
            if (state != null)
            {
                if (state.Category != category || state.Level != options.Level ||
                    state.TargetCount != options.TargetCount || state.SourceEid != options.SourceBaseEid)
                    throw new InvalidOperationException(
                        $"Persistent mission goal for character {context.Actor.Id} does not match configuration.");
                return state;
            }
            state = new AutonomousMissionGoalState(
                context.Actor.Id,
                category,
                options.Level,
                options.TargetCount,
                0,
                "ready_for_mission",
                options.SourceBaseEid);
            _goals.Save(state);
            return state;
        }

        private void Write(ref AutonomousMissionGoalState state, AutonomousMissionGoalState next)
        {
            if (state.Phase == next.Phase &&
                state.MissionGuid == next.MissionGuid &&
                state.TargetEid == next.TargetEid &&
                state.BlockedReason == next.BlockedReason &&
                state.CompletedCount == next.CompletedCount)
                return;
            _goals.Save(next);
            state = next;
        }

        private bool DelayPassed(int seconds)
        {
            return _dockedElapsed >= TimeSpan.FromSeconds(seconds);
        }

        private void ResetDelay()
        {
            _dockedElapsed = TimeSpan.Zero;
            _retryElapsed = TimeSpan.Zero;
        }

        private static long[] SelectItems(
            IEnumerable<AutonomousEquipmentItemSnapshot> items,
            int definition,
            int quantity)
        {
            var selected = new List<long>();
            int total = 0;
            foreach (AutonomousEquipmentItemSnapshot item in items
                         .Where(item => item.Definition == definition)
                         .OrderBy(item => item.ItemEid))
            {
                selected.Add(item.ItemEid);
                total += item.Quantity;
                if (total >= quantity)
                    break;
            }
            return total >= quantity ? selected.ToArray() : Array.Empty<long>();
        }

        private static bool IsTerminal(AutonomousDestinationTravelStatus status)
        {
            return status == AutonomousDestinationTravelStatus.Arrived ||
                   status == AutonomousDestinationTravelStatus.BaseUnavailable ||
                   status == AutonomousDestinationTravelStatus.RouteUnavailable ||
                   status == AutonomousDestinationTravelStatus.Blocked ||
                   status == AutonomousDestinationTravelStatus.TransitionTimedOut;
        }

        private static bool IsTerminal(AutonomousPositionTravelStatus status)
        {
            return status == AutonomousPositionTravelStatus.Arrived ||
                   status == AutonomousPositionTravelStatus.RouteUnavailable ||
                   status == AutonomousPositionTravelStatus.Blocked ||
                   status == AutonomousPositionTravelStatus.TransitionTimedOut;
        }

        private static void Validate(GameActionContext context, AutonomousMissionOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            options.Validate(context.Actor.Id);
        }
    }
}
