using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Extensions
{
    public class ExtensionBuyForPoints : IRequestHandler
    {
        private readonly IExtensionTrainingActionService _training;

        public ExtensionBuyForPoints(IExtensionTrainingActionService training)
        {
            _training = training;
        }

        public void HandleRequest(IRequest request)
        {
            ExtensionTrainingResult result = _training.Execute(
                new GameActionContext(request.Session.Character, GameActionSource.Client),
                new ExtensionTrainingAction(request.Data.GetOrDefault<int>(k.extensionID)));
            Message.Builder.FromRequest(request).WithData(result.Payload).Send();
        }
    }
}
