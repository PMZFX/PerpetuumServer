using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.EntityFramework;
using Perpetuum.Players;
using Perpetuum.Services.Actions;
using Perpetuum.Units.DockingBases;

namespace Perpetuum.Services.Autonomous
{
    public static class AutonomousMarketSurveyPolicy
    {
        public static long SelectNextBase(
            IEnumerable<long> configuredBaseEids,
            IEnumerable<int> definitions,
            IEnumerable<AutonomousMarketMemory> memories,
            long currentBaseEid)
        {
            if (configuredBaseEids == null)
                throw new ArgumentNullException(nameof(configuredBaseEids));
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            if (memories == null)
                throw new ArgumentNullException(nameof(memories));

            int[] observedDefinitions = definitions.Distinct().ToArray();
            AutonomousMarketMemory[] observations = memories.Where(memory => memory != null).ToArray();
            return configuredBaseEids
                .Where(eid => eid > 0 && eid != currentBaseEid)
                .Distinct()
                .Select(eid => new
                {
                    Eid = eid,
                    Oldest = observedDefinitions
                        .Select(definition => observations
                            .Where(memory => memory.DockingBaseEid == eid && memory.Definition == definition)
                            .Select(memory => memory.ObservedAtUtc)
                            .DefaultIfEmpty(DateTime.MinValue)
                            .Max())
                        .DefaultIfEmpty(DateTime.MinValue)
                        .Min()
                })
                .OrderBy(candidate => candidate.Oldest)
                .ThenBy(candidate => candidate.Eid)
                .Select(candidate => candidate.Eid)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Visits configured public markets, remembers only locally observed books,
    /// and executes profitable regional shipments through normal player actions.
    /// Human ownership stops movement; durable shipment state is retained.
    /// </summary>
    public sealed class TraderAutonomousActorBehavior : IAutonomousActorBehavior
    {
        private enum TraderState
        {
            Docked,
            WaitingForWorld,
            Travelling,
            RouteRetry
        }

        private static readonly TimeSpan RouteRetryDelay = TimeSpan.FromSeconds(5);

        private readonly AutonomousActorDefinition _definition;
        private readonly IEntityDefaultReader _entityDefaults;
        private readonly DockingBaseHelper _dockingBases;
        private readonly IAutonomousRegionalMarketService _regionalMarket;
        private readonly IAutonomousCargoService _cargo;
        private readonly IAutonomousTradeExecutionService _trade;
        private readonly IAutonomousTradeStateStore _tradeStateStore;
        private readonly IAutonomousDestinationTravelService _travel;
        private readonly IUndockActionService _undock;
        private readonly IAutonomousActorStateStore _actorStateStore;
        private readonly IAutonomousEquipmentRecoveryCoordinator _equipmentRecovery;
        private readonly IAutonomousActorAudit _audit;
        private int[] _definitions;
        private AutonomousTradeCommodity[] _commodities;
        private AutonomousTradeState _shipment;
        private AutonomousRobotRecoveryTracker _recovery;
        private long _expectedRobotEid;
        private long _targetBaseEid;
        private TraderState _state;
        private TimeSpan _stateElapsed;
        private bool _robotRecoveryRequired;

        public TraderAutonomousActorBehavior(
            AutonomousActorDefinition definition,
            IEntityDefaultReader entityDefaults,
            DockingBaseHelper dockingBases,
            IAutonomousRegionalMarketService regionalMarket,
            IAutonomousCargoService cargo,
            IAutonomousTradeExecutionService trade,
            IAutonomousTradeStateStore tradeStateStore,
            IAutonomousDestinationTravelService travel,
            IUndockActionService undock,
            IAutonomousActorStateStore actorStateStore,
            IAutonomousEquipmentRecoveryCoordinator equipmentRecovery,
            IAutonomousActorAudit audit)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _entityDefaults = entityDefaults ?? throw new ArgumentNullException(nameof(entityDefaults));
            _dockingBases = dockingBases ?? throw new ArgumentNullException(nameof(dockingBases));
            _regionalMarket = regionalMarket ?? throw new ArgumentNullException(nameof(regionalMarket));
            _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
            _trade = trade ?? throw new ArgumentNullException(nameof(trade));
            _tradeStateStore = tradeStateStore ?? throw new ArgumentNullException(nameof(tradeStateStore));
            _travel = travel ?? throw new ArgumentNullException(nameof(travel));
            _undock = undock ?? throw new ArgumentNullException(nameof(undock));
            _actorStateStore = actorStateStore ?? throw new ArgumentNullException(nameof(actorStateStore));
            _equipmentRecovery = equipmentRecovery ?? throw new ArgumentNullException(nameof(equipmentRecovery));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public string Name => "trader";

        public void Start(GameActionContext context)
        {
            AutonomousTraderOptions options = _definition.Trader;
            options.Validate(_definition.CharacterId);
            ResolveConfiguration(options);
            _travel.Stop(context);
            _shipment = _tradeStateStore.Load(context.Actor.Id);
            _targetBaseEid = context.Actor.IsDocked
                ? 0
                : _shipment?.DestinationBaseEid ?? context.Actor.CurrentDockingBaseEid;
            _state = context.Actor.IsDocked ? TraderState.Docked : TraderState.WaitingForWorld;
            _stateElapsed = context.Actor.IsDocked
                ? TimeSpan.FromSeconds(options.DockedDwellSeconds)
                : TimeSpan.Zero;

            _recovery = new AutonomousRobotRecoveryTracker(context.Actor.Id, Name, _actorStateStore);
            AutonomousRecoveryStartDisposition disposition = _recovery.Start(
                context.Actor.ActiveRobotEid,
                _definition.RecoveryRevision);
            _expectedRobotEid = _recovery.ExpectedRobotEid;
            _robotRecoveryRequired = _recovery.RecoveryRequired;
            if (disposition == AutonomousRecoveryStartDisposition.RecoveryRequired)
                _audit.Write(context.Actor.Id, "trader_recovery_required", AutonomousActorStatus.Active,
                    _recovery.RecoveryReason);
            else if (disposition == AutonomousRecoveryStartDisposition.RecoveryAcknowledged)
                _audit.Write(context.Actor.Id, "trader_recovery_acknowledged", AutonomousActorStatus.Active,
                    $"revision_{_definition.RecoveryRevision}");
        }

        public void Stop(GameActionContext context)
        {
            _travel.Stop(context);
            _state = TraderState.Docked;
            _stateElapsed = TimeSpan.Zero;
            _targetBaseEid = 0;
        }

        public void Update(GameActionContext context, TimeSpan elapsed)
        {
            _stateElapsed += elapsed;
            if (context.Actor.IsDocked)
            {
                HandleRobotRecovery(context, false);
                if (!_equipmentRecovery.PrepareDocked(
                        context,
                        _definition.Equipment,
                        _recovery,
                        Name,
                        context.Actor.ActiveRobotEid))
                    return;
                _expectedRobotEid = _recovery.ExpectedRobotEid;
                _robotRecoveryRequired = _recovery.RecoveryRequired;
                if (_state != TraderState.Docked)
                    SetState(TraderState.Docked);
                if (_stateElapsed >= TimeSpan.FromSeconds(_definition.Trader.DockedDwellSeconds))
                    UpdateDocked(context);
                return;
            }

            if (HandleRobotRecovery(context, false))
                return;

            Player player = context.Actor.GetPlayerRobotFromZone();
            if (player == null)
                return;
            if (HandleRobotRecovery(context, player.States.Dead))
                return;

            switch (_state)
            {
                case TraderState.Docked:
                case TraderState.WaitingForWorld:
                    StartTravel(context);
                    break;
                case TraderState.Travelling:
                    UpdateTravel(context, elapsed);
                    break;
                case TraderState.RouteRetry:
                    if (_stateElapsed >= RouteRetryDelay)
                        StartTravel(context);
                    break;
            }
        }

        private void UpdateDocked(GameActionContext context)
        {
            if (_robotRecoveryRequired)
                return;

            long currentBaseEid = context.Actor.CurrentDockingBaseEid;
            if (_shipment != null)
            {
                if (currentBaseEid != _shipment.DestinationBaseEid)
                {
                    TryBeginTrip(context, _shipment.DestinationBaseEid);
                    return;
                }

                _regionalMarket.ObserveAndRemember(context, _shipment.Definition);
                AutonomousTradeDisposition disposition = _trade.Dispose(
                    context,
                    _shipment,
                    _definition.Trader.MinimumUnitProfit,
                    _definition.Trader.MinimumMargin,
                    _definition.Trader.OrderDurationHours);
                _audit.Write(context.Actor.Id, "trader_dispose", AutonomousActorStatus.Active,
                    $"definition_{_shipment.Definition}_quantity_{disposition.QuantityRemoved}_price_{disposition.UnitPrice:0.###}");
                _shipment = disposition.Completed
                    ? null
                    : _shipment.WithRemainingQuantity(disposition.QuantityRemaining);
                SetState(TraderState.Docked);
                return;
            }

            if (IsConfiguredBase(currentBaseEid))
            {
                foreach (int definition in _definitions)
                    _regionalMarket.ObserveAndRemember(context, definition);
            }

            AutonomousTradePlan plan = SelectPlan(context);
            if (plan != null && plan.Source.DockingBaseEid == currentBaseEid)
            {
                _regionalMarket.ObserveAndRemember(context, plan.Definition);
                plan = SelectPlan(context);
                if (plan != null && plan.Source.DockingBaseEid == currentBaseEid)
                {
                    AutonomousTradeAcquisition acquisition = _trade.Acquire(context, plan);
                    if (acquisition.Purchased)
                    {
                        _shipment = acquisition.State;
                        _audit.Write(context.Actor.Id, "trader_acquire", AutonomousActorStatus.Active,
                            $"definition_{_shipment.Definition}_quantity_{_shipment.QuantityRemaining}_price_{_shipment.UnitCost:0.###}");
                        TryBeginTrip(context, _shipment.DestinationBaseEid);
                        return;
                    }
                }
            }

            long nextBase = plan?.Source.DockingBaseEid ?? AutonomousMarketSurveyPolicy.SelectNextBase(
                _definition.Trader.MarketBaseEids,
                _definitions,
                RelevantMemories(context.Actor.Id),
                currentBaseEid);
            if (nextBase > 0)
                TryBeginTrip(context, nextBase);
        }

        private AutonomousTradePlan SelectPlan(GameActionContext context)
        {
            AutonomousCargoSnapshot cargo = _cargo.Observe(context);
            return AutonomousRegionalTradePolicy.Select(
                context.Actor.Id,
                RelevantMemories(context.Actor.Id),
                _commodities,
                DateTime.UtcNow,
                TimeSpan.FromMinutes(_definition.Trader.MaximumObservationAgeMinutes),
                _definition.Trader.MinimumUnitProfit,
                _definition.Trader.MinimumMargin,
                _definition.Trader.MaximumQuantity,
                Math.Max(0, context.Actor.Credit - _definition.Trader.WalletReserve),
                cargo.FreeCapacity);
        }

        private IReadOnlyList<AutonomousMarketMemory> RelevantMemories(int characterId)
        {
            var bases = new HashSet<long>(_definition.Trader.MarketBaseEids);
            var definitions = new HashSet<int>(_definitions);
            return _regionalMarket.Recall(characterId)
                .Where(memory => bases.Contains(memory.DockingBaseEid) && definitions.Contains(memory.Definition))
                .ToArray();
        }

        private void TryBeginTrip(GameActionContext context, long targetBaseEid)
        {
            _targetBaseEid = targetBaseEid;
            if (!AutonomousUndockPolicy.IsReady(
                    _stateElapsed,
                    TimeSpan.FromSeconds(_definition.Trader.DockedDwellSeconds),
                    context.Actor.NextAvailableUndockTime,
                    DateTime.Now))
                return;

            _undock.Execute(context);
            SetState(TraderState.WaitingForWorld);
            _audit.Write(context.Actor.Id, "trader_undock", AutonomousActorStatus.Active,
                $"target_base_{targetBaseEid}");
        }

        private void StartTravel(GameActionContext context)
        {
            if (_targetBaseEid <= 0)
                _targetBaseEid = _shipment?.DestinationBaseEid ?? context.Actor.CurrentDockingBaseEid;
            if (_targetBaseEid <= 0 || !_travel.TryStart(
                    context,
                    _targetBaseEid,
                    _definition.Trader.Throttle))
            {
                SetState(TraderState.RouteRetry);
                return;
            }
            SetState(TraderState.Travelling);
        }

        private void UpdateTravel(GameActionContext context, TimeSpan elapsed)
        {
            AutonomousDestinationTravelStatus status = _travel.Update(context, elapsed);
            if (status == AutonomousDestinationTravelStatus.Arrived)
            {
                _audit.Write(context.Actor.Id, "trader_arrive", AutonomousActorStatus.Active,
                    $"base_{_targetBaseEid}");
                _targetBaseEid = 0;
                SetState(TraderState.Docked);
            }
            else if (status == AutonomousDestinationTravelStatus.BaseUnavailable ||
                     status == AutonomousDestinationTravelStatus.RouteUnavailable ||
                     status == AutonomousDestinationTravelStatus.Blocked ||
                     status == AutonomousDestinationTravelStatus.TransitionTimedOut)
            {
                _audit.Write(context.Actor.Id, "trader_route_retry", AutonomousActorStatus.Active,
                    status.ToString());
                _travel.Stop(context);
                SetState(TraderState.RouteRetry);
            }
        }

        private bool HandleRobotRecovery(GameActionContext context, bool liveRobotDead)
        {
            if (_robotRecoveryRequired)
            {
                _travel.Stop(context);
                return true;
            }
            AutonomousRobotRecoveryReason reason = AutonomousRobotRecoveryPolicy.Assess(
                _expectedRobotEid,
                context.Actor.ActiveRobotEid,
                liveRobotDead);
            if (reason == AutonomousRobotRecoveryReason.None)
                return false;

            _travel.Stop(context);
            if (_recovery.RequireRecovery(reason, context.Actor.ActiveRobotEid))
            {
                _robotRecoveryRequired = true;
                _audit.Write(context.Actor.Id, "trader_recovery_required", AutonomousActorStatus.Active,
                    reason.ToString());
            }
            return true;
        }

        private void ResolveConfiguration(AutonomousTraderOptions options)
        {
            EntityDefault[] defaults = options.Commodities
                .Select(name => _entityDefaults.GetAll().FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (defaults.Any(entityDefault => entityDefault == null || entityDefault == EntityDefault.None))
                throw new InvalidOperationException($"Autonomous trader {_definition.CharacterId} has an unknown commodity definition name.");
            if (defaults.Any(entityDefault => !entityDefault.IsSellable))
                throw new InvalidOperationException($"Autonomous trader {_definition.CharacterId} has a commodity that cannot be sold.");

            DockingBase[] bases = options.MarketBaseEids
                .Select(_dockingBases.GetDockingBase)
                .ToArray();
            if (bases.Any(dockingBase => dockingBase?.Zone == null || dockingBase.GetMarket() == null))
                throw new InvalidOperationException($"Autonomous trader {_definition.CharacterId} has an unavailable market base.");
            if (bases.Select(dockingBase => dockingBase.Zone.Id).Distinct().Count() < 2)
                throw new InvalidOperationException($"Autonomous trader {_definition.CharacterId} requires markets in at least two zones.");

            _definitions = defaults.Select(entityDefault => entityDefault.Definition).ToArray();
            _commodities = defaults.Select(entityDefault => new AutonomousTradeCommodity(
                entityDefault.Definition,
                entityDefault.CalculateVolume(false))).ToArray();
        }

        private bool IsConfiguredBase(long eid)
        {
            return _definition.Trader.MarketBaseEids.Contains(eid);
        }

        private void SetState(TraderState state)
        {
            _state = state;
            _stateElapsed = TimeSpan.Zero;
        }
    }
}
