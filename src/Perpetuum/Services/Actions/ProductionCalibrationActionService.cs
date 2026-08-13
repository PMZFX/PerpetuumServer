using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.Actions
{
    public sealed class ProductionCalibrationAction
    {
        public ProductionCalibrationAction(long facilityEid, long calibrationProgramEid)
        {
            FacilityEid = facilityEid;
            CalibrationProgramEid = calibrationProgramEid;
        }

        public long FacilityEid { get; }
        public long CalibrationProgramEid { get; }
    }

    public interface IProductionCalibrationActionService
    {
        CalibrationProgramQuote Quote(GameActionContext context, ProductionCalibrationAction action);
        CalibrationLineResult Execute(GameActionContext context, ProductionCalibrationAction action);
    }

    public sealed class ProductionCalibrationActionService : IProductionCalibrationActionService
    {
        private readonly ProductionManager _productionManager;
        private readonly IGameActionAudit _audit;

        public ProductionCalibrationActionService(
            ProductionManager productionManager,
            IGameActionAudit audit)
        {
            _productionManager = productionManager ?? throw new ArgumentNullException(nameof(productionManager));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public CalibrationProgramQuote Quote(GameActionContext context, ProductionCalibrationAction action)
        {
            return _audit.Execute(context, "productionCalibrationQuote", () => QuoteCore(context, action));
        }

        public CalibrationLineResult Execute(GameActionContext context, ProductionCalibrationAction action)
        {
            return _audit.Execute(context, "productionCalibration", () => ExecuteCore(context, action));
        }

        private CalibrationProgramQuote QuoteCore(GameActionContext context, ProductionCalibrationAction action)
        {
            Validate(action);
            _productionManager.PrepareProductionForPublicContainer(
                action.FacilityEid,
                context.Actor,
                out Mill mill,
                out PublicContainer container);
            return ProductionProcessor.GetCalibrationProgramQuote(
                context.Actor,
                container,
                action.CalibrationProgramEid,
                mill);
        }

        private CalibrationLineResult ExecuteCore(GameActionContext context, ProductionCalibrationAction action)
        {
            Validate(action);
            using (var scope = Db.CreateTransaction())
            {
                _productionManager.PrepareProductionForPublicContainer(
                    action.FacilityEid,
                    context.Actor,
                    out Mill mill,
                    out PublicContainer container);
                CalibrationLineResult result = mill.CalibrateLineTyped(
                    context.Actor,
                    action.CalibrationProgramEid,
                    container);
                scope.Complete();
                return result;
            }
        }

        private static void Validate(ProductionCalibrationAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            action.FacilityEid.ThrowIfLessOrEqual(0, ErrorCodes.ProductionFacilityNotFound);
            action.CalibrationProgramEid.ThrowIfLessOrEqual(0, ErrorCodes.ItemNotFound);
        }
    }
}
