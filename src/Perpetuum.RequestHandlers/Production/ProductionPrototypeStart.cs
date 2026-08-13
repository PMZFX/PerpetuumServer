using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionPrototypeStart : IRequestHandler
    {
        private readonly IProductionPrototypeActionService _prototype;

        public ProductionPrototypeStart(IProductionPrototypeActionService prototype)
        {
            _prototype = prototype;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new ProductionPrototypeAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<int>(k.definition),
                request.Data.GetOrDefault<int>(k.useCorporationWallet) == 1);
            var context = new GameActionContext(
                request.Session.Character,
                GameActionSource.Client);
            var result = _prototype.Execute(context, action);
            Message.Builder.FromRequest(request)
                .WithData(result.ToDictionary(context.Actor))
                .Send();
        }
    }
}
