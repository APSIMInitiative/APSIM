using Models.Core;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Models.CLEM.Groupings;
using Models.Core.Attributes;
using Models.CLEM.Reporting;
using System.IO;
using Models.CLEM.Interfaces;

namespace Models.CLEM.Activities
{
    /// <summary>Ruminant wean activity</summary>
    /// <summary>This activity will wean the herd</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Manages weaning of suckling ruminant individuals based on age and/or weight")]
    [Version(1, 1, 0, "Implements event based activity control")]
    [Version(1, 0, 2, "Weaning style added. Allows decision rule (age, weight, or both to be considered.")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantWean.htm")]
    public class RuminantActivityWean: CLEMRuminantActivityBase, IHandlesActivityCompanionModels, IValidatableObject
    {
        [Link]
        private IClock clock = null;

        private string grazeStore;
        private int numberToSkip = 0;
        private int sucklingToSkip = 0;
        private int numberToDo = 0;
        private int sucklingsToCheck = 0;
        private IEnumerable<Ruminant> uniqueIndividuals;
        private IEnumerable<RuminantGroup> filterGroups = new List<RuminantGroup>();

        /// <summary>
        /// Style of weaning rule
        /// </summary>
        [Description("Weaning rule")]
        public WeaningStyle Style { get; set; }

        /// <summary>
        /// Weaning age (months)
        /// </summary>
        [Description("Weaning age (months)")]
        [Required, GreaterThanEqualValue(0)]
        public double WeaningAge { get; set; }

        /// <summary>
        /// Weaning weight (kg)
        /// </summary>
        [Description("Weaning weight (kg)")]
        [Required, GreaterThanEqualValue(0)]
        public double WeaningWeight { get; set; }

        /// <summary>
        /// Name of GrazeFoodStore (paddock) to place weaners (leave blank for general yards)
        /// </summary>
        [Description("GrazeFoodStore (paddock) to place weaners")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { "Not specified - general yards", "Leave at current location", typeof(GrazeFoodStore) } })]
        [System.ComponentModel.DefaultValue("Leave at current location")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Weaned individuals' location required")]
        public string GrazeFoodStoreName { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public RuminantActivityWean()
        {
            this.SetDefaults();
            AllocationStyle = ResourceAllocationStyle.Manual;
        }

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
                            "Number sucklings checked",
                            "Number weaned"
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

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            this.InitialiseHerd(false, true);

            // activity is performed in ManageAnimals
            this.AllocationStyle = ResourceAllocationStyle.Manual;

            // check GrazeFoodStoreExists
            grazeStore = "";
            if (GrazeFoodStoreName != null)
            {
                if (GrazeFoodStoreName.Contains("."))
                {
                    grazeStore = GrazeFoodStoreName.Split('.').Last();
                }
                else
                {
                    if (GrazeFoodStoreName == "Not specified - general yards")
                    {
                        grazeStore = "";
                        ActivitiesHolder ah = this.FindInScope<ActivitiesHolder>();
                        if (ah.FindAllDescendants<PastureActivityManage>().Count() != 0)
                            Summary.WriteMessage(this, $"Individuals weaned by [a={NameWithParent}] will be placed in [Not specified - general yards] while a managed pasture is available. These animals will not graze until moved and will require feeding while in yards.\r\nSolution: Set the [GrazeFoodStore to place weaners in] located in the properties.", MessageType.Warning);
                    }
                }
            }

            filterGroups = GetCompanionModelsByIdentifier<RuminantGroup>(false, true);
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
            numberToSkip = 0;
            sucklingToSkip = 0;
            IEnumerable<Ruminant> sucklingherd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Weaned == false);
            uniqueIndividuals = GetUniqueIndividuals<Ruminant>(filterGroups, sucklingherd);
            sucklingsToCheck = uniqueIndividuals?.Count() ?? 0;
            numberToDo = uniqueIndividuals.Where(a => (a.Age >= WeaningAge && (Style == WeaningStyle.AgeOrWeight || Style == WeaningStyle.AgeOnly)) || (a.Weight >= WeaningWeight && (Style == WeaningStyle.AgeOrWeight || Style == WeaningStyle.WeightOnly)))?.Count() ?? 0;

            // provide updated measure for companion models
            foreach (var valueToSupply in valuesForCompanionModels)
            {
                int number = numberToDo;
                if (valueToSupply.Key.identifier == "Number sucklings checked")
                    number = sucklingsToCheck;

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
                var sucklingShort = shortfalls.Where(a => a.CompanionModelDetails.identifier == "Number sucklings checked").FirstOrDefault();
                if(sucklingShort != null)
                    sucklingToSkip = Convert.ToInt32(sucklingsToCheck * (1 - sucklingShort.Available / sucklingShort.Required));

                var weanShort = shortfalls.Where(a => a.CompanionModelDetails.identifier == "Number weaned").FirstOrDefault();
                if (weanShort != null)
                    numberToSkip = Convert.ToInt32(numberToDo * (1 - weanShort.Available / weanShort.Required));

                if (numberToSkip == numberToDo || sucklingsToCheck == sucklingToSkip)
                {
                    Status = ActivityStatus.Warning;
                    AddStatusMessage("Resource shortfall prevented any action");
                }
            }
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            if (numberToDo - numberToSkip > 0 && sucklingsToCheck - sucklingToSkip > 0)
            {
                ConceptionStatusChangedEventArgs conceptionArgs = new ConceptionStatusChangedEventArgs();
                int weaned = 0;
                foreach (Ruminant ind in uniqueIndividuals.SkipLast(sucklingToSkip).ToList())
                {
                    bool readyToWean = false;
                    string reason = "";
                    switch (Style)
                    {
                        case WeaningStyle.AgeOrWeight:
                            readyToWean = (ind.Age >= WeaningAge || ind.Weight >= WeaningWeight);
                            reason = (ind.Age >= WeaningAge) ? ((ind.Weight >= WeaningWeight) ? "AgeAndWeight" : "Age") : "Weight";
                            break;
                        case WeaningStyle.AgeOnly:
                            readyToWean = (ind.Age >= WeaningAge);
                            reason = "Age";
                            break;
                        case WeaningStyle.WeightOnly:
                            readyToWean = (ind.Weight >= WeaningWeight);
                            reason = "Weight";
                            break;
                    }

                    if (readyToWean)
                    {
                        ind.Wean(true, reason);

                        // leave where weaned or move to specified location
                        if (GrazeFoodStoreName != "Leave at current location")
                            if (GrazeFoodStoreName == "Not specified - general yards")
                                ind.Location = "";
                            else
                                ind.Location = grazeStore;

                        // report wean. If mother has died create temp female with the mother's ID for reporting only
                        conceptionArgs.Update(ConceptionStatus.Weaned, ind.Mother ?? new RuminantFemale(ind.BreedParams, -1, 999) { ID = ind.MotherID }, clock.Today, ind);
                        ind.BreedParams.OnConceptionStatusChanged(conceptionArgs);

                        weaned++;
                        if (weaned > numberToDo - numberToSkip)
                            break;
                    }
                }
                SetStatusSuccessOrPartial(weaned != numberToDo);
            }
        }

        #region validation
        /// <summary>
        /// Validate model
        /// </summary>
        /// <param name="validationContext"></param>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();

            if(GrazeFoodStoreName.Contains("."))
            {
                ResourcesHolder resHolder = FindInScope<ResourcesHolder>();
                if (resHolder is null || resHolder.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreName) is null)
                {
                    string[] memberNames = new string[] { "Location is not valid" };
                    results.Add(new ValidationResult($"The location where ruminants are to be moved [r={GrazeFoodStoreName}] is not found.{Environment.NewLine}Ensure [r=GrazeFoodStore] is present and the [GrazeFoodStoreType] is present", memberNames));
                }
            }
            return results;
        }
        #endregion

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write("\r\n<div class=\"activityentry\">Individuals are weaned at ");
                if (Style == WeaningStyle.AgeOrWeight | Style == WeaningStyle.AgeOnly)
                {
                    htmlWriter.Write($"{CLEMModel.DisplaySummaryValueSnippet(WeaningAge)} months");
                    if (Style == WeaningStyle.AgeOrWeight)
                        htmlWriter.Write(" or  ");
                }
                if (Style == WeaningStyle.AgeOrWeight | Style == WeaningStyle.WeightOnly)
                    htmlWriter.Write($"{CLEMModel.DisplaySummaryValueSnippet(WeaningWeight)} kg");

                htmlWriter.Write("</div>");

                htmlWriter.Write("\r\n<div class=\"activityentry\">Weaned individuals will ");
                if (GrazeFoodStoreName == "Leave at current location")
                    htmlWriter.Write("remain at the location they were weaned");
                else
                    htmlWriter.Write($"be place in {DisplaySummaryResourceTypeSnippet(GrazeFoodStoreName, nullGeneralYards:true)}");

                htmlWriter.Write("</div>");
                // ToDo: warn if natural weaning will take place
                return htmlWriter.ToString();
            }
        }
        #endregion
    }
}
