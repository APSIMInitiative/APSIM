using Models.CLEM.Interfaces;
using Models.CLEM.Limiters;
using Models.CLEM.Resources;
using Models.Core;
using Models.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Newtonsoft.Json;
using System.IO;

namespace Models.CLEM.Activities
{
    /// <summary>Activity to perform manual cut and carry from a pasture</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Perform cut and carry from a specified graze food store (i.e. native pasture paddock)")]
    [Version(1, 0, 1, "Included new ProportionOfAvailable option for moving pasture")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Pasture/CutAndCarry.htm")]
    public class PastureActivityCutAndCarry : CLEMRuminantActivityBase, IHandlesActivityCompanionModels
    {
        [Link]
        private IClock clock = null;
        private GrazeFoodStoreType pasture;
        private AnimalFoodStoreType foodstore;
        private ActivityCarryLimiter limiter;
        private double amountToDo;
        private double amountToSkip;

        /// <summary>
        /// Name of graze food store/paddock to cut and carry from
        /// </summary>
        [Description("Graze food store/paddock")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Graze food store where pasture is located required")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(GrazeFoodStore) } })]
        public string PaddockName { get; set; }

        /// <summary>
        /// Animal food store to receive pasture
        /// </summary>
        [Description("Animal food store to receive pasture")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Animal food store to receive pasture is required")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(AnimalFoodStore) } })]
        public string AnimalFoodStoreName { get; set; }

        /// <summary>
        /// Cut and carry amount type
        /// </summary>
        [System.ComponentModel.DefaultValueAttribute(RuminantFeedActivityTypes.SpecifiedDailyAmount)]
        [Description("Cut and carry amount type")]
        [Required]
        public RuminantFeedActivityTypes CutStyle { get; set; }

        /// <summary>
        /// Value to supply
        /// </summary>
        [Description("Daily value to supply")]
        [GreaterThanValue("0")]
        public double Supply { get; set; }

        /// <summary>
        /// Amount harvested this timestep after limiter accounted for
        /// </summary>
        [JsonIgnore]
        public double AmountHarvested { get; set; }

        /// <summary>
        /// Amount available for harvest from crop file
        /// </summary>
        [JsonIgnore]
        public double AmountAvailableForHarvest { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public PastureActivityCutAndCarry()
        {
            AllocationStyle = ResourceAllocationStyle.Manual;
        }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            // activity is performed in CLEMDoCutAndCarry not CLEMGetResources
            this.AllocationStyle = ResourceAllocationStyle.Manual;

            // get pasture
            pasture = Resources.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, PaddockName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);

            // get food store
            foodstore = Resources.FindResourceType<AnimalFoodStore, AnimalFoodStoreType>(this, AnimalFoodStoreName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);

            // locate a cut and carry limiter associarted with this event.
            limiter = ActivityCarryLimiter.Locate(this);

            switch (CutStyle)
            {
                case RuminantFeedActivityTypes.ProportionOfPotentialIntake:
                case RuminantFeedActivityTypes.ProportionOfRemainingIntakeRequired:
                case RuminantFeedActivityTypes.ProportionOfWeight:
                case RuminantFeedActivityTypes.SpecifiedDailyAmountPerIndividual:
                    InitialiseHerd(false, true);
                    break;
                default:
                    break;
            }
        }

        /// <inheritdoc/>
        public override LabelsForCompanionModels DefineCompanionModelLabels(string type)
        {
            switch (type)
            {
                case "ActivityFee":
                case "LabourRequirement":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>(),
                        measures: new List<string>() {
                            "fixed",
                            "per kg collected",
                        }
                        );
                default:
                    return new LabelsForCompanionModels();
            }
        }

        /// <inheritdoc/>
        [EventSubscribe("CLEMDoCutAndCarry")]
        protected override void OnGetResourcesPerformActivity(object sender, EventArgs e)
        {
            ManageActivityResourcesAndTasks();
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            amountToSkip = 0;

            switch (CutStyle)
            {
                case RuminantFeedActivityTypes.ProportionOfFeedAvailable:
                    amountToDo += pasture.Amount * Supply;
                    break;
                case RuminantFeedActivityTypes.SpecifiedDailyAmount:
                    amountToDo += Supply * 30.4;
                    break;
                case RuminantFeedActivityTypes.ProportionOfWeight:
                    foreach (Ruminant ind in CurrentHerd(false))
                    {
                        amountToDo += Supply * ind.Weight * 30.4;
                    }
                    break;
                case RuminantFeedActivityTypes.ProportionOfPotentialIntake:
                    foreach (Ruminant ind in CurrentHerd(false))
                    {
                        amountToDo += Supply * ind.PotentialIntake;
                    }
                    break;
                case RuminantFeedActivityTypes.ProportionOfRemainingIntakeRequired:
                    foreach (Ruminant ind in CurrentHerd(false))
                    {
                        amountToDo += Supply * (ind.PotentialIntake - ind.Intake);
                    }
                    break;
                default:
                    throw new Exception(String.Format("FeedActivityType {0} is not supported in {1}", CutStyle, this.Name));
            }

            // reduce amount by limiter if present.
            if (limiter != null)
            {
                double canBeCarried = limiter.GetAmountAvailable(clock.Today.Month);
                Status = ActivityStatus.Warning;
                AddStatusMessage("CutCarry limit enforced");
                amountToDo = Math.Max(amountToDo, canBeCarried);
            }

            // provide updated measure for companion models
            foreach (var valueToSupply in valuesForCompanionModels)
            {
                switch (valueToSupply.Key.unit)
                {
                    case "fixed":
                        valuesForCompanionModels[valueToSupply.Key] = 1;
                        break;
                    case "per kg collected":
                        valuesForCompanionModels[valueToSupply.Key] = amountToDo;
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
                amountToSkip = Convert.ToInt32(amountToDo * (1 - tagsShort.Available / tagsShort.Required));
                if (amountToSkip < 0)
                {
                    Status = ActivityStatus.Warning;
                    AddStatusMessage("Resource shortfall prevented any action");
                }
            }
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            if (amountToDo > 0)
            {
                pasture.Remove(new ResourceRequest()
                {
                    ActivityModel = this,
                    AdditionalDetails = this,
                    Category = TransactionCategory,
                    Required = amountToDo - amountToSkip,
                    Resource = pasture
                });
                AmountAvailableForHarvest = amountToDo;
                AmountHarvested = amountToDo - amountToSkip;

                FoodResourcePacket packet = new FoodResourcePacket()
                {
                    Amount = amountToDo - amountToSkip,
                    PercentN = pasture.Nitrogen,
                    DMD = pasture.EstimateDMD(pasture.Nitrogen)
                };

                foodstore.Add(packet, this, null, TransactionCategory);
                limiter.AddWeightCarried(amountToDo - amountToSkip);
                SetStatusSuccessOrPartial(amountToSkip > 0);
            }
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write("\r\n<div class=\"activityentry\">");
                htmlWriter.Write($"Cut {CLEMModel.DisplaySummaryValueSnippet(Supply, warnZero:true)}");
                switch (CutStyle)
                {
                    case RuminantFeedActivityTypes.SpecifiedDailyAmount:
                        htmlWriter.Write(" kg ");
                        break;
                    case RuminantFeedActivityTypes.ProportionOfWeight:
                        htmlWriter.Write(" of herd <span class=\"setvalue\">live weight</span> ");
                        break;
                    case RuminantFeedActivityTypes.ProportionOfPotentialIntake:
                        htmlWriter.Write(" of herd <span class=\"setvalue\">potential intake</span> ");
                        break;
                    case RuminantFeedActivityTypes.ProportionOfRemainingIntakeRequired:
                        htmlWriter.Write(" of herd <span class=\"setvalue\">remaining intake required</span> ");
                        break;
                    default:
                        break;
                }

                htmlWriter.Write("from ");
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(PaddockName, "Pasture not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write(" and carry to ");
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(AnimalFoodStoreName, "Store not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write("</div>");
                return htmlWriter.ToString();
            }
        }
        #endregion

    }
}
