using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Production
{
    //raw material -> basic commodity
    public class ProductionRefine : IRequestHandler
    {
        private readonly IProductionRefineActionService _refine;

        public ProductionRefine(IProductionRefineActionService refine)
        {
            _refine = refine;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new ProductionRefineAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<int>(k.definition),
                request.Data.GetOrDefault<int>(k.amount));
            ProductionRefineResult result = _refine.Execute(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                action);
            var replyDict = new System.Collections.Generic.Dictionary<string, object>
            {
                {k.sourceContainer, result.SourceContainer.ToDictionary()}
            };
            Message.Builder.FromRequest(request).WithData(replyDict).Send();
        }
    }
}
