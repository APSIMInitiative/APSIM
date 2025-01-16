using Models.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Models.CLEM.Resources;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using Models.Core.Attributes;
using System.IO;
using APSIM.Shared.Utilities;
using Models.CLEM.Reporting;
using Models.CLEM.Groupings;

namespace Models.CLEM.Activities
{
    /// <summary>Ruminant grazing activity</summary>
    /// <summary>Specific version where pasture and breed is specified</summary>
    /// <summary>This activity determines how a ruminant breed will graze on a particular pasture (GrazeFoodSotreType)</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Performs grazing of a specified herd and pasture (paddock)")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantGraze.htm")]
    class RuminantActivityGrazePastureHerd : CLEMRuminantActivityBase, IValidatableObject
    {
        /// <summary>
        /// Link to clock
        /// Public so children can be dynamically created after links defined
        /// </summary>
        [Link]
        public IClock Clock = null;

        private DateTime lastResourceRequest = new DateTime();
        private double totalPastureRequired = 0;
        private double totalPastureDesired = 0;
        private FoodResourcePacket foodDetails = new FoodResourcePacket();
        private ResourceRequest pastureRequest = null;
        private double shortfallReportingCutoff = 0.01;
        private bool isStandAloneModel = false;

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

        /// <summary>
        /// Ruminant group to graze
        /// </summary>
        [Description("Ruminant type to graze")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Ruminant Type required")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { typeof(RuminantHerd) } })]
        public string RuminantTypeName { get; set; }

        /// <summary>
        /// Ruminant group to graze
        /// </summary>
        [JsonIgnore]
        public RuminantType RuminantTypeModel { get; set; }

        /// <summary>
        /// The proportion of required graze that is available determined from parent activity arbitration
        /// </summary>
        [JsonIgnore]
        public double GrazingCompetitionLimiter { get; set; }

        /// <summary>
        /// The biomass of pasture per hectare at start of allocation
        /// </summary>
        [JsonIgnore]
        public double BiomassPerHectare { get; set; }

        /// <summary>
        /// Potential intake limiter based on pasture quality
        /// </summary>
        [JsonIgnore]
        public double PotentialIntakePastureQualityLimiter { get; set; }

        /// <summary>
        /// Potential intake limiter based on the biomass of available pasture
        /// </summary>
        [JsonIgnore]
        public double PotentialIntakePastureBiomassLimiter { get; set; }

        /// <summary>
        /// Potential intake limiter based on the proprtion of 8 hours grazing allowed
        /// </summary>
        [JsonIgnore]
        public double PotentialIntakeGrazingTimeLimiter { get; set; }

        /// <summary>
        /// Potential intake limit
        /// </summary>
        [JsonIgnore]
        public double PotentialIntakeLimit
        {
            get { return PotentialIntakePastureQualityLimiter * PotentialIntakePastureBiomassLimiter * PotentialIntakeGrazingTimeLimiter * GrazingCompetitionLimiter; }
        }

        /// <summary>
        /// Dry matter digestibility of pasture consumed (%)
        /// </summary>
        [JsonIgnore]
        public double DMD { get; set; }

        /// <summary>
        /// Nitrogen of pasture consumed (%)
        /// </summary>
        [JsonIgnore]
        public double N { get; set; }

        /// <summary>
        /// Proportion of intake that can be taken from each pool
        /// </summary>
        [JsonIgnore]
        public List<GrazeBreedPoolLimit> PoolFeedLimits { get; set; }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            // This method will only fire if the user has added this activity to the UI
            // Otherwise all details will be provided from GrazeAll or GrazePaddock code [CLEMInitialiseActivity]

            isStandAloneModel = true;

            // add ruminant activity filter group to ensure correct individuals are selected
            RuminantActivityGroup herdGroup = new()
            {
                Name = $"Filter_{RuminantTypeName}"
            };
            herdGroup.Children.Add(
                new FilterByProperty()
                {
                    PropertyOfIndividual = "HerdName",
                    Operator = System.Linq.Expressions.ExpressionType.Equal,
                    Value = RuminantTypeName
                }
            );
            this.Children.Add(herdGroup);

            this.InitialiseHerd(false, false);

            // if no settings have been provided from parent set limiter to 1.0. i.e. no limitation
            if (MathUtilities.FloatsAreEqual(GrazingCompetitionLimiter, 0))
                GrazingCompetitionLimiter = 1.0;

            GrazeFoodStoreModel = Resources.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreTypeName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
            RuminantTypeModel = Resources.FindResourceType<RuminantHerd, RuminantType>(this, RuminantTypeName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
        }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMValidate")]
        private void OnFinalInitialise(object sender, EventArgs e)
        {
            shortfallReportingCutoff = FindInScope<ReportResourceShortfalls>()?.PropPastureShortfallOfDesiredIntake??0.02;

            // if this is the last of newly added models that will be set to hidden
            // reset the simulation subscriptions to correct the new order before running the simulation.
            if (IsHidden)
            {
                Events events = new Events(FindAncestor<Simulation>());
                //events.DisconnectEvents();
                events.ReconnectEvents("Models.Clock", "CLEMGetResourcesRequired");
            }
        }

        /// <summary>An event handler to allow us to clear requests at start of month.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("StartOfMonth")]
        private void OnStartOfMonth(object sender, EventArgs e)
        {
            ResourceRequestList = null;
            this.PoolFeedLimits = null;
            PotentialIntakeGrazingTimeLimiter = HoursGrazed / 8;
        }

        /// <summary>
        /// Calculate the potential intake limiter based on pasture quality.
        /// </summary>
        /// <returns>Limiter as proportion</returns>
        public double CalculatePotentialIntakePastureQualityLimiter()
        {
            // determine pasture quality from all pools (DMD) at start of grazing
            double pastureDMD = GrazeFoodStoreModel.DMD;
            // Reduce potential intake based on pasture quality for the proportion consumed (zero legume).
            // TODO: check that this doesn't need to be performed for each breed based on how pasture taken
            // this will still occur when grazing on improved, irrigated or crops.
            // CLEM does not allow grazing on two pastures in the month, whereas NABSA allowed irrigated pasture and supplemented with native for remainder needed.
            if (MathUtilities.IsGreaterThanOrEqual(0.8 - GrazeFoodStoreModel.IntakeTropicalQualityCoefficient - pastureDMD / 100, 0))
                return 1 - GrazeFoodStoreModel.IntakeQualityCoefficient * (0.8 - GrazeFoodStoreModel.IntakeTropicalQualityCoefficient - pastureDMD / 100);
            else
                return 1;
        }

        /// <summary>
        /// Method to allow another activity to request the activity determines it's resources
        /// </summary>
        public List<ResourceRequest> RequestDetermineResources()
        {
            return RequestResourcesForTimestep();
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            // if this is the first time of this request not being partially managed by a RuminantActivityGradePaddock work independently
            if (lastResourceRequest != Clock.Today)
            {
                ResourceRequestList = new List<ResourceRequest>();
                pastureRequest = null;
                IEnumerable<Ruminant> herd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Location == this.GrazeFoodStoreModel.Name && a.HerdName == this.RuminantTypeModel.Name);

                totalPastureRequired = 0;
                totalPastureDesired = 0;
                Status = ActivityStatus.NotNeeded;
                PotentialIntakePastureBiomassLimiter = 1;

                if (herd.Any())
                {
                    // Stand alone model has not been set by parent RuminantActivityGrazePasture
                    if (isStandAloneModel)
                    {
                        SetupPoolsAndLimits(1.0);
                        PotentialIntakePastureQualityLimiter = CalculatePotentialIntakePastureQualityLimiter();
                    }

                    PotentialIntakePastureBiomassLimiter = 1 - Math.Exp(-herd.FirstOrDefault().BreedParams.IntakeCoefficientBiomass * this.GrazeFoodStoreModel.TonnesPerHectareStartOfTimeStep * 1000);

                    // get list of all Ruminants of specified breed in this paddock
                    foreach (Ruminant ind in herd)
                    {
                        if (ind.Weaned)
                        {
                            // Reduce potential intake (monthly) based on pasture quality for the proportion consumed calculated in GrazePasture.
                            // calculate intake from potential modified by pasture availability and hours grazed
                            // min of grazed and potential remaining
                            totalPastureRequired += Math.Min(Math.Max(0, ind.PotentialIntake - ind.Intake), ind.PotentialIntake * PotentialIntakePastureQualityLimiter * PotentialIntakePastureBiomassLimiter * PotentialIntakeGrazingTimeLimiter);
                            // potential graing minus low biomass limiter
                            totalPastureDesired += Math.Min(Math.Max(0, ind.PotentialIntake - ind.Intake), ind.PotentialIntake * PotentialIntakePastureQualityLimiter * PotentialIntakeGrazingTimeLimiter);
                        }
                        else
                        {
                            // treat sucklings separate
                            // potentialIntake defined based on proportion of body weight and MilkLWTFodderSubstitutionProportion when milk intake is low or missing (lost mother) (see RuminantActivityGrow.CalculatePotentialIntake)
                            // they can eat defined potential intake minus what's already been fed. Milk intake assumed elsewhere.
                            double amountToEat = Math.Max(0, ind.PotentialIntake - ind.Intake);
                            totalPastureRequired += amountToEat;
                            // desired same as required
                            // TODO: check with researchers, but this should also include the PastureQuality, PastureBiomass and GrazingTime limiters
                            totalPastureDesired += amountToEat;
                        }
                    }
                    if (MathUtilities.IsPositive(totalPastureRequired))
                    {
                        pastureRequest = new ResourceRequest()
                        {
                            AllowTransmutation = false,
                            Required = totalPastureRequired,
                            Resource = GrazeFoodStoreModel,
                            ResourceType = typeof(GrazeFoodStore),
                            ResourceTypeName = this.GrazeFoodStoreModel.Name,
                            ActivityModel = this,
                            AdditionalDetails = this,
                            Category = TransactionCategory,
                            RelatesToResource = this.RuminantTypeModel.Name
                        };
                        ResourceRequestList.Add(pastureRequest);
                    }
                }
                lastResourceRequest = Clock.Today;
                return ResourceRequestList;
            }
            return null;
        }

        /// <inheritdoc/>
        protected override void AdjustResourcesForTimestep()
        {
            if (pastureRequest != null && MathUtilities.IsLessThan(Math.Round(GrazingCompetitionLimiter,4), 1))
            {
                // reduce the amount provided by the grazing competition limiter
                // accounts for reduction based on other herds of ruminants in the paddock
                ResourceRequestList.Where(a => a.Resource is GrazeFoodStoreType).FirstOrDefault().Required *= GrazingCompetitionLimiter; ;
            }
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            // Go through amount received and put it into the animals intake with quality measures.
            // get resource list, handles if already called by parent.
            RequestDetermineResources();

            if (MathUtilities.IsPositive(totalPastureRequired))
            {
                IEnumerable<Ruminant> herd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Location == this.GrazeFoodStoreModel.Name && a.HerdName == this.RuminantTypeModel.Name);

                // shortfall already takes into account competition (AdjustResourcesForActivity) and availability
                double shortfall = (pastureRequest?.Provided??0) / totalPastureRequired;

                // allocate to individuals in proportion to what they requested

                // current DMD and N of intake is stored in th DMD and N properties of this class as passed to GrazeFoodStoreType.Remove as AdditionalDetails with breed pool limits
                foodDetails = new FoodResourcePacket()
                {
                    DMD = DMD,
                    PercentN = N
                };

                foreach (Ruminant ind in herd)
                {
                    double eaten;
                    if (ind.Weaned)
                        eaten = Math.Min(Math.Max(0,ind.PotentialIntake - ind.Intake), ind.PotentialIntake * PotentialIntakePastureQualityLimiter * (1 - Math.Exp(-ind.BreedParams.IntakeCoefficientBiomass * this.GrazeFoodStoreModel.TonnesPerHectareStartOfTimeStep * 1000)) * (HoursGrazed / 8));
                    else
                        eaten = Math.Max(0, ind.PotentialIntake - ind.Intake); ;

                    foodDetails.Amount = eaten * shortfall;
                    ind.AddIntake(foodDetails);
                }
                Status = ActivityStatus.Success;

                if (MathUtilities.IsLessThan(shortfall, 1) || MathUtilities.IsGreaterThan(totalPastureDesired - (pastureRequest?.Provided??0), totalPastureDesired* shortfallReportingCutoff))
                {
                    ResourceRequest shortfallRequest = pastureRequest;
                    shortfallRequest.Required = totalPastureRequired;
                    if (shortfallRequest is null)
                    {
                        shortfallRequest = new ResourceRequest()
                        {
                            Available = pastureRequest?.Provided??0, // display all that was given
                            Required = totalPastureRequired,
                            ResourceType = typeof(GrazeFoodStore),
                            ResourceTypeName = GrazeFoodStoreModel.Name
                        };
                    }
                    if (MathUtilities.IsLessThan(shortfall, 1))
                    {
                        this.Status = ((pastureRequest?.Provided ?? 0) == 0) ? ActivityStatus.Warning : ActivityStatus.Partial;
                        shortfallRequest.ShortfallStatus = "BelowRequired";
                    }
                    else
                    {
                        if ((pastureRequest?.Provided ?? 0) == 0)
                            Status = ActivityStatus.Warning;
                        shortfallRequest.Required = totalPastureDesired;
                        shortfallRequest.ShortfallStatus = "BelowDesired";
                    }

                    ActivitiesHolder.ReportActivityShortfall(new ResourceRequestEventArgs() { Request = shortfallRequest });

                    // only allow the stop error if this is a shortfall in required not desired.
                    if (MathUtilities.IsLessThan(Math.Round(shortfall, 4), 1) && this.OnPartialResourcesAvailableAction == OnPartialResourcesAvailableActionTypes.ReportErrorAndStop)
                        throw new ApsimXException(this, $"Insufficient pasture available for grazing in paddock ({GrazeFoodStoreModel.Name}) in {Clock.Today:dd\\yyyy}");
                }
            }
        }

        /// <summary>
        /// Method to set up pools from currently available graze pools and limit based upon green content herd limit parameters
        /// </summary>
        /// <param name="limit">The competition limit defined from GrazePasture parent</param>
        public void SetupPoolsAndLimits(double limit)
        {
            this.GrazingCompetitionLimiter = limit;
            // store kg/ha available for consumption calculation
            this.BiomassPerHectare = GrazeFoodStoreModel.KilogramsPerHa;

            // calculate breed feed limits
            if (this.PoolFeedLimits == null)
                this.PoolFeedLimits = new List<GrazeBreedPoolLimit>();
            else
                this.PoolFeedLimits.Clear();

            foreach (var pool in GrazeFoodStoreModel.Pools)
                this.PoolFeedLimits.Add(new GrazeBreedPoolLimit() { Limit = 1.0, Pool = pool });

            // if Jan-March then use first three months otherwise use 2
            int greenage = (Clock.Today.Month <= 3) ? 2 : 1;

            double green = GrazeFoodStoreModel.Pools.Where(a => (a.Age <= greenage)).Sum(b => b.Amount);
            double propgreen = green / GrazeFoodStoreModel.Amount;

            // All values are now proportions.
            // Convert to percentage before calculation

            double greenlimit = (this.RuminantTypeModel.GreenDietMax * 100) * (1 - Math.Exp(-this.RuminantTypeModel.GreenDietCoefficient * ((propgreen * 100) - (this.RuminantTypeModel.GreenDietZero * 100))));
            greenlimit = Math.Max(0.0, greenlimit);
            if (MathUtilities.IsGreaterThan(propgreen, 0.9))
                greenlimit = 100;

            foreach (var pool in this.PoolFeedLimits.Where(a => a.Pool.Age <= greenage))
                pool.Limit = greenlimit / 100.0;

            // order feedpools by age so that diet is taken from youngest greenest first
            this.PoolFeedLimits = this.PoolFeedLimits.OrderBy(a => a.Pool.Age).ToList();
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
                htmlWriter.Write("\r\n<div class=\"activityentry\">All individuals of ");
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(RuminantTypeName, "Herd not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write(" in ");
                htmlWriter.Write(CLEMModel.DisplaySummaryValueSnippet(GrazeFoodStoreTypeName, "Pasture not set", HTMLSummaryStyle.Resource));
                htmlWriter.Write(" will graze for ");
                htmlWriter.Write("\r\n<div class=\"activityentry\">All individuals in managed pastures will graze for ");
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
