using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionLineCalibrate : IRequestHandler
    {
        private readonly IProductionCalibrationActionService _calibration;

        public ProductionLineCalibrate(IProductionCalibrationActionService calibration)
        {
            _calibration = calibration;
        }

        public void HandleRequest(IRequest request)
        {
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            var action = new ProductionCalibrationAction(
                request.Data.GetOrDefault<long>(k.facility),
                request.Data.GetOrDefault<long>(k.eid));
            var result = _calibration.Execute(context, action);
            Message.Builder.FromRequest(request).WithData(result.ToDictionary(context.Actor)).Send();
        }
    }
}
