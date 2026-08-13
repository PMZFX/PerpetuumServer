using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionResearch : IRequestHandler
    {
        private readonly IProductionResearchActionService _research;

        public ProductionResearch(IProductionResearchActionService research)
        {
            _research = research;
        }

        public void HandleRequest(IRequest request)
        {
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            var action = new ProductionResearchAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<long>(k.item),
                request.Data.GetOrDefault<long>(k.researchKitEID),
                request.Data.GetOrDefault<int>(k.useCorporationWallet) == 1);
            var result = _research.Execute(context, action);
            Message.Builder.FromRequest(request).WithData(result.ToDictionary(context.Actor)).Send();
        }
    }
}
