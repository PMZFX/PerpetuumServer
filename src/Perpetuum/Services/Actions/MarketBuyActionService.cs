using Perpetuum.Accounting.Characters;
using Perpetuum.Common.Loggers.Transaction;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Items;
using Perpetuum.Services.MarketEngine;

namespace Perpetuum.Services.Actions
{
    public sealed class MarketBuyAction
    {
        public MarketBuyAction(int marketItemId, bool useCorporationWallet, int quantity)
        {
            MarketItemId = marketItemId;
            UseCorporationWallet = useCorporationWallet;
            Quantity = quantity;
        }

        public int MarketItemId { get; }
        public bool UseCorporationWallet { get; }
        public int Quantity { get; }
    }

    public sealed class MarketBuyResult
    {
        public MarketBuyResult(
            PublicContainer publicContainer,
            Item boughtItem,
            MarketOrder sellOrder,
            Character seller,
            bool sendSellOrderUpdate,
            bool sendSellOrderUpdateToSeller)
        {
            PublicContainer = publicContainer;
            BoughtItem = boughtItem;
            SellOrder = sellOrder;
            Seller = seller;
            SendSellOrderUpdate = sendSellOrderUpdate;
            SendSellOrderUpdateToSeller = sendSellOrderUpdateToSeller;
        }

        public PublicContainer PublicContainer { get; }
        public Item BoughtItem { get; }
        public MarketOrder SellOrder { get; }
        public Character Seller { get; }
        public bool SendSellOrderUpdate { get; }
        public bool SendSellOrderUpdateToSeller { get; }
    }

    public interface IMarketBuyActionService
    {
        MarketBuyResult Execute(GameActionContext context, MarketBuyAction action);
    }

    public sealed class MarketBuyActionService : IMarketBuyActionService
    {
        private readonly MarketHandler _marketHandler;
        private readonly MarketHelper _marketHelper;
        private readonly IMarketOrderRepository _marketOrderRepository;
        private readonly IGameActionAudit _audit;

        public MarketBuyActionService(
            MarketHandler marketHandler,
            MarketHelper marketHelper,
            IMarketOrderRepository marketOrderRepository,
            IGameActionAudit audit)
        {
            _marketHandler = marketHandler;
            _marketHelper = marketHelper;
            _marketOrderRepository = marketOrderRepository;
            _audit = audit;
        }

        public MarketBuyResult Execute(GameActionContext context, MarketBuyAction action)
        {
            return _audit.Execute(context, "marketBuy", () => ExecuteCore(context, action));
        }

        private MarketBuyResult ExecuteCore(GameActionContext context, MarketBuyAction action)
        {
            if (action == null)
                throw new System.ArgumentNullException(nameof(action));

            using (var scope = Db.CreateTransaction())
            {
                var buyer = context.Actor;

                action.Quantity.ThrowIfLessOrEqual(0, ErrorCodes.WTFErrorMedicalAttentionSuggested);
                buyer.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
                buyer.CheckPrivilegedTransactionsAndThrowIfFailed();

                var market = buyer.GetCurrentDockingBase().GetMarketOrThrow();
                var sellOrder = _marketOrderRepository.Get(action.MarketItemId).ThrowIfNull(ErrorCodes.ItemNotFound);

                sellOrder.submitterEID.ThrowIfEqual(buyer.Eid, ErrorCodes.CannotBuyFromYourself);

                var corporationEid = buyer.CorporationEid;
                if (sellOrder.forMembersOf != null)
                {
                    corporationEid.ThrowIfNotEqual((long)sellOrder.forMembersOf, ErrorCodes.AccessDenied);
                }

                var publicContainer = buyer.GetPublicContainerWithItems();
                Item boughtItem;
                Character seller = null;
                bool sendSellOrderUpdate = false;
                bool sendSellOrderUpdateToSeller = false;

                if (!sellOrder.isVendorItem)
                {
                    var boughtQuantity = action.Quantity;
                    seller = Character.GetByEid(sellOrder.submitterEID);
                    seller.ThrowIfEqual(null, ErrorCodes.CharacterNotFound);

                    var itemOnMarket = market.GetItemByMarketOrder(sellOrder);
                    boughtItem = itemOnMarket;

                    if (sellOrder.quantity == action.Quantity)
                    {
                        _marketOrderRepository.Delete(sellOrder);
                        sellOrder.quantity = 0;
                    }
                    else if (sellOrder.quantity > action.Quantity)
                    {
                        sellOrder.quantity -= action.Quantity;
                        _marketOrderRepository.UpdateQuantity(sellOrder);

                        boughtItem = itemOnMarket.Unstack(action.Quantity);
                        itemOnMarket.Save();
                    }
                    else
                    {
                        boughtQuantity = sellOrder.quantity;
                        _marketOrderRepository.Delete(sellOrder);
                        sellOrder.quantity = 0;
                    }

                    boughtItem.Owner = buyer.Eid;
                    publicContainer.AddItem(boughtItem, false);
                    publicContainer.Save();

                    _marketHelper.CashIn(
                        buyer,
                        action.UseCorporationWallet,
                        sellOrder.price,
                        sellOrder.itemDefinition,
                        boughtQuantity,
                        TransactionType.marketBuy);
                    market.PayOutToSeller(
                        seller,
                        sellOrder.useCorporationWallet,
                        boughtItem.Definition,
                        sellOrder.price,
                        boughtQuantity,
                        TransactionType.marketSell,
                        sellOrder.IsAffectsAverage(),
                        sellOrder.forMembersOf != null);

                    sendSellOrderUpdate = true;
                    sendSellOrderUpdateToSeller = true;
                }
                else if (sellOrder.quantity < 0)
                {
                    _marketHelper.CashIn(
                        buyer,
                        action.UseCorporationWallet,
                        sellOrder.price,
                        sellOrder.itemDefinition,
                        action.Quantity,
                        TransactionType.marketBuy);

                    boughtItem = publicContainer.CreateAndAddItem(sellOrder.itemDefinition, false, item =>
                    {
                        item.Owner = buyer.Eid;
                        item.Quantity = action.Quantity;
                    });

                    _marketHandler.InsertAveragePrice(
                        market,
                        sellOrder.itemDefinition,
                        action.Quantity * sellOrder.price,
                        action.Quantity);
                    market.AddCentralBank(TransactionType.marketBuy, action.Quantity * sellOrder.price);
                }
                else
                {
                    var boughtQuantity = action.Quantity;

                    if (sellOrder.quantity == action.Quantity)
                    {
                        sellOrder.quantity = 0;
                        _marketOrderRepository.Delete(sellOrder);
                    }
                    else if (sellOrder.quantity < action.Quantity)
                    {
                        boughtQuantity = sellOrder.quantity;
                        sellOrder.quantity = 0;
                        _marketOrderRepository.Delete(sellOrder);
                    }
                    else
                    {
                        sellOrder.quantity -= action.Quantity;
                        _marketOrderRepository.UpdateQuantity(sellOrder);
                    }

                    _marketHelper.CashIn(
                        buyer,
                        action.UseCorporationWallet,
                        sellOrder.price,
                        sellOrder.itemDefinition,
                        boughtQuantity,
                        TransactionType.marketBuy);

                    _marketHandler.InsertAveragePrice(
                        market,
                        sellOrder.itemDefinition,
                        boughtQuantity * sellOrder.price,
                        boughtQuantity);
                    market.AddCentralBank(TransactionType.marketBuy, boughtQuantity * sellOrder.price);

                    boughtItem = publicContainer.CreateAndAddItem(sellOrder.itemDefinition, false, item =>
                    {
                        item.Owner = buyer.Eid;
                        item.Quantity = boughtQuantity;
                    });

                    sendSellOrderUpdate = true;
                }

                publicContainer.Save();
                scope.Complete();

                return new MarketBuyResult(
                    publicContainer,
                    boughtItem,
                    sellOrder,
                    seller,
                    sendSellOrderUpdate,
                    sendSellOrderUpdateToSeller);
            }
        }
    }
}
