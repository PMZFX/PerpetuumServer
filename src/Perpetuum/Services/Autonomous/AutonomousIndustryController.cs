using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Containers;
using Perpetuum.EntityFramework;
using Perpetuum.Items;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.CalibrationPrograms;
using Perpetuum.Services.ProductionEngine.ResearchKits;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousManufacturerDirective
    {
        Complete,
        WaitForProduction,
        WaitForPrototype,
        WaitForResearch,
        WaitForResearchInputs,
        WaitForCalibration,
        WaitForMaterials,
        StartResearch,
        StartPrototype,
        CalibrateLine,
        StartProduction
    }

    public static class AutonomousManufacturerPolicy
    {
        public static AutonomousManufacturerDirective Select(
            bool complete,
            bool matchingProduction,
            bool hasUsableLine,
            bool hasMissingMaterials)
        {
            if (complete)
                return AutonomousManufacturerDirective.Complete;
            if (matchingProduction)
                return AutonomousManufacturerDirective.WaitForProduction;
            if (!hasUsableLine)
                return AutonomousManufacturerDirective.WaitForCalibration;
            return hasMissingMaterials
                ? AutonomousManufacturerDirective.WaitForMaterials
                : AutonomousManufacturerDirective.StartProduction;
        }

        public static IReadOnlyDictionary<int, long> FindMissingMaterials(
            MassProductionQuote quote,
            IReadOnlyDictionary<int, long> inventory)
        {
            if (quote == null)
                throw new ArgumentNullException(nameof(quote));
            return FindMissingMaterials(quote.Materials, inventory);
        }

        public static IReadOnlyDictionary<int, long> FindMissingMaterials(
            IEnumerable<ProductionMaterialQuote> materials,
            IReadOnlyDictionary<int, long> inventory)
        {
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));
            inventory = inventory ?? new Dictionary<int, long>();
            return materials
                .Select(material => new
                {
                    material.Definition,
                    Missing = Math.Max(
                        0,
                        (long)material.RequiredAmount - GetQuantity(inventory, material.Definition))
                })
                .Where(item => item.Missing > 0)
                .OrderBy(item => item.Definition)
                .ToDictionary(item => item.Definition, item => item.Missing);
        }

        public static AutonomousManufacturerDirective SelectCalibrationStep(
            bool matchingResearch,
            bool hasCalibrationProgram,
            bool researchEnabled,
            bool hasResearchInputs)
        {
            if (matchingResearch)
                return AutonomousManufacturerDirective.WaitForResearch;
            if (hasCalibrationProgram)
                return AutonomousManufacturerDirective.CalibrateLine;
            if (!researchEnabled || !hasResearchInputs)
                return AutonomousManufacturerDirective.WaitForResearchInputs;
            return AutonomousManufacturerDirective.StartResearch;
        }

        public static AutonomousManufacturerDirective SelectPrototypeStep(
            bool matchingPrototype,
            bool prototypeEnabled,
            bool hasMissingMaterials)
        {
            if (matchingPrototype)
                return AutonomousManufacturerDirective.WaitForPrototype;
            if (!prototypeEnabled || hasMissingMaterials)
                return AutonomousManufacturerDirective.WaitForResearchInputs;
            return AutonomousManufacturerDirective.StartPrototype;
        }

        public static IReadOnlyDictionary<int, long> MergeProcurement(
            params IReadOnlyDictionary<int, long>[] requirements)
        {
            var merged = new Dictionary<int, long>();
            foreach (IReadOnlyDictionary<int, long> requirement in requirements ??
                     Array.Empty<IReadOnlyDictionary<int, long>>())
            {
                if (requirement == null)
                    continue;
                foreach (KeyValuePair<int, long> item in requirement)
                {
                    if (item.Key <= 0 || item.Value <= 0)
                        throw new ArgumentOutOfRangeException(nameof(requirements));
                    merged.TryGetValue(item.Key, out long existing);
                    merged[item.Key] = checked(existing + item.Value);
                }
            }
            return merged.OrderBy(item => item.Key).ToDictionary(item => item.Key, item => item.Value);
        }

        private static long GetQuantity(IReadOnlyDictionary<int, long> inventory, int definition)
        {
            return inventory.TryGetValue(definition, out long quantity) ? quantity : 0;
        }
    }

    public interface IAutonomousIndustryController
    {
        void Update(GameActionContext context, AutonomousManufacturerOptions options);
    }

    public sealed class AutonomousIndustryController : IAutonomousIndustryController
    {
        private readonly ProductionProcessor _productionProcessor;
        private readonly IAutonomousIndustryPlanner _planner;
        private readonly IProductionRecipeCatalog _recipes;
        private readonly IProductionResearchActionService _research;
        private readonly IProductionCalibrationActionService _calibration;
        private readonly IProductionPrototypeActionService _prototype;
        private readonly IProductionMassProductionActionService _massProduction;
        private readonly IAutonomousIndustryGoalStore _goals;
        private readonly IAutonomousActorAudit _audit;

        public AutonomousIndustryController(
            ProductionProcessor productionProcessor,
            IAutonomousIndustryPlanner planner,
            IProductionRecipeCatalog recipes,
            IProductionResearchActionService research,
            IProductionCalibrationActionService calibration,
            IProductionPrototypeActionService prototype,
            IProductionMassProductionActionService massProduction,
            IAutonomousIndustryGoalStore goals,
            IAutonomousActorAudit audit)
        {
            _productionProcessor = productionProcessor ?? throw new ArgumentNullException(nameof(productionProcessor));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _research = research ?? throw new ArgumentNullException(nameof(research));
            _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
            _prototype = prototype ?? throw new ArgumentNullException(nameof(prototype));
            _massProduction = massProduction ?? throw new ArgumentNullException(nameof(massProduction));
            _goals = goals ?? throw new ArgumentNullException(nameof(goals));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void Update(GameActionContext context, AutonomousManufacturerOptions options)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            AutonomousIndustryGoalState state = _goals.Load(context.Actor.Id);
            if (state != null &&
                (state.TargetDefinition != options.TargetDefinition ||
                 state.TargetQuantity != options.Quantity ||
                 state.MillFacilityEid != options.MillFacilityEid ||
                 state.ResearchFacilityEid != options.ResearchFacilityEid ||
                 state.PrototypeFacilityEid != options.PrototypeFacilityEid))
            {
                WriteState(state.WithProgress("BlockedConfiguration", blockedReason: "persistent_goal_mismatch"));
                return;
            }

            if (!context.Actor.IsDocked || context.Actor.CurrentDockingBaseEid <= 0)
            {
                if (state != null)
                    WriteState(state.WithProgress("WaitingFacility", blockedReason: "character_not_docked"));
                return;
            }

            PublicContainer container = context.Actor.GetPublicContainerWithItems();
            IReadOnlyDictionary<int, long> inventory = SnapshotInventory(container);
            if (state == null)
            {
                state = new AutonomousIndustryGoalState(
                    context.Actor.Id,
                    options.TargetDefinition,
                    options.Quantity,
                    GetQuantity(inventory, options.TargetDefinition),
                    options.MillFacilityEid,
                    "Planning",
                    researchFacilityEid: options.ResearchFacilityEid,
                    prototypeFacilityEid: options.PrototypeFacilityEid);
                WriteState(state);
            }

            long completionQuantity = checked(state.InitialInventoryQuantity + state.TargetQuantity);
            bool complete = GetQuantity(inventory, state.TargetDefinition) >= completionQuantity;
            ProductionInProgress running = _productionProcessor.RunningProductions
                .GetByCharacter(context.Actor)
                .FirstOrDefault(production =>
                    production.type == ProductionInProgressType.massProduction &&
                    production.resultDefinition == state.TargetDefinition);
            ProductionLine line = FindUsableLine(context, state);

            if (complete)
            {
                WriteState(state.WithProgress("Complete", line?.Id));
                return;
            }
            if (running != null)
            {
                WriteState(state.WithProgress("WaitingProduction", line?.Id, running.ID));
                return;
            }
            if (state.Phase == "WaitingProduction" && state.ProductionId.HasValue)
            {
                WriteState(state.WithProgress("ObservingProductionCompletion", line?.Id));
                return;
            }
            if (line == null)
            {
                HandleCalibrationPrerequisite(context, options, state, container, inventory, completionQuantity);
                return;
            }

            try
            {
                var action = new ProductionMassProductionAction(
                    state.MillFacilityEid,
                    line.Id,
                    options.UseCorporationWallet,
                    searchInRobots: false,
                    rounds: 0);
                MassProductionLineQuote quote = _massProduction.Quote(context, action);
                IReadOnlyDictionary<int, long> missing =
                    AutonomousManufacturerPolicy.FindMissingMaterials(quote.Quote, inventory);
                if (missing.Count > 0)
                {
                    WriteState(state.WithProgress(
                        "WaitingMaterials",
                        line.Id,
                        procurement: missing,
                        blockedReason: "required_components_missing"));
                    return;
                }

                MassProductionResult result = _massProduction.Execute(context, action);
                WriteState(state.WithProgress("WaitingProduction", line.Id, result.Production.ID));
            }
            catch (PerpetuumException exception)
            {
                WriteState(state.WithProgress(
                    "WaitingPolicy",
                    line.Id,
                    procurement: state.Procurement,
                    blockedReason: exception.error.ToString()));
            }
        }

        private void HandleCalibrationPrerequisite(
            GameActionContext context,
            AutonomousManufacturerOptions options,
            AutonomousIndustryGoalState state,
            PublicContainer container,
            IReadOnlyDictionary<int, long> inventory,
            long completionQuantity)
        {
            if (!_recipes.TryGet(state.TargetDefinition, out ProductionRecipe recipe) ||
                !recipe.CalibrationProgramDefinition.HasValue)
            {
                WritePlanningFailure(state, inventory, completionQuantity, "calibration_recipe_unavailable");
                return;
            }

            int calibrationDefinition = recipe.CalibrationProgramDefinition.Value;
            ProductionInProgress runningResearch = _productionProcessor.RunningProductions
                .GetByCharacter(context.Actor)
                .FirstOrDefault(production =>
                    production.type == ProductionInProgressType.research &&
                    production.resultDefinition == calibrationDefinition);
            if (runningResearch != null)
            {
                WriteState(state.WithProgress("WaitingResearch", productionId: runningResearch.ID));
                return;
            }

            CalibrationProgram calibrationProgram = SelectCalibrationProgram(container, calibrationDefinition);
            if (calibrationProgram != null)
            {
                TryCalibrate(context, state, calibrationProgram);
                return;
            }
            if (state.Phase == "WaitingResearch" && state.ProductionId.HasValue)
            {
                WriteState(state.WithProgress("ObservingResearchCompletion"));
                return;
            }

            if (state.ResearchFacilityEid <= 0)
            {
                AutonomousIndustryPlan plan = _planner.Plan(
                    state.TargetDefinition,
                    completionQuantity - GetQuantity(inventory, state.TargetDefinition),
                    inventory);
                IReadOnlyDictionary<int, long> calibrationProcurement =
                    new Dictionary<int, long> {{calibrationDefinition, 1}};
                WriteState(state.WithProgress(
                    "WaitingCalibration",
                    procurement: AutonomousManufacturerPolicy.MergeProcurement(
                        plan.IsSuccessful ? plan.Procurement : null,
                        calibrationProcurement),
                    blockedReason: plan.IsSuccessful
                        ? "calibration_program_required"
                        : plan.Failure.ToString()));
                return;
            }

            int sourceDefinition = recipe.PrototypeDefinition;
            Item sourceItem = SelectResearchSource(container, sourceDefinition);
            if (sourceItem == null && recipe.RequiresPrototype && state.PrototypeFacilityEid > 0)
            {
                TryCreatePrototype(context, options, state, inventory, recipe);
                return;
            }
            ResearchKit researchKit = SelectResearchKit(container, recipe.ResearchLevel);
            if (sourceItem == null || researchKit == null)
            {
                var prerequisites = new List<IReadOnlyDictionary<int, long>>();
                if (sourceItem == null)
                {
                    int sourceQuantity = EntityDefault.Get(sourceDefinition).Quantity;
                    prerequisites.Add(new Dictionary<int, long>
                    {
                        {sourceDefinition, Math.Max(1, sourceQuantity)}
                    });
                }
                if (researchKit == null)
                {
                    ErrorCodes result = ProductionHelper.FindResearchKitDefinitionByLevel(
                        recipe.ResearchLevel,
                        out int researchKitDefinition);
                    if (result != ErrorCodes.NoError || researchKitDefinition <= 0)
                    {
                        WriteState(state.WithProgress(
                            "WaitingResearchInputs",
                            blockedReason: "research_kit_definition_unavailable"));
                        return;
                    }
                    prerequisites.Add(new Dictionary<int, long> {{researchKitDefinition, 1}});
                }

                WriteState(state.WithProgress(
                    "WaitingResearchInputs",
                    procurement: AutonomousManufacturerPolicy.MergeProcurement(prerequisites.ToArray()),
                    blockedReason: "research_inputs_missing"));
                return;
            }

            try
            {
                var quoteAction = new ProductionResearchQuoteAction(
                    state.ResearchFacilityEid,
                    researchKit.Definition,
                    sourceItem.Definition);
                _research.Quote(context, quoteAction);
                ResearchProductionResult result = _research.Execute(
                    context,
                    new ProductionResearchAction(
                        state.ResearchFacilityEid,
                        sourceItem.Eid,
                        researchKit.Eid,
                        options.UseCorporationWallet));
                WriteState(state.WithProgress("WaitingResearch", productionId: result.Production.ID));
            }
            catch (PerpetuumException exception)
            {
                WriteState(state.WithProgress(
                    "WaitingPolicy",
                    procurement: state.Procurement,
                    blockedReason: exception.error.ToString()));
            }
        }

        private void TryCreatePrototype(
            GameActionContext context,
            AutonomousManufacturerOptions options,
            AutonomousIndustryGoalState state,
            IReadOnlyDictionary<int, long> inventory,
            ProductionRecipe recipe)
        {
            ProductionInProgress runningPrototype = _productionProcessor.RunningProductions
                .GetByCharacter(context.Actor)
                .FirstOrDefault(production =>
                    production.type == ProductionInProgressType.prototype &&
                    production.resultDefinition == recipe.PrototypeDefinition);
            if (runningPrototype != null)
            {
                WriteState(state.WithProgress("WaitingPrototype", productionId: runningPrototype.ID));
                return;
            }
            if (state.Phase == "WaitingPrototype" && state.ProductionId.HasValue)
            {
                WriteState(state.WithProgress("ObservingPrototypeCompletion"));
                return;
            }

            try
            {
                var action = new ProductionPrototypeAction(
                    state.PrototypeFacilityEid,
                    state.TargetDefinition,
                    options.UseCorporationWallet);
                PrototypeQuote quote = _prototype.Quote(context, action);
                IReadOnlyDictionary<int, long> missing =
                    AutonomousManufacturerPolicy.FindMissingMaterials(quote.Materials, inventory);
                if (missing.Count > 0)
                {
                    WriteState(state.WithProgress(
                        "WaitingPrototypeMaterials",
                        procurement: missing,
                        blockedReason: "prototype_components_missing"));
                    return;
                }

                PrototypeProductionResult result = _prototype.Execute(context, action);
                WriteState(state.WithProgress("WaitingPrototype", productionId: result.Production.ID));
            }
            catch (PerpetuumException exception)
            {
                WriteState(state.WithProgress(
                    "WaitingPolicy",
                    procurement: state.Procurement,
                    blockedReason: exception.error.ToString()));
            }
        }

        private void TryCalibrate(
            GameActionContext context,
            AutonomousIndustryGoalState state,
            CalibrationProgram calibrationProgram)
        {
            try
            {
                var action = new ProductionCalibrationAction(
                    state.MillFacilityEid,
                    calibrationProgram.Eid);
                _calibration.Quote(context, action);
                _calibration.Execute(context, action);
                ProductionLine line = FindUsableLine(context, state);
                WriteState(state.WithProgress(
                    "ReadyProduction",
                    line?.Id,
                    blockedReason: line == null ? "calibrated_line_not_visible" : null));
            }
            catch (PerpetuumException exception)
            {
                WriteState(state.WithProgress(
                    "WaitingPolicy",
                    procurement: state.Procurement,
                    blockedReason: exception.error.ToString()));
            }
        }

        private void WritePlanningFailure(
            AutonomousIndustryGoalState state,
            IReadOnlyDictionary<int, long> inventory,
            long completionQuantity,
            string reason)
        {
            AutonomousIndustryPlan plan = _planner.Plan(
                state.TargetDefinition,
                completionQuantity - GetQuantity(inventory, state.TargetDefinition),
                inventory);
            WriteState(state.WithProgress(
                "WaitingCalibration",
                procurement: plan.IsSuccessful ? plan.Procurement : null,
                blockedReason: plan.IsSuccessful ? reason : plan.Failure.ToString()));
        }

        private static CalibrationProgram SelectCalibrationProgram(
            Container container,
            int calibrationDefinition)
        {
            CalibrationProgram selected = null;
            foreach (CalibrationProgram program in container.GetItems()
                         .OfType<CalibrationProgram>()
                         .Where(program =>
                             program.Definition == calibrationDefinition &&
                             program.Quantity == 1 &&
                             !program.IsMissionRelated)
                         .OrderBy(program => program.Eid))
            {
                if (selected == null || program.IsBetterThanOther(selected))
                    selected = program;
            }
            return selected;
        }

        private static Item SelectResearchSource(Container container, int sourceDefinition)
        {
            return container.GetItems()
                .Where(item =>
                    item.Definition == sourceDefinition &&
                    item.Quantity >= item.ED.Quantity &&
                    item.HealthRatio == 1.0 &&
                    (!item.ED.AttributeFlags.Repackable || item.IsRepackaged))
                .OrderBy(item => item.Eid)
                .FirstOrDefault();
        }

        private static ResearchKit SelectResearchKit(Container container, int researchLevel)
        {
            return container.GetItems()
                .OfType<ResearchKit>()
                .Where(kit => !kit.IsMissionRelated && kit.GetResearchLevel() >= researchLevel)
                .OrderBy(kit => kit.GetResearchLevel())
                .ThenBy(kit => kit.Eid)
                .FirstOrDefault();
        }

        private ProductionLine FindUsableLine(GameActionContext context, AutonomousIndustryGoalState state)
        {
            IEnumerable<ProductionLine> lines = ProductionLine
                .GetLinesByCharacter(context.Actor.Id, state.MillFacilityEid)
                .Where(line => line.TargetDefinition == state.TargetDefinition);
            if (state.LineId.HasValue)
            {
                ProductionLine persisted = lines.FirstOrDefault(line => line.Id == state.LineId.Value);
                if (persisted != null && !persisted.IsActive() && !persisted.IsAtZero())
                    return persisted;
            }
            return lines.FirstOrDefault(line => !line.IsActive() && !line.IsAtZero());
        }

        private void WriteState(AutonomousIndustryGoalState state)
        {
            AutonomousIndustryGoalState previous = _goals.Load(state.CharacterId);
            _goals.Save(state);
            if (previous == null || previous.Phase != state.Phase || previous.BlockedReason != state.BlockedReason)
            {
                _audit.Write(
                    state.CharacterId,
                    "industry_" + state.Phase.ToLowerInvariant(),
                    AutonomousActorStatus.Active,
                    state.BlockedReason);
            }
        }

        private static IReadOnlyDictionary<int, long> SnapshotInventory(Container container)
        {
            return container.GetItems()
                .GroupBy(item => item.Definition)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Quantity));
        }

        private static long GetQuantity(IReadOnlyDictionary<int, long> inventory, int definition)
        {
            return inventory.TryGetValue(definition, out long quantity) ? quantity : 0;
        }
    }
}
