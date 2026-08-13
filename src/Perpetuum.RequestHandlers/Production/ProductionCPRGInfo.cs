using Perpetuum.Host.Requests;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.RequestHandlers.Production
{
    public class ProductionCPRGInfo : IRequestHandler
    {
        private readonly IProductionCalibrationActionService _calibration;

        public ProductionCPRGInfo(IProductionCalibrationActionService calibration)
        {
            _calibration = calibration;
        }

        public void HandleRequest(IRequest request)
        {
            var facility = request.Data.GetOrDefault<long>(k.facility);
            var cprgEid = request.Data.GetOrDefault<long>(k.eid);
            var context = new GameActionContext(request.Session.Character, GameActionSource.Client);
            CalibrationProgramQuote quote = _calibration.Quote(
                context,
                new ProductionCalibrationAction(facility, cprgEid));
            Message.Builder.FromRequest(request).WithData(quote.ToDictionary()).Send();
        }
    }
}
