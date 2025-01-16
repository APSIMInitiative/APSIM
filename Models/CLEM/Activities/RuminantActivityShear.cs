using Models.CLEM.Interfaces;
using Models.CLEM.Resources;
using Models.Core;
using Models.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Newtonsoft.Json;
using Models.CLEM.Groupings;
using APSIM.Shared.Utilities;
using System.IO;

namespace Models.CLEM.Activities
{
    /// <summary>Ruminant shear activity</summary>
    /// <summary>This activity shears the specified ruminants and placed clip in a store</summary>
    /// <version>1.0</version>
    /// <updates>1.0 First implementation of this activity using IAT/NABSA processes</updates>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Perform ruminant shearing and place clip in a specified store")]
    [Version(1, 1, 0, "Implements event based activity control")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantShear.htm")]
    public class RuminantActivityShear : CLEMRuminantActivityBase, IHandlesActivityCompanionModels
    {
        private int numberToDo;
        private int numberToSkip;
        private double amountToSkip;
        private double amountToDo;
        private IEnumerable<Ruminant> uniqueIndividuals;
        private IEnumerable<RuminantGroup> filterGroups;

        /// <summary>
        /// Name of Product store to place wool clip (with Resource Group name appended to the front [separated with a '.'])
        /// </summary>
        [Description("Store to place wool clip")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(ProductStore) } })]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Product store type required")]
        public string WoolProductStoreName { get; set; }

        /// <summary>
        /// Name of Product store to place cahsmere clip (with Resource Group name appended to the front [separated with a '.'])
        /// </summary>
        [Description("Store to place cashmere clip")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(ProductStore) } })]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Product store type required")]
        public string CashmereProductStoreName { get; set; }

        /// <summary>
        /// Product store for wool clip
        /// </summary>
        [JsonIgnore]
        public ProductStoreType WoolStoreType { get; set; }

        /// <summary>
        /// Product store for cashmere clip
        /// </summary>
        [JsonIgnore]
        public ProductStoreType CashmereStoreType { get; set; }

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
                            "Number shorn",
                            "Fleece weight"
                        },
                        measures: new List<string>() {
                            "fixed",
                            "per head",
                            "per kg fleece"
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
            // get all ui tree herd filters that relate to this activity
            this.InitialiseHerd(false, true);
            filterGroups = GetCompanionModelsByIdentifier<RuminantGroup>( false, true);

            // locate StoreType resource
            WoolStoreType = Resources.FindResourceType<ProductStore, ProductStoreType>(this, WoolProductStoreName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
            CashmereStoreType = Resources.FindResourceType<ProductStore, ProductStoreType>(this, CashmereProductStoreName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            amountToDo = 0;
            amountToSkip = 0;
            numberToDo = 0;
            numberToSkip = 0;
            IEnumerable<Ruminant> herd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Wool + a.Cashmere > 0);
            uniqueIndividuals = GetUniqueIndividuals<Ruminant>(filterGroups, herd);
            numberToDo = uniqueIndividuals?.Count() ?? 0;

            // provide updated measure for companion models
            foreach (var valueToSupply in valuesForCompanionModels)
            {
                int number = numberToDo;
                switch (valueToSupply.Key.identifier)
                {
                    case "Number shorn":
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
                    case "Fleece weight":
                        switch (valueToSupply.Key.unit)
                        {
                            case "fixed":
                                valuesForCompanionModels[valueToSupply.Key] = 1;
                                break;
                            case "per kg fleece":
                                amountToDo = uniqueIndividuals.Sum(a => a.Wool + a.Cashmere);
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
                var numberShort = shortfalls.Where(a => a.CompanionModelDetails.identifier == "Number shorn").FirstOrDefault();
                if (numberShort != null)
                    numberToSkip = Convert.ToInt32(numberToDo * (1 - numberShort.Available / numberShort.Required));

                var kgShort = shortfalls.Where(a => a.CompanionModelDetails.identifier == "Weight of fleece").FirstOrDefault();
                if (kgShort != null)
                    amountToSkip = Convert.ToInt32(amountToDo * (1 - kgShort.Available / kgShort.Required));

                if(numberToSkip + amountToSkip > 0)
                    Status = ActivityStatus.Partial;
                else if(MathUtilities.FloatsAreEqual(numberToSkip + amountToSkip, numberToDo + amountToDo) == false)
                {
                    Status = ActivityStatus.Critical;
                    AddStatusMessage("Resource shortfall prevented any action");
                }
            }
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            if (numberToDo - numberToSkip > 0)
            {
                amountToDo -= amountToSkip;
                double kgWoolShorn = 0;
                double kgCashmereShorn = 0;
                int shorn = 0;
                foreach (Ruminant ruminant in uniqueIndividuals.SkipLast(numberToSkip).ToList())
                {
                    kgWoolShorn += ruminant.Wool;
                    amountToDo -= ruminant.Wool;
                    kgCashmereShorn += ruminant.Cashmere;
                    amountToDo -= ruminant.Cashmere;
                    ruminant.Wool = 0;
                    ruminant.Cashmere = 0;
                    shorn++;
                    if (amountToDo <= 0)
                        break;
                }
                // add clip to stores
                (WoolStoreType as IResourceType).Add(kgWoolShorn, this, this.PredictedHerdNameToDisplay, TransactionCategory);
                (CashmereStoreType as IResourceType).Add(kgCashmereShorn, this, this.PredictedHerdNameToDisplay, TransactionCategory);

                SetStatusSuccessOrPartial(shorn != numberToDo || MathUtilities.IsPositive(amountToDo));
            }
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write("\r\n<div class=\"activityentry\">Shear selected herd and place wool clip in ");
                htmlWriter.Write($"{CLEMModel.DisplaySummaryValueSnippet(WoolProductStoreName, "Store Type not set")}");
                htmlWriter.Write(" and cashmere clip in ");
                htmlWriter.Write($"{CLEMModel.DisplaySummaryValueSnippet(CashmereProductStoreName, "Store Type not set")}");
                htmlWriter.Write("</div>");
                return htmlWriter.ToString();
            }
        }
        #endregion

    }
}
