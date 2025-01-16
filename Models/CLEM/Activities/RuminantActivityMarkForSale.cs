using Models.CLEM.Groupings;
using Models.CLEM.Resources;
using Models.CLEM.Interfaces;
using Models.Core;
using Models.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Models.CLEM.Activities
{
    /// <summary>Mark specified individual ruminants for sale.</summary>
    /// <summary>This activity is in addition to those identified in RuminantActivityManage</summary>
    /// <version>1.0</version>
    /// <updates>1.0 First implementation of this activity using IAT/NABSA processes</updates>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Mark the specified individuals for sale with specified sale reason")]
    [Version(1, 1, 0, "Implements event based activity control")]
    [Version(1, 0, 2, "Allows specification of sale reason for reporting")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantMarkForSale.htm")]
    public class RuminantActivityMarkForSale: CLEMRuminantActivityBase, IHandlesActivityCompanionModels
    {
        private int numberToDo;
        private int numberToSkip;
        private IEnumerable<Ruminant> uniqueIndividuals;
        private IEnumerable<RuminantGroup> filterGroups;

        /// <summary>
        /// Sale flag to use
        /// </summary>
        [Description("Sale reason to apply")]
        [System.ComponentModel.DefaultValueAttribute("MarkedSale")]
        [GreaterThanValue(0, ErrorMessage = "A sale reason must be provided")]
        [HerdSaleReason("sale", ErrorMessage = "The herd change reason provided must relate to a sale")]
        public HerdChangeReason SaleFlagToUse { get; set; }

        /// <summary>
        /// Overwrite any currently recorded sale flag
        /// </summary>
        [Description("Overwrite existing sale flag")]
        [System.ComponentModel.DefaultValueAttribute(false)]
        public bool OverwriteFlag { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public RuminantActivityMarkForSale()
        {
            // activity is performed in ManageAnimals
            AllocationStyle = ResourceAllocationStyle.Manual;
        }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            // get all ui tree herd filters that relate to this activity
            this.InitialiseHerd(false, true);
            filterGroups = GetCompanionModelsByIdentifier<RuminantGroup>( true, false);
        }

        /// <inheritdoc/>
        public override LabelsForCompanionModels DefineCompanionModelLabels(string type)
        {
            switch (type)
            {
                case "RuminantGroup":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>() ,
                        measures: new List<string>()
                        );
                case "ActivityFee":
                case "LabourRequirement":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>() {
                        },
                        measures: new List<string>() {
                            "fixed",
                            "per head"
                        }
                        );
                default:
                    return new LabelsForCompanionModels();
            }
        }

        /// <inheritdoc/>
        [EventSubscribe("CLEMAnimalMark")]
        protected override void OnGetResourcesPerformActivity(object sender, EventArgs e)
        {
            ManageActivityResourcesAndTasks();
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            numberToDo = 0;
            numberToSkip = 0;
            IEnumerable<Ruminant> herd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => OverwriteFlag || a.SaleFlag == HerdChangeReason.None);
            uniqueIndividuals = GetUniqueIndividuals<Ruminant>(filterGroups, herd);
            numberToDo = uniqueIndividuals?.Count() ?? 0;

            // provide updated measure for companion models
            foreach (var valueToSupply in valuesForCompanionModels)
            {
                int number = numberToDo;
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
                var tagsShort = shortfalls.FirstOrDefault();
                if (tagsShort != null)
                {
                    numberToSkip = Convert.ToInt32(numberToDo * (1 - tagsShort.Available / tagsShort.Required));
                    if (numberToSkip == numberToDo)
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
                int number = 0;
                foreach (Ruminant ruminant in uniqueIndividuals.SkipLast(numberToSkip).ToList())
                {
                    ruminant.SaleFlag = SaleFlagToUse;
                    number++;
                }
                SetStatusSuccessOrPartial(number != numberToDo);
            }
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            return $"\r\n<div class=\"activityentry\">Flag individuals for sale as {CLEMModel.DisplaySummaryValueSnippet(SaleFlagToUse.ToString(), "SaleFlag not set")}</div>";
        } 
        #endregion
    }
}
