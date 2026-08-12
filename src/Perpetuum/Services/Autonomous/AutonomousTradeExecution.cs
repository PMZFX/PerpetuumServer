using System;
using System.Linq;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.EntityFramework;
using Perpetuum.Items;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousTradeAcquisition
    {
        private AutonomousTradeAcquisition(AutonomousTradeState state)
        {
            State = state;
        }

        public AutonomousTradeState State { get; }
        public bool Purchased => State != null;

        public static AutonomousTradeAcquisition NoOffer() => new AutonomousTradeAcquisition(null);
        public static AutonomousTradeAcquisition Acquired(AutonomousTradeState state) =>
            new AutonomousTradeAcquisition(state ?? throw new ArgumentNullException(nameof(state)));
    }

    public sealed class AutonomousTradeDisposition
    {
        public AutonomousTradeDisposition(int quantityRemoved, double unitPrice, int quantityRemaining)
        {
            QuantityRemoved = quantityRemoved;
            UnitPrice = unitPrice;
            QuantityRemaining = quantityRemaining;
        }

        public int QuantityRemoved { get; }
        public double UnitPrice { get; }
        public int QuantityRemaining { get; }
        public bool Completed => QuantityRemaining == 0;
    }

    public interface IAutonomousTradeExecutionService
    {
        AutonomousTradeAcquisition Acquire(GameActionContext context, AutonomousTradePlan plan);
        AutonomousTradeDisposition Dispose(
            GameActionContext context,
            AutonomousTradeState state,
            double minimumUnitPrice,
            double minimumMargin,
            int orderDurationHours);
    }

    /// <summary>
    /// Couples normal player market and relocation actions with durable shipment
    /// state in one ambient transaction. A committed purchase therefore always
    /// has a recoverable destination, and a committed sale cannot be replayed
    /// after a restart.
    /// </summary>
    public sealed class AutonomousTradeExecutionService : IAutonomousTradeExecutionService
    {
        private readonly IMarketOrderRepository _orders;
        private readonly IMarketBuyActionService _marketBuy;
        private readonly IMarketCreateSellOrderActionService _marketSell;
        private readonly IRelocateItemsActionService _relocate;
        private readonly IAutonomousTradeStateStore _stateStore;
        private readonly IEntityDefaultReader _entityDefaults;

        public AutonomousTradeExecutionService(
            IMarketOrderRepository orders,
            IMarketBuyActionService marketBuy,
            IMarketCreateSellOrderActionService marketSell,
            IRelocateItemsActionService relocate,
            IAutonomousTradeStateStore stateStore,
            IEntityDefaultReader entityDefaults)
        {
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _marketBuy = marketBuy ?? throw new ArgumentNullException(nameof(marketBuy));
            _marketSell = marketSell ?? throw new ArgumentNullException(nameof(marketSell));
            _relocate = relocate ?? throw new ArgumentNullException(nameof(relocate));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _entityDefaults = entityDefaults ?? throw new ArgumentNullException(nameof(entityDefaults));
        }

        public AutonomousTradeAcquisition Acquire(GameActionContext context, AutonomousTradePlan plan)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);

            Market market = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            if (market.Eid != plan.Source.MarketEid ||
                context.Actor.CurrentDockingBaseEid != plan.Source.DockingBaseEid)
                return AutonomousTradeAcquisition.NoOffer();

            double priceCap = plan.Source.BestSellPrice ?? 0;
            if (priceCap <= 0)
                return AutonomousTradeAcquisition.NoOffer();

            MarketOrder offer = _orders.GetLowestSellOrder(
                plan.Definition,
                priceCap,
                context.Actor.Eid,
                market,
                context.Actor.CorporationEid);
            if (offer == null || !offer.isSell || offer.marketEID != market.Eid)
                return AutonomousTradeAcquisition.NoOffer();

            Robot robot = context.Actor.GetActiveRobot().ThrowIfNull(ErrorCodes.ARobotMustBeSelected);
            RobotInventory cargo = robot.GetContainer().ThrowIfNull(ErrorCodes.ItemNotFound);
            EntityDefault itemDefault = _entityDefaults.Get(plan.Definition)
                .ThrowIfEqual(EntityDefault.None, ErrorCodes.DefinitionNotSupported);
            int quantity = Math.Min(plan.Quantity, offer.quantity < 0 ? plan.Quantity : offer.quantity);
            quantity = Math.Min(quantity, Fits(cargo.FreeCapacity, itemDefault.CalculateVolume(false)));
            if (quantity <= 0)
                return AutonomousTradeAcquisition.NoOffer();

            using (var scope = Db.CreateTransaction())
            {
                if (_stateStore.Load(context.Actor.Id) != null)
                    throw new InvalidOperationException($"Autonomous actor {context.Actor.Id} already has an active shipment.");

                MarketBuyResult purchase = _marketBuy.Execute(
                    context,
                    new MarketBuyAction(offer.id, false, quantity, priceCap));
                int acquiredQuantity = purchase.BoughtItem.Quantity;
                _relocate.Execute(context, new RelocateItemsAction(
                    purchase.PublicContainer.Eid,
                    cargo.Eid,
                    new[] {purchase.BoughtItem.Eid}));

                var state = new AutonomousTradeState(
                    context.Actor.Id,
                    purchase.BoughtItem.Definition,
                    acquiredQuantity,
                    offer.price,
                    plan.Source.MarketEid,
                    plan.Source.DockingBaseEid,
                    plan.Source.ZoneId,
                    plan.Destination.MarketEid,
                    plan.Destination.DockingBaseEid,
                    plan.Destination.ZoneId,
                    DateTime.UtcNow);
                _stateStore.Save(state);
                scope.Complete();
                return AutonomousTradeAcquisition.Acquired(state);
            }
        }

        public AutonomousTradeDisposition Dispose(
            GameActionContext context,
            AutonomousTradeState state,
            double minimumUnitProfit,
            double minimumMargin,
            int orderDurationHours)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (context.Actor.Id != state.CharacterId)
                throw new InvalidOperationException("A shipment can only be disposed by its owning actor.");
            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
            if (context.Actor.CurrentDockingBaseEid != state.DestinationBaseEid)
                throw new InvalidOperationException("A shipment can only be disposed at its planned destination.");
            if (orderDurationHours < 1)
                throw new ArgumentOutOfRangeException(nameof(orderDurationHours));

            Robot robot = context.Actor.GetActiveRobot().ThrowIfNull(ErrorCodes.ARobotMustBeSelected);
            Container cargo = robot.GetContainer().ThrowIfNull(ErrorCodes.ItemNotFound);
            Item item = cargo.GetItems()
                .Where(candidate => candidate.Definition == state.Definition && candidate.Quantity > 0)
                .OrderBy(candidate => candidate.Eid)
                .FirstOrDefault()
                .ThrowIfNull(ErrorCodes.ItemNotFound);
            int quantity = Math.Min(item.Quantity, state.QuantityRemaining);
            double floor = AutonomousTradeSalePolicy.GetMinimumUnitPrice(
                state.UnitCost,
                minimumUnitProfit,
                minimumMargin);

            Market market = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            MarketOrder bid = _orders.GetHighestBuyOrder(
                state.Definition,
                floor,
                context.Actor.Eid,
                market,
                context.Actor.CorporationEid);
            int targetOrderId = 0;
            double unitPrice = floor;
            if (bid != null && !bid.isSell && bid.marketEID == market.Eid)
            {
                unitPrice = bid.price;
                targetOrderId = bid.id;
                if (bid.quantity > 0)
                    quantity = Math.Min(quantity, bid.quantity);
            }

            using (var scope = Db.CreateTransaction())
            {
                int before = QuantityIn(cargo, state.Definition);
                _marketSell.Execute(context, new MarketCreateSellOrderAction(
                    item.Eid,
                    orderDurationHours,
                    unitPrice,
                    quantity,
                    false,
                    cargo.Eid,
                    false,
                    targetOrderId));
                int removed = before - QuantityIn(cargo, state.Definition);
                if (removed <= 0 || removed > state.QuantityRemaining)
                    throw new InvalidOperationException("The market action did not remove the expected shipment quantity.");

                int remaining = state.QuantityRemaining - removed;
                if (remaining == 0)
                    _stateStore.Delete(state.CharacterId);
                else
                    _stateStore.Save(state.WithRemainingQuantity(remaining));
                scope.Complete();
                return new AutonomousTradeDisposition(removed, unitPrice, remaining);
            }
        }

        private static int Fits(double freeCapacity, double unitVolume)
        {
            if (freeCapacity <= 0 || unitVolume <= 0 || double.IsNaN(unitVolume) || double.IsInfinity(unitVolume))
                return 0;
            double value = Math.Floor(freeCapacity / unitVolume);
            return value >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);
        }

        private static int QuantityIn(Container container, int definition)
        {
            return container.GetItems()
                .Where(item => item.Definition == definition)
                .Sum(item => item.Quantity);
        }
    }

    public static class AutonomousTradeSalePolicy
    {
        public static double GetMinimumUnitPrice(
            double unitCost,
            double minimumUnitProfit,
            double minimumMargin)
        {
            ValidatePositive(unitCost, nameof(unitCost));
            ValidateNonNegative(minimumUnitProfit, nameof(minimumUnitProfit));
            ValidateNonNegative(minimumMargin, nameof(minimumMargin));
            return Math.Max(unitCost + minimumUnitProfit, unitCost * (1 + minimumMargin));
        }

        private static void ValidatePositive(double value, string name)
        {
            if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void ValidateNonNegative(double value, string name)
        {
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
