using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.Autonomous;
using Perpetuum.Services.ProductionEngine;
using Xunit;

namespace Perpetuum.Tests.Services.Autonomous
{
    public class AutonomousIndustryPlannerTests
    {
        [Fact]
        public void MultiUnitOutputRoundsUpToWholeBatches()
        {
            var planner = Planner(Recipe(100, 1000, Component(10, 25)));

            AutonomousIndustryPlan plan = planner.Plan(100, 1500);

            Assert.True(plan.IsSuccessful);
            Assert.Equal(50, plan.Procurement[10]);
            AutonomousIndustryProductionStep step = Assert.Single(plan.ProductionSteps);
            Assert.Equal(2, step.Batches);
            Assert.Equal(2000, step.OutputQuantity);
            Assert.Equal(500, plan.Surplus[100]);
        }

        [Fact]
        public void RecursivePlanConsumesInventoryBeforeExpandingRecipes()
        {
            var planner = Planner(
                Recipe(100, 1, Component(50, 2)),
                Recipe(50, 1, Component(10, 3)));

            AutonomousIndustryPlan plan = planner.Plan(
                100,
                3,
                new Dictionary<int, long>
                {
                    {100, 1},
                    {50, 1},
                    {10, 2}
                });

            Assert.True(plan.IsSuccessful);
            Assert.Equal(7, plan.Procurement[10]);
            Assert.Equal(1, plan.InventoryUsed[100]);
            Assert.Equal(1, plan.InventoryUsed[50]);
            Assert.Equal(2, plan.InventoryUsed[10]);
            Assert.Equal(new[] {50, 100}, plan.ProductionSteps.Select(step => step.Definition));
            Assert.Equal(3, plan.ProductionSteps[0].Batches);
            Assert.Equal(2, plan.ProductionSteps[1].Batches);
        }

        [Fact]
        public void BatchSurplusIsReusedAcrossSiblingRequirements()
        {
            var planner = Planner(
                Recipe(1, 1, Component(2, 1), Component(3, 1)),
                Recipe(2, 1, Component(4, 1)),
                Recipe(3, 1, Component(4, 5)),
                Recipe(4, 10, Component(9, 7)));

            AutonomousIndustryPlan plan = planner.Plan(1, 1);

            Assert.True(plan.IsSuccessful);
            Assert.Equal(7, plan.Procurement[9]);
            Assert.Equal(4, plan.Surplus[4]);
            Assert.False(plan.InventoryUsed.ContainsKey(4));
            Assert.Equal(new[] {4, 2, 3, 1}, plan.ProductionSteps.Select(step => step.Definition));
            Assert.Equal(1, plan.ProductionSteps.Single(step => step.Definition == 4).Batches);
        }

        [Fact]
        public void PlannerUsesOnlyComponentsRequiredByTheSelectedProcess()
        {
            var recipe = new ProductionRecipe(
                1,
                "def_robot",
                1,
                new[]
                {
                    new ProductionRecipeComponent(10, 20, requiredForMassProduction: false),
                    new ProductionRecipeComponent(20, 5)
                },
                ProductionRecipeProcess.MassProduction,
                2,
                2,
                3);

            AutonomousIndustryPlan plan = Planner(recipe).Plan(1, 2);

            Assert.True(plan.IsSuccessful);
            Assert.False(plan.Procurement.ContainsKey(10));
            Assert.Equal(10, plan.Procurement[20]);
        }

        [Fact]
        public void UnsupportedComponentGraphIsAnExternalRequirement()
        {
            var recipe = new ProductionRecipe(
                1,
                "def_unknown",
                1,
                new[] {Component(10, 5)},
                ProductionRecipeProcess.Unsupported,
                1,
                0,
                null);

            AutonomousIndustryPlan plan = Planner(recipe).Plan(1, 3);

            Assert.True(plan.IsSuccessful);
            Assert.Equal(3, plan.Procurement[1]);
            Assert.False(plan.Procurement.ContainsKey(10));
            Assert.Empty(plan.ProductionSteps);
        }

        [Fact]
        public void RobotGoalRecursivelyExpandsRefinedCommoditiesButNotPrototypeShards()
        {
            const int prometheus = 194;
            const int titan = 168;
            const int titanium = 169;
            const int crude = 170;
            const int stermonit = 171;
            const int liquizit = 173;
            const int triandlus = 176;
            const int prismocitae = 177;
            const int plasteosine = 178;
            const int metachropin = 181;
            const int prilumium = 182;
            const int polynucleit = 187;
            const int commonShard = 1980;
            const int factionShard = 1983;
            const int axicol = 4605;
            const int axicoline = 4606;

            var robot = new ProductionRecipe(
                prometheus,
                "def_prometheus_bot",
                1,
                new[]
                {
                    Component(axicol, 100),
                    Component(axicoline, 200),
                    Component(metachropin, 150),
                    Component(plasteosine, 150),
                    Component(polynucleit, 200),
                    Component(prilumium, 100),
                    Component(titanium, 750),
                    new ProductionRecipeComponent(commonShard, 20, requiredForMassProduction: false),
                    new ProductionRecipeComponent(factionShard, 20, requiredForMassProduction: false)
                },
                ProductionRecipeProcess.MassProduction,
                5298,
                2,
                1611);

            AutonomousIndustryPlan plan = Planner(
                robot,
                Refining(axicol, Component(crude, 50), Component(liquizit, 25)),
                Refining(axicoline, Component(liquizit, 50), Component(titan, 25)),
                Refining(metachropin, Component(crude, 25), Component(prismocitae, 30), Component(stermonit, 50)),
                Refining(plasteosine, Component(crude, 25), Component(titan, 50)),
                Refining(polynucleit, Component(liquizit, 25), Component(prismocitae, 30), Component(stermonit, 50)),
                Refining(prilumium, Component(crude, 25), Component(stermonit, 50), Component(triandlus, 30)),
                Refining(titanium, Component(titan, 75)))
                .Plan(prometheus, 1);

            Assert.True(plan.IsSuccessful);
            Assert.Equal(15000, plan.Procurement[crude]);
            Assert.Equal(22500, plan.Procurement[stermonit]);
            Assert.Equal(17500, plan.Procurement[liquizit]);
            Assert.Equal(68750, plan.Procurement[titan]);
            Assert.Equal(10500, plan.Procurement[prismocitae]);
            Assert.Equal(3000, plan.Procurement[triandlus]);
            Assert.False(plan.Procurement.ContainsKey(commonShard));
            Assert.False(plan.Procurement.ContainsKey(factionShard));
            Assert.Equal(8, plan.ProductionSteps.Count);
            Assert.Equal(prometheus, plan.ProductionSteps.Last().Definition);
        }

        [Fact]
        public void ExternalRequirementsAreAggregatedDeterministically()
        {
            var planner = Planner(
                Recipe(1, 1, Component(30, 2), Component(10, 1)),
                Recipe(2, 1, Component(10, 4)));

            AutonomousIndustryPlan first = planner.Plan(1, 2);
            AutonomousIndustryPlan second = planner.Plan(2, 1);

            Assert.Equal(new[] {10, 30}, first.Procurement.Keys);
            Assert.Equal(2, first.Procurement[10]);
            Assert.Equal(4, first.Procurement[30]);
            Assert.Equal(4, second.Procurement[10]);
        }

        [Fact]
        public void CycleReportsTheExactRecipePath()
        {
            var planner = Planner(
                Recipe(1, 1, Component(2, 1)),
                Recipe(2, 1, Component(1, 1)));

            AutonomousIndustryPlan plan = planner.Plan(1, 1);

            Assert.False(plan.IsSuccessful);
            Assert.Equal(AutonomousIndustryPlanFailure.CycleDetected, plan.Failure);
            Assert.Equal(new[] {1, 2, 1}, plan.FailurePath);
            Assert.Empty(plan.ProductionSteps);
            Assert.Empty(plan.Procurement);
        }

        [Fact]
        public void ExistingInventoryCanTerminateAnOtherwiseCyclicBranch()
        {
            var planner = Planner(
                Recipe(1, 1, Component(2, 1)),
                Recipe(2, 1, Component(1, 1)));

            AutonomousIndustryPlan plan = planner.Plan(
                1,
                1,
                new Dictionary<int, long> {{2, 1}});

            Assert.True(plan.IsSuccessful);
            Assert.Equal(1, plan.InventoryUsed[2]);
            Assert.Equal(1, Assert.Single(plan.ProductionSteps).Definition);
        }

        [Fact]
        public void PlanningLimitsFailWithoutReturningPartialWork()
        {
            var planner = new AutonomousIndustryPlanner(
                new FakeRecipeCatalog(
                    Recipe(1, 1, Component(2, 1)),
                    Recipe(2, 1, Component(3, 1)),
                    Recipe(3, 1, Component(4, 1))),
                new AutonomousIndustryPlanningLimits(maximumDepth: 1));

            AutonomousIndustryPlan plan = planner.Plan(1, 1);

            Assert.Equal(AutonomousIndustryPlanFailure.MaximumDepthExceeded, plan.Failure);
            Assert.Equal(new[] {1, 2, 3}, plan.FailurePath);
            Assert.Empty(plan.ProductionSteps);
            Assert.Empty(plan.Procurement);
        }

        [Fact]
        public void QuantityOverflowBecomesABoundedFailure()
        {
            var planner = Planner(Recipe(1, 2, Component(2, 1)));

            AutonomousIndustryPlan plan = planner.Plan(1, long.MaxValue);

            Assert.Equal(AutonomousIndustryPlanFailure.QuantityOverflow, plan.Failure);
            Assert.Empty(plan.ProductionSteps);
        }

        private static AutonomousIndustryPlanner Planner(params ProductionRecipe[] recipes)
        {
            return new AutonomousIndustryPlanner(new FakeRecipeCatalog(recipes));
        }

        private static ProductionRecipe Recipe(
            int definition,
            int outputQuantity,
            params ProductionRecipeComponent[] components)
        {
            return new ProductionRecipe(
                definition,
                "def_" + definition,
                outputQuantity,
                components,
                ProductionRecipeProcess.MassProduction,
                definition,
                0,
                null);
        }

        private static ProductionRecipeComponent Component(int definition, int amount)
        {
            return new ProductionRecipeComponent(definition, amount);
        }

        private static ProductionRecipe Refining(
            int definition,
            params ProductionRecipeComponent[] components)
        {
            return new ProductionRecipe(
                definition,
                "def_" + definition,
                1,
                components,
                ProductionRecipeProcess.Refining,
                definition,
                0,
                null);
        }

        private sealed class FakeRecipeCatalog : IProductionRecipeCatalog
        {
            private readonly IReadOnlyDictionary<int, ProductionRecipe> _recipes;

            public FakeRecipeCatalog(params ProductionRecipe[] recipes)
            {
                _recipes = recipes.ToDictionary(recipe => recipe.Definition);
            }

            public bool TryGet(int definition, out ProductionRecipe recipe) =>
                _recipes.TryGetValue(definition, out recipe);
        }
    }
}
