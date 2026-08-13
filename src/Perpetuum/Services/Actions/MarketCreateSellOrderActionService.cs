using Perpetuum.Common.Loggers.Transaction;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Groups.Corporations;
using Perpetuum.Services.MarketEngine;

namespace Perpetuum.Services.Actions
{
    public sealed class MarketCreateSellOrderAction
    {
        public MarketCreateSellOrderAction(
            long itemEid,
            int duration,
            double pricePerPiece,
            int quantity,
            bool useCorporationWallet,
            long containerEid,
            bool forCorporation,
            int targetOrderId)
        {
            ItemEid = itemEid;
            Duration = duration;
            PricePerPiece = pricePerPiece;
            Quantity = quantity;
            UseCorporationWallet = useCorporationWallet;
            ContainerEid = containerEid;
            ForCorporation = forCorporation;
            TargetOrderId = targetOrderId;
        }

        public long ItemEid { get; }
        public int Duration { get; }
        public double PricePerPiece { get; }
        public int Quantity { get; }
        public bool UseCorporationWallet { get; }
        public long ContainerEid { get; }
        public bool ForCorporation { get; }
        public int TargetOrderId { get; }
    }

    public sealed class MarketCreateSellOrderResult
    {
        public MarketCreateSellOrderResult(PublicContainer publicContainer)
        {
            PublicContainer = publicContainer;
        }

        public PublicContainer PublicContainer { get; }
    }

    public interface IMarketCreateSellOrderActionService
    {
        MarketCreateSellOrderResult Execute(GameActionContext context, MarketCreateSellOrderAction action);
    }

    public sealed class MarketCreateSellOrderActionService : IMarketCreateSellOrderActionService
    {
        private readonly MarketHandler _marketHandler;
        private readonly MarketHelper _marketHelper;
        private readonly IMarketInfoService _marketInfoService;
        private readonly IMarketOrderRepository _marketOrderRepository;
        private readonly IGameActionAudit _audit;

        public MarketCreateSellOrderActionService(
            MarketHandler marketHandler,
            MarketHelper marketHelper,
            IMarketInfoService marketInfoService,
            IMarketOrderRepository marketOrderRepository,
            IGameActionAudit audit)
        {
            _marketHandler = marketHandler;
            _marketHelper = marketHelper;
            _marketInfoService = marketInfoService;
            _marketOrderRepository = marketOrderRepository;
            _audit = audit;
        }

        public MarketCreateSellOrderResult Execute(GameActionContext context, MarketCreateSellOrderAction action)
        {
            return _audit.Execute(context, "marketCreateSellOrder", () => ExecuteCore(context, action));
        }

        private MarketCreateSellOrderResult ExecuteCore(GameActionContext context, MarketCreateSellOrderAction action)
        {
            if (action == null)
                throw new System.ArgumentNullException(nameof(action));

            using (var scope = Db.CreateTransaction())
            {
                var seller = context.Actor;

                seller.IsDocked.ThrowIfFalse(ErrorCodes.CharacterHasToBeDocked);
                seller.CheckPrivilegedTransactionsAndThrowIfFailed();

                action.Quantity.ThrowIfLessOrEqual(0, ErrorCodes.WTFErrorMedicalAttentionSuggested);
                action.PricePerPiece.ThrowIfLessOrEqual(0, ErrorCodes.IllegalMarketPrice);
                action.Duration.ThrowIfLess(1, ErrorCodes.MinimalDurationNotReached);

                var market = seller.GetCurrentDockingBase().GetMarketOrThrow();
                var corporationEid = seller.CorporationEid;
                var publicContainer = seller.GetPublicContainerWithItems();
                var sourceContainer = action.ContainerEid == publicContainer.Eid
                    ? publicContainer
                    : (Container)publicContainer.GetItemOrThrow(action.ContainerEid, true);

                bool forCorporation = action.ForCorporation;
                long? forMembersOf = null;
                if (forCorporation)
                {
                    if (!DefaultCorporationDataCache.IsCorporationDefault(corporationEid))
                    {
                        forMembersOf = corporationEid;
                    }
                    else
                    {
                        forCorporation = false;
                    }
                }

                var itemToSell = market.PrepareItemForSale(
                    seller,
                    action.ItemEid,
                    action.Quantity,
                    sourceContainer);

                if (_marketInfoService.CheckAveragePrice)
                {
                    var averagePrice = _marketHandler.GetAveragePriceByMarket(market, itemToSell.Definition);
                    if (averagePrice != null && averagePrice.AveragePrice > 0)
                    {
                        (action.PricePerPiece < averagePrice.AveragePrice * (1 - _marketInfoService.Margin) ||
                         action.PricePerPiece > averagePrice.AveragePrice * (1 + _marketInfoService.Margin))
                            .ThrowIfTrue(ErrorCodes.PriceOutOfAverageRange);
                    }
                }

                MarketOrder highestBuyOrder;
                if (action.TargetOrderId > 0)
                {
                    highestBuyOrder = _marketOrderRepository.Get(action.TargetOrderId)
                        .ThrowIfNull(ErrorCodes.ItemNotFound);
                    highestBuyOrder.forMembersOf?.ThrowIfNotEqual(corporationEid, ErrorCodes.AccessDenied);

                    market.FulfillSellOrderInstantly(
                        seller,
                        action.UseCorporationWallet,
                        highestBuyOrder,
                        itemToSell,
                        sourceContainer);
                }
                else
                {
                    highestBuyOrder = _marketOrderRepository.GetHighestBuyOrder(
                        itemToSell.Definition,
                        action.PricePerPiece,
                        seller.Eid,
                        market,
                        corporationEid);

                    if (!forCorporation && highestBuyOrder != null)
                    {
                        market.FulfillSellOrderInstantly(
                            seller,
                            action.UseCorporationWallet,
                            highestBuyOrder,
                            itemToSell,
                            sourceContainer);
                    }
                    else
                    {
                        if (!forCorporation)
                        {
                            _marketHelper.CheckSellOrderCounts(seller)
                                .ThrowIfFalse(ErrorCodes.MarketItemsExceed);
                        }

                        var realMarketFee = Market.GetRealMarketFee(seller, action.Duration);
                        _marketHelper.CashInMarketFee(
                            seller,
                            action.UseCorporationWallet,
                            realMarketFee);
                        market.GetDockingBase().AddCentralBank(TransactionType.marketFee, realMarketFee);

                        market.CreateSellOrder(
                            seller.Eid,
                            itemToSell,
                            action.Duration,
                            action.PricePerPiece,
                            action.Quantity,
                            action.UseCorporationWallet,
                            forMembersOf);
                    }
                }

                publicContainer.Save();
                scope.Complete();

                return new MarketCreateSellOrderResult(publicContainer);
            }
        }
    }
}
