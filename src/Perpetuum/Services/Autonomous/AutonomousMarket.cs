using System;
using System.Linq;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public sealed class AutonomousMarketQuote
    {
        public AutonomousMarketQuote(long marketEid, int definition, double? bestBuyPrice, double? averagePrice)
        {
            MarketEid = marketEid;
            Definition = definition;
            BestBuyPrice = bestBuyPrice;
            AveragePrice = averagePrice;
        }

        public long MarketEid { get; }
        public int Definition { get; }
        public double? BestBuyPrice { get; }
        public double? AveragePrice { get; }
    }

    public interface IAutonomousMarketObservationService
    {
        AutonomousMarketQuote Observe(GameActionContext context, int definition);
    }

    /// <summary>
    /// Projects only the local order-book and trade-average data available in
    /// the market UI at the actor's current docking base.
    /// </summary>
    public sealed class AutonomousMarketObservationService : IAutonomousMarketObservationService
    {
        private readonly IMarketOrderRepository _orders;
        private readonly MarketHandler _marketHandler;

        public AutonomousMarketObservationService(IMarketOrderRepository orders, MarketHandler marketHandler)
        {
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _marketHandler = marketHandler ?? throw new ArgumentNullException(nameof(marketHandler));
        }

        public AutonomousMarketQuote Observe(GameActionContext context, int definition)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);

            Market market = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            MarketOrder bestBuy = _orders.GetHighestBuyOrder(
                definition,
                double.Epsilon,
                context.Actor.Eid,
                market,
                context.Actor.CorporationEid);
            MarketAveragePriceEntry average = _marketHandler.GetAveragePriceByMarket(market, definition);
            return new AutonomousMarketQuote(
                market.Eid,
                definition,
                Positive(bestBuy?.price),
                Positive(average?.AveragePrice));
        }

        private static double? Positive(double? value)
        {
            return value.HasValue && value.Value > 0 &&
                   !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? value
                : null;
        }
    }

    public static class AutonomousMarketPricingPolicy
    {
        public static double SelectUnitPrice(
            double minimumUnitPrice,
            double listPriceFactor,
            double? bestBuyPrice,
            double? averagePrice)
        {
            if (bestBuyPrice.HasValue && bestBuyPrice.Value >= minimumUnitPrice)
                return bestBuyPrice.Value;
            if (averagePrice.HasValue && averagePrice.Value > 0)
                return Math.Max(minimumUnitPrice, averagePrice.Value * listPriceFactor);
            return minimumUnitPrice;
        }
    }

    public enum AutonomousCargoDispositionResult
    {
        Disabled,
        NoEligibleItems,
        Sold
    }

    public sealed class AutonomousCargoDisposition
    {
        private AutonomousCargoDisposition(
            AutonomousCargoDispositionResult result,
            int definition = 0,
            int quantity = 0,
            double unitPrice = 0)
        {
            Result = result;
            Definition = definition;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public AutonomousCargoDispositionResult Result { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public double UnitPrice { get; }

        public static AutonomousCargoDisposition Disabled() =>
            new AutonomousCargoDisposition(AutonomousCargoDispositionResult.Disabled);

        public static AutonomousCargoDisposition NoEligibleItems() =>
            new AutonomousCargoDisposition(AutonomousCargoDispositionResult.NoEligibleItems);

        public static AutonomousCargoDisposition Sold(int definition, int quantity, double unitPrice) =>
            new AutonomousCargoDisposition(AutonomousCargoDispositionResult.Sold, definition, quantity, unitPrice);
    }

    public interface IAutonomousCargoDispositionService
    {
        AutonomousCargoDisposition SellNext(
            GameActionContext context,
            MaterialType configuredMaterial,
            AutonomousMarketOptions options);
    }

    public sealed class AutonomousCargoDispositionService : IAutonomousCargoDispositionService
    {
        private readonly IAutonomousCargoService _cargo;
        private readonly IAutonomousMarketObservationService _market;
        private readonly IMarketCreateSellOrderActionService _sellOrders;
        private readonly MaterialHelper _materials;

        public AutonomousCargoDispositionService(
            IAutonomousCargoService cargo,
            IAutonomousMarketObservationService market,
            IMarketCreateSellOrderActionService sellOrders,
            MaterialHelper materials)
        {
            _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _sellOrders = sellOrders ?? throw new ArgumentNullException(nameof(sellOrders));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        }

        public AutonomousCargoDisposition SellNext(
            GameActionContext context,
            MaterialType configuredMaterial,
            AutonomousMarketOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.Enabled)
                return AutonomousCargoDisposition.Disabled();

            int configuredDefinition = _materials.GetMaterialInfo(configuredMaterial).EntityDefault.Definition;
            AutonomousCargoSnapshot cargo = _cargo.Observe(context);
            AutonomousCargoItemSnapshot item = SelectNextItem(cargo, configuredDefinition, options.SellAllRawMaterials);
            if (item == null)
                return AutonomousCargoDisposition.NoEligibleItems();

            AutonomousMarketQuote quote = _market.Observe(context, item.Definition);
            double unitPrice = AutonomousMarketPricingPolicy.SelectUnitPrice(
                options.MinimumUnitPrice,
                options.ListPriceFactor,
                quote.BestBuyPrice,
                quote.AveragePrice);
            _sellOrders.Execute(context, new MarketCreateSellOrderAction(
                item.Eid,
                options.OrderDurationHours,
                unitPrice,
                item.Quantity,
                false,
                cargo.ContainerEid,
                false,
                0));
            return AutonomousCargoDisposition.Sold(item.Definition, item.Quantity, unitPrice);
        }

        public static AutonomousCargoItemSnapshot SelectNextItem(
            AutonomousCargoSnapshot cargo,
            int configuredMaterialDefinition,
            bool sellAllRawMaterials)
        {
            if (cargo == null)
                throw new ArgumentNullException(nameof(cargo));
            return cargo.RawMaterials
                .Where(item => sellAllRawMaterials || item.Definition == configuredMaterialDefinition)
                .OrderBy(item => item.Eid)
                .FirstOrDefault();
        }
    }
}
