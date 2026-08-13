using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.Actions
{
    public sealed class ProductionResearchQuoteAction
    {
        public ProductionResearchQuoteAction(long facilityEid, int researchKitDefinition, int targetDefinition)
        {
            FacilityEid = facilityEid;
            ResearchKitDefinition = researchKitDefinition;
            TargetDefinition = targetDefinition;
        }

        public long FacilityEid { get; }
        public int ResearchKitDefinition { get; }
        public int TargetDefinition { get; }
    }

    public sealed class ProductionResearchAction
    {
        public ProductionResearchAction(
            long facilityEid,
            long itemEid,
            long researchKitEid,
            bool useCorporationWallet)
        {
            FacilityEid = facilityEid;
            ItemEid = itemEid;
            ResearchKitEid = researchKitEid;
            UseCorporationWallet = useCorporationWallet;
        }

        public long FacilityEid { get; }
        public long ItemEid { get; }
        public long ResearchKitEid { get; }
        public bool UseCorporationWallet { get; }
    }

    public interface IProductionResearchActionService
    {
        ResearchQuote Quote(GameActionContext context, ProductionResearchQuoteAction action);
        ResearchProductionResult Execute(GameActionContext context, ProductionResearchAction action);
    }

    public sealed class ProductionResearchActionService : IProductionResearchActionService
    {
        private readonly ProductionManager _productionManager;
        private readonly ProductionProcessor _productionProcessor;
        private readonly IGameActionAudit _audit;

        public ProductionResearchActionService(
            ProductionManager productionManager,
            ProductionProcessor productionProcessor,
            IGameActionAudit audit)
        {
            _productionManager = productionManager ?? throw new ArgumentNullException(nameof(productionManager));
            _productionProcessor = productionProcessor ?? throw new ArgumentNullException(nameof(productionProcessor));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public ResearchQuote Quote(GameActionContext context, ProductionResearchQuoteAction action)
        {
            return _audit.Execute(context, "productionResearchQuote", () => QuoteCore(context, action));
        }

        public ResearchProductionResult Execute(GameActionContext context, ProductionResearchAction action)
        {
            return _audit.Execute(context, "productionResearch", () => ExecuteCore(context, action));
        }

        private ResearchQuote QuoteCore(GameActionContext context, ProductionResearchQuoteAction action)
        {
            Validate(action);
            _productionManager.GetFacilityAndCheckDocking(
                action.FacilityEid,
                context.Actor,
                out ResearchLab researchLab);
            return researchLab.GetResearchQuote(
                context.Actor,
                action.ResearchKitDefinition,
                action.TargetDefinition);
        }

        private ResearchProductionResult ExecuteCore(GameActionContext context, ProductionResearchAction action)
        {
            Validate(action);
            using (var scope = Db.CreateTransaction())
            {
                _productionManager.PrepareProductionForPublicContainer(
                    action.FacilityEid,
                    context.Actor,
                    out ResearchLab researchLab,
                    out PublicContainer container);
                ResearchProductionResult result = _productionProcessor.ResearchItemTyped(
                    researchLab,
                    context.Actor,
                    container,
                    action.ItemEid,
                    action.ResearchKitEid,
                    action.UseCorporationWallet);
                scope.Complete();
                return result;
            }
        }

        private static void Validate(ProductionResearchQuoteAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            action.FacilityEid.ThrowIfLessOrEqual(0, ErrorCodes.ProductionFacilityNotFound);
            action.ResearchKitDefinition.ThrowIfLessOrEqual(0, ErrorCodes.DefinitionNotSupported);
            action.TargetDefinition.ThrowIfLessOrEqual(0, ErrorCodes.DefinitionNotSupported);
        }

        private static void Validate(ProductionResearchAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            action.FacilityEid.ThrowIfLessOrEqual(0, ErrorCodes.ProductionFacilityNotFound);
            action.ItemEid.ThrowIfLessOrEqual(0, ErrorCodes.ItemNotFound);
            action.ResearchKitEid.ThrowIfLessOrEqual(0, ErrorCodes.ItemNotFound);
        }
    }
}
