using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Containers;
using Perpetuum.Items.Ammos;
using Perpetuum.Modules;
using Perpetuum.Robots;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;
using Perpetuum.Zones.Scanning.Ammos;
using Perpetuum.Zones.Scanning.Modules;
using Perpetuum.Zones.Terrains.Materials;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousMiningProcurementResult
    {
        Disabled,
        NoPurchaseNeeded,
        NoKnownAmmo,
        NoEligibleOffer,
        Transferred,
        Purchased
    }

    public sealed class AutonomousMiningProcurement
    {
        private AutonomousMiningProcurement(
            AutonomousMiningProcurementResult result,
            int definition = 0,
            int quantity = 0,
            double unitPrice = 0)
        {
            Result = result;
            Definition = definition;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public AutonomousMiningProcurementResult Result { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public double UnitPrice { get; }

        public static AutonomousMiningProcurement For(AutonomousMiningProcurementResult result) =>
            new AutonomousMiningProcurement(result);

        public static AutonomousMiningProcurement Transferred(int definition, int quantity) =>
            new AutonomousMiningProcurement(AutonomousMiningProcurementResult.Transferred, definition, quantity);

        public static AutonomousMiningProcurement Purchased(int definition, int quantity, double unitPrice) =>
            new AutonomousMiningProcurement(AutonomousMiningProcurementResult.Purchased, definition, quantity, unitPrice);
    }

    public interface IAutonomousMiningProcurementService
    {
        AutonomousMiningProcurement StockNext(
            GameActionContext context,
            MaterialType material,
            AutonomousMiningResupplyOptions options);
    }

    /// <summary>
    /// Stocks fitted mining ammunition only through visible sell orders at the
    /// actor's current market, then moves it through the same public-container
    /// relocation action available to a docked player.
    /// </summary>
    public sealed class AutonomousMiningProcurementService : IAutonomousMiningProcurementService
    {
        private readonly IMarketOrderRepository _orders;
        private readonly IMarketBuyActionService _marketBuy;
        private readonly IRelocateItemsActionService _relocate;

        public AutonomousMiningProcurementService(
            IMarketOrderRepository orders,
            IMarketBuyActionService marketBuy,
            IRelocateItemsActionService relocate)
        {
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _marketBuy = marketBuy ?? throw new ArgumentNullException(nameof(marketBuy));
            _relocate = relocate ?? throw new ArgumentNullException(nameof(relocate));
        }

        public AutonomousMiningProcurement StockNext(
            GameActionContext context,
            MaterialType material,
            AutonomousMiningResupplyOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.Enabled || !options.BuyFromMarket)
                return AutonomousMiningProcurement.For(AutonomousMiningProcurementResult.Disabled);

            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
            Robot robot = context.Actor.GetActiveRobot().ThrowIfNull(ErrorCodes.ARobotMustBeSelected);
            Container cargo = robot.GetContainer().ThrowIfNull(ErrorCodes.ItemNotFound);
            PublicContainer publicContainer = context.Actor.GetPublicContainerWithItems();
            Ammo[] cargoAmmo = cargo.GetItems().OfType<Ammo>().ToArray();
            Ammo[] publicAmmo = publicContainer.GetItems().OfType<Ammo>().ToArray();
            SupplyNeed[] needs = FindSupplyNeeds(robot, cargoAmmo, publicAmmo, material, options).ToArray();
            if (needs.Length == 0)
                return AutonomousMiningProcurement.For(AutonomousMiningProcurementResult.NoKnownAmmo);

            var blockedDefinitions = new HashSet<int>();
            foreach (SupplyNeed need in needs)
            {
                int cargoQuantity = cargoAmmo
                    .Where(ammo => ammo.Definition == need.Definition)
                    .Sum(ammo => ammo.Quantity);
                if (SelectPurchaseQuantity(
                        need.ReserveQuantity,
                        cargoQuantity,
                        -1,
                        options.MaximumPurchaseQuantity) <= 0)
                    continue;

                Ammo pending = publicAmmo
                    .Where(ammo => ammo.Definition == need.Definition && ammo.Quantity > 0)
                    .OrderBy(ammo => ammo.Eid)
                    .FirstOrDefault();
                if (pending != null)
                {
                    if (!CanTransferPending(
                            need.ReserveQuantity,
                            cargoQuantity,
                            pending.Quantity))
                    {
                        blockedDefinitions.Add(need.Definition);
                        continue;
                    }

                    _relocate.Execute(context, new RelocateItemsAction(
                        publicContainer.Eid,
                        cargo.Eid,
                        new[] {pending.Eid}));
                    return AutonomousMiningProcurement.Transferred(
                        pending.Definition,
                        pending.Quantity);
                }
            }

            Market market = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            bool stockNeeded = false;
            foreach (SupplyNeed need in needs)
            {
                if (blockedDefinitions.Contains(need.Definition))
                    continue;

                int cargoQuantity = cargoAmmo
                    .Where(ammo => ammo.Definition == need.Definition)
                    .Sum(ammo => ammo.Quantity);
                int purchaseQuantity = SelectPurchaseQuantity(
                    need.ReserveQuantity,
                    cargoQuantity,
                    -1,
                    options.MaximumPurchaseQuantity);
                if (purchaseQuantity <= 0)
                    continue;

                stockNeeded = true;
                MarketOrder sellOrder = _orders.GetLowestSellOrder(
                    need.Definition,
                    options.MaximumUnitPrice,
                    context.Actor.Eid,
                    market,
                    context.Actor.CorporationEid);
                if (sellOrder == null || !sellOrder.isSell || sellOrder.marketEID != market.Eid)
                    continue;

                purchaseQuantity = SelectPurchaseQuantity(
                    need.ReserveQuantity,
                    cargoQuantity,
                    sellOrder.quantity,
                    options.MaximumPurchaseQuantity);
                if (purchaseQuantity <= 0)
                    continue;

                MarketBuyResult purchase = _marketBuy.Execute(
                    context,
                    new MarketBuyAction(
                        sellOrder.id,
                        false,
                        purchaseQuantity,
                        options.MaximumUnitPrice));
                _relocate.Execute(context, new RelocateItemsAction(
                    purchase.PublicContainer.Eid,
                    cargo.Eid,
                    new[] {purchase.BoughtItem.Eid}));
                return AutonomousMiningProcurement.Purchased(
                    purchase.BoughtItem.Definition,
                    purchase.BoughtItem.Quantity,
                    sellOrder.price);
            }

            return AutonomousMiningProcurement.For(stockNeeded
                ? AutonomousMiningProcurementResult.NoEligibleOffer
                : AutonomousMiningProcurementResult.NoPurchaseNeeded);
        }

        public static int SelectPurchaseQuantity(
            int reserveQuantity,
            int cargoQuantity,
            int offerQuantity,
            int maximumPurchaseQuantity)
        {
            if (reserveQuantity <= cargoQuantity || reserveQuantity <= 0 || maximumPurchaseQuantity <= 0)
                return 0;

            int quantity = Math.Min(reserveQuantity - Math.Max(0, cargoQuantity), maximumPurchaseQuantity);
            if (offerQuantity >= 0)
                quantity = Math.Min(quantity, offerQuantity);
            return Math.Max(0, quantity);
        }

        public static bool CanTransferPending(
            int reserveQuantity,
            int cargoQuantity,
            int pendingQuantity)
        {
            return pendingQuantity > 0 &&
                   cargoQuantity < reserveQuantity &&
                   pendingQuantity <= reserveQuantity - Math.Max(0, cargoQuantity);
        }

        private static IEnumerable<SupplyNeed> FindSupplyNeeds(
            Robot robot,
            IReadOnlyCollection<Ammo> cargoAmmo,
            IReadOnlyCollection<Ammo> publicAmmo,
            MaterialType material,
            AutonomousMiningResupplyOptions options)
        {
            var allLooseAmmo = cargoAmmo.Concat(publicAmmo).ToArray();

            foreach (GeoScannerModule module in Order(robot.ActiveModules.OfType<GeoScannerModule>()))
            {
                TileScannerAmmo ammo = module.GetAmmo() as TileScannerAmmo ?? allLooseAmmo
                    .OfType<TileScannerAmmo>()
                    .FirstOrDefault(candidate =>
                        candidate.MaterialType == material &&
                        module.CheckLoadableAmmo(candidate.Definition));
                if (ammo != null && ammo.MaterialType == material && options.TileProbeReserve > 0)
                    yield return new SupplyNeed(ammo.Definition, options.TileProbeReserve);
            }

            foreach (DrillerModule module in Order(robot.ActiveModules.OfType<DrillerModule>()))
            {
                MiningAmmo ammo = module.GetAmmo() as MiningAmmo ?? allLooseAmmo
                    .OfType<MiningAmmo>()
                    .FirstOrDefault(candidate =>
                        candidate.MaterialType == material &&
                        module.CheckLoadableAmmo(candidate.Definition));
                if (ammo != null && ammo.MaterialType == material && options.MiningChargeReserve > 0)
                    yield return new SupplyNeed(ammo.Definition, options.MiningChargeReserve);
            }
        }

        private static IEnumerable<T> Order<T>(IEnumerable<T> modules) where T : ActiveModule
        {
            return modules.OrderBy(module => module.ParentComponent.Type).ThenBy(module => module.Slot);
        }

        private sealed class SupplyNeed
        {
            public SupplyNeed(int definition, int reserveQuantity)
            {
                Definition = definition;
                ReserveQuantity = reserveQuantity;
            }

            public int Definition { get; }
            public int ReserveQuantity { get; }
        }
    }
}
