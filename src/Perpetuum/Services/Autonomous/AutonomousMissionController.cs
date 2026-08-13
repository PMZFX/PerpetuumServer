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
            int level)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            return options
                .Where(option => !option.Random &&
                                 option.Category == category &&
                                 option.Level == level &&
                                 option.Available &&
                                 !option.StandingBlocked)
                .OrderByDescending(option => option.AvailableCount)
                .FirstOrDefault();
        }

        public static bool IsSupported(AutonomousMissionTargetSnapshot target)
        {
            return target != null &&
                   target.Type == MissionTargetType.fetch_item &&
                   target.Definition > 0 &&
                   target.RemainingQuantity > 0 &&
                   target.DestinationEid > 0;
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
        private readonly IAutonomousEquipmentObservationService _equipment;
        private readonly IAutonomousCargoService _cargo;
        private readonly IRelocateItemsActionService _relocate;
        private readonly IUndockActionService _undock;
        private readonly IAutonomousDestinationTravelService _travel;
        private readonly IExtensionTrainingActionService _extensions;
        private readonly IAutonomousActorAudit _audit;
        private TimeSpan _dockedElapsed;
        private TimeSpan _retryElapsed;

        public AutonomousMissionController(
            IMissionActionService missions,
            IAutonomousMissionObservationService observations,
            IAutonomousMissionGoalStore goals,
            IAutonomousEquipmentObservationService equipment,
            IAutonomousCargoService cargo,
            IRelocateItemsActionService relocate,
            IUndockActionService undock,
            IAutonomousDestinationTravelService travel,
            IExtensionTrainingActionService extensions,
            IAutonomousActorAudit audit)
        {
            _missions = missions ?? throw new ArgumentNullException(nameof(missions));
            _observations = observations ?? throw new ArgumentNullException(nameof(observations));
            _goals = goals ?? throw new ArgumentNullException(nameof(goals));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
            _relocate = relocate ?? throw new ArgumentNullException(nameof(relocate));
            _undock = undock ?? throw new ArgumentNullException(nameof(undock));
            _travel = travel ?? throw new ArgumentNullException(nameof(travel));
            _extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void Start(GameActionContext context, AutonomousMissionOptions options)
        {
            Validate(context, options);
            _travel.Stop(context);
            _dockedElapsed = TimeSpan.FromSeconds(options.DockedDwellSeconds);
            _retryElapsed = TimeSpan.FromSeconds(options.RetrySeconds);
            LoadOrCreate(context, options);
        }

        public void Stop(GameActionContext context)
        {
            _travel.Stop(context);
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
            _retryElapsed += elapsed;
            _dockedElapsed = context.Actor.IsDocked ? _dockedElapsed + elapsed : TimeSpan.Zero;
            if (context.Actor.IsDocked && _travel.Status != AutonomousDestinationTravelStatus.Idle)
                _travel.Stop(context);

            AutonomousMissionGoalState state = LoadOrCreate(context, options);
            try
            {
                IReadOnlyList<AutonomousMissionSnapshot> running = _observations.ObserveRunning(context);
                state = Reconcile(context, state, running);
                if (state.Complete)
                    return HandleProgression(context, options, state, elapsed);

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
                    return MoveToSourceOrStart(context, options, ref state, elapsed);
                }

                AutonomousMissionTargetSnapshot target = mission.CurrentTarget;
                if (!AutonomousMissionPolicy.IsSupported(target))
                {
                    string reason = target == null
                        ? "waiting_for_target_progress"
                        : $"unsupported_target_{target.Type}";
                    return Wait(ref state, "mission_blocked", reason, AutonomousMissionUpdateResult.Blocked);
                }

                if (state.TargetEid != target.DestinationEid)
                    Write(ref state, state.WithProgress(
                        "mission_observed",
                        mission.MissionGuid,
                        target.DestinationEid));
                return MoveCargoTravelOrDeliver(context, options, mission, target, ref state, elapsed);
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
            ref AutonomousMissionGoalState state,
            TimeSpan elapsed)
        {
            if (!context.Actor.IsDocked)
                return Travel(context, options, ref state, state.SourceEid, elapsed);
            if (context.Actor.CurrentDockingBaseEid != state.SourceEid)
                return LeaveDock(context, options, ref state, state.SourceEid, "travelling_to_mission_source");
            if (!DelayPassed(options.DockedDwellSeconds))
                return AutonomousMissionUpdateResult.Waiting;

            MissionOptionsResult optionsResult = _missions.ObserveOptions(
                context,
                new MissionLocationAction());
            MissionAvailability selected = AutonomousMissionPolicy.SelectAvailability(
                optionsResult.Options,
                state.Category,
                state.Level);
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

        private AutonomousMissionUpdateResult MoveCargoTravelOrDeliver(
            GameActionContext context,
            AutonomousMissionOptions options,
            AutonomousMissionSnapshot mission,
            AutonomousMissionTargetSnapshot target,
            ref AutonomousMissionGoalState state,
            TimeSpan elapsed)
        {
            if (!context.Actor.IsDocked)
                return Travel(context, options, ref state, target.DestinationEid, elapsed);
            if (context.Actor.CurrentDockingBaseEid == target.DestinationEid)
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
            AutonomousMissionGoalState state,
            TimeSpan elapsed)
        {
            if (options.ProgressionExtensionId <= 0)
            {
                Write(ref state, state.WithProgress("complete"));
                return AutonomousMissionUpdateResult.Complete;
            }
            int currentLevel = context.Actor.GetExtensionLevel(options.ProgressionExtensionId);
            if (currentLevel >= options.ProgressionExtensionLevel)
            {
                Write(ref state, state.WithProgress("progression_complete"));
                return AutonomousMissionUpdateResult.Complete;
            }
            if (!context.Actor.IsDocked)
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
