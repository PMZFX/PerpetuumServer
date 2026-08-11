using System.Collections.Generic;
using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers
{
    public class RelocateItems : IRequestHandler
    {
        private readonly IRelocateItemsActionService _relocateItemsActionService;

        public RelocateItems(IRelocateItemsActionService relocateItemsActionService)
        {
            _relocateItemsActionService = relocateItemsActionService;
        }

        public void HandleRequest(IRequest request)
        {
            var action = new RelocateItemsAction(
                request.Data.GetOrDefault<long>(k.sourceContainer),
                request.Data.GetOrDefault<long>(k.targetContainer),
                request.Data.GetOrDefault<long[]>(k.eid));
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            RelocateItemsResult actionResult = _relocateItemsActionService.Execute(context, action);
            if (!actionResult.Changed)
                return;

            var target = actionResult.TargetContainer.ToDictionary();
            if (!actionResult.CanListTargetContents)
            {
                target[k.items] = new Dictionary<string, object>();
            }

            var result = new Dictionary<string, object>
            {
                {k.source, actionResult.SourceContainer.ToDictionary()},
                {k.target, target}
            };

            Message.Builder.FromRequest(request).WithData(result).Send();
        }
    }
}
