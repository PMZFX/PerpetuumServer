using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionRepair : IRequestHandler
    {
        private readonly IProductionRepairActionService _repair;

        public ProductionRepair(IProductionRepairActionService repair)
        {
            _repair = repair;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new ProductionRepairAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<long[]>(k.target),
                request.Data.GetOrDefault<int>(k.useCorporationWallet) == 1);
            ProductionRepairResult repairResult = _repair.Execute(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                action);
            var result = new Dictionary<string, object>
            {
                {k.container, repairResult.SourceContainer.ToDictionary()}
            };
            Message.Builder.FromRequest(request).WithData(result).Send();
        }
    }
}
