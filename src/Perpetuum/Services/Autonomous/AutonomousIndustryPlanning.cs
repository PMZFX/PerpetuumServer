using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.Services.ProductionEngine;

namespace Perpetuum.Services.Autonomous
{
    public enum AutonomousIndustryPlanFailure
    {
        None,
        CycleDetected,
        MaximumDepthExceeded,
        MaximumDefinitionsExceeded,
        QuantityOverflow
    }

    public sealed class AutonomousIndustryPlanningLimits
    {
        public AutonomousIndustryPlanningLimits(int maximumDepth = 32, int maximumDefinitions = 4096)
        {
            if (maximumDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            if (maximumDefinitions <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumDefinitions));

            MaximumDepth = maximumDepth;
            MaximumDefinitions = maximumDefinitions;
        }

        public int MaximumDepth { get; }
        public int MaximumDefinitions { get; }
    }

    public sealed class AutonomousIndustryProductionStep
    {
        public AutonomousIndustryProductionStep(ProductionRecipe recipe, long batches)
        {
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            if (batches <= 0)
                throw new ArgumentOutOfRangeException(nameof(batches));

            Batches = batches;
            OutputQuantity = checked(batches * recipe.OutputQuantity);
        }

        public ProductionRecipe Recipe { get; }
        public int Definition => Recipe.Definition;
        public long Batches { get; }
        public long OutputQuantity { get; }
    }

    public sealed class AutonomousIndustryPlan
    {
        internal AutonomousIndustryPlan(
            int targetDefinition,
            long targetQuantity,
            AutonomousIndustryPlanFailure failure,
            IEnumerable<int> failurePath,
            IEnumerable<AutonomousIndustryProductionStep> productionSteps,
            IReadOnlyDictionary<int, long> procurement,
            IReadOnlyDictionary<int, long> inventoryUsed,
            IReadOnlyDictionary<int, long> surplus)
        {
            TargetDefinition = targetDefinition;
            TargetQuantity = targetQuantity;
            Failure = failure;
            FailurePath = (failurePath ?? Array.Empty<int>()).ToArray();
            ProductionSteps = (productionSteps ?? Array.Empty<AutonomousIndustryProductionStep>()).ToArray();
            Procurement = procurement ?? new Dictionary<int, long>();
            InventoryUsed = inventoryUsed ?? new Dictionary<int, long>();
            Surplus = surplus ?? new Dictionary<int, long>();
        }

        public int TargetDefinition { get; }
        public long TargetQuantity { get; }
        public AutonomousIndustryPlanFailure Failure { get; }
        public IReadOnlyList<int> FailurePath { get; }
        public IReadOnlyList<AutonomousIndustryProductionStep> ProductionSteps { get; }
        public IReadOnlyDictionary<int, long> Procurement { get; }
        public IReadOnlyDictionary<int, long> InventoryUsed { get; }
        public IReadOnlyDictionary<int, long> Surplus { get; }
        public bool IsSuccessful => Failure == AutonomousIndustryPlanFailure.None;
    }

    /// <summary>
    /// Expands a strategic manufacturing goal using nominal recipe quantities.
    /// It does not perform unlock checks, quote a facility, reserve inventory,
    /// spend credits, create production lines, or start production.
    /// </summary>
    public interface IAutonomousIndustryPlanner
    {
        AutonomousIndustryPlan Plan(
            int targetDefinition,
            long targetQuantity,
            IReadOnlyDictionary<int, long> inventory = null);
    }

    public sealed class AutonomousIndustryPlanner : IAutonomousIndustryPlanner
    {
        private readonly IProductionRecipeCatalog _recipes;
        private readonly AutonomousIndustryPlanningLimits _limits;

        public AutonomousIndustryPlanner(
            IProductionRecipeCatalog recipes,
            AutonomousIndustryPlanningLimits limits = null)
        {
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _limits = limits ?? new AutonomousIndustryPlanningLimits();
        }

        public AutonomousIndustryPlan Plan(
            int targetDefinition,
            long targetQuantity,
            IReadOnlyDictionary<int, long> inventory = null)
        {
            if (targetDefinition <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetDefinition));
            if (targetQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetQuantity));

            var state = new PlanningState(_recipes, _limits, inventory);
            try
            {
                state.Require(targetDefinition, targetQuantity, 0);
                return state.Build(targetDefinition, targetQuantity);
            }
            catch (PlanningFailureException failure)
            {
                return state.BuildFailure(
                    targetDefinition,
                    targetQuantity,
                    failure.Failure,
                    failure.Path);
            }
            catch (OverflowException)
            {
                return state.BuildFailure(
                    targetDefinition,
                    targetQuantity,
                    AutonomousIndustryPlanFailure.QuantityOverflow,
                    state.CurrentPath);
            }
        }

        private sealed class PlanningState
        {
            private readonly IProductionRecipeCatalog _recipes;
            private readonly AutonomousIndustryPlanningLimits _limits;
            private readonly Dictionary<int, long> _inventoryAvailable;
            private readonly Dictionary<int, long> _producedAvailable = new Dictionary<int, long>();
            private readonly Dictionary<int, long> _inventoryUsed = new Dictionary<int, long>();
            private readonly Dictionary<int, long> _procurement = new Dictionary<int, long>();
            private readonly Dictionary<int, long> _batches = new Dictionary<int, long>();
            private readonly Dictionary<int, ProductionRecipe> _usedRecipes = new Dictionary<int, ProductionRecipe>();
            private readonly List<int> _path = new List<int>();
            private readonly HashSet<int> _expandedDefinitions = new HashSet<int>();

            public PlanningState(
                IProductionRecipeCatalog recipes,
                AutonomousIndustryPlanningLimits limits,
                IReadOnlyDictionary<int, long> inventory)
            {
                _recipes = recipes;
                _limits = limits;
                _inventoryAvailable = NormalizeInventory(inventory);
            }

            public IReadOnlyList<int> CurrentPath => _path.ToArray();

            public void Require(int definition, long quantity, int depth)
            {
                long remaining = ConsumeInventory(definition, quantity);
                if (remaining == 0)
                    return;

                if (depth > _limits.MaximumDepth)
                    throw Fail(AutonomousIndustryPlanFailure.MaximumDepthExceeded, definition);
                if (_path.Contains(definition))
                    throw Fail(AutonomousIndustryPlanFailure.CycleDetected, definition);
                if (_expandedDefinitions.Add(definition) &&
                    _expandedDefinitions.Count > _limits.MaximumDefinitions)
                {
                    throw Fail(AutonomousIndustryPlanFailure.MaximumDefinitionsExceeded, definition);
                }

                if (!_recipes.TryGet(definition, out ProductionRecipe recipe) ||
                    recipe.Process == ProductionRecipeProcess.Unsupported)
                {
                    Add(_procurement, definition, remaining);
                    return;
                }

                _path.Add(definition);
                try
                {
                    long batches = DivideRoundUp(remaining, recipe.OutputQuantity);
                    Add(_batches, definition, batches);
                    _usedRecipes[definition] = recipe;

                    foreach (ProductionRecipeComponent component in recipe.Components
                                 .Where(component => component.IsRequiredFor(recipe.Process)))
                    {
                        long componentQuantity = checked(batches * component.AmountPerBatch);
                        Require(component.Definition, componentQuantity, depth + 1);
                    }

                    long produced = checked(batches * recipe.OutputQuantity);
                    long excess = produced - remaining;
                    if (excess > 0)
                        Add(_producedAvailable, definition, excess);
                }
                finally
                {
                    _path.RemoveAt(_path.Count - 1);
                }
            }

            public AutonomousIndustryPlan Build(int targetDefinition, long targetQuantity)
            {
                IReadOnlyList<AutonomousIndustryProductionStep> steps = BuildOrderedSteps();
                return new AutonomousIndustryPlan(
                    targetDefinition,
                    targetQuantity,
                    AutonomousIndustryPlanFailure.None,
                    Array.Empty<int>(),
                    steps,
                    Ordered(_procurement),
                    Ordered(_inventoryUsed),
                    BuildSurplus());
            }

            public AutonomousIndustryPlan BuildFailure(
                int targetDefinition,
                long targetQuantity,
                AutonomousIndustryPlanFailure failure,
                IEnumerable<int> path)
            {
                return new AutonomousIndustryPlan(
                    targetDefinition,
                    targetQuantity,
                    failure,
                    path,
                    Array.Empty<AutonomousIndustryProductionStep>(),
                    new Dictionary<int, long>(),
                    Ordered(_inventoryUsed),
                    new Dictionary<int, long>());
            }

            private IReadOnlyList<AutonomousIndustryProductionStep> BuildOrderedSteps()
            {
                var ordered = new List<AutonomousIndustryProductionStep>();
                var visited = new HashSet<int>();
                VisitForOrdering(_usedRecipes.Keys.OrderBy(definition => definition), visited, ordered);
                return ordered;
            }

            private void VisitForOrdering(
                IEnumerable<int> definitions,
                ISet<int> visited,
                ICollection<AutonomousIndustryProductionStep> ordered)
            {
                foreach (int definition in definitions)
                {
                    if (!visited.Add(definition))
                        continue;

                    ProductionRecipe recipe = _usedRecipes[definition];
                    VisitForOrdering(
                        recipe.Components
                            .Where(component => component.IsRequiredFor(recipe.Process))
                            .Select(component => component.Definition)
                            .Where(_usedRecipes.ContainsKey)
                            .OrderBy(componentDefinition => componentDefinition),
                        visited,
                        ordered);
                    ordered.Add(new AutonomousIndustryProductionStep(recipe, _batches[definition]));
                }
            }

            private long ConsumeInventory(int definition, long requested)
            {
                long remaining = requested;
                if (_inventoryAvailable.TryGetValue(definition, out long inventoryAvailable) &&
                    inventoryAvailable > 0)
                {
                    long inventoryConsumed = Math.Min(remaining, inventoryAvailable);
                    _inventoryAvailable[definition] = inventoryAvailable - inventoryConsumed;
                    Add(_inventoryUsed, definition, inventoryConsumed);
                    remaining -= inventoryConsumed;
                }

                if (remaining == 0 ||
                    !_producedAvailable.TryGetValue(definition, out long producedAvailable) ||
                    producedAvailable <= 0)
                {
                    return remaining;
                }

                long producedConsumed = Math.Min(remaining, producedAvailable);
                _producedAvailable[definition] = producedAvailable - producedConsumed;
                return remaining - producedConsumed;
            }

            private IReadOnlyDictionary<int, long> BuildSurplus()
            {
                return Ordered(_producedAvailable.Where(pair => pair.Value > 0));
            }

            private PlanningFailureException Fail(AutonomousIndustryPlanFailure failure, int definition)
            {
                return new PlanningFailureException(failure, _path.Concat(new[] {definition}));
            }

            private static Dictionary<int, long> NormalizeInventory(IReadOnlyDictionary<int, long> inventory)
            {
                if (inventory == null)
                    return new Dictionary<int, long>();

                var normalized = new Dictionary<int, long>();
                foreach (KeyValuePair<int, long> pair in inventory)
                {
                    if (pair.Key <= 0)
                        throw new ArgumentOutOfRangeException(nameof(inventory), "Inventory definitions must be positive.");
                    if (pair.Value < 0)
                        throw new ArgumentOutOfRangeException(nameof(inventory), "Inventory quantities cannot be negative.");
                    if (pair.Value > 0)
                        normalized[pair.Key] = pair.Value;
                }
                return normalized;
            }

            private static long DivideRoundUp(long value, int divisor)
            {
                return checked((value + divisor - 1) / divisor);
            }

            private static void Add(IDictionary<int, long> values, int definition, long quantity)
            {
                values.TryGetValue(definition, out long current);
                values[definition] = checked(current + quantity);
            }

            private static IReadOnlyDictionary<int, long> Ordered(IEnumerable<KeyValuePair<int, long>> values)
            {
                return values
                    .OrderBy(pair => pair.Key)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
        }

        private sealed class PlanningFailureException : Exception
        {
            public PlanningFailureException(
                AutonomousIndustryPlanFailure failure,
                IEnumerable<int> path)
            {
                Failure = failure;
                Path = (path ?? Array.Empty<int>()).ToArray();
            }

            public AutonomousIndustryPlanFailure Failure { get; }
            public IReadOnlyList<int> Path { get; }
        }
    }
}
