using System;
using System.Linq;
using Perpetuum.Containers;
using Perpetuum.Items;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousManufacturedOutputResult
    {
        NoOutput,
        NoDemand,
        Sold
    }

    public sealed class AutonomousManufacturedOutputDisposition
    {
        private AutonomousManufacturedOutputDisposition(
            AutonomousManufacturedOutputResult result,
            int quantity = 0,
            double unitPrice = 0)
        {
            Result = result;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public AutonomousManufacturedOutputResult Result { get; }
        public int Quantity { get; }
        public double UnitPrice { get; }

        public static AutonomousManufacturedOutputDisposition NoOutput() =>
            new AutonomousManufacturedOutputDisposition(AutonomousManufacturedOutputResult.NoOutput);

        public static AutonomousManufacturedOutputDisposition NoDemand() =>
            new AutonomousManufacturedOutputDisposition(AutonomousManufacturedOutputResult.NoDemand);

        public static AutonomousManufacturedOutputDisposition Sold(int quantity, double unitPrice) =>
            new AutonomousManufacturedOutputDisposition(
                AutonomousManufacturedOutputResult.Sold,
                quantity,
                unitPrice);
    }

    public interface IAutonomousManufacturedOutputService
    {
        AutonomousManufacturedOutputDisposition SellToDemand(
            GameActionContext context,
            int definition,
            long maximumQuantity,
            AutonomousManufacturerOptions options);
    }

    /// <summary>
    /// Rechecks local demand immediately before selling manufactured output and
    /// executes through the same audited market action as a player client.
    /// </summary>
    public sealed class AutonomousManufacturedOutputService : IAutonomousManufacturedOutputService
    {
        private readonly IMarketOrderRepository _orders;
        private readonly IMarketCreateSellOrderActionService _marketSell;

        public AutonomousManufacturedOutputService(
            IMarketOrderRepository orders,
            IMarketCreateSellOrderActionService marketSell)
        {
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _marketSell = marketSell ?? throw new ArgumentNullException(nameof(marketSell));
        }

        public AutonomousManufacturedOutputDisposition SellToDemand(
            GameActionContext context,
            int definition,
            long maximumQuantity,
            AutonomousManufacturerOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (definition <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (maximumQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
            if (options?.Sales == null)
                throw new ArgumentNullException(nameof(options));

            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
            Market market = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            MarketOrder bid = _orders.GetHighestBuyOrder(
                definition,
                options.Sales.MinimumUnitPrice,
                context.Actor.Eid,
                market,
                context.Actor.CorporationEid);
            if (bid == null || bid.isSell || bid.isVendorItem || bid.marketEID != market.Eid ||
                bid.itemDefinition != definition || bid.quantity <= 0)
                return AutonomousManufacturedOutputDisposition.NoDemand();

            PublicContainer container = context.Actor.GetPublicContainerWithItems();
            Item item = SelectOutput(container, definition);
            if (item == null)
                return AutonomousManufacturedOutputDisposition.NoOutput();

            int quantity = (int)Math.Min(
                Math.Min((long)item.Quantity, maximumQuantity),
                bid.quantity);
            if (quantity <= 0)
                return AutonomousManufacturedOutputDisposition.NoDemand();

            int before = QuantityIn(container, definition);
            MarketCreateSellOrderResult result = _marketSell.Execute(
                context,
                new MarketCreateSellOrderAction(
                    item.Eid,
                    options.Sales.OrderDurationHours,
                    bid.price,
                    quantity,
                    options.UseCorporationWallet,
                    container.Eid,
                    false,
                    bid.id));
            int removed = before - QuantityIn(result.PublicContainer, definition);
            if (removed <= 0 || removed > quantity)
                throw new InvalidOperationException("The market action did not remove the expected manufactured output quantity.");

            return AutonomousManufacturedOutputDisposition.Sold(removed, bid.price);
        }

        public static Item SelectOutput(Container container, int definition)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));
            return container.GetItems()
                .Where(item =>
                    item.Definition == definition &&
                    item.Quantity > 0 &&
                    item.ED.IsSellable &&
                    !item.IsDamaged &&
                    (!item.ED.AttributeFlags.Repackable || item.IsRepackaged))
                .OrderBy(item => item.Eid)
                .FirstOrDefault();
        }

        private static int QuantityIn(Container container, int definition)
        {
            return container.GetItems()
                .Where(item => item.Definition == definition)
                .Sum(item => item.Quantity);
        }
    }

    public static class AutonomousManufacturerEconomyPolicy
    {
        public static long SelectDemandQuantity(
            long maximumQuantity,
            double minimumUnitPrice,
            double? bestBuyPrice,
            int? bestBuyQuantity,
            bool bestBuyIsVendor = false)
        {
            if (maximumQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
            if (minimumUnitPrice <= 0 || double.IsNaN(minimumUnitPrice) ||
                double.IsInfinity(minimumUnitPrice))
                throw new ArgumentOutOfRangeException(nameof(minimumUnitPrice));
            if (bestBuyIsVendor || !bestBuyPrice.HasValue || !bestBuyQuantity.HasValue ||
                bestBuyPrice.Value < minimumUnitPrice || bestBuyQuantity.Value <= 0)
                return 0;
            return Math.Min(maximumQuantity, bestBuyQuantity.Value);
        }

        public static long CompletionQuantity(AutonomousIndustryGoalState state, bool salesEnabled)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            long quantity = salesEnabled ? state.CommittedDemandQuantity : state.TargetQuantity;
            return checked(state.InitialInventoryQuantity + quantity);
        }

        public static long RemainingDemand(long committedDemandQuantity, int quantitySold)
        {
            if (committedDemandQuantity < 0)
                throw new ArgumentOutOfRangeException(nameof(committedDemandQuantity));
            if (quantitySold <= 0 || quantitySold > committedDemandQuantity)
                throw new ArgumentOutOfRangeException(nameof(quantitySold));
            return committedDemandQuantity - quantitySold;
        }
    }
}
