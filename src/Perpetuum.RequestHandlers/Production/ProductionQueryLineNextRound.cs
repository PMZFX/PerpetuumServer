using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionQueryLineNextRound : IRequestHandler
    {
        private readonly IProductionMassProductionActionService _massProduction;

        public ProductionQueryLineNextRound(IProductionMassProductionActionService massProduction)
        {
            _massProduction = massProduction;
        }

        public void HandleRequest(IRequest request)
        {
            var lineId = request.Data.GetOrDefault<int>(k.ID);
            var facilityEid = request.Data.GetOrDefault<long>(k.facility);
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            var action = new ProductionMassProductionAction(
                facilityEid,
                lineId,
                false,
                false,
                0,
                quoteNextRound: true);
            var quote = _massProduction.Quote(context, action);
            Message.Builder.FromRequest(request).WithData(quote.ToDictionary()).Send();
        }

    }
}
