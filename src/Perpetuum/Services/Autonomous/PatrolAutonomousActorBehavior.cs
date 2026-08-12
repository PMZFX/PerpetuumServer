using System;
using System.Linq;
using Perpetuum.ExportedTypes;
using Perpetuum.Modules;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Units;
using Perpetuum.Zones.Locking;
using Perpetuum.Zones.Locking.Locks;

namespace Perpetuum.Services.Autonomous
{
    /// <summary>
    /// A bounded field loop: undock through the player action, travel using
    /// normal movement input, return to the undock point, and dock through the
    /// player action. Optional combat is defensive and damage-triggered only.
    /// Human ownership suspends and resets the loop.
    /// </summary>
    public sealed class PatrolAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private enum PatrolState
        {
            Docked,
            WaitingForWorld,
            Outbound,
            FieldDwell,
            Returning,
            WaitingToDock,
            RouteRetry
        }

        private static readonly TimeSpan WorldLoadTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan RouteRetryDelay = TimeSpan.FromSeconds(5);

        private readonly AutonomousActorDefinition _definition;
        private readonly IUndockActionService _undockActionService;
        private readonly IDockActionService _dockActionService;
        private readonly IAutonomousNavigationService _navigation;
        private readonly IAutonomousPerceptionService _perception;
        private readonly IAutonomousDamageMonitor _damageMonitor;
        private readonly ITargetLockActionService _targetLockActionService;
        private readonly IModuleActionService _moduleActionService;
        private readonly IAutonomousActorStateStore _actorStateStore;
        private readonly IAutonomousActorAudit _audit;
        private AutonomousDefensiveEngagement _defense;
        private AutonomousRobotRecoveryTracker _recoveryTracker;
        private PatrolState _state;
        private TimeSpan _stateElapsed;
        private Position _origin;
        private long _dockingBaseEid;
        private long _expectedRobotEid;
        private bool _recoveringWorldEntry;
        private bool _retreatingFromThreat;
        private bool _robotRecoveryRequired;
        private bool _defenseLockOwned;

        public PatrolAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            IUndockActionService undockActionService,
            IDockActionService dockActionService,
            IAutonomousNavigationService navigation,
            IAutonomousPerceptionService perception,
            IAutonomousDamageMonitor damageMonitor,
            ITargetLockActionService targetLockActionService,
            IModuleActionService moduleActionService,
            IAutonomousActorStateStore actorStateStore,
            IAutonomousActorAudit audit)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _undockActionService = undockActionService;
            _dockActionService = dockActionService;
            _navigation = navigation;
            _perception = perception ?? throw new ArgumentNullException(nameof(perception));
            _damageMonitor = damageMonitor ?? throw new ArgumentNullException(nameof(damageMonitor));
            _targetLockActionService = targetLockActionService ?? throw new ArgumentNullException(nameof(targetLockActionService));
            _moduleActionService = moduleActionService ?? throw new ArgumentNullException(nameof(moduleActionService));
            _actorStateStore = actorStateStore ?? throw new ArgumentNullException(nameof(actorStateStore));
            _audit = audit;
        }

        public string Name => "patrol";

        public void Start(GameActionContext context)
        {
            _definition.Patrol.Validate(_definition.CharacterId);
            _defense = new AutonomousDefensiveEngagement(
                TimeSpan.FromSeconds(_definition.Patrol.Defense.LockTimeoutSeconds),
                TimeSpan.FromSeconds(_definition.Patrol.Defense.MaxEngagementSeconds));
            _damageMonitor.Reset();
            _navigation.Stop(context);
            _state = PatrolState.Docked;
            _stateElapsed = context.Actor.IsDocked
                ? TimeSpan.FromSeconds(_definition.Patrol.DockedDwellSeconds)
                : TimeSpan.Zero;
            _dockingBaseEid = context.Actor.CurrentDockingBaseEid;
            _recoveryTracker = new AutonomousRobotRecoveryTracker(
                context.Actor.Id,
                Name,
                _actorStateStore);
            AutonomousRecoveryStartDisposition recoveryDisposition = _recoveryTracker.Start(
                context.Actor.ActiveRobotEid,
                _definition.RecoveryRevision);
            _expectedRobotEid = _recoveryTracker.ExpectedRobotEid;
            _recoveringWorldEntry = !context.Actor.IsDocked;
            _retreatingFromThreat = false;
            _robotRecoveryRequired = _recoveryTracker.RecoveryRequired;
            _defenseLockOwned = false;

            if (recoveryDisposition == AutonomousRecoveryStartDisposition.RecoveryRequired)
            {
                _audit.Write(context.Actor.Id, "patrol_recovery_required", AutonomousActorStatus.Active,
                    _recoveryTracker.RecoveryReason);
            }
            else if (recoveryDisposition == AutonomousRecoveryStartDisposition.RecoveryAcknowledged)
            {
                _audit.Write(context.Actor.Id, "patrol_recovery_acknowledged", AutonomousActorStatus.Active,
                    $"revision_{_definition.RecoveryRevision}");
            }
        }

        public void Stop(GameActionContext context)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player != null)
            {
                StopDefensiveModules(context);
                UnitLock defenseLock = FindDefenseLock(player);
                if (_defenseLockOwned && defenseLock != null)
                    _targetLockActionService.Cancel(context, defenseLock.Id);
            }
            _damageMonitor.Reset();
            _defense?.Reset();
            _navigation.Reset();
            _state = PatrolState.Docked;
            _stateElapsed = TimeSpan.Zero;
            _retreatingFromThreat = false;
            _robotRecoveryRequired = false;
            _defenseLockOwned = false;
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            _stateElapsed += elapsed;

            if (HandleRobotRecovery(context, false))
                return;

            if (context.Actor.IsDocked)
            {
                _damageMonitor.Reset();
                _defense?.Reset();
                _defenseLockOwned = false;
                UpdateDocked(context);
                return;
            }

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player == null)
                return;

            if (HandleRobotRecovery(context, player.States.Dead))
                return;

            if (_definition.Patrol.Defense.Enabled)
            {
                _damageMonitor.Bind(player);
                HandleDefensiveCombat(context, player, elapsed);
            }

            if (HandleVisibleThreat(context, player))
                return;

            switch (_state)
            {
                case PatrolState.Docked:
                    if (_recoveringWorldEntry)
                        BeginWorldRecovery(context, player);
                    else
                        BeginOutbound(context, player);
                    break;

                case PatrolState.WaitingForWorld:
                    BeginOutbound(context, player);
                    break;

                case PatrolState.Outbound:
                    UpdateOutbound(context, elapsed);
                    break;

                case PatrolState.FieldDwell:
                    if (_stateElapsed >= TimeSpan.FromSeconds(_definition.Patrol.FieldDwellSeconds))
                        BeginReturn(context, player);
                    break;

                case PatrolState.Returning:
                    UpdateReturn(context, elapsed);
                    break;

                case PatrolState.WaitingToDock:
                    TryDock(context, player);
                    break;

                case PatrolState.RouteRetry:
                    if (_stateElapsed >= RouteRetryDelay)
                    {
                        if (_recoveringWorldEntry)
                            BeginWorldRecovery(context, player);
                        else
                            BeginReturn(context, player);
                    }
                    break;
            }
        }

        private void UpdateDocked(GameActionContext context)
        {
            if (_robotRecoveryRequired)
                return;

            if (_state == PatrolState.WaitingForWorld && _stateElapsed < WorldLoadTimeout)
                return;

            if (_state != PatrolState.Docked)
            {
                _navigation.Stop(context);
                SetState(PatrolState.Docked);
                return;
            }

            int dwellSeconds = _retreatingFromThreat
                ? _definition.Patrol.Threat.DockedDwellSeconds
                : _definition.Patrol.DockedDwellSeconds;
            if (!AutonomousUndockPolicy.IsReady(
                    _stateElapsed,
                    TimeSpan.FromSeconds(dwellSeconds),
                    context.Actor.NextAvailableUndockTime,
                    DateTime.Now))
                return;

            _retreatingFromThreat = false;
            _dockingBaseEid = context.Actor.CurrentDockingBaseEid;
            _undockActionService.Execute(context);
            SetState(PatrolState.WaitingForWorld);
            _audit.Write(context.Actor.Id, "patrol_undock", AutonomousActorStatus.Active);
        }

        private void BeginOutbound(GameActionContext context, Player player)
        {
            _origin = player.CurrentPosition;
            if (_dockingBaseEid == 0)
                _dockingBaseEid = context.Actor.CurrentDockingBaseEid;

            int radius = _definition.Patrol.Radius;
            int offset = Math.Abs(context.Actor.Id) % 16;
            for (int reduction = 0; reduction <= radius - 4; reduction += 2)
            {
                int candidateRadius = radius - reduction;
                for (int index = 0; index < 16; index++)
                {
                    double direction = ((index + offset) % 16) / 16.0;
                    Position candidate = _origin.OffsetInDirection(direction, candidateRadius).Center;
                    if (!_navigation.TryStart(context, candidate, _definition.Patrol.Throttle))
                        continue;

                    SetState(PatrolState.Outbound);
                    _audit.Write(context.Actor.Id, "patrol_outbound", AutonomousActorStatus.Active,
                        $"zone_{player.Zone.Id}");
                    WritePerceptionAudit(context);
                    return;
                }
            }

            _navigation.Stop(context);
            SetState(PatrolState.RouteRetry);
            _audit.Write(context.Actor.Id, "patrol_route_unavailable", AutonomousActorStatus.Active);
        }

        private void BeginWorldRecovery(GameActionContext context, Player player)
        {
            var dockingBase = context.Actor.GetCurrentDockingBase();
            if (dockingBase == null || dockingBase.Zone != player.Zone)
            {
                SetState(PatrolState.RouteRetry);
                return;
            }

            if (dockingBase.IsInDockingRange(player))
            {
                SetState(PatrolState.WaitingToDock);
                _audit.Write(context.Actor.Id, "patrol_recovery_in_range", AutonomousActorStatus.Active);
                return;
            }

            int minRadius = dockingBase.Size + 1;
            int maxRadius = Math.Max(minRadius, dockingBase.Size + dockingBase.SpawnRange);
            int offset = Math.Abs(context.Actor.Id) % 16;
            for (int radius = minRadius; radius <= maxRadius; radius += 2)
            {
                for (int index = 0; index < 16; index++)
                {
                    double direction = ((index + offset) % 16) / 16.0;
                    Position candidate = dockingBase.CurrentPosition.OffsetInDirection(direction, radius).Center;
                    if (!_navigation.TryStart(context, candidate, _definition.Patrol.Throttle))
                        continue;

                    _origin = candidate;
                    SetState(PatrolState.Returning);
                    _audit.Write(context.Actor.Id, "patrol_recovering", AutonomousActorStatus.Active,
                        $"zone_{player.Zone.Id}");
                    return;
                }
            }

            _navigation.Stop(context);
            SetState(PatrolState.RouteRetry);
            _audit.Write(context.Actor.Id, "patrol_recovery_blocked", AutonomousActorStatus.Active);
        }

        private void UpdateOutbound(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousNavigationStatus status = _navigation.Update(context, elapsed);
            if (status == AutonomousNavigationStatus.Arrived)
            {
                SetState(PatrolState.FieldDwell);
                _audit.Write(context.Actor.Id, "patrol_destination", AutonomousActorStatus.Active);
            }
            else if (status == AutonomousNavigationStatus.Blocked || status == AutonomousNavigationStatus.Stuck)
            {
                SetState(PatrolState.RouteRetry);
                _audit.Write(context.Actor.Id, "patrol_outbound_blocked", AutonomousActorStatus.Active, status.ToString());
            }
        }

        private void BeginReturn(GameActionContext context, Player player)
        {
            if (_navigation.TryStart(context, _origin, _definition.Patrol.Throttle))
            {
                SetState(PatrolState.Returning);
                _audit.Write(context.Actor.Id, "patrol_returning", AutonomousActorStatus.Active);
                return;
            }

            _navigation.Stop(context);
            SetState(PatrolState.RouteRetry);
        }

        private void UpdateReturn(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousNavigationStatus status = _navigation.Update(context, elapsed);
            if (status == AutonomousNavigationStatus.Arrived)
            {
                SetState(PatrolState.WaitingToDock);
                _audit.Write(context.Actor.Id, "patrol_returned", AutonomousActorStatus.Active);
            }
            else if (status == AutonomousNavigationStatus.Blocked || status == AutonomousNavigationStatus.Stuck)
            {
                SetState(PatrolState.RouteRetry);
                _audit.Write(context.Actor.Id, "patrol_return_blocked", AutonomousActorStatus.Active, status.ToString());
            }
        }

        private void TryDock(GameActionContext context, Player player)
        {
            _navigation.Stop(context);
            if (player.EffectHandler.ContainsEffect(EffectType.effect_aggressor) ||
                player.HasPvpEffect ||
                player.HasTeleportSicknessEffect)
                return;

            _dockActionService.Execute(context, new DockAction(_dockingBaseEid));
            _recoveringWorldEntry = false;
            _audit.Write(context.Actor.Id, "patrol_dock", AutonomousActorStatus.Active);
        }

        private bool HandleVisibleThreat(GameActionContext context, Player player)
        {
            if (!_definition.Patrol.Threat.Enabled)
                return false;

            AutonomousPerceptionSnapshot snapshot = _perception.Observe(context);
            AutonomousThreatAssessment assessment = AutonomousThreatAssessment.From(
                snapshot,
                _definition.Patrol.Threat.ResponseRange);
            if (!assessment.HasThreat)
                return false;

            if (!_retreatingFromThreat)
            {
                _retreatingFromThreat = true;
                _audit.Write(context.Actor.Id, "patrol_threat_retreat", AutonomousActorStatus.Active,
                    $"eid_{assessment.Nearest.Eid}_distance_{Math.Ceiling(assessment.Nearest.Distance)}");
            }

            AutonomousThreatDirective directive = AutonomousThreatResponsePolicy.Select(
                assessment,
                GetFieldActivity());
            return ApplyRetreatDirective(context, player, directive);
        }

        private void HandleDefensiveCombat(GameActionContext context, Player player, TimeSpan elapsed)
        {
            if (_damageMonitor.TryTakeAttacker(out long attackerEid) && _defense.RecordDamage(attackerEid))
            {
                _audit.Write(context.Actor.Id, "patrol_damage_response", AutonomousActorStatus.Active,
                    $"eid_{attackerEid}");
                BeginDamageRetreat(context, player);
            }

            if (_defense.State == AutonomousDefenseState.Idle)
                return;

            Unit target = player.Zone?.GetUnit(_defense.TargetEid);
            UnitLock defenseLock = FindDefenseLock(player);
            bool targetAvailable = target != null &&
                                   target.InZone &&
                                   !target.States.Dead &&
                                   player.IsVisible(target) &&
                                   player.GetDistance(target) <= _definition.Patrol.Defense.ResponseRange;
            bool hasUsableWeapon = player.ActiveModules.Any(module =>
                module.IsCategory(CategoryFlags.cf_weapons) &&
                (!module.IsAmmoable || module.GetAmmo()?.Quantity > 0));
            AutonomousDefenseLockState lockState = GetDefenseLockState(defenseLock);
            AutonomousDefenseDecision decision = _defense.Update(
                elapsed,
                targetAvailable,
                hasUsableWeapon,
                lockState);

            switch (decision.Directive)
            {
                case AutonomousDefenseDirective.RequestLock:
                    _targetLockActionService.LockUnit(
                        context,
                        new UnitTargetLockAction(decision.TargetEid, true));
                    _defenseLockOwned = true;
                    _audit.Write(context.Actor.Id, "patrol_defense_lock", AutonomousActorStatus.Active,
                        $"eid_{decision.TargetEid}");
                    break;

                case AutonomousDefenseDirective.Engage:
                    _targetLockActionService.SetPrimary(context, defenseLock.Id);
                    _moduleActionService.UseByCategory(
                        context,
                        new ModuleCategoryUseAction(
                            defenseLock.Id,
                            CategoryFlags.cf_weapons,
                            ModuleStateType.AutoRepeat));
                    _audit.Write(context.Actor.Id, "patrol_defense_engage", AutonomousActorStatus.Active,
                        $"eid_{decision.TargetEid}");
                    break;

                case AutonomousDefenseDirective.Disengage:
                    StopDefensiveModules(context);
                    if (_defenseLockOwned && defenseLock != null)
                        _targetLockActionService.Cancel(context, defenseLock.Id);
                    _audit.Write(context.Actor.Id, "patrol_defense_disengage", AutonomousActorStatus.Active,
                        decision.EndReason.ToString());
                    _defenseLockOwned = false;
                    _defense.CompleteDisengagement();
                    break;
            }
        }

        private void BeginDamageRetreat(GameActionContext context, Player player)
        {
            _retreatingFromThreat = true;
            AutonomousThreatDirective directive;
            switch (GetFieldActivity())
            {
                case AutonomousFieldActivity.Deploying:
                    directive = AutonomousThreatDirective.Dock;
                    break;
                case AutonomousFieldActivity.TravellingOutbound:
                case AutonomousFieldActivity.Dwelling:
                    directive = AutonomousThreatDirective.Return;
                    break;
                default:
                    directive = AutonomousThreatDirective.None;
                    break;
            }

            ApplyRetreatDirective(context, player, directive);
        }

        private bool ApplyRetreatDirective(
            GameActionContext context,
            Player player,
            AutonomousThreatDirective directive)
        {
            if (directive == AutonomousThreatDirective.Dock)
            {
                _origin = player.CurrentPosition;
                _navigation.Stop(context);
                SetState(PatrolState.WaitingToDock);
                return true;
            }

            if (directive == AutonomousThreatDirective.Return)
            {
                BeginReturn(context, player);
                return true;
            }

            return false;
        }

        private void StopDefensiveModules(GameActionContext context)
        {
            if (_defense == null || _defense.State == AutonomousDefenseState.Idle)
                return;

            _moduleActionService.DeactivateByCategory(context, CategoryFlags.cf_weapons);
        }

        private static AutonomousDefenseLockState GetDefenseLockState(UnitLock defenseLock)
        {
            if (defenseLock == null)
                return AutonomousDefenseLockState.Missing;
            return defenseLock.State == LockState.Locked
                ? AutonomousDefenseLockState.Locked
                : AutonomousDefenseLockState.InProgress;
        }

        private UnitLock FindDefenseLock(Player player)
        {
            if (player == null || _defense == null || _defense.TargetEid <= 0)
                return null;

            return player.GetLocks()
                .OfType<UnitLock>()
                .FirstOrDefault(candidate => candidate.Target?.Eid == _defense.TargetEid);
        }

        private bool HandleRobotRecovery(GameActionContext context, bool liveRobotDead)
        {
            if (_robotRecoveryRequired)
            {
                _navigation.Stop(context);
                return true;
            }

            AutonomousRobotRecoveryReason reason = AutonomousRobotRecoveryPolicy.Assess(
                _expectedRobotEid,
                context.Actor.ActiveRobotEid,
                liveRobotDead);
            if (reason == AutonomousRobotRecoveryReason.None)
                return false;

            _damageMonitor.Reset();
            _defense?.Reset();
            _defenseLockOwned = false;
            _navigation.Stop(context);
            if (_recoveryTracker.RequireRecovery(reason, context.Actor.ActiveRobotEid))
            {
                _robotRecoveryRequired = true;
                _audit.Write(context.Actor.Id, "patrol_recovery_required", AutonomousActorStatus.Active,
                    reason.ToString());
            }
            return true;
        }

        private AutonomousFieldActivity GetFieldActivity()
        {
            switch (_state)
            {
                case PatrolState.WaitingForWorld:
                    return AutonomousFieldActivity.Deploying;
                case PatrolState.Outbound:
                    return AutonomousFieldActivity.TravellingOutbound;
                case PatrolState.FieldDwell:
                    return AutonomousFieldActivity.Dwelling;
                case PatrolState.Returning:
                    return AutonomousFieldActivity.Returning;
                case PatrolState.WaitingToDock:
                    return AutonomousFieldActivity.Docking;
                default:
                    return AutonomousFieldActivity.Other;
            }
        }

        private void WritePerceptionAudit(GameActionContext context)
        {
            AutonomousPerceptionSnapshot snapshot = _perception.Observe(context);
            int hostileCount = snapshot.VisibleUnits.Count(unit => unit.Hostile);
            _audit.Write(context.Actor.Id, "patrol_perception", AutonomousActorStatus.Active,
                $"visible_{snapshot.VisibleUnits.Count}_hostile_{hostileCount}");
        }

        private void SetState(PatrolState state)
        {
            _state = state;
            _stateElapsed = TimeSpan.Zero;
        }
    }
}
