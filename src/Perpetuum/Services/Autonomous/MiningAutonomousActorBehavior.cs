using System;
using System.Drawing;
using System.Linq;
using Perpetuum.ExportedTypes;
using Perpetuum.Modules;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Zones.Locking;
using Perpetuum.Zones.Locking.Locks;
using Perpetuum.Zones.Scanning;
using Perpetuum.Zones.Scanning.Results;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    /// <summary>
    /// Mines through the same fitted scanner, terrain locks, active modules,
    /// movement input, cargo, undock, and dock actions used by a player.
    /// Persisted coordinates are only results previously observed by this actor.
    /// </summary>
    public sealed class MiningAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private enum MiningState
        {
            Docked,
            WaitingForWorld,
            ResumingTarget,
            ResumingReturn,
            RecoveringWorld,
            WaitingForScan,
            ScanRetry,
            TravellingToSurvey,
            TravellingToDeposit,
            LockingDeposit,
            Mining,
            Returning,
            WaitingToDock,
            RouteRetry
        }

        private static readonly TimeSpan WorldLoadTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan RouteRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ScanRetryDelay = TimeSpan.FromSeconds(1);

        private readonly AutonomousActorDefinition _definition;
        private readonly IUndockActionService _undock;
        private readonly IDockActionService _dock;
        private readonly IAutonomousNavigationService _navigation;
        private readonly IAutonomousPerceptionService _perception;
        private readonly IAutonomousMiningEquipmentService _equipment;
        private readonly IAutonomousCargoService _cargo;
        private readonly IAutonomousCargoDispositionService _cargoDisposition;
        private readonly IAutonomousMiningResupplyService _resupply;
        private readonly IMineralScanObservationService _scanObservations;
        private readonly ITargetLockActionService _targetLocks;
        private readonly IModuleActionService _modules;
        private readonly IAutonomousActorStateStore _actorStateStore;
        private readonly IAutonomousWorkStateStore _workStateStore;
        private readonly IAutonomousActorAudit _audit;
        private AutonomousRobotRecoveryTracker _recovery;
        private MaterialType _material;
        private MiningState _state;
        private TimeSpan _stateElapsed;
        private TimeSpan _miningElapsed;
        private Position? _origin;
        private Position? _target;
        private long _dockingBaseEid;
        private long _expectedRobotEid;
        private long _ownedTerrainLockId;
        private AutonomousMiningModuleSnapshot _scanner;
        private IMineralScanObservation _observationBeforeScan;
        private int _scanAttempts;
        private int _surveySiteIndex;
        private bool _robotRecoveryRequired;
        private bool _cargoHoldAudited;
        private bool _retreatingFromThreat;
        private bool _drillActive;
        private TimeSpan _marketRetryRemaining;
        private TimeSpan _resupplyRetryRemaining;
        private bool _equipmentHoldAudited;

        public MiningAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            IUndockActionService undock,
            IDockActionService dock,
            IAutonomousNavigationService navigation,
            IAutonomousPerceptionService perception,
            IAutonomousMiningEquipmentService equipment,
            IAutonomousCargoService cargo,
            IAutonomousCargoDispositionService cargoDisposition,
            IAutonomousMiningResupplyService resupply,
            IMineralScanObservationService scanObservations,
            ITargetLockActionService targetLocks,
            IModuleActionService modules,
            IAutonomousActorStateStore actorStateStore,
            IAutonomousWorkStateStore workStateStore,
            IAutonomousActorAudit audit)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _undock = undock ?? throw new ArgumentNullException(nameof(undock));
            _dock = dock ?? throw new ArgumentNullException(nameof(dock));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _perception = perception ?? throw new ArgumentNullException(nameof(perception));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
            _cargoDisposition = cargoDisposition ?? throw new ArgumentNullException(nameof(cargoDisposition));
            _resupply = resupply ?? throw new ArgumentNullException(nameof(resupply));
            _scanObservations = scanObservations ?? throw new ArgumentNullException(nameof(scanObservations));
            _targetLocks = targetLocks ?? throw new ArgumentNullException(nameof(targetLocks));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            _actorStateStore = actorStateStore ?? throw new ArgumentNullException(nameof(actorStateStore));
            _workStateStore = workStateStore ?? throw new ArgumentNullException(nameof(workStateStore));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public string Name => "mining";

        public void Start(GameActionContext context)
        {
            AutonomousMiningOptions options = _definition.Mining;
            options.Validate(_definition.CharacterId);
            _material = options.GetMaterialType();
            _navigation.Reset();
            _recovery = new AutonomousRobotRecoveryTracker(context.Actor.Id, Name, _actorStateStore);
            AutonomousRecoveryStartDisposition disposition = _recovery.Start(
                context.Actor.ActiveRobotEid,
                _definition.RecoveryRevision);
            _expectedRobotEid = _recovery.ExpectedRobotEid;
            _robotRecoveryRequired = _recovery.RecoveryRequired;
            _ownedTerrainLockId = 0;
            _scanner = null;
            _observationBeforeScan = null;
            _scanAttempts = 0;
            _miningElapsed = TimeSpan.Zero;
            _cargoHoldAudited = false;
            _retreatingFromThreat = false;
            _drillActive = false;
            _marketRetryRemaining = TimeSpan.Zero;
            _resupplyRetryRemaining = TimeSpan.Zero;
            _equipmentHoldAudited = false;

            AutonomousWorkState persisted = _workStateStore.Load(context.Actor.Id);
            _surveySiteIndex = AutonomousMiningSurveyPolicy.SelectResumeSite(
                _material,
                options.MaxSurveySites,
                persisted);
            AutonomousMiningResumeDirective resume = AutonomousMiningResumePolicy.Select(
                context.Actor.IsDocked,
                context.Actor.ZoneId,
                _material,
                persisted);
            _dockingBaseEid = context.Actor.IsDocked
                ? context.Actor.CurrentDockingBaseEid
                : persisted?.DockingBaseEid > 0
                ? persisted.DockingBaseEid
                : context.Actor.CurrentDockingBaseEid;
            _origin = context.Actor.IsDocked ? null : persisted?.Origin;
            _target = context.Actor.IsDocked ? null : persisted?.Target;
            switch (resume)
            {
                case AutonomousMiningResumeDirective.StartDocked:
                    _state = MiningState.Docked;
                    _stateElapsed = TimeSpan.FromSeconds(options.DockedDwellSeconds);
                    SaveWorkState(context);
                    break;
                case AutonomousMiningResumeDirective.ResumeTarget:
                    _state = MiningState.ResumingTarget;
                    _stateElapsed = TimeSpan.Zero;
                    break;
                case AutonomousMiningResumeDirective.ReturnToOrigin:
                    _state = MiningState.ResumingReturn;
                    _stateElapsed = TimeSpan.Zero;
                    break;
                default:
                    _state = MiningState.RecoveringWorld;
                    _stateElapsed = TimeSpan.Zero;
                    break;
            }

            if (disposition == AutonomousRecoveryStartDisposition.RecoveryRequired)
                _audit.Write(context.Actor.Id, "mining_recovery_required", AutonomousActorStatus.Active, _recovery.RecoveryReason);
            else if (disposition == AutonomousRecoveryStartDisposition.RecoveryAcknowledged)
                _audit.Write(context.Actor.Id, "mining_recovery_acknowledged", AutonomousActorStatus.Active,
                    $"revision_{_definition.RecoveryRevision}");
        }

        public void Stop(GameActionContext context)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player != null)
            {
                if (_drillActive)
                    _modules.DeactivateByCategory(context, CategoryFlags.cf_mining_turrets);
                if (_ownedTerrainLockId > 0 && player.GetLock(_ownedTerrainLockId) != null)
                    _targetLocks.Cancel(context, _ownedTerrainLockId);
            }
            _navigation.Reset();
            _ownedTerrainLockId = 0;
            _drillActive = false;
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            _stateElapsed += elapsed;
            if (HandleRobotRecovery(context, false))
                return;

            if (context.Actor.IsDocked)
            {
                UpdateDocked(context, elapsed);
                return;
            }

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player == null)
                return;
            if (HandleRobotRecovery(context, player.States.Dead))
                return;

            if (ShouldRetreatFromThreat(context) &&
                _state != MiningState.Returning &&
                _state != MiningState.WaitingToDock &&
                _state != MiningState.RouteRetry)
            {
                _retreatingFromThreat = true;
                BeginReturn(context, "threat");
                return;
            }

            switch (_state)
            {
                case MiningState.Docked:
                case MiningState.WaitingForWorld:
                    _origin = player.CurrentPosition;
                    _dockingBaseEid = context.Actor.CurrentDockingBaseEid;
                    BeginScan(context);
                    break;
                case MiningState.ResumingTarget:
                    BeginTargetTravel(context, true);
                    break;
                case MiningState.ResumingReturn:
                    BeginReturn(context, "restart_resume");
                    break;
                case MiningState.RecoveringWorld:
                    BeginBaseRecovery(context, player);
                    break;
                case MiningState.WaitingForScan:
                    UpdateWaitingForScan(context);
                    break;
                case MiningState.ScanRetry:
                    if (_stateElapsed >= ScanRetryDelay)
                        BeginScan(context);
                    break;
                case MiningState.TravellingToSurvey:
                    UpdateSurveyTravel(context, elapsed);
                    break;
                case MiningState.TravellingToDeposit:
                    UpdateTargetTravel(context, elapsed);
                    break;
                case MiningState.LockingDeposit:
                    UpdateTerrainLock(context, player);
                    break;
                case MiningState.Mining:
                    UpdateMining(context, player, elapsed);
                    break;
                case MiningState.Returning:
                    UpdateReturn(context, elapsed);
                    break;
                case MiningState.WaitingToDock:
                    TryDock(context, player);
                    break;
                case MiningState.RouteRetry:
                    if (_stateElapsed >= RouteRetryDelay)
                        BeginReturn(context, "route_retry");
                    break;
            }
        }

        private void UpdateDocked(GameActionContext context, TimeSpan elapsed)
        {
            if (_robotRecoveryRequired)
                return;
            if (_state == MiningState.WaitingForWorld && _stateElapsed < WorldLoadTimeout)
                return;
            if (_state != MiningState.Docked)
            {
                _navigation.Stop(context);
                _origin = null;
                _target = null;
                _scanAttempts = 0;
                SetState(context, MiningState.Docked);
                return;
            }

            if (_marketRetryRemaining > TimeSpan.Zero)
            {
                _marketRetryRemaining -= elapsed;
                return;
            }

            if (_definition.Mining.Market.Enabled)
            {
                try
                {
                    AutonomousCargoDisposition disposition = _cargoDisposition.SellNext(
                        context,
                        _material,
                        _definition.Mining.Market);
                    if (disposition.Result == AutonomousCargoDispositionResult.Sold)
                    {
                        _audit.Write(context.Actor.Id, "mining_market_sell", AutonomousActorStatus.Active,
                            $"definition_{disposition.Definition}_quantity_{disposition.Quantity}_unit_price_{disposition.UnitPrice}");
                        return;
                    }
                }
                catch (PerpetuumException exception)
                {
                    _marketRetryRemaining = TimeSpan.FromSeconds(_definition.Mining.Market.RetrySeconds);
                    _audit.Write(context.Actor.Id, "mining_market_blocked", AutonomousActorStatus.Active,
                        exception.error.ToString());
                    return;
                }
            }

            if (_resupplyRetryRemaining > TimeSpan.Zero)
            {
                _resupplyRetryRemaining -= elapsed;
                return;
            }

            try
            {
                AutonomousMiningResupply resupply = _resupply.ReloadNext(
                    context,
                    _material,
                    _definition.Mining.Resupply);
                if (resupply.Result == AutonomousMiningResupplyResult.Reloaded)
                {
                    _equipmentHoldAudited = false;
                    _audit.Write(context.Actor.Id, "mining_resupplied", AutonomousActorStatus.Active,
                        $"module_{resupply.ModuleEid}_ammo_{resupply.AmmoDefinition}");
                    return;
                }
            }
            catch (PerpetuumException exception)
            {
                _resupplyRetryRemaining = TimeSpan.FromSeconds(_definition.Mining.Resupply.RetrySeconds);
                _audit.Write(context.Actor.Id, "mining_resupply_blocked", AutonomousActorStatus.Active,
                    exception.error.ToString());
                return;
            }

            AutonomousMiningEquipmentSnapshot equipment = _equipment.Observe(context);
            if (equipment.SelectScanner(_material, MaterialProbeType.Tile) == null ||
                equipment.SelectDrill(_material) == null)
            {
                if (!_equipmentHoldAudited)
                {
                    _equipmentHoldAudited = true;
                    _audit.Write(context.Actor.Id, "mining_equipment_ready_required", AutonomousActorStatus.Active,
                        $"material_{_material}");
                }
                _resupplyRetryRemaining = TimeSpan.FromSeconds(_definition.Mining.Resupply.RetrySeconds);
                return;
            }
            _equipmentHoldAudited = false;

            AutonomousCargoSnapshot cargo = _cargo.Observe(context);
            if (cargo.FillRatio >= _definition.Mining.CargoFillRatio)
            {
                if (!_cargoHoldAudited)
                {
                    _cargoHoldAudited = true;
                    _audit.Write(context.Actor.Id, "mining_cargo_ready", AutonomousActorStatus.Active,
                        $"load_{Math.Round(cargo.FillRatio, 3)}");
                }
                return;
            }

            if (!AutonomousUndockPolicy.IsReady(
                    _stateElapsed,
                    TimeSpan.FromSeconds(_retreatingFromThreat
                        ? _definition.Mining.Threat.DockedDwellSeconds
                        : _definition.Mining.DockedDwellSeconds),
                    context.Actor.NextAvailableUndockTime,
                    DateTime.Now))
                return;

            _cargoHoldAudited = false;
            _equipmentHoldAudited = false;
            _retreatingFromThreat = false;
            _dockingBaseEid = context.Actor.CurrentDockingBaseEid;
            _undock.Execute(context);
            SetState(context, MiningState.WaitingForWorld);
            _audit.Write(context.Actor.Id, "mining_undock", AutonomousActorStatus.Active);
        }

        private void BeginScan(GameActionContext context)
        {
            AutonomousMiningEquipmentSnapshot equipment = _equipment.Observe(context);
            _scanner = equipment.SelectScanner(_material, MaterialProbeType.Tile);
            AutonomousMiningModuleSnapshot drill = equipment.SelectDrill(_material);
            if (_scanner == null || drill == null)
            {
                BeginReturn(context, "equipment_unavailable");
                return;
            }

            Player player = context.Actor.GetPlayerRobotFromZone();
            ActiveModule scannerModule = player?.GetModule(_scanner.ModuleEid) as ActiveModule;
            if (scannerModule == null)
            {
                BeginReturn(context, "scanner_unavailable");
                return;
            }
            if (scannerModule.State.Type != ModuleStateType.Idle)
            {
                SetState(context, MiningState.ScanRetry);
                return;
            }

            _observationBeforeScan = _scanObservations.GetLatest(
                context,
                _scanner.Component,
                _scanner.Slot);
            _modules.Use(context, new ModuleUseAction(
                0,
                _scanner.Component,
                _scanner.Slot,
                ModuleStateType.Oneshot));
            _scanAttempts++;
            SetState(context, MiningState.WaitingForScan);
            _audit.Write(context.Actor.Id, "mining_scan", AutonomousActorStatus.Active,
                $"material_{_material}_attempt_{_scanAttempts}");
        }

        private void UpdateWaitingForScan(GameActionContext context)
        {
            IMineralScanObservation observation = _scanObservations.GetLatest(
                context,
                _scanner.Component,
                _scanner.Slot);
            if (!ReferenceEquals(observation, _observationBeforeScan) &&
                observation is MineralScanResult tileResult &&
                tileResult.MaterialType == _material)
            {
                if (tileResult.TryGetRichestLocation(out Point location, out uint amount))
                {
                    _surveySiteIndex = 0;
                    _target = new Position(location.X + 0.5, location.Y + 0.5);
                    _audit.Write(context.Actor.Id, "mining_scan_target", AutonomousActorStatus.Active,
                        $"x_{location.X}_y_{location.Y}_sample_{amount}");
                    BeginTargetTravel(context, false);
                    return;
                }

                BeginSurvey(context);
                return;
            }

            if (_stateElapsed >= TimeSpan.FromSeconds(_definition.Mining.ScanTimeoutSeconds))
            {
                Player player = context.Actor.GetPlayerRobotFromZone();
                ActiveModule scannerModule = player?.GetModule(_scanner.ModuleEid) as ActiveModule;
                if (scannerModule?.State.Type == ModuleStateType.Idle)
                    RetryOrReturn(context, "scan_timeout");
                else if (_stateElapsed >= TimeSpan.FromSeconds(
                             _definition.Mining.ScanTimeoutSeconds + 120))
                    BeginReturn(context, "scanner_busy_timeout");
            }
        }

        private void RetryOrReturn(GameActionContext context, string reason)
        {
            if (_scanAttempts < _definition.Mining.MaxScanAttempts)
            {
                SetState(context, MiningState.ScanRetry);
                return;
            }
            BeginReturn(context, reason);
        }

        private void BeginSurvey(GameActionContext context)
        {
            if (!_origin.HasValue || _surveySiteIndex >= _definition.Mining.MaxSurveySites)
            {
                int surveyedSites = _surveySiteIndex;
                _surveySiteIndex = 0;
                _audit.Write(context.Actor.Id, "mining_survey_exhausted", AutonomousActorStatus.Active,
                    $"sites_{surveyedSites}");
                BeginReturn(context, "survey_exhausted");
                return;
            }

            while (_surveySiteIndex < _definition.Mining.MaxSurveySites)
            {
                int site = _surveySiteIndex++;
                Position candidate = AutonomousMiningSurveyPolicy.GetSite(
                    _origin.Value,
                    site,
                    _definition.Mining.SurveyStepDistance);
                if (!_navigation.TryStart(context, candidate, _definition.Mining.Throttle))
                    continue;

                SetState(context, MiningState.TravellingToSurvey);
                _audit.Write(context.Actor.Id, "mining_survey_travel", AutonomousActorStatus.Active,
                    $"site_{site + 1}");
                return;
            }

            int unreachableSites = _surveySiteIndex;
            _surveySiteIndex = 0;
            _audit.Write(context.Actor.Id, "mining_survey_exhausted", AutonomousActorStatus.Active,
                $"sites_{unreachableSites}");
            BeginReturn(context, "survey_unreachable");
        }

        private void UpdateSurveyTravel(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousNavigationStatus status = _navigation.Update(context, elapsed);
            if (status == AutonomousNavigationStatus.Arrived)
            {
                _navigation.Stop(context);
                _scanAttempts = 0;
                _audit.Write(context.Actor.Id, "mining_survey_arrived", AutonomousActorStatus.Active,
                    $"site_{_surveySiteIndex}");
                BeginScan(context);
            }
            else if (status == AutonomousNavigationStatus.Blocked || status == AutonomousNavigationStatus.Stuck)
            {
                _navigation.Stop(context);
                BeginSurvey(context);
            }
        }

        private void BeginTargetTravel(GameActionContext context, bool resumed)
        {
            if (!_target.HasValue || !_navigation.TryStart(context, _target.Value, _definition.Mining.Throttle))
            {
                BeginReturn(context, resumed ? "resume_target_unavailable" : "target_unavailable");
                return;
            }

            SetState(context, MiningState.TravellingToDeposit);
            _audit.Write(context.Actor.Id, resumed ? "mining_target_resumed" : "mining_target_travel",
                AutonomousActorStatus.Active);
        }

        private void UpdateTargetTravel(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousNavigationStatus status = _navigation.Update(context, elapsed);
            if (status == AutonomousNavigationStatus.Arrived)
            {
                _navigation.Stop(context);
                _targetLocks.LockTerrain(context, new TerrainTargetLockAction(
                    _target.Value.intX,
                    _target.Value.intY,
                    true));
                SetState(context, MiningState.LockingDeposit);
                _audit.Write(context.Actor.Id, "mining_lock_requested", AutonomousActorStatus.Active);
            }
            else if (status == AutonomousNavigationStatus.Blocked || status == AutonomousNavigationStatus.Stuck)
                BeginReturn(context, $"target_{status.ToString().ToLowerInvariant()}");
        }

        private void UpdateTerrainLock(GameActionContext context, Player player)
        {
            TerrainLock terrainLock = FindTargetLock(player);
            if (terrainLock?.State == LockState.Locked)
            {
                AutonomousMiningModuleSnapshot drill = _equipment.Observe(context).SelectDrill(_material);
                if (drill == null)
                {
                    BeginReturn(context, "drill_unavailable");
                    return;
                }

                _ownedTerrainLockId = terrainLock.Id;
                _modules.Use(context, new ModuleUseAction(
                    terrainLock.Id,
                    drill.Component,
                    drill.Slot,
                    ModuleStateType.AutoRepeat));
                _drillActive = true;
                _miningElapsed = TimeSpan.Zero;
                SetState(context, MiningState.Mining);
                _audit.Write(context.Actor.Id, "mining_drill_started", AutonomousActorStatus.Active);
                return;
            }

            if (_stateElapsed >= TimeSpan.FromSeconds(_definition.Mining.LockTimeoutSeconds))
                BeginReturn(context, "lock_timeout");
        }

        private void UpdateMining(GameActionContext context, Player player, TimeSpan elapsed)
        {
            _miningElapsed += elapsed;
            AutonomousCargoSnapshot cargo = _cargo.Observe(context);
            ActiveModule drill = player.ActiveModules
                .OfType<DrillerModule>()
                .FirstOrDefault(module => module.GetAmmo() is MiningAmmo ammo && ammo.MaterialType == _material);
            bool equipmentAvailable = drill != null &&
                (_miningElapsed < TimeSpan.FromSeconds(1) || drill.State.Type != ModuleStateType.Idle);
            AutonomousMiningReturnReason reason = AutonomousMiningReturnPolicy.Assess(
                cargo.FillRatio,
                _definition.Mining.CargoFillRatio,
                _miningElapsed,
                TimeSpan.FromSeconds(_definition.Mining.MaxMiningSeconds),
                false,
                equipmentAvailable);
            if (reason != AutonomousMiningReturnReason.None)
                BeginReturn(context, reason.ToString());
        }

        private void BeginReturn(GameActionContext context, string reason)
        {
            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player != null)
            {
                if (_drillActive)
                    _modules.DeactivateByCategory(context, CategoryFlags.cf_mining_turrets);
                if (_ownedTerrainLockId > 0 && player.GetLock(_ownedTerrainLockId) != null)
                    _targetLocks.Cancel(context, _ownedTerrainLockId);
            }
            _drillActive = false;
            _ownedTerrainLockId = 0;
            if (!_origin.HasValue || !_navigation.TryStart(context, _origin.Value, _definition.Mining.Throttle))
            {
                SetState(context, MiningState.RecoveringWorld);
                BeginBaseRecovery(context, player);
                return;
            }

            SetState(context, MiningState.Returning);
            _audit.Write(context.Actor.Id, "mining_return", AutonomousActorStatus.Active, reason);
        }

        private void UpdateReturn(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousNavigationStatus status = _navigation.Update(context, elapsed);
            if (status == AutonomousNavigationStatus.Arrived)
            {
                SetState(context, MiningState.WaitingToDock);
                _audit.Write(context.Actor.Id, "mining_returned", AutonomousActorStatus.Active);
            }
            else if (status == AutonomousNavigationStatus.Blocked || status == AutonomousNavigationStatus.Stuck)
            {
                _navigation.Stop(context);
                SetState(context, MiningState.RouteRetry);
            }
        }

        private void BeginBaseRecovery(GameActionContext context, Player player)
        {
            var dockingBase = context.Actor.GetCurrentDockingBase();
            if (player == null || dockingBase == null || dockingBase.Zone != player.Zone)
            {
                SetState(context, MiningState.RouteRetry);
                return;
            }
            _dockingBaseEid = dockingBase.Eid;
            if (dockingBase.IsInDockingRange(player))
            {
                SetState(context, MiningState.WaitingToDock);
                return;
            }

            int minimum = dockingBase.Size + 1;
            int maximum = Math.Max(minimum, dockingBase.Size + dockingBase.SpawnRange);
            int offset = Math.Abs(context.Actor.Id) % 16;
            for (int radius = minimum; radius <= maximum; radius += 2)
            {
                for (int index = 0; index < 16; index++)
                {
                    double direction = ((index + offset) % 16) / 16.0;
                    Position candidate = dockingBase.CurrentPosition.OffsetInDirection(direction, radius).Center;
                    if (!_navigation.TryStart(context, candidate, _definition.Mining.Throttle))
                        continue;
                    _origin = candidate;
                    SetState(context, MiningState.Returning);
                    _audit.Write(context.Actor.Id, "mining_base_recovery", AutonomousActorStatus.Active);
                    return;
                }
            }
            SetState(context, MiningState.RouteRetry);
        }

        private void TryDock(GameActionContext context, Player player)
        {
            _navigation.Stop(context);
            if (player.EffectHandler.ContainsEffect(EffectType.effect_aggressor) ||
                player.HasPvpEffect ||
                player.HasTeleportSicknessEffect)
                return;
            _dock.Execute(context, new DockAction(_dockingBaseEid));
            _audit.Write(context.Actor.Id, "mining_dock", AutonomousActorStatus.Active);
        }

        private bool ShouldRetreatFromThreat(GameActionContext context)
        {
            if (!_definition.Mining.Threat.Enabled)
                return false;
            AutonomousThreatAssessment assessment = AutonomousThreatAssessment.From(
                _perception.Observe(context),
                _definition.Mining.Threat.ResponseRange);
            return assessment.HasThreat;
        }

        private TerrainLock FindTargetLock(Player player)
        {
            if (!_target.HasValue)
                return null;
            return player.GetLocks()
                .OfType<TerrainLock>()
                .FirstOrDefault(candidate =>
                    candidate.Location.intX == _target.Value.intX &&
                    candidate.Location.intY == _target.Value.intY);
        }

        private bool HandleRobotRecovery(GameActionContext context, bool dead)
        {
            if (_robotRecoveryRequired)
            {
                _navigation.Stop(context);
                return true;
            }
            AutonomousRobotRecoveryReason reason = AutonomousRobotRecoveryPolicy.Assess(
                _expectedRobotEid,
                context.Actor.ActiveRobotEid,
                dead);
            if (reason == AutonomousRobotRecoveryReason.None)
                return false;
            _navigation.Stop(context);
            if (_recovery.RequireRecovery(reason, context.Actor.ActiveRobotEid))
            {
                _robotRecoveryRequired = true;
                _audit.Write(context.Actor.Id, "mining_recovery_required", AutonomousActorStatus.Active,
                    reason.ToString());
            }
            return true;
        }

        private void SetState(GameActionContext context, MiningState state)
        {
            _state = state;
            _stateElapsed = TimeSpan.Zero;
            SaveWorkState(context);
        }

        private void SaveWorkState(GameActionContext context)
        {
            _workStateStore.Save(new AutonomousWorkState(
                context.Actor.Id,
                Name,
                _state.ToString(),
                _dockingBaseEid,
                context.Actor.ZoneId,
                _origin,
                _target,
                _material,
                _surveySiteIndex));
        }
    }
}
