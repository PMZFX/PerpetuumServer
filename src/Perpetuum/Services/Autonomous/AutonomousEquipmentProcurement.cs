using System;
using Perpetuum.Common.Loggers.Transaction;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;
using Perpetuum.Wallets;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousEquipmentProcurementResult
    {
        Disabled,
        NoEligibleOffer,
        WalletReserveReached,
        Purchased
    }

    public sealed class AutonomousEquipmentProcurement
    {
        private AutonomousEquipmentProcurement(
            AutonomousEquipmentProcurementResult result,
            int definition = 0,
            int quantity = 0,
            double unitPrice = 0)
        {
            Result = result;
            Definition = definition;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public AutonomousEquipmentProcurementResult Result { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public double UnitPrice { get; }

        public static AutonomousEquipmentProcurement For(AutonomousEquipmentProcurementResult result) =>
            new AutonomousEquipmentProcurement(result);

        public static AutonomousEquipmentProcurement Purchased(
            int definition,
            int quantity,
            double unitPrice) =>
            new AutonomousEquipmentProcurement(
                AutonomousEquipmentProcurementResult.Purchased,
                definition,
                quantity,
                unitPrice);
    }

    public interface IAutonomousEquipmentProcurementService
    {
        AutonomousEquipmentProcurement PurchaseOne(
            GameActionContext context,
            int definition,
            AutonomousEquipmentProcurementOptions options,
            bool useCorporationWallet);
    }

    /// <summary>
    /// Buys one missing robot or module from the acting character's current
    /// visible market. Selection uses the same corporation visibility rules as
    /// the market UI, and the shared market action revalidates the exact order,
    /// price cap, wallet, ownership, and transaction immediately before sale.
    /// </summary>
    public sealed class AutonomousEquipmentProcurementService : IAutonomousEquipmentProcurementService
    {
        private readonly IMarketOrderRepository _orders;
        private readonly IMarketBuyActionService _marketBuy;

        public AutonomousEquipmentProcurementService(
            IMarketOrderRepository orders,
            IMarketBuyActionService marketBuy)
        {
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _marketBuy = marketBuy ?? throw new ArgumentNullException(nameof(marketBuy));
        }

        public AutonomousEquipmentProcurement PurchaseOne(
            GameActionContext context,
            int definition,
            AutonomousEquipmentProcurementOptions options,
            bool useCorporationWallet)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (definition <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.Enabled)
                return AutonomousEquipmentProcurement.For(AutonomousEquipmentProcurementResult.Disabled);

            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
            Market market = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            MarketOrder offer = _orders.GetLowestSellOrder(
                definition,
                options.MaximumUnitPrice,
                context.Actor.Eid,
                market,
                context.Actor.CorporationEid);
            if (offer == null || !offer.isSell || offer.marketEID != market.Eid ||
                offer.price <= 0 || double.IsNaN(offer.price) || double.IsInfinity(offer.price) ||
                offer.quantity == 0)
                return AutonomousEquipmentProcurement.For(AutonomousEquipmentProcurementResult.NoEligibleOffer);

            IWallet<double> wallet = context.Actor.GetWallet(
                useCorporationWallet,
                TransactionType.marketBuy);
            if (!CanPurchaseOne(wallet.Balance, options.WalletReserve, offer.price))
                return AutonomousEquipmentProcurement.For(AutonomousEquipmentProcurementResult.WalletReserveReached);

            MarketBuyResult purchase = _marketBuy.Execute(
                context,
                new MarketBuyAction(
                    offer.id,
                    useCorporationWallet,
                    1,
                    options.MaximumUnitPrice));
            return AutonomousEquipmentProcurement.Purchased(
                purchase.BoughtItem.Definition,
                purchase.BoughtItem.Quantity,
                offer.price);
        }

        public static bool CanPurchaseOne(double walletBalance, double walletReserve, double unitPrice)
        {
            if (double.IsNaN(walletBalance) || double.IsInfinity(walletBalance) ||
                double.IsNaN(walletReserve) || double.IsInfinity(walletReserve) ||
                double.IsNaN(unitPrice) || double.IsInfinity(unitPrice) ||
                walletBalance < 0 || walletReserve < 0 || unitPrice <= 0)
                return false;
            return Math.Max(0, walletBalance - walletReserve) >= unitPrice;
        }
    }
}
