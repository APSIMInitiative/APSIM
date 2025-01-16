using Models.Core;
using Models.CLEM.Interfaces;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Models.Core.Attributes;
using System.IO;
using Models.CLEM.Groupings;
using APSIM.Shared.Utilities;

namespace Models.CLEM.Activities
{
    /// <summary>Activity to undertake milking of particular herd</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Perform milking of lactating breeders")]
    [Version(1, 1, 0, "Implements event based activity control")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantMilking.htm")]
    public class RuminantActivityMilking: CLEMRuminantActivityBase, IHandlesActivityCompanionModels
    {
        private int numberToDo;
        private int numberToSkip;
        private double amountToDo;
        private double amountToSkip;
        private object milkStore;
        private IEnumerable<RuminantFemale> uniqueIndividuals;
        private IEnumerable<RuminantGroup> filterGroups;

        /// <summary>
        /// Resource type to store milk in
        /// </summary>
        [Description("Store to place milk")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(AnimalFoodStore), typeof(HumanFoodStore), typeof(ProductStore) } })]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Name of milk store required")]
        public string ResourceTypeName { get; set; }

        /// <inheritdoc/>
        public override LabelsForCompanionModels DefineCompanionModelLabels(string type)
        {
            switch (type)
            {
                case "RuminantGroup":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>(),
                        measures: new List<string>()
                        );
                case "ActivityFee":
                case "LabourRequirement":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>() {
                            "Number milked",
                            "Litres milked",
                        },
                        measures: new List<string>() {
                            "fixed",
                            "per head",
                            "per L milked"
                        }
                        );
                default:
                    return new LabelsForCompanionModels();
            }
        }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            this.InitialiseHerd(false, true);
            filterGroups = GetCompanionModelsByIdentifier<RuminantGroup>( false, true);

            // find milk store
            milkStore = Resources.FindResourceType<ResourceBaseWithTransactions, IResourceType>(this, ResourceTypeName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
        }

        /// <summary>An event handler to call for all herd management activities</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMAnimalMilkProduction")]
        private void OnCLEMMilkProduction(object sender, EventArgs e)
        {
            // this method will ensure the milking status is defined for females after births when lactation is set and before milk production is determined
            foreach (RuminantFemale item in this.CurrentHerd(true).OfType<RuminantFemale>().Where(a => a.IsLactating))
                // set these females to state milking performed so they switch to the non-suckling milk production curves.
                item.MilkingPerformed = true;
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            amountToDo = 0;
            amountToSkip = 0;
            numberToDo = 0;
            numberToSkip = 0;
            IEnumerable<RuminantFemale> herd = GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.IsLactating);
            uniqueIndividuals = GetUniqueIndividuals<RuminantFemale>(filterGroups, herd);
            numberToDo = uniqueIndividuals?.Count() ?? 0;

            // provide updated measure for companion models
            foreach (var valueToSupply in valuesForCompanionModels)
            {
                int number = numberToDo;
                switch (valueToSupply.Key.identifier)
                {
                    case "Number milked":
                        switch (valueToSupply.Key.unit)
                        {
                            case "fixed":
                                valuesForCompanionModels[valueToSupply.Key] = 1;
                                break;
                            case "per head":
                                valuesForCompanionModels[valueToSupply.Key] = number;
                                break;
                            default:
                                throw new NotImplementedException(UnknownUnitsErrorText(this, valueToSupply.Key));
                        }
                        break;
                    case "Litres milked":
                        switch (valueToSupply.Key.unit)
                        {
                            case "fixed":
                                valuesForCompanionModels[valueToSupply.Key] = 1;
                                break;
                            case "per L milked":
                                amountToDo = uniqueIndividuals.Sum(a => a.MilkCurrentlyAvailable);
                                valuesForCompanionModels[valueToSupply.Key] = amountToDo;
                                break;
                            default:
                                throw new NotImplementedException(UnknownUnitsErrorText(this, valueToSupply.Key));
                        }
                        break;
                    default:
                        throw new NotImplementedException(UnknownCompanionModelErrorText(this, valueToSupply.Key));
                }
            }
            return null;
        }

        /// <inheritdoc/>
        protected override void AdjustResourcesForTimestep()
        {
            IEnumerable<ResourceRequest> shortfalls = MinimumShortfallProportion();
            if (shortfalls.Any())
            {
                // find shortfall by identifiers as these may have different influence on outcome
                var numberShort = shortfalls.Where(a => a.CompanionModelDetails.identifier == "Number milked").FirstOrDefault();
                if (numberShort != null)
                {
                    numberToSkip = Convert.ToInt32(numberToDo * (1 - numberShort.Available / numberShort.Required));
                    if (numberToSkip == numberToDo)
                    {
                        Status = ActivityStatus.Warning;
                        AddStatusMessage("Resource shortfall prevented any action");
                    }
                }

                var amountShort = shortfalls.Where(a => a.CompanionModelDetails.identifier == "Litres milked").FirstOrDefault();
                if (amountShort != null)
                {
                    amountToSkip = Convert.ToInt32(amountToDo * (1 - amountShort.Available / amountShort.Required));
                    if (MathUtilities.FloatsAreEqual(amountToSkip, amountToDo))
                    {
                        Status = ActivityStatus.Warning;
                        AddStatusMessage("Resource shortfall prevented any action");
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            if (numberToDo - numberToSkip > 0)
            {
                amountToDo -= amountToSkip;
                double amountDone = 0;
                int number = 0;
                foreach (RuminantFemale ruminant in uniqueIndividuals.SkipLast(numberToSkip).ToList())
                {
                    amountDone += ruminant.MilkCurrentlyAvailable;
                    amountToDo -= ruminant.MilkCurrentlyAvailable;
                    ruminant.TakeMilk(ruminant.MilkCurrentlyAvailable, MilkUseReason.Milked);
                    number++;
                    if (amountToDo <= 0)
                        break;
                }
                // add clip to stores
                (milkStore as IResourceType).Add(amountDone, this, this.PredictedHerdNameToDisplay, TransactionCategory);

                SetStatusSuccessOrPartial((number == numberToDo && amountToDo <= 0) == false);
            }
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write("\r\n<div class=\"activityentry\">Milk is placed in ");
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(ResourceTypeName, "Not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write("</div>");
                return htmlWriter.ToString(); 
            }
        } 
        #endregion

    }
}
