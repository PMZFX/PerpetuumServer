using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionPrototypeQuery : IRequestHandler
    {
        private readonly IProductionPrototypeActionService _prototype;

        public ProductionPrototypeQuery(IProductionPrototypeActionService prototype)
        {
            _prototype = prototype;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new ProductionPrototypeAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<int>(k.definition),
                false);
            PrototypeQuote quote = _prototype.Quote(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                action);
            Message.Builder.FromRequest(request)
                .WithData(quote.ToDictionary(request.Session.Character))
                .Send();
        }
    }
}
