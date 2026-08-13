using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.RequestHandlers.Production
{
    //raw material -> basic commodity QUERY
    public class ProductionRefineQuery : IRequestHandler
    {
        private readonly IProductionRefineActionService _refine;

        public ProductionRefineQuery(IProductionRefineActionService refine)
        {
            _refine = refine;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new ProductionRefineAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<int>(k.definition),
                request.Data.GetOrDefault<int>(k.amount));
            RefineQuote quote = _refine.Quote(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                action);
            var components = quote.Components.ToDictionary("c", component => new Dictionary<string, object>
            {
                {k.definition, component.Definition},
                {k.real, component.EffectiveAmount},
                {k.nominal, component.NominalAmount}
            });
            var replyDict = new Dictionary<string, object>
            {
                {k.components, components},
                {k.targetAmount, quote.TargetAmount},
                {k.targetDefinition, quote.TargetDefinition},
                {k.facility, quote.FacilityEid}
            };
            Message.Builder.FromRequest(request).WithData(replyDict).Send();
        }
    }
}
