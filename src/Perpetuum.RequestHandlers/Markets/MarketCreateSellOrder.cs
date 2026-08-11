using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Markets
{
    public class MarketCreateSellOrder : IRequestHandler
    {
        private readonly IMarketCreateSellOrderActionService _marketCreateSellOrderActionService;

        public MarketCreateSellOrder(IMarketCreateSellOrderActionService marketCreateSellOrderActionService)
        {
            _marketCreateSellOrderActionService = marketCreateSellOrderActionService;
        }

        public void HandleRequest(IRequest request)
        {
            var seller = request.Session.Character;
            var action = new MarketCreateSellOrderAction(
                request.Data.GetOrDefault<long>(k.itemEID),
                request.Data.GetOrDefault<int>(k.duration),
                request.Data.GetOrDefault<double>(k.price),
                request.Data.GetOrDefault<int>(k.quantity),
                request.Data.GetOrDefault<int>(k.useCorporationWallet) == 1,
                request.Data.GetOrDefault<long>(k.container),
                request.Data.GetOrDefault<int>(k.forMembersOf) == 1,
                request.Data.GetOrDefault<int>(k.targetOrder));
            var context = new GameActionContext(seller, GameActionSource.Client);
            MarketCreateSellOrderResult actionResult = _marketCreateSellOrderActionService.Execute(context, action);

            Message.Builder.SetCommand(Commands.ListContainer)
                .WithData(actionResult.PublicContainer.ToDictionary())
                .ToCharacter(seller)
                .Send();
        }
    }
}
