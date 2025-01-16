using Models.Core;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Newtonsoft.Json;
using Models.Core.Attributes;
using System.IO;
using APSIM.Shared.Utilities;
using Models.Core.ApsimFile;
using Models.CLEM.Groupings;

namespace Models.CLEM.Activities
{
    /// <summary>Ruminant graze activity</summary>
    /// <summary>This activity determines how a ruminant group will graze</summary>
    /// <summary>It is designed to request food via a food store arbitrator</summary>
    /// <version>1.0</version>
    /// <updates>1.0 First implementation of this activity using NABSA processes</updates>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Perform grazing of all herds within a specified pasture (paddock)")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantGraze.htm")]
    public class RuminantActivityGrazePasture : CLEMRuminantActivityBase, IValidatableObject
    {
        /// <summary>
        /// Link to clock
        /// Public so children can be dynamically created after links defined
        /// </summary>
        [Link]
        public IClock Clock = null;

        /// <summary>
        /// Number of hours grazed
        /// Based on 8 hour grazing days
        /// Could be modified to account for rain/heat walking to water etc.
        /// </summary>
        [Description("Number of hours grazed (based on 8 hr grazing day)")]
        [Required, Range(0, 8, ErrorMessage = "Value based on maximum 8 hour grazing day"), GreaterThanValue(0)]
        public double HoursGrazed { get; set; }

        /// <summary>
        /// Paddock or pasture to graze
        /// </summary>
        [Description("GrazeFoodStore/pasture to graze")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Graze Food Store/pasture required")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(GrazeFoodStore) } })]
        public string GrazeFoodStoreTypeName { get; set; }

        /// <summary>
        /// paddock or pasture to graze
        /// </summary>
        [JsonIgnore]
        public GrazeFoodStoreType GrazeFoodStoreModel { get; set; }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            // This method will only fire if the user has added this activity to the UI
            // Otherwise all details will be provided from GrazeAll code [CLEMInitialiseActivity]

            GrazeFoodStoreModel = Resources.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreTypeName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);

            //Create list of children by breed
            Guid currentUid = UniqueID;
            List<IModel> grazePastureList = new List<IModel>();

            bool buildTransactionFromTree = FindAncestor<ZoneCLEM>().BuildTransactionCategoryFromTree;
            string transCat = "";
            if (!buildTransactionFromTree)
                transCat = TransactionCategory;

            foreach (RuminantType herdType in HerdResource.FindAllChildren<RuminantType>())
            {
                RuminantActivityGrazePastureHerd grazePastureHerd = new RuminantActivityGrazePastureHerd
                {
                    RuminantTypeName = herdType.NameWithParent,
                    GrazeFoodStoreTypeName = GrazeFoodStoreTypeName,
                    ActivitiesHolder = ActivitiesHolder,
                    GrazeFoodStoreModel = GrazeFoodStoreModel,
                    RuminantTypeModel = herdType,
                    HoursGrazed = HoursGrazed,
                    Parent = this,
                    Clock = this.Clock,
                    Name = "Graze_" + (GrazeFoodStoreModel as Model).Name + "_" + herdType.Name,
                    OnPartialResourcesAvailableAction = this.OnPartialResourcesAvailableAction,
                    TransactionCategory = transCat
                };
                currentUid = ActivitiesHolder.AddToGuID(currentUid, 1);
                grazePastureHerd.UniqueID = currentUid;

                grazePastureHerd.SetLinkedModels(Resources);

                // add ruminant activity filter group to ensure correct individuals are selected
                RuminantActivityGroup herdGroup = new()
                {
                    Name = "Filter_" + grazePastureHerd.Name,
                    Parent = this
                };
                herdGroup.Children.Add(
                    new FilterByProperty()
                    {
                        PropertyOfIndividual = "HerdName",
                        Operator = System.Linq.Expressions.ExpressionType.Equal,
                        Value = herdType.Name,
                        Parent = herdGroup
                    }
                );
                grazePastureHerd.Children.Add(herdGroup);
                grazePastureHerd.FindChild<RuminantActivityGroup>().InitialiseFilters();

                grazePastureHerd.InitialiseHerd(false, false);
                Children.Add(grazePastureHerd);
                Structure.Add(grazePastureHerd, this);
            }
            this.FindAllDescendants<RuminantActivityGrazePastureHerd>().LastOrDefault().IsHidden = true;
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            // This method does not take any resources but is used to arbitrate resources for all breed grazing activities it contains

            // check nested graze breed requirements for this pasture
            double totalNeeded = 0;
            IEnumerable<RuminantActivityGrazePastureHerd> grazeHerdChildren = FindAllChildren<RuminantActivityGrazePastureHerd>();
            double potentialIntakeLimiter = -1;
            foreach (RuminantActivityGrazePastureHerd item in grazeHerdChildren)
            {
                if(MathUtilities.IsNegative(potentialIntakeLimiter))
                    potentialIntakeLimiter = item.CalculatePotentialIntakePastureQualityLimiter();
                item.ResourceRequestList = null;
                item.PotentialIntakePastureQualityLimiter = potentialIntakeLimiter;
                var resourceRequest = item.RequestDetermineResources().Where(a => a.Resource is GrazeFoodStoreType).FirstOrDefault();
                if(resourceRequest != null)
                    totalNeeded += resourceRequest.Required;
            }

            // Check available resources
            // This determines the proportional amount available for competing breeds with different green diet proportions
            // It does not truly account for how the pasture is provided from pools but will suffice unless more detailed model required
            double available = GrazeFoodStoreModel.Amount;
            double limit = 1.0;
            if(MathUtilities.IsPositive(totalNeeded))
                limit = Math.Min(1.0, available / totalNeeded);

            // apply limits to children
            foreach (RuminantActivityGrazePastureHerd item in grazeHerdChildren)
                item.SetupPoolsAndLimits(limit);

            return ResourceRequestList;
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            if (Status != ActivityStatus.Partial && Status != ActivityStatus.Critical)
                Status = ActivityStatus.NoTask;
            return;
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

            if (GrazeFoodStoreTypeName.Contains("."))
            {
                ResourcesHolder resHolder = FindInScope<ResourcesHolder>();
                if (resHolder is null || resHolder.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreTypeName) is null)
                {
                    string[] memberNames = new string[] { "Location is not valid" };
                    results.Add(new ValidationResult($"The location defined for grazing [r={GrazeFoodStoreTypeName}] in [a={Name}] is not found.{Environment.NewLine}Ensure [r=GrazeFoodStore] is present and the [GrazeFoodStoreType] is present", memberNames));
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
                htmlWriter.Write("\r\n<div class=\"activityentry\">All individuals in ");
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(GrazeFoodStoreTypeName, "Pasture not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write(" will graze for ");
                if (HoursGrazed <= 0)
                    htmlWriter.Write("<span class=\"errorlink\">" + HoursGrazed.ToString("0.#") + "</span> hours of ");
                else
                    htmlWriter.Write(((HoursGrazed == 8) ? "" : "<span class=\"setvalue\">" + HoursGrazed.ToString("0.#") + "</span> hours of "));
                htmlWriter.Write("the maximum 8 hours each day</span>");
                htmlWriter.Write("</div>");
                return htmlWriter.ToString();
            }
        }
        #endregion

    }
}
