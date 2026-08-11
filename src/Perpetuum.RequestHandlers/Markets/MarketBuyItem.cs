using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;
using Perpetuum.Services.MarketEngine;

namespace Perpetuum.RequestHandlers.Markets
{
    public class MarketBuyItem : IRequestHandler
    {
        private readonly IMarketBuyActionService _marketBuyActionService;

        public MarketBuyItem(IMarketBuyActionService marketBuyActionService)
        {
            _marketBuyActionService = marketBuyActionService;
        }

        public void HandleRequest(IRequest request)
        {
            var buyer = request.Session.Character;
            var action = new MarketBuyAction(
                request.Data.GetOrDefault<int>(k.marketItemID),
                request.Data.GetOrDefault<int>(k.useCorporationWallet) == 1,
                request.Data.GetOrDefault<int>(k.quantity));
            var context = new GameActionContext(buyer, GameActionSource.Client);
            MarketBuyResult actionResult = _marketBuyActionService.Execute(context, action);

            Market.SendMarketItemBoughtMessage(buyer, actionResult.BoughtItem);

            if (actionResult.SendSellOrderUpdate)
            {
                var message = Message.Builder.SetCommand(Commands.MarketSellOrderUpdate)
                    .WithData(new Dictionary<string, object>
                    {
                        {k.sellOrder, actionResult.SellOrder.ToDictionary()}
                    });

                if (actionResult.SendSellOrderUpdateToSeller)
                {
                    message.ToCharacters(actionResult.Seller, buyer).Send();
                }
                else
                {
                    message.ToCharacter(buyer).Send();
                }
            }

            Message.Builder.SetCommand(Commands.ListContainer)
                .WithData(actionResult.PublicContainer.ToDictionary())
                .ToCharacter(buyer)
                .Send();
        }
    }
}
