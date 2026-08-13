using System.Collections.Generic;
using Perpetuum.Services.Actions;
using Perpetuum.Services.Autonomous;
using Perpetuum.Services.ProductionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousIndustryControllerTests
    {
        [Fact]
        public void ExistingProductionWinsOverStartingDuplicateAfterRestart()
        {
            Assert.Equal(
                AutonomousManufacturerDirective.WaitForProduction,
                AutonomousManufacturerPolicy.Select(
                    complete: false,
                    matchingProduction: true,
                    hasUsableLine: true,
                    hasMissingMaterials: false));
        }

        [Theory]
        [InlineData(true, false, false, false, AutonomousManufacturerDirective.Complete)]
        [InlineData(false, false, false, false, AutonomousManufacturerDirective.WaitForCalibration)]
        [InlineData(false, false, true, true, AutonomousManufacturerDirective.WaitForMaterials)]
        [InlineData(false, false, true, false, AutonomousManufacturerDirective.StartProduction)]
        public void PolicySelectsNaturalWaitOrStart(
            bool complete,
            bool matchingProduction,
            bool hasUsableLine,
            bool hasMissingMaterials,
            AutonomousManufacturerDirective expected)
        {
            Assert.Equal(expected, AutonomousManufacturerPolicy.Select(
                complete,
                matchingProduction,
                hasUsableLine,
                hasMissingMaterials));
        }

        [Fact]
        public void ExactCharacterQuoteProducesProcurementGoalsWithoutGrantingItems()
        {
            var quote = new MassProductionQuote(
                100,
                10,
                20,
                1.1,
                false,
                1,
                new[]
                {
                    new ProductionMaterialQuote(200, 7, 5, false),
                    new ProductionMaterialQuote(300, 1, 1, true)
                });

            IReadOnlyDictionary<int, long> missing = AutonomousManufacturerPolicy.FindMissingMaterials(
                quote,
                new Dictionary<int, long> {{200, 2}, {300, 1}});

            Assert.Equal(new Dictionary<int, long> {{200, 5}}, missing);
        }

        [Fact]
        public void ExactRefineryQuoteUsesEffectiveCharacterAmounts()
        {
            var quote = new RefineQuote(
                100,
                2,
                400,
                new[]
                {
                    new RefineComponentQuote(200, 10, 12),
                    new RefineComponentQuote(300, 5, 7)
                });

            IReadOnlyDictionary<int, long> missing =
                AutonomousManufacturerPolicy.FindMissingMaterials(
                    quote,
                    new Dictionary<int, long> {{200, 9}, {300, 7}});

            Assert.Equal(new Dictionary<int, long> {{200, 3}}, missing);
        }

        [Fact]
        public void PlannerOnlyOffersItsFirstOrderedStepForRefining()
        {
            var recipes = new DictionaryRecipeCatalog(
                new ProductionRecipe(
                    100,
                    "refined",
                    1,
                    new[] {new ProductionRecipeComponent(200, 5)},
                    ProductionRecipeProcess.Refining,
                    100,
                    0,
                    null),
                new ProductionRecipe(
                    300,
                    "manufactured",
                    1,
                    new[] {new ProductionRecipeComponent(100, 1)},
                    ProductionRecipeProcess.MassProduction,
                    300,
                    1,
                    301));
            var planner = new AutonomousIndustryPlanner(recipes);

            AutonomousIndustryProductionStep step =
                AutonomousManufacturerPolicy.SelectNextRefiningStep(planner.Plan(300, 2));

            Assert.Equal(100, step.Definition);
            Assert.Equal(2, AutonomousManufacturerPolicy.SelectRefineAmount(step));
            Assert.Equal(
                ProductionRefineAmountPolicy.MaximumAmount,
                AutonomousManufacturerPolicy.SelectRefineAmount(
                    new AutonomousIndustryProductionStep(
                        step.Recipe,
                        ProductionRefineAmountPolicy.MaximumAmount + 1L)));
            Assert.Null(AutonomousManufacturerPolicy.SelectNextRefiningStep(
                planner.Plan(300, 1, new Dictionary<int, long> {{100, 1}})));

            IReadOnlyDictionary<int, long> remainingInventory =
                AutonomousManufacturerPolicy.ForRemainingTarget(
                    new Dictionary<int, long> {{100, 1}, {300, 9}},
                    300);
            Assert.Equal(100, AutonomousManufacturerPolicy.SelectNextRefiningStep(
                planner.Plan(300, 2, remainingInventory)).Definition);
        }

        [Theory]
        [InlineData(true, false, false, false, AutonomousManufacturerDirective.WaitForResearch)]
        [InlineData(false, true, false, false, AutonomousManufacturerDirective.CalibrateLine)]
        [InlineData(false, false, false, false, AutonomousManufacturerDirective.WaitForResearchInputs)]
        [InlineData(false, false, true, false, AutonomousManufacturerDirective.WaitForResearchInputs)]
        [InlineData(false, false, true, true, AutonomousManufacturerDirective.StartResearch)]
        public void CalibrationPolicyWaitsOrChoosesOneLegitimateAction(
            bool matchingResearch,
            bool hasCalibrationProgram,
            bool researchEnabled,
            bool hasResearchInputs,
            AutonomousManufacturerDirective expected)
        {
            Assert.Equal(
                expected,
                AutonomousManufacturerPolicy.SelectCalibrationStep(
                    matchingResearch,
                    hasCalibrationProgram,
                    researchEnabled,
                    hasResearchInputs));
        }

        [Theory]
        [InlineData(true, true, false, AutonomousManufacturerDirective.WaitForPrototype)]
        [InlineData(false, false, false, AutonomousManufacturerDirective.WaitForResearchInputs)]
        [InlineData(false, true, true, AutonomousManufacturerDirective.WaitForResearchInputs)]
        [InlineData(false, true, false, AutonomousManufacturerDirective.StartPrototype)]
        public void PrototypePolicyWaitsOrStartsOnlyWithFacilityAndMaterials(
            bool matchingPrototype,
            bool prototypeEnabled,
            bool hasMissingMaterials,
            AutonomousManufacturerDirective expected)
        {
            Assert.Equal(
                expected,
                AutonomousManufacturerPolicy.SelectPrototypeStep(
                    matchingPrototype,
                    prototypeEnabled,
                    hasMissingMaterials));
        }

        [Fact]
        public void ProcurementMergesResearchInputsDeterministically()
        {
            IReadOnlyDictionary<int, long> procurement =
                AutonomousManufacturerPolicy.MergeProcurement(
                    new Dictionary<int, long> {{300, 1}, {100, 2}},
                    new Dictionary<int, long> {{100, 3}, {200, 1}});

            Assert.Equal(
                new Dictionary<int, long> {{100, 5}, {200, 1}, {300, 1}},
                procurement);
        }

        [Fact]
        public void ProcurementCodecIsDeterministicAndRejectsInvalidState()
        {
            string json = AutonomousIndustryProcurementCodec.Serialize(
                new Dictionary<int, long> {{300, 2}, {100, 4}});

            Assert.Equal("{\"100\":4,\"300\":2}", json);
            Assert.Equal(4, AutonomousIndustryProcurementCodec.Deserialize(json)[100]);
            Assert.Empty(AutonomousIndustryProcurementCodec.Deserialize("{\"0\":2}"));
            Assert.Empty(AutonomousIndustryProcurementCodec.Deserialize("not json"));
        }

        [Fact]
        public void ProgressUpdatesPreservePersistentGoalIdentity()
        {
            var goal = new AutonomousIndustryGoalState(
                7,
                100,
                2,
                3,
                400,
                "Planning",
                researchFacilityEid: 450,
                prototypeFacilityEid: 460);

            AutonomousIndustryGoalState waiting = goal.WithProgress(
                "WaitingProduction",
                lineId: 5,
                productionId: 6);

            Assert.Equal(100, waiting.TargetDefinition);
            Assert.Equal(2, waiting.TargetQuantity);
            Assert.Equal(3, waiting.InitialInventoryQuantity);
            Assert.Equal(400, waiting.MillFacilityEid);
            Assert.Equal(450, waiting.ResearchFacilityEid);
            Assert.Equal(460, waiting.PrototypeFacilityEid);
            Assert.Equal(0, waiting.RefineryFacilityEid);
            Assert.Equal(5, waiting.LineId);
            Assert.Equal(6, waiting.ProductionId);
        }

        [Theory]
        [InlineData("WaitingPrototype", "ObservingPrototypeCompletion")]
        [InlineData("WaitingResearch", "ObservingResearchCompletion")]
        [InlineData("WaitingProduction", "ObservingProductionCompletion")]
        public void CompletionObservationClearsPersistedProductionBeforeAnyRetry(
            string waitingPhase,
            string observationPhase)
        {
            var goal = new AutonomousIndustryGoalState(
                7,
                100,
                1,
                0,
                400,
                waitingPhase,
                productionId: 55);

            AutonomousIndustryGoalState observing = goal.WithProgress(observationPhase);

            Assert.Equal(observationPhase, observing.Phase);
            Assert.Null(observing.ProductionId);
        }

        private sealed class DictionaryRecipeCatalog : IProductionRecipeCatalog
        {
            private readonly IReadOnlyDictionary<int, ProductionRecipe> _recipes;

            public DictionaryRecipeCatalog(params ProductionRecipe[] recipes)
            {
                var values = new Dictionary<int, ProductionRecipe>();
                foreach (ProductionRecipe recipe in recipes)
                    values.Add(recipe.Definition, recipe);
                _recipes = values;
            }

            public bool TryGet(int definition, out ProductionRecipe recipe)
            {
                return _recipes.TryGetValue(definition, out recipe);
            }
        }
    }
}
