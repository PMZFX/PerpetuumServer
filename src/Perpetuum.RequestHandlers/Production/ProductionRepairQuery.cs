using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionRepairQuery : IRequestHandler
    {
        private readonly IProductionRepairActionService _repair;

        public ProductionRepairQuery(IProductionRepairActionService repair)
        {
            _repair = repair;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new ProductionRepairAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<long[]>(k.target));
            RepairQuote quote = _repair.Quote(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                action);
            var prices = quote.Items.ToDictionary("e", item => new Dictionary<string, object>
            {
                {k.eid, item.ItemEid},
                {k.price, item.Price},
                {k.health, item.HealthRatio}
            });
            var result = new Dictionary<string, object> {{"prices", prices}};
            Message.Builder.FromRequest(request).WithData(result).Send();
        }
    }
}
