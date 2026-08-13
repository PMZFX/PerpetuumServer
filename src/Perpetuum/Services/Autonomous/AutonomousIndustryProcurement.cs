using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Common.Loggers.Transaction;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;
using Perpetuum.Wallets;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousIndustryProcurementResult
    {
        Disabled,
        NoPurchaseNeeded,
        NoEligibleOffer,
        WalletReserveReached,
        Purchased
    }

    public sealed class AutonomousIndustryProcurement
    {
        private AutonomousIndustryProcurement(
            AutonomousIndustryProcurementResult result,
            int definition = 0,
            int quantity = 0,
            double unitPrice = 0)
        {
            Result = result;
            Definition = definition;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public AutonomousIndustryProcurementResult Result { get; }
        public int Definition { get; }
        public int Quantity { get; }
        public double UnitPrice { get; }

        public static AutonomousIndustryProcurement For(AutonomousIndustryProcurementResult result) =>
            new AutonomousIndustryProcurement(result);

        public static AutonomousIndustryProcurement Purchased(int definition, int quantity, double unitPrice) =>
            new AutonomousIndustryProcurement(
                AutonomousIndustryProcurementResult.Purchased,
                definition,
                quantity,
                unitPrice);
    }

    public interface IAutonomousIndustryProcurementService
    {
        AutonomousIndustryProcurement PurchaseNext(
            GameActionContext context,
            IReadOnlyDictionary<int, long> requirements,
            AutonomousManufacturerProcurementOptions options,
            bool useCorporationWallet,
            int? preferredDefinition = null);
    }

    /// <summary>
    /// Fulfils one bounded requirement from a visible sell order in the
    /// character's current market. The shared market action remains
    /// authoritative for ownership, access, price, wallet, and persistence.
    /// </summary>
    public sealed class AutonomousIndustryProcurementService : IAutonomousIndustryProcurementService
    {
        private readonly IMarketOrderRepository _orders;
        private readonly IMarketBuyActionService _marketBuy;

        public AutonomousIndustryProcurementService(
            IMarketOrderRepository orders,
            IMarketBuyActionService marketBuy)
        {
            _orders = orders ?? throw new ArgumentNullException(nameof(orders));
            _marketBuy = marketBuy ?? throw new ArgumentNullException(nameof(marketBuy));
        }

        public AutonomousIndustryProcurement PurchaseNext(
            GameActionContext context,
            IReadOnlyDictionary<int, long> requirements,
            AutonomousManufacturerProcurementOptions options,
            bool useCorporationWallet,
            int? preferredDefinition = null)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (!options.Enabled)
                return AutonomousIndustryProcurement.For(AutonomousIndustryProcurementResult.Disabled);
            if (requirements.Count == 0)
                return AutonomousIndustryProcurement.For(AutonomousIndustryProcurementResult.NoPurchaseNeeded);

            context.Actor.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
            Market market = context.Actor.GetCurrentDockingBase().GetMarketOrThrow();
            IWallet<double> wallet = context.Actor.GetWallet(
                useCorporationWallet,
                TransactionType.marketBuy);
            double spendable = Math.Max(0, wallet.Balance - options.WalletReserve);
            if (spendable <= 0)
                return AutonomousIndustryProcurement.For(AutonomousIndustryProcurementResult.WalletReserveReached);

            bool unaffordableOffer = false;
            foreach (KeyValuePair<int, long> requirement in OrderRequirements(
                         requirements,
                         preferredDefinition))
            {
                MarketOrder offer = _orders.GetLowestSellOrder(
                    requirement.Key,
                    options.MaximumUnitPrice,
                    context.Actor.Eid,
                    market,
                    context.Actor.CorporationEid);
                if (offer == null || !offer.isSell || offer.marketEID != market.Eid ||
                    offer.price <= 0 || double.IsNaN(offer.price) || double.IsInfinity(offer.price))
                    continue;

                int quantity = SelectPurchaseQuantity(
                    requirement.Value,
                    offer.quantity,
                    options.MaximumPurchaseQuantity,
                    offer.price,
                    spendable);
                if (quantity <= 0)
                {
                    unaffordableOffer = true;
                    continue;
                }

                MarketBuyResult purchase = _marketBuy.Execute(
                    context,
                    new MarketBuyAction(
                        offer.id,
                        useCorporationWallet,
                        quantity,
                        options.MaximumUnitPrice));
                return AutonomousIndustryProcurement.Purchased(
                    purchase.BoughtItem.Definition,
                    purchase.BoughtItem.Quantity,
                    offer.price);
            }

            return AutonomousIndustryProcurement.For(unaffordableOffer
                ? AutonomousIndustryProcurementResult.WalletReserveReached
                : AutonomousIndustryProcurementResult.NoEligibleOffer);
        }

        public static int SelectPurchaseQuantity(
            long requiredQuantity,
            int offerQuantity,
            int maximumPurchaseQuantity,
            double unitPrice,
            double spendableCredit)
        {
            if (requiredQuantity <= 0 || maximumPurchaseQuantity <= 0 || unitPrice <= 0 ||
                double.IsNaN(unitPrice) || double.IsInfinity(unitPrice) ||
                spendableCredit <= 0 || double.IsNaN(spendableCredit) ||
                double.IsInfinity(spendableCredit))
                return 0;

            long quantity = Math.Min(requiredQuantity, maximumPurchaseQuantity);
            if (offerQuantity >= 0)
                quantity = Math.Min(quantity, offerQuantity);
            double affordableQuantity = Math.Floor(spendableCredit / unitPrice);
            if (affordableQuantity < quantity)
                quantity = (long)Math.Max(0, affordableQuantity);
            return (int)Math.Max(0, Math.Min(quantity, int.MaxValue));
        }

        public static IReadOnlyDictionary<int, long> SubtractPurchased(
            IReadOnlyDictionary<int, long> requirements,
            int definition,
            int quantity)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (definition <= 0 || quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));

            return requirements
                .Select(item => new KeyValuePair<int, long>(
                    item.Key,
                    item.Key == definition ? Math.Max(0, item.Value - quantity) : item.Value))
                .Where(item => item.Value > 0)
                .OrderBy(item => item.Key)
                .ToDictionary(item => item.Key, item => item.Value);
        }

        private static IEnumerable<KeyValuePair<int, long>> OrderRequirements(
            IReadOnlyDictionary<int, long> requirements,
            int? preferredDefinition)
        {
            if (requirements.Any(item => item.Key <= 0 || item.Value <= 0))
                throw new ArgumentOutOfRangeException(nameof(requirements));
            return requirements
                .OrderBy(item => preferredDefinition.HasValue && item.Key == preferredDefinition.Value ? 0 : 1)
                .ThenBy(item => item.Key);
        }
    }
}
