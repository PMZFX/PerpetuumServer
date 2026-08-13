using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.ExportedTypes;
using Perpetuum.Items.Ammos;
using Perpetuum.Modules;
using Perpetuum.Players;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Units;
using Perpetuum.Zones.Locking;
using Perpetuum.Zones.Locking.Locks;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousPveWeaponSnapshot
    {
        public AutonomousPveWeaponSnapshot(
            RobotComponentType component,
            int slot,
            bool ammoable,
            int ammoDefinition,
            int ammoQuantity,
            bool active)
        {
            Component = component;
            Slot = slot;
            Ammoable = ammoable;
            AmmoDefinition = ammoDefinition;
            AmmoQuantity = ammoQuantity;
            Active = active;
        }

        public RobotComponentType Component { get; }
        public int Slot { get; }
        public bool Ammoable { get; }
        public int AmmoDefinition { get; }
        public int AmmoQuantity { get; }
        public bool Active { get; }
        public bool Usable => !Ammoable || AmmoQuantity > 0;
        public bool CanReload => Ammoable && AmmoDefinition > 0 && AmmoQuantity == 0;
    }

    public sealed class AutonomousPveTargetSnapshot
    {
        public AutonomousPveTargetSnapshot(
            long eid,
            Position position,
            double distance,
            bool visible,
            bool hostile,
            bool npc,
            bool dead,
            AutonomousDefenseLockState lockState = AutonomousDefenseLockState.Missing,
            long lockId = 0)
        {
            Eid = eid;
            Position = position;
            Distance = distance;
            Visible = visible;
            Hostile = hostile;
            Npc = npc;
            Dead = dead;
            LockState = lockState;
            LockId = lockId;
        }

        public long Eid { get; }
        public Position Position { get; }
        public double Distance { get; }
        public bool Visible { get; }
        public bool Hostile { get; }
        public bool Npc { get; }
        public bool Dead { get; }
        public AutonomousDefenseLockState LockState { get; }
        public long LockId { get; }
    }

    public sealed class AutonomousPveCombatSnapshot
    {
        public AutonomousPveCombatSnapshot(
            bool worldAvailable,
            double armorRatio,
            double coreRatio,
            IEnumerable<AutonomousPveTargetSnapshot> visibleTargets,
            IEnumerable<AutonomousPveWeaponSnapshot> weapons,
            AutonomousPveTargetSnapshot trackedTarget = null)
        {
            WorldAvailable = worldAvailable;
            ArmorRatio = armorRatio;
            CoreRatio = coreRatio;
            VisibleTargets = (visibleTargets ?? Enumerable.Empty<AutonomousPveTargetSnapshot>())
                .Where(target => target.Visible)
                .OrderBy(target => target.Distance)
                .ThenBy(target => target.Eid)
                .ToArray();
            Weapons = (weapons ?? Enumerable.Empty<AutonomousPveWeaponSnapshot>())
                .OrderBy(weapon => weapon.Component)
                .ThenBy(weapon => weapon.Slot)
                .ToArray();
            TrackedTarget = trackedTarget;
        }

        public bool WorldAvailable { get; }
        public double ArmorRatio { get; }
        public double CoreRatio { get; }
        public IReadOnlyList<AutonomousPveTargetSnapshot> VisibleTargets { get; }
        public IReadOnlyList<AutonomousPveWeaponSnapshot> Weapons { get; }
        public AutonomousPveTargetSnapshot TrackedTarget { get; }
    }

    public interface IAutonomousPveCombatObservationService
    {
        AutonomousPveCombatSnapshot Observe(GameActionContext context, long trackedTargetEid);
    }

    /// <summary>
    /// Projects the controlled player's live armor, core, fitted weapons,
    /// existing locks, and real client-visible NPC set. A tracked target may
    /// only remain observable after leaving that set to determine whether the
    /// already-observed unit died; it cannot be selected or pursued while
    /// hidden.
    /// </summary>
    public sealed class AutonomousPveCombatObservationService : IAutonomousPveCombatObservationService
    {
        private readonly IAutonomousPerceptionService _perception;

        public AutonomousPveCombatObservationService(IAutonomousPerceptionService perception)
        {
            _perception = perception ?? throw new ArgumentNullException(nameof(perception));
        }

        public AutonomousPveCombatSnapshot Observe(GameActionContext context, long trackedTargetEid)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player?.Zone == null)
                return new AutonomousPveCombatSnapshot(false, 0, 0, null, null);

            AutonomousPerceptionSnapshot perception = _perception.Observe(context);
            AutonomousPveTargetSnapshot[] visibleTargets = perception.VisibleUnits
                .Select(unit => new AutonomousPveTargetSnapshot(
                    unit.Eid,
                    unit.Position,
                    unit.Distance,
                    true,
                    unit.Hostile,
                    unit.Kind == AutonomousVisibleUnitKind.Npc,
                    false))
                .ToArray();
            AutonomousPveTargetSnapshot tracked = trackedTargetEid > 0
                ? CreateTrackedTarget(player, visibleTargets, trackedTargetEid)
                : null;
            AutonomousPveWeaponSnapshot[] weapons = player.ActiveModules
                .Where(module => module.IsCategory(CategoryFlags.cf_weapons))
                .Select(CreateWeapon)
                .ToArray();
            return new AutonomousPveCombatSnapshot(
                true,
                player.ArmorPercentage,
                player.CorePercentage,
                visibleTargets,
                weapons,
                tracked);
        }

        private static AutonomousPveTargetSnapshot CreateTrackedTarget(
            Player player,
            IEnumerable<AutonomousPveTargetSnapshot> visibleTargets,
            long targetEid)
        {
            AutonomousPveTargetSnapshot visible = visibleTargets.FirstOrDefault(target => target.Eid == targetEid);
            Unit target = player.Zone.GetUnit(targetEid);
            if (target == null)
                return null;
            UnitLock targetLock = player.GetLocks()
                .OfType<UnitLock>()
                .FirstOrDefault(candidate => candidate.Target?.Eid == targetEid);
            AutonomousDefenseLockState lockState = targetLock == null
                ? AutonomousDefenseLockState.Missing
                : targetLock.State == LockState.Locked
                    ? AutonomousDefenseLockState.Locked
                    : AutonomousDefenseLockState.InProgress;
            bool isVisible = visible != null;
            return new AutonomousPveTargetSnapshot(
                targetEid,
                isVisible ? visible.Position : target.CurrentPosition,
                isVisible ? visible.Distance : player.GetDistance(target),
                isVisible,
                isVisible && visible.Hostile,
                target is Perpetuum.Zones.NpcSystem.Npc,
                target.States.Dead,
                lockState,
                targetLock?.Id ?? 0);
        }

        private static AutonomousPveWeaponSnapshot CreateWeapon(ActiveModule module)
        {
            Ammo ammo = module.GetAmmo();
            return new AutonomousPveWeaponSnapshot(
                module.ParentComponent.Type,
                module.Slot,
                module.IsAmmoable,
                ammo?.Definition ?? 0,
                ammo?.Quantity ?? 0,
                module.State.Type != ModuleStateType.Idle);
        }
    }

    public enum AutonomousPveCombatUpdateResult
    {
        Waiting,
        Searching,
        TargetSelected,
        Approach,
        Acted,
        Engaging,
        TargetCompleted,
        Retreat,
        Resupply,
        LossBudgetReached,
        Complete,
        Blocked
    }

    public sealed class AutonomousPveCombatUpdate
    {
        public AutonomousPveCombatUpdate(
            AutonomousPveCombatUpdateResult result,
            long targetEid = 0,
            Position? targetPosition = null,
            string reason = null)
        {
            Result = result;
            TargetEid = targetEid;
            TargetPosition = targetPosition;
            Reason = reason;
        }

        public AutonomousPveCombatUpdateResult Result { get; }
        public long TargetEid { get; }
        public Position? TargetPosition { get; }
        public string Reason { get; }
    }

    public interface IAutonomousPveCombatController
    {
        AutonomousPveCombatUpdate Update(
            GameActionContext context,
            AutonomousPveOptions options);
        void Stop(GameActionContext context);
        bool RecordLoss(GameActionContext context, AutonomousPveOptions options, long lostRobotEid);
        void AcknowledgeDockedPreparation(GameActionContext context);
        long GetTargetEid(int characterId);
        bool ShouldDeploy(int characterId);
    }

    /// <summary>
    /// Executes a bounded normal targeting, ammunition, and module sequence.
    /// Durable state is intent only; visibility, target life, locks,
    /// armor, core, ammunition, and module state are freshly observed before
    /// every decision.
    /// </summary>
    public sealed class AutonomousPveCombatController : IAutonomousPveCombatController
    {
        private readonly IAutonomousPveCombatObservationService _observations;
        private readonly IAutonomousPveCombatGoalStore _goals;
        private readonly ITargetLockActionService _targetLocks;
        private readonly IModuleActionService _modules;
        private readonly IAutonomousActorAudit _audit;

        public AutonomousPveCombatController(
            IAutonomousPveCombatObservationService observations,
            IAutonomousPveCombatGoalStore goals,
            ITargetLockActionService targetLocks,
            IModuleActionService modules,
            IAutonomousActorAudit audit)
        {
            _observations = observations ?? throw new ArgumentNullException(nameof(observations));
            _goals = goals ?? throw new ArgumentNullException(nameof(goals));
            _targetLocks = targetLocks ?? throw new ArgumentNullException(nameof(targetLocks));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public AutonomousPveCombatUpdate Update(
            GameActionContext context,
            AutonomousPveOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            options.Validate(context.Actor.Id);

            DateTime now = DateTime.UtcNow;
            AutonomousPveCombatGoalState state = LoadOrCreate(context.Actor.Id, options, now);
            AutonomousPveCombatSnapshot snapshot = _observations.Observe(context, state.TargetEid);
            if (!snapshot.WorldAvailable)
                return new AutonomousPveCombatUpdate(AutonomousPveCombatUpdateResult.Waiting);

            if (state.Complete)
            {
                StopActions(context, state, snapshot);
                return new AutonomousPveCombatUpdate(AutonomousPveCombatUpdateResult.Complete);
            }
            if (state.LossBudgetReached)
            {
                StopActions(context, state, snapshot);
                return new AutonomousPveCombatUpdate(
                    AutonomousPveCombatUpdateResult.LossBudgetReached,
                    reason: "loss_budget_reached");
            }
            if (snapshot.ArmorRatio <= options.RetreatArmorRatio)
                return Retreat(context, state, snapshot, now, "armor_threshold");
            if (snapshot.CoreRatio <= options.RetreatCoreRatio)
                return Retreat(context, state, snapshot, now, "core_threshold");

            if (state.TargetEid <= 0)
                return SelectTarget(context, options, state, snapshot, now);

            AutonomousPveTargetSnapshot target = snapshot.TrackedTarget;
            if (target?.Dead == true)
                return CompleteTarget(context, state, snapshot, now);
            if (target == null || !target.Visible || !target.Npc || !target.Hostile ||
                target.Distance > options.AcquisitionRange)
            {
                StopActions(context, state, snapshot);
                Save(Next(state, "searching", now, targetEid: 0, lockId: 0,
                    blockedReason: "target_unavailable", engagementStartedAt: null));
                return new AutonomousPveCombatUpdate(
                    AutonomousPveCombatUpdateResult.Searching,
                    reason: "target_unavailable");
            }

            if (state.EngagementStartedAt.HasValue &&
                now - state.EngagementStartedAt.Value >= TimeSpan.FromSeconds(options.MaxEngagementSeconds))
                return Retreat(context, state, snapshot, now, "engagement_limit");

            AutonomousPveWeaponSnapshot reload = snapshot.Weapons.FirstOrDefault(weapon => weapon.CanReload);
            if (!snapshot.Weapons.Any(weapon => weapon.Usable))
            {
                if (reload != null)
                {
                    try
                    {
                        _modules.LoadAmmo(context, new ModuleAmmoLoadAction(
                            reload.AmmoDefinition,
                            reload.Component,
                            reload.Slot));
                        Save(Next(state, "reloading", now, state.TargetEid, state.LockId,
                            engagementStartedAt: state.EngagementStartedAt));
                        _audit.Write(context.Actor.Id, "pve_reload", AutonomousActorStatus.Active,
                            $"target_{state.TargetEid}");
                        return new AutonomousPveCombatUpdate(
                            AutonomousPveCombatUpdateResult.Acted,
                            state.TargetEid);
                    }
                    catch (PerpetuumException exception)
                    {
                        return Blocked(context, state, snapshot, now, exception.error.ToString(), true);
                    }
                }

                StopActions(context, state, snapshot);
                Save(Next(state, "resupply_required", now, targetEid: 0, lockId: 0,
                    blockedReason: "weapon_ammunition_unavailable", engagementStartedAt: null));
                _audit.Write(context.Actor.Id, "pve_resupply_required", AutonomousActorStatus.Active,
                    "weapon_ammunition_unavailable");
                return new AutonomousPveCombatUpdate(
                    AutonomousPveCombatUpdateResult.Resupply,
                    reason: "weapon_ammunition_unavailable");
            }

            if (target.Distance > options.EngagementRange)
            {
                if (state.Phase != "engaging")
                    Save(Next(state, "approaching", state.Phase == "approaching" ? state.PhaseStartedAt : now,
                        state.TargetEid, state.LockId, engagementStartedAt: state.EngagementStartedAt));
                return new AutonomousPveCombatUpdate(
                    AutonomousPveCombatUpdateResult.Approach,
                    target.Eid,
                    target.Position);
            }

            if (target.LockState != AutonomousDefenseLockState.Locked)
            {
                DateTime lockStarted = state.Phase == "locking" ? state.PhaseStartedAt : now;
                if (now - lockStarted >= TimeSpan.FromSeconds(options.LockTimeoutSeconds))
                    return Retreat(context, state, snapshot, now, "lock_timeout");
                if (target.LockState == AutonomousDefenseLockState.Missing)
                {
                    if (state.Phase == "locking")
                    {
                        return new AutonomousPveCombatUpdate(
                            AutonomousPveCombatUpdateResult.Waiting,
                            target.Eid);
                    }
                    try
                    {
                        _targetLocks.LockUnit(context, new UnitTargetLockAction(target.Eid, true));
                        Save(Next(state, "locking", lockStarted, state.TargetEid,
                            engagementStartedAt: state.EngagementStartedAt));
                        _audit.Write(context.Actor.Id, "pve_lock", AutonomousActorStatus.Active,
                            $"target_{target.Eid}");
                        return new AutonomousPveCombatUpdate(
                            AutonomousPveCombatUpdateResult.Acted,
                            target.Eid);
                    }
                    catch (PerpetuumException exception)
                    {
                        return Blocked(context, state, snapshot, now, exception.error.ToString(), false);
                    }
                }

                Save(Next(state, "locking", lockStarted, state.TargetEid, target.LockId,
                    engagementStartedAt: state.EngagementStartedAt));
                return new AutonomousPveCombatUpdate(
                    AutonomousPveCombatUpdateResult.Waiting,
                    target.Eid);
            }

            if (state.Phase != "engaging")
            {
                try
                {
                    _targetLocks.SetPrimary(context, target.LockId);
                    _modules.UseByCategory(context, new ModuleCategoryUseAction(
                        target.LockId,
                        CategoryFlags.cf_weapons,
                        ModuleStateType.AutoRepeat));
                    Save(Next(state, "engaging", now, state.TargetEid, target.LockId,
                        engagementStartedAt: state.EngagementStartedAt));
                    _audit.Write(context.Actor.Id, "pve_engage", AutonomousActorStatus.Active,
                        $"target_{target.Eid}");
                    return new AutonomousPveCombatUpdate(
                        AutonomousPveCombatUpdateResult.Acted,
                        target.Eid);
                }
                catch (PerpetuumException exception)
                {
                    return Blocked(context, state, snapshot, now, exception.error.ToString(), false);
                }
            }

            if (state.LockId != target.LockId)
                Save(Next(state, "engaging", state.PhaseStartedAt, state.TargetEid, target.LockId,
                    engagementStartedAt: state.EngagementStartedAt));
            return new AutonomousPveCombatUpdate(
                AutonomousPveCombatUpdateResult.Engaging,
                target.Eid);
        }

        public void Stop(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            AutonomousPveCombatGoalState state = _goals.Load(context.Actor.Id);
            if (state == null)
                return;
            StopActions(context, state, _observations.Observe(context, state.TargetEid));
        }

        public bool RecordLoss(GameActionContext context, AutonomousPveOptions options, long lostRobotEid)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (lostRobotEid <= 0)
                return false;
            DateTime now = DateTime.UtcNow;
            AutonomousPveCombatGoalState state = LoadOrCreate(context.Actor.Id, options, now);
            if (state.LastLostRobotEid == lostRobotEid)
                return state.LossBudgetReached;
            int losses = checked(state.LossCount + 1);
            string phase = losses >= state.MaxLosses ? "loss_budget_reached" : "recovering_loss";
            state = Next(
                state,
                phase,
                now,
                targetEid: 0,
                lockId: 0,
                lastLostRobotEid: lostRobotEid,
                lossCount: losses,
                blockedReason: phase,
                engagementStartedAt: null);
            Save(state);
            _audit.Write(context.Actor.Id, "pve_loss_recorded", AutonomousActorStatus.Active,
                $"robot_{lostRobotEid}_loss_{losses}_of_{state.MaxLosses}");
            return state.LossBudgetReached;
        }

        public long GetTargetEid(int characterId)
        {
            return _goals.Load(characterId)?.TargetEid ?? 0;
        }

        public void AcknowledgeDockedPreparation(GameActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            AutonomousPveCombatGoalState state = _goals.Load(context.Actor.Id);
            if (state == null || state.Phase != "resupply_required")
                return;
            Save(Next(state, "searching", DateTime.UtcNow, targetEid: 0, lockId: 0,
                engagementStartedAt: null));
            _audit.Write(context.Actor.Id, "pve_resupply_ready", AutonomousActorStatus.Active);
        }

        public bool ShouldDeploy(int characterId)
        {
            AutonomousPveCombatGoalState state = _goals.Load(characterId);
            return state == null ||
                   (!state.Complete && !state.LossBudgetReached && state.Phase != "resupply_required");
        }

        private AutonomousPveCombatUpdate SelectTarget(
            GameActionContext context,
            AutonomousPveOptions options,
            AutonomousPveCombatGoalState state,
            AutonomousPveCombatSnapshot snapshot,
            DateTime now)
        {
            AutonomousPveTargetSnapshot target = snapshot.VisibleTargets.FirstOrDefault(candidate =>
                candidate.Npc && candidate.Hostile && !candidate.Dead &&
                candidate.Distance <= options.AcquisitionRange);
            if (target == null)
            {
                DateTime searchStarted = state.Phase == "searching" ? state.PhaseStartedAt : now;
                if (now - searchStarted >= TimeSpan.FromSeconds(options.SearchSeconds))
                {
                    Save(Next(state, "search_exhausted", now, blockedReason: "no_visible_npc",
                        engagementStartedAt: null));
                    return new AutonomousPveCombatUpdate(
                        AutonomousPveCombatUpdateResult.Retreat,
                        reason: "no_visible_npc");
                }
                if (state.Phase != "searching")
                    Save(Next(state, "searching", searchStarted, engagementStartedAt: null));
                return new AutonomousPveCombatUpdate(AutonomousPveCombatUpdateResult.Searching);
            }

            Save(Next(state, "target_selected", now, target.Eid,
                engagementStartedAt: now));
            _audit.Write(context.Actor.Id, "pve_target_selected", AutonomousActorStatus.Active,
                $"target_{target.Eid}_distance_{Math.Ceiling(target.Distance)}");
            return new AutonomousPveCombatUpdate(
                AutonomousPveCombatUpdateResult.TargetSelected,
                target.Eid,
                target.Position);
        }

        private AutonomousPveCombatUpdate CompleteTarget(
            GameActionContext context,
            AutonomousPveCombatGoalState state,
            AutonomousPveCombatSnapshot snapshot,
            DateTime now)
        {
            StopActions(context, state, snapshot);
            bool engaged = state.Phase == "engaging";
            int completed = engaged ? Math.Min(state.TargetCount, state.CompletedCount + 1) : state.CompletedCount;
            string phase = completed >= state.TargetCount ? "complete" : "searching";
            state = Next(state, phase, now, targetEid: 0, lockId: 0,
                completedCount: completed,
                blockedReason: engaged ? null : "target_lost_before_engagement",
                engagementStartedAt: null);
            Save(state);
            if (!engaged)
            {
                return new AutonomousPveCombatUpdate(
                    AutonomousPveCombatUpdateResult.Searching,
                    reason: "target_lost_before_engagement");
            }
            _audit.Write(context.Actor.Id, "pve_target_completed", AutonomousActorStatus.Active,
                $"completed_{completed}_of_{state.TargetCount}");
            return new AutonomousPveCombatUpdate(
                state.Complete
                    ? AutonomousPveCombatUpdateResult.Complete
                    : AutonomousPveCombatUpdateResult.TargetCompleted);
        }

        private AutonomousPveCombatUpdate Retreat(
            GameActionContext context,
            AutonomousPveCombatGoalState state,
            AutonomousPveCombatSnapshot snapshot,
            DateTime now,
            string reason)
        {
            StopActions(context, state, snapshot);
            Save(Next(state, "retreating", now, targetEid: 0, lockId: 0,
                blockedReason: reason, engagementStartedAt: null));
            _audit.Write(context.Actor.Id, "pve_retreat", AutonomousActorStatus.Active, reason);
            return new AutonomousPveCombatUpdate(
                AutonomousPveCombatUpdateResult.Retreat,
                reason: reason);
        }

        private AutonomousPveCombatUpdate Blocked(
            GameActionContext context,
            AutonomousPveCombatGoalState state,
            AutonomousPveCombatSnapshot snapshot,
            DateTime now,
            string reason,
            bool resupply)
        {
            StopActions(context, state, snapshot);
            Save(Next(state, resupply ? "resupply_required" : "blocked", now,
                targetEid: 0, lockId: 0, blockedReason: reason, engagementStartedAt: null));
            _audit.Write(context.Actor.Id, "pve_action_blocked", AutonomousActorStatus.Active, reason);
            return new AutonomousPveCombatUpdate(
                resupply ? AutonomousPveCombatUpdateResult.Resupply : AutonomousPveCombatUpdateResult.Blocked,
                reason: reason);
        }

        private void StopActions(
            GameActionContext context,
            AutonomousPveCombatGoalState state,
            AutonomousPveCombatSnapshot snapshot)
        {
            if (!snapshot.WorldAvailable)
                return;
            if (snapshot.Weapons.Any(weapon => weapon.Active))
                _modules.DeactivateByCategory(context, CategoryFlags.cf_weapons);
            AutonomousPveTargetSnapshot target = snapshot.TrackedTarget;
            if (target?.LockId > 0 &&
                (state.LockId == 0 || state.LockId == target.LockId) &&
                (state.Phase == "locking" || state.Phase == "engaging" || state.Phase == "approaching"))
                _targetLocks.Cancel(context, target.LockId);
        }

        private AutonomousPveCombatGoalState LoadOrCreate(
            int characterId,
            AutonomousPveOptions options,
            DateTime now)
        {
            AutonomousPveCombatGoalState state = _goals.Load(characterId);
            if (state == null)
            {
                state = new AutonomousPveCombatGoalState(
                    characterId,
                    options.TargetCount,
                    0,
                    options.MaxLosses,
                    0,
                    "searching",
                    now);
                Save(state);
                return state;
            }
            if (state.TargetCount != options.TargetCount || state.MaxLosses != options.MaxLosses)
                throw new InvalidOperationException(
                    $"Persistent combat goal for character {characterId} does not match configuration.");
            return state;
        }

        private void Save(AutonomousPveCombatGoalState state)
        {
            AutonomousPveCombatGoalState current = _goals.Load(state.CharacterId);
            if (current != null &&
                current.TargetCount == state.TargetCount &&
                current.CompletedCount == state.CompletedCount &&
                current.MaxLosses == state.MaxLosses &&
                current.LossCount == state.LossCount &&
                current.Phase == state.Phase &&
                current.PhaseStartedAt == state.PhaseStartedAt &&
                current.TargetEid == state.TargetEid &&
                current.LockId == state.LockId &&
                current.LastLostRobotEid == state.LastLostRobotEid &&
                current.BlockedReason == state.BlockedReason &&
                current.EngagementStartedAt == state.EngagementStartedAt)
                return;
            _goals.Save(state);
        }

        private static AutonomousPveCombatGoalState Next(
            AutonomousPveCombatGoalState state,
            string phase,
            DateTime phaseStartedAt,
            long targetEid = 0,
            long lockId = 0,
            long? lastLostRobotEid = null,
            int? completedCount = null,
            int? lossCount = null,
            string blockedReason = null,
            DateTime? engagementStartedAt = null)
        {
            return new AutonomousPveCombatGoalState(
                state.CharacterId,
                state.TargetCount,
                completedCount ?? state.CompletedCount,
                state.MaxLosses,
                lossCount ?? state.LossCount,
                phase,
                phaseStartedAt,
                targetEid,
                lockId,
                lastLostRobotEid ?? state.LastLostRobotEid,
                blockedReason,
                engagementStartedAt,
                state.Revision + 1);
        }
    }
}
