using System.Collections.Generic;
using System.Linq;
using Perpetuum.EntityFramework;
using Perpetuum.ExportedTypes;
using Perpetuum.Services.ProductionEngine;
using Perpetuum.Services.ProductionEngine.CalibrationPrograms;
using Xunit;

namespace Perpetuum.Tests.Services.ProductionEngine
{
    public class ProductionRecipeCatalogTests
    {
        [Fact]
        public void ProjectsBatchComponentsAndResearchPrerequisites()
        {
            var output = Definition(100, "def_ammo", 1000);
            var material = Definition(10, "def_titan", 1);
            var entityDefaults = new FakeEntityDefaultReader(output, material);
            var productionData = new FakeProductionDataAccess(
                new[] {new KeyValuePair<int, ProductionComponent>(100, new ProductionComponent(material, 25))},
                new Dictionary<int, int> {{100, 200}},
                new Dictionary<int, ItemResearchLevel>
                {
                    {
                        200,
                        new ItemResearchLevel
                        {
                            definition = 200,
                            researchLevel = 3,
                            calibrationProgramDefinition = 300
                        }
                    }
                });

            var catalog = new ProductionRecipeCatalog(productionData, entityDefaults);

            Assert.True(catalog.TryGet(100, out ProductionRecipe recipe));
            Assert.Equal(1000, recipe.OutputQuantity);
            Assert.Equal(ProductionRecipeProcess.MassProduction, recipe.Process);
            Assert.Equal(200, recipe.PrototypeDefinition);
            Assert.True(recipe.RequiresPrototype);
            Assert.Equal(3, recipe.ResearchLevel);
            Assert.Equal(300, recipe.CalibrationProgramDefinition);
            Assert.True(recipe.HasMassProductionResearch);
            ProductionRecipeComponent component = Assert.Single(recipe.Components);
            Assert.Equal(10, component.Definition);
            Assert.Equal(25, component.AmountPerBatch);
        }

        [Fact]
        public void PreservesFacilitySpecificComponentApplicability()
        {
            var output = Definition(100, "def_robot", 1);
            var robotShard = Definition(10, "def_robot_shard", 1);
            robotShard.CategoryFlags = CategoryFlags.cf_robotshards;
            var productionData = new FakeProductionDataAccess(
                new[] {new KeyValuePair<int, ProductionComponent>(100, new ProductionComponent(robotShard, 20))},
                new Dictionary<int, int> {{100, 200}},
                new Dictionary<int, ItemResearchLevel>
                {
                    {
                        200,
                        new ItemResearchLevel
                        {
                            definition = 200,
                            calibrationProgramDefinition = 300
                        }
                    }
                });

            var catalog = new ProductionRecipeCatalog(
                productionData,
                new FakeEntityDefaultReader(output, robotShard));

            Assert.True(catalog.TryGet(100, out ProductionRecipe recipe));
            ProductionRecipeComponent component = Assert.Single(recipe.Components);
            Assert.False(component.RequiredForRefining);
            Assert.True(component.RequiredForPrototype);
            Assert.False(component.RequiredForMassProduction);
        }

        [Fact]
        public void BasicCommodityUsesPerUnitRefinerySemantics()
        {
            var output = Definition(100, "def_basic_commodity", 100);
            output.CategoryFlags = CategoryFlags.cf_basic_commodities;
            var material = Definition(10, "def_raw_material", 1);
            var productionData = new FakeProductionDataAccess(
                new[] {new KeyValuePair<int, ProductionComponent>(100, new ProductionComponent(material, 25))});

            var catalog = new ProductionRecipeCatalog(
                productionData,
                new FakeEntityDefaultReader(output, material));

            Assert.True(catalog.TryGet(100, out ProductionRecipe recipe));
            Assert.Equal(ProductionRecipeProcess.Refining, recipe.Process);
            Assert.Equal(1, recipe.OutputQuantity);
        }

        [Fact]
        public void DefinitionWithoutComponentsIsNotARecipe()
        {
            var catalog = new ProductionRecipeCatalog(
                new FakeProductionDataAccess(),
                new FakeEntityDefaultReader(Definition(10, "def_titan", 1)));

            Assert.False(catalog.TryGet(10, out ProductionRecipe recipe));
            Assert.Null(recipe);
        }

        private static EntityDefault Definition(int definition, string name, int quantity)
        {
            return new EntityDefault
            {
                Definition = definition,
                Name = name,
                Quantity = quantity
            };
        }

        private sealed class FakeEntityDefaultReader : IEntityDefaultReader
        {
            private readonly Dictionary<int, EntityDefault> _definitions;

            public FakeEntityDefaultReader(params EntityDefault[] definitions)
            {
                _definitions = definitions.ToDictionary(definition => definition.Definition);
            }

            public bool Exists(int definition) => _definitions.ContainsKey(definition);

            public EntityDefault Get(int definition) =>
                _definitions.TryGetValue(definition, out EntityDefault entityDefault)
                    ? entityDefault
                    : EntityDefault.None;

            public EntityDefault GetByEid(long eid) => EntityDefault.None;

            public bool TryGet(int definition, out EntityDefault entityDefault) =>
                _definitions.TryGetValue(definition, out entityDefault);

            public IEnumerable<EntityDefault> GetAll() => _definitions.Values;
        }

        private sealed class FakeProductionDataAccess : IProductionDataAccess
        {
            public FakeProductionDataAccess(
                IEnumerable<KeyValuePair<int, ProductionComponent>> components = null,
                IDictionary<int, int> prototypes = null,
                IDictionary<int, ItemResearchLevel> researchLevels = null)
            {
                ProductionComponents = (components ?? Enumerable.Empty<KeyValuePair<int, ProductionComponent>>())
                    .ToLookup(pair => pair.Key, pair => pair.Value);
                Prototypes = prototypes ?? new Dictionary<int, int>();
                ResearchLevels = researchLevels ?? new Dictionary<int, ItemResearchLevel>();
            }

            public IDictionary<int, int> Prototypes { get; }
            public IDictionary<int, ItemResearchLevel> ResearchLevels { get; }
            public ILookup<int, ProductionComponent> ProductionComponents { get; }
            public IDictionary<CategoryFlags, double> ProductionDurations { get; } =
                new Dictionary<CategoryFlags, double>();
            public IDictionary<int, double> ProductionCost { get; } = new Dictionary<int, double>();
            public IDictionary<int, CalibrationDefault> CalibrationDefaults { get; } =
                new Dictionary<int, CalibrationDefault>();

            public ProductionDecalibration GetDecalibration(int targetDefinition) =>
                ProductionDecalibration.Default;
        }
    }
}
