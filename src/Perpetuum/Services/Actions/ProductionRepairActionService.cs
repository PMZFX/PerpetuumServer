using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.Actions
{
    public sealed class ProductionRepairAction
    {
        public ProductionRepairAction(
            long facilityEid,
            long[] targetEids,
            bool useCorporationWallet = false)
        {
            FacilityEid = facilityEid;
            TargetEids = targetEids == null ? null : (long[])targetEids.Clone();
            UseCorporationWallet = useCorporationWallet;
        }

        public long FacilityEid { get; }
        public long[] TargetEids { get; }
        public bool UseCorporationWallet { get; }
    }

    public sealed class ProductionRepairResult
    {
        public ProductionRepairResult(PublicContainer sourceContainer)
        {
            SourceContainer = sourceContainer ?? throw new ArgumentNullException(nameof(sourceContainer));
        }

        public PublicContainer SourceContainer { get; }
    }

    public interface IProductionRepairActionService
    {
        RepairQuote Quote(GameActionContext context, ProductionRepairAction action);
        ProductionRepairResult Execute(GameActionContext context, ProductionRepairAction action);
    }

    public sealed class ProductionRepairActionService : IProductionRepairActionService
    {
        private readonly ProductionManager _productionManager;
        private readonly IGameActionAudit _audit;

        public ProductionRepairActionService(
            ProductionManager productionManager,
            IGameActionAudit audit)
        {
            _productionManager = productionManager ?? throw new ArgumentNullException(nameof(productionManager));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public RepairQuote Quote(GameActionContext context, ProductionRepairAction action)
        {
            return _audit.Execute(context, "productionRepairQuote", () => QuoteCore(context, action));
        }

        public ProductionRepairResult Execute(GameActionContext context, ProductionRepairAction action)
        {
            return _audit.Execute(context, "productionRepair", () => ExecuteCore(context, action));
        }

        private RepairQuote QuoteCore(GameActionContext context, ProductionRepairAction action)
        {
            Validate(context, action);
            _productionManager.PrepareProductionForPublicContainer(
                action.FacilityEid,
                context.Actor,
                out Repair repairFacility,
                out PublicContainer sourceContainer);
            return repairFacility.GetRepairQuote(context.Actor, sourceContainer, action.TargetEids);
        }

        private ProductionRepairResult ExecuteCore(
            GameActionContext context,
            ProductionRepairAction action)
        {
            Validate(context, action);
            using (var scope = Db.CreateTransaction())
            {
                _productionManager.PrepareProductionForPublicContainer(
                    action.FacilityEid,
                    context.Actor,
                    out Repair repairFacility,
                    out PublicContainer sourceContainer);
                repairFacility.RepairItems(
                    context.Actor,
                    action.TargetEids,
                    sourceContainer,
                    action.UseCorporationWallet);
                scope.Complete();
                return new ProductionRepairResult(sourceContainer);
            }
        }

        private static void Validate(GameActionContext context, ProductionRepairAction action)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            action.FacilityEid.ThrowIfLessOrEqual(0, ErrorCodes.ProductionFacilityNotFound);
            if (action.TargetEids == null)
                throw new ArgumentNullException(nameof(action.TargetEids));
        }
    }
}
