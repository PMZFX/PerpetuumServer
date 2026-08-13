using System.Collections.Generic;
using System.Linq;
using Perpetuum.Accounting.Characters;
using Perpetuum.Common.Loggers.Transaction;
using Perpetuum.Containers;
using Perpetuum.ExportedTypes;

namespace Perpetuum.Services.ProductionEngine.Facilities
{
    public class Refinery : ProductionFacility
    {
        public override Dictionary<string, object> GetFacilityInfo(Character character)
        {
            var infoData = base.GetFacilityInfo(character);

            var additiveComponent = GetAdditiveComponent(character);

            infoData.Add(k.percentageMaterial, GetPercentageFromAdditiveComponent(additiveComponent));
            infoData.Add(k.myPointsMaterial,additiveComponent );
            infoData.Add(k.extensionPoints, (int)GetMaterialExtensionBonus(character));

            return infoData;
        }

        public override ProductionFacilityType FacilityType
        {
            get { return ProductionFacilityType.Refine; }
        }

        public IDictionary<string,object> RefineQuery(Character character, int targetDefinition, int targetAmount, ProductionDescription productionDescription)
        {
            RefineQuote quote = GetQuote(character, targetDefinition, targetAmount, productionDescription);
            var components = quote.Components
                .ToDictionary("c", component => new Dictionary<string, object>
                {
                    {k.definition, component.Definition},
                    {k.real, component.EffectiveAmount},
                    {k.nominal, component.NominalAmount}
                });

            return new Dictionary<string, object>
            {
                {k.components, components},
                {k.targetAmount, quote.TargetAmount},
                {k.targetDefinition, quote.TargetDefinition},
                {k.facility, quote.FacilityEid}
            };
        }

        public RefineQuote GetQuote(
            Character character,
            int targetDefinition,
            int targetAmount,
            ProductionDescription productionDescription)
        {
            if (character == null)
                throw new System.ArgumentNullException(nameof(character));
            if (productionDescription == null)
                throw new System.ArgumentNullException(nameof(productionDescription));

            double materialEfficiency = GetMaterialMultiplier(character);
            RefineComponentQuote[] components = productionDescription.Components
                .Where(component => !component.IsSkipped(ProductionInProgressType.refine))
                .Select(component => new RefineComponentQuote(
                    component.EntityDefault.Definition,
                    component.Amount * targetAmount,
                    component.EffectiveAmount(targetAmount, materialEfficiency)))
                .ToArray();
            return new RefineQuote(targetDefinition, targetAmount, Eid, components);
        }

        public IDictionary<string,object> Refine(Character character, Container sourceContainer, int targetAmount, ProductionDescription productionDescription)
        {
            var materialMultiplier = GetMaterialMultiplier(character);

            //collect availabe materials
            var foundComponents = productionDescription.SearchForAvailableComponents(sourceContainer).ToList();

            //generate a list of the used components
            var itemsUsed = productionDescription.ProcessComponentRequirement(ProductionInProgressType.refine, foundComponents, targetAmount, materialMultiplier);

            //create item
            productionDescription.CreateRefineResult(character.Eid, sourceContainer, targetAmount, character);

            //update / delete components from source container
            ProductionDescription.UpdateUsedComponents(itemsUsed, sourceContainer, character, TransactionType.RefineDelete).ThrowIfError();

            sourceContainer.Save();

            var sourceInformData = sourceContainer.ToDictionary();

            var replyDict = new Dictionary<string, object> {{k.sourceContainer, sourceInformData}};
            return replyDict;
        }

        private int GetAdditiveComponent(Character character)
        {
            var extensionPoints = GetMaterialExtensionBonus(character);
            var refinerypoints = GetFacilityPoint();
            var standingPoints = GetStandingPoints(character);

            return (int)( extensionPoints + refinerypoints + standingPoints);
        }


        private double GetMaterialMultiplier(Character character)
        {
            //(1+(50/(STANDING + PC_EXT_PONT + REFINERY.ME_PONT + 100)))

            var multiplier = (1 + (50/(GetAdditiveComponent(character) + 100.0)));
            
            return multiplier;

        }

        public override int RealMaxSlotsPerCharacter(Character character)
        {
            return 1;
        }

        public override double GetMaterialExtensionBonus(Character character)
        {
            return character.GetExtensionsBonusSummary(ExtensionNames.PRODUCTION_REFINE);
        }
    }
}
