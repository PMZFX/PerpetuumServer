using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.Actions
{
    public static class ProductionRefineAmountPolicy
    {
        public const int MaximumAmount = 1000000;

        public static int Normalize(int requestedAmount)
        {
            return Math.Max(0, Math.Min(requestedAmount, MaximumAmount));
        }
    }

    public sealed class ProductionRefineAction
    {
        public ProductionRefineAction(long facilityEid, int targetDefinition, int amount)
        {
            FacilityEid = facilityEid;
            TargetDefinition = targetDefinition;
            Amount = amount;
        }

        public long FacilityEid { get; }
        public int TargetDefinition { get; }
        public int Amount { get; }
    }

    public sealed class ProductionRefineResult
    {
        public ProductionRefineResult(
            PublicContainer sourceContainer,
            int targetDefinition,
            int amount)
        {
            SourceContainer = sourceContainer ?? throw new ArgumentNullException(nameof(sourceContainer));
            TargetDefinition = targetDefinition;
            Amount = amount;
        }

        public PublicContainer SourceContainer { get; }
        public int TargetDefinition { get; }
        public int Amount { get; }
    }

    public interface IProductionRefineActionService
    {
        RefineQuote Quote(GameActionContext context, ProductionRefineAction action);
        ProductionRefineResult Execute(GameActionContext context, ProductionRefineAction action);
    }

    public sealed class ProductionRefineActionService : IProductionRefineActionService
    {
        private readonly ProductionManager _productionManager;
        private readonly ProductionProcessor _productionProcessor;
        private readonly IGameActionAudit _audit;

        public ProductionRefineActionService(
            ProductionManager productionManager,
            ProductionProcessor productionProcessor,
            IGameActionAudit audit)
        {
            _productionManager = productionManager ?? throw new ArgumentNullException(nameof(productionManager));
            _productionProcessor = productionProcessor ?? throw new ArgumentNullException(nameof(productionProcessor));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public RefineQuote Quote(GameActionContext context, ProductionRefineAction action)
        {
            return _audit.Execute(context, "productionRefineQuote", () => QuoteCore(context, action));
        }

        public ProductionRefineResult Execute(GameActionContext context, ProductionRefineAction action)
        {
            return _audit.Execute(context, "productionRefine", () => ExecuteCore(context, action));
        }

        private RefineQuote QuoteCore(GameActionContext context, ProductionRefineAction action)
        {
            int amount = Validate(action);
            Refinery refinery = _productionManager.GetFacility<Refinery>(action.FacilityEid);
            refinery.IsOpen.ThrowIfFalse(ErrorCodes.FacilityClosed);
            ProductionDescription description = _productionProcessor
                .GetProductionDescription(action.TargetDefinition)
                .ThrowIfNull(ErrorCodes.DefinitionNotSupported);
            return refinery.GetQuote(
                context.Actor,
                action.TargetDefinition,
                amount,
                description);
        }

        private ProductionRefineResult ExecuteCore(
            GameActionContext context,
            ProductionRefineAction action)
        {
            int amount = Validate(action);
            using (var scope = Db.CreateTransaction())
            {
                _productionManager.PrepareProductionForPublicContainer(
                    action.FacilityEid,
                    context.Actor,
                    out Refinery refinery,
                    out PublicContainer sourceContainer);
                _productionProcessor.Refine(
                    refinery,
                    context.Actor,
                    sourceContainer,
                    action.TargetDefinition,
                    amount);
                scope.Complete();
                return new ProductionRefineResult(
                    sourceContainer,
                    action.TargetDefinition,
                    amount);
            }
        }

        private int Validate(ProductionRefineAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            int amount = ProductionRefineAmountPolicy.Normalize(action.Amount);
            _productionProcessor.CheckTargetDefinitionAndThrowIfFailed(action.TargetDefinition);
            return amount;
        }
    }
}
