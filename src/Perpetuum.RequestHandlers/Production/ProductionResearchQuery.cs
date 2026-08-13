using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionResearchQuery : IRequestHandler
    {
        private readonly IProductionResearchActionService _research;

        public ProductionResearchQuery(IProductionResearchActionService research)
        {
            _research = research;
        }

        public void HandleRequest(IRequest request)
        {
            var researchKitDefinition = request.Data.GetOrDefault<int>(k.definition);
            var targetDefinition = request.Data.GetOrDefault<int>(k.target);
            var facilityEid = request.Data.GetOrDefault<long>(k.facility);
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            ResearchQuote quote = _research.Quote(
                context,
                new ProductionResearchQuoteAction(facilityEid, researchKitDefinition, targetDefinition));
            Message.Builder.FromRequest(request).WithData(quote.ToDictionary()).Send();
        }
    }
}
