using System;
using Perpetuum.Containers;
using Perpetuum.Data;
using Perpetuum.Log;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.Facilities;

namespace Perpetuum.Services.Actions
{
    public static class ProductionRoundsPolicy
    {
        public static int NormalizeRequestedRounds(int rounds)
        {
            return rounds < 0 ? 1 : rounds;
        }
    }

    public sealed class ProductionMassProductionAction
    {
        public ProductionMassProductionAction(
            long facilityEid,
            int lineId,
            bool useCorporationWallet,
            bool searchInRobots,
            int rounds,
            bool quoteNextRound = false)
        {
            FacilityEid = facilityEid;
            LineId = lineId;
            UseCorporationWallet = useCorporationWallet;
            SearchInRobots = searchInRobots;
            Rounds = rounds;
            QuoteNextRound = quoteNextRound;
        }

        public long FacilityEid { get; }
        public int LineId { get; }
        public bool UseCorporationWallet { get; }
        public bool SearchInRobots { get; }
        public int Rounds { get; }
        public bool QuoteNextRound { get; }
    }

    public interface IProductionMassProductionActionService
    {
        MassProductionLineQuote Quote(GameActionContext context, ProductionMassProductionAction action);
        MassProductionResult Execute(GameActionContext context, ProductionMassProductionAction action);
    }

    public sealed class ProductionMassProductionActionService : IProductionMassProductionActionService
    {
        private readonly ProductionManager _productionManager;
        private readonly ProductionProcessor _productionProcessor;
        private readonly IGameActionAudit _audit;

        public ProductionMassProductionActionService(
            ProductionManager productionManager,
            ProductionProcessor productionProcessor,
            IGameActionAudit audit)
        {
            _productionManager = productionManager ?? throw new ArgumentNullException(nameof(productionManager));
            _productionProcessor = productionProcessor ?? throw new ArgumentNullException(nameof(productionProcessor));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public MassProductionLineQuote Quote(GameActionContext context, ProductionMassProductionAction action)
        {
            return _audit.Execute(context, "productionMassQuote", () => QuoteCore(context, action));
        }

        public MassProductionResult Execute(GameActionContext context, ProductionMassProductionAction action)
        {
            return _audit.Execute(context, "productionMassStart", () => ExecuteCore(context, action));
        }

        private MassProductionLineQuote QuoteCore(GameActionContext context, ProductionMassProductionAction action)
        {
            Validate(action);
            _productionManager.GetFacilityAndCheckDocking(
                action.FacilityEid,
                context.Actor,
                out Mill mill);
            ProductionLine line = ProductionLine.LoadByIdAndCharacterAndFacility(
                context.Actor,
                action.LineId,
                action.FacilityEid);

            MassProductionQuote quote = mill.GetMassProductionQuote(
                line.GetOrCreateCalibrationProgram(mill),
                context.Actor,
                line.TargetDefinition,
                line.GetMaterialPoints(),
                line.GetTimePoints(),
                action.QuoteNextRound);

            if (action.QuoteNextRound)
            {
                double materialEfficiency = line.MaterialEfficiency;
                double timeEfficiency = line.TimeEfficiency;
                Logger.Info("pre decalibration mateff:" + materialEfficiency + " timeeff:" + timeEfficiency);
                line.GetDecalibratedEfficiencies(ref materialEfficiency, ref timeEfficiency);
                Logger.Info("post decalibration mateff:" + materialEfficiency + " timeeff:" + timeEfficiency);
                line.MaterialEfficiency = materialEfficiency;
                line.TimeEfficiency = timeEfficiency;
            }

            return new MassProductionLineQuote(quote, line);
        }

        private MassProductionResult ExecuteCore(GameActionContext context, ProductionMassProductionAction action)
        {
            Validate(action);
            using (var scope = Db.CreateTransaction())
            {
                _productionManager.PrepareProductionForPublicContainer(
                    action.FacilityEid,
                    context.Actor,
                    out Mill mill,
                    out PublicContainer sourceContainer);
                MassProductionResult result = _productionProcessor.LineStartInMillTyped(
                    context.Actor,
                    sourceContainer,
                    action.LineId,
                    1,
                    action.UseCorporationWallet,
                    action.SearchInRobots,
                    mill,
                    ProductionRoundsPolicy.NormalizeRequestedRounds(action.Rounds));
                scope.Complete();
                return result;
            }
        }

        private static void Validate(ProductionMassProductionAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            action.FacilityEid.ThrowIfLessOrEqual(0, ErrorCodes.ProductionFacilityNotFound);
            action.LineId.ThrowIfLessOrEqual(0, ErrorCodes.ItemNotFound);
        }
    }
}
