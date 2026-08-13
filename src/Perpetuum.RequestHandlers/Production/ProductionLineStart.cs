using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionLineStart : IRequestHandler
    {
        private readonly IProductionMassProductionActionService _massProduction;

        public ProductionLineStart(IProductionMassProductionActionService massProduction)
        {
            _massProduction = massProduction;
        }

        public void HandleRequest(IRequest request)
        {
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            var action = new ProductionMassProductionAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<int>(k.ID),
                request.Data.GetOrDefault<int>(k.useCorporationWallet) == 1,
                request.Data.GetOrDefault<int>(k.inventory) != 1,
                request.Data.GetOrDefault<int>(k.rounds));
            var result = _massProduction.Execute(context, action);
            Message.Builder.FromRequest(request).WithData(result.ToDictionary(context.Actor)).Send();
        }
    }
}
