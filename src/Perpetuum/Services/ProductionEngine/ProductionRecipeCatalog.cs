using System;
using System.Collections.Generic;
using System.Linq;
using Perpetuum.EntityFramework;
using Perpetuum.ExportedTypes;

namespace Perpetuum.Services.ProductionEngine
{
    public enum ProductionRecipeProcess
    {
        Unsupported,
        Refining,
        MassProduction
    }

    /// <summary>
    /// A nominal production component requirement for one output batch. Actual
    /// facility quotes may change this amount through material efficiency.
    /// </summary>
    public sealed class ProductionRecipeComponent
    {
        public ProductionRecipeComponent(
            int definition,
            int amountPerBatch,
            bool requiredForRefining = true,
            bool requiredForPrototype = true,
            bool requiredForMassProduction = true)
        {
            if (definition <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (amountPerBatch <= 0)
                throw new ArgumentOutOfRangeException(nameof(amountPerBatch));

            Definition = definition;
            AmountPerBatch = amountPerBatch;
            RequiredForRefining = requiredForRefining;
            RequiredForPrototype = requiredForPrototype;
            RequiredForMassProduction = requiredForMassProduction;
        }

        public int Definition { get; }
        public int AmountPerBatch { get; }
        public bool RequiredForRefining { get; }
        public bool RequiredForPrototype { get; }
        public bool RequiredForMassProduction { get; }

        public bool IsRequiredFor(ProductionRecipeProcess process)
        {
            switch (process)
            {
                case ProductionRecipeProcess.Refining:
                    return RequiredForRefining;
                case ProductionRecipeProcess.MassProduction:
                    return RequiredForMassProduction;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Immutable projection of the authoritative production tables. This is a
    /// strategic recipe, not permission to produce and not an executable quote.
    /// </summary>
    public sealed class ProductionRecipe
    {
        public ProductionRecipe(
            int definition,
            string definitionName,
            int outputQuantity,
            IEnumerable<ProductionRecipeComponent> components,
            ProductionRecipeProcess process,
            int prototypeDefinition,
            int researchLevel,
            int? calibrationProgramDefinition)
        {
            if (definition <= 0)
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (outputQuantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(outputQuantity));
            if (components == null)
                throw new ArgumentNullException(nameof(components));

            ProductionRecipeComponent[] componentArray = components
                .OrderBy(component => component.Definition)
                .ToArray();
            if (componentArray.Length == 0)
                throw new ArgumentException("A production recipe must contain components.", nameof(components));

            Definition = definition;
            DefinitionName = definitionName ?? string.Empty;
            OutputQuantity = outputQuantity;
            Components = componentArray;
            Process = process;
            PrototypeDefinition = prototypeDefinition > 0 ? prototypeDefinition : definition;
            ResearchLevel = Math.Max(0, researchLevel);
            CalibrationProgramDefinition = calibrationProgramDefinition > 0
                ? calibrationProgramDefinition
                : null;
        }

        public int Definition { get; }
        public string DefinitionName { get; }
        public int OutputQuantity { get; }
        public IReadOnlyList<ProductionRecipeComponent> Components { get; }
        public ProductionRecipeProcess Process { get; }
        public int PrototypeDefinition { get; }
        public int ResearchLevel { get; }
        public int? CalibrationProgramDefinition { get; }
        public bool RequiresPrototype => PrototypeDefinition != Definition;
        public bool HasMassProductionResearch => CalibrationProgramDefinition.HasValue;
    }

    public interface IProductionRecipeCatalog
    {
        bool TryGet(int definition, out ProductionRecipe recipe);
    }

    /// <summary>
    /// Read-only recipe graph backed by the same initialized caches used by the
    /// prototyper, research lab, and mill.
    /// </summary>
    public sealed class ProductionRecipeCatalog : IProductionRecipeCatalog
    {
        private readonly IProductionDataAccess _productionData;
        private readonly IEntityDefaultReader _entityDefaults;

        public ProductionRecipeCatalog(
            IProductionDataAccess productionData,
            IEntityDefaultReader entityDefaults)
        {
            _productionData = productionData ?? throw new ArgumentNullException(nameof(productionData));
            _entityDefaults = entityDefaults ?? throw new ArgumentNullException(nameof(entityDefaults));
        }

        public bool TryGet(int definition, out ProductionRecipe recipe)
        {
            recipe = null;
            if (definition <= 0 || !_entityDefaults.TryGet(definition, out EntityDefault output))
                return false;
            if (output.Quantity <= 0)
                return false;

            ProductionRecipeComponent[] components = _productionData.ProductionComponents
                .GetOrEmpty(definition)
                .Where(component => component != null && component.EntityDefault != null)
                .Select(component => new ProductionRecipeComponent(
                    component.EntityDefault.Definition,
                    component.Amount,
                    !component.IsSkipped(ProductionInProgressType.refine),
                    !component.IsSkipped(ProductionInProgressType.prototype),
                    !component.IsSkipped(ProductionInProgressType.massProduction)))
                .ToArray();
            if (components.Length == 0)
                return false;

            int prototypeDefinition = _productionData.Prototypes.TryGetValue(definition, out int prototype)
                ? prototype
                : definition;
            ItemResearchLevel research = null;
            if (!_productionData.ResearchLevels.TryGetValue(prototypeDefinition, out research))
                _productionData.ResearchLevels.TryGetValue(definition, out research);

            ProductionRecipeProcess process = output.CategoryFlags.IsCategory(CategoryFlags.cf_basic_commodities)
                ? ProductionRecipeProcess.Refining
                : research?.calibrationProgramDefinition > 0
                    ? ProductionRecipeProcess.MassProduction
                    : ProductionRecipeProcess.Unsupported;
            int outputQuantity = process == ProductionRecipeProcess.Refining
                ? 1
                : output.Quantity;

            recipe = new ProductionRecipe(
                definition,
                output.Name,
                outputQuantity,
                components,
                process,
                prototypeDefinition,
                research?.researchLevel ?? 0,
                research?.calibrationProgramDefinition);
            return true;
        }
    }
}
