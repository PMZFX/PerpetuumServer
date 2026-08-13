using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.Actions
{
    public sealed class ProductionPrototypeAction
    {
        public ProductionPrototypeAction(
            long facilityEid,
            int targetDefinition,
            bool useCorporationWallet)
        {
            FacilityEid = facilityEid;
            TargetDefinition = targetDefinition;
            UseCorporationWallet = useCorporationWallet;
        }

        public long FacilityEid { get; }
        public int TargetDefinition { get; }
        public bool UseCorporationWallet { get; }
    }

    public interface IProductionPrototypeActionService
    {
        PrototypeQuote Quote(GameActionContext context, ProductionPrototypeAction action);
        PrototypeProductionResult Execute(GameActionContext context, ProductionPrototypeAction action);
    }

    public sealed class ProductionPrototypeActionService : IProductionPrototypeActionService
    {
        private readonly ProductionManager _productionManager;
        private readonly ProductionProcessor _productionProcessor;
        private readonly IGameActionAudit _audit;

        public ProductionPrototypeActionService(
            ProductionManager productionManager,
            ProductionProcessor productionProcessor,
            IGameActionAudit audit)
        {
            _productionManager = productionManager ?? throw new ArgumentNullException(nameof(productionManager));
            _productionProcessor = productionProcessor ?? throw new ArgumentNullException(nameof(productionProcessor));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public PrototypeQuote Quote(GameActionContext context, ProductionPrototypeAction action)
        {
            return _audit.Execute(context, "productionPrototypeQuote", () => QuoteCore(context, action));
        }

        public PrototypeProductionResult Execute(GameActionContext context, ProductionPrototypeAction action)
        {
            return _audit.Execute(context, "productionPrototype", () => ExecuteCore(context, action));
        }

        private PrototypeQuote QuoteCore(
            GameActionContext context,
            ProductionPrototypeAction action)
        {
            Validate(action);
            _productionManager.GetFacilityAndCheckDocking(
                action.FacilityEid,
                context.Actor,
                out Prototyper prototyper);
            return _productionProcessor.GetPrototypeQuote(
                context.Actor,
                action.TargetDefinition,
                prototyper);
        }

        private PrototypeProductionResult ExecuteCore(
            GameActionContext context,
            ProductionPrototypeAction action)
        {
            Validate(action);
            using (var scope = Db.CreateTransaction())
            {
                context.Actor.TechTreeNodeUnlocked(action.TargetDefinition)
                    .ThrowIfFalse(ErrorCodes.TechTreeNodeNotFound);
                _productionManager.PrepareProductionForPublicContainer(
                    action.FacilityEid,
                    context.Actor,
                    out Prototyper prototyper,
                    out PublicContainer sourceContainer);
                PrototypeProductionResult result = _productionProcessor.PrototypeStartTyped(
                    context.Actor,
                    action.TargetDefinition,
                    sourceContainer,
                    prototyper,
                    action.UseCorporationWallet);
                scope.Complete();
                return result;
            }
        }

        private static void Validate(ProductionPrototypeAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            action.FacilityEid.ThrowIfLessOrEqual(0, ErrorCodes.ProductionFacilityNotFound);
            action.TargetDefinition.ThrowIfLessOrEqual(0, ErrorCodes.DefinitionNotSupported);
        }
    }
}
