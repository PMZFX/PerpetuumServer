using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Containers;
using Perpetuum.EntityFramework;
using Perpetuum.Services.Actions;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousManufacturerDirective
    {
        Complete,
        WaitForProduction,
        WaitForCalibration,
        WaitForMaterials,
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
            inventory = inventory ?? new Dictionary<int, long>();
            return quote.Materials
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
        private readonly IProductionMassProductionActionService _massProduction;
        private readonly IAutonomousIndustryGoalStore _goals;
        private readonly IAutonomousActorAudit _audit;

        public AutonomousIndustryController(
            ProductionProcessor productionProcessor,
            IAutonomousIndustryPlanner planner,
            IProductionMassProductionActionService massProduction,
            IAutonomousIndustryGoalStore goals,
            IAutonomousActorAudit audit)
        {
            _productionProcessor = productionProcessor ?? throw new ArgumentNullException(nameof(productionProcessor));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
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
                 state.MillFacilityEid != options.MillFacilityEid))
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
                    "Planning");
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
            if (line == null)
            {
                AutonomousIndustryPlan plan = _planner.Plan(
                    state.TargetDefinition,
                    completionQuantity - GetQuantity(inventory, state.TargetDefinition),
                    inventory);
                WriteState(state.WithProgress(
                    "WaitingCalibration",
                    procurement: plan.IsSuccessful ? plan.Procurement : null,
                    blockedReason: plan.IsSuccessful ? "calibration_line_required" : plan.Failure.ToString()));
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
