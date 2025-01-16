using Models.Core;
using Models.CLEM.Groupings;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Models.Core.Attributes;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using Models.CLEM.Interfaces;
using APSIM.Shared.Utilities;

namespace Models.CLEM.Activities
{
    /// <summary>Ruminant herd management activity</summary>
    /// <summary>This activity will maintain a breeding herd at the desired levels of age/breeders etc</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyCategorisedView")]
    [PresenterName("UserInterface.Presenters.PropertyCategorisedPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Overall management of ruminant numbers with multiple management actions")]
    [Version(1, 2, 0, "Implements event based activity control")]
    [Version(1, 1, 1, "Improved custom filtering of task individuals")]
    [Version(1, 1, 0, "Allow all tasks to be controlled and cleaned up logic")]
    [Version(1, 0, 10, "Allows control of the order individuals are identified for removal and keeping")]
    [Version(1, 0, 9, "Allows details of breeders and sires for purchase to be specified")]
    [Version(1, 0, 8, "Reworking of rules to better allow small herd management")]
    [Version(1, 0, 7, "Added ability to turn on/off marking max age breeders and sires and age/weight males for sale and allow this action in other activities")]
    [Version(1, 0, 6, "Allow user to specify individuals that should be sold to reduce herd before young emales taken")]
    [Version(1, 0, 5, "Renamed all 'bulls' to 'sires' in properties. Requires resetting of values")]
    [Version(1, 0, 4, "Allow sires to be placed in different pasture to breeders")]
    [Version(1, 0, 3, "Allows herd to be adjusted to sires and max breeders kept at startup")]
    [Version(1, 0, 2, "Implements minimum breeders kept to define breeder purchase limits")]
    [Version(1, 0, 1, "First implementation of this activity using IAT/NABSA processes")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantManage.htm")]
    public class RuminantActivityManage : CLEMRuminantActivityBase, IValidatableObject, IHandlesActivityCompanionModels
    {
        private int maxBreeders;
        private int minBreeders;
        private int femaleBreedersRequired = 0;
        private int maleBreedersRequired = 0;
        private string grazeStoreSires = "";
        private string grazeStoreBreeders = "";
        private string grazeStoreGrowOutFemales = "";
        private string grazeStoreGrowOutMales = "";
        private GrazeFoodStoreType foodStoreSires;
        private GrazeFoodStoreType foodStoreBreeders;
        private RuminantType breedParams;
        private IEnumerable<SpecifiedRuminantListItem> purchaseDetails;
        private double mortalityRate = 0;
        private IEnumerable<Ruminant> selectHerdAvailable = null;

        private int numberMaleSiresInHerd = 0;
        private int numberMaleSiresInPurchases = 0;
        private int numberFemaleInPurchases = 0;
        private int numberFemalePreBreedersInHerd = 0;
        private int siresPresent = 0;
        private int numberFemaleBreedingInHerd = 0;
        private int excessBreeders = 0;
        private bool sufficientFoodBreeders = true;
        private bool sufficientFoodSires = true;

        /// <summary>
        /// Manage female breeder numbers
        /// </summary>
        [Category("Task:Herd size", "Breeding females:Breeding females")]
        [Description("Manage female breeder numbers")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool ManageFemaleBreederNumbers { get; set; }

        /// <summary>
        /// Perform female destocking (sales) tasks
        /// </summary>
        [Category("Task:Destock", "Breeding females:Breeding females")]
        [Description("Perform female destocking (sale) tasks")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool PerformFemaleDestocking { get; set; }

        /// <summary>
        /// Perform breeder stocking (purchases) tasks
        /// </summary>
        [Category("Task:Restock", "Breeding females:Breeding females")]
        [Description("Perform female stocking (purchase) tasks")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool PerformFemaleStocking { get; set; }

        /// <summary>
        /// Perform male destocking (sales) tasks
        /// </summary>
        [Description("Perform male destocking (sale) tasks")]
        [Category("Task:Destock", "Breeding males:Breeding males")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool PerformMaleDestocking { get; set; }

        /// <summary>
        /// Perform male stocking (purchases) tasks
        /// </summary>
        [Category("Task:Restock", "Breeding males:Breeding males")]
        [Description("Perform male stocking (purchase) tasks")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool PerformMaleStocking { get; set; }

        /// <summary>
        /// Maximum number of breeders that can be kept
        /// </summary>
        [Category("Herd size", "Breeding females")]
        [Description("Maximum number of female breeders to be kept")]
        [Core.Display(EnabledCallback = "EnableFemaleNumberProperties")]
        [Required, GreaterThanEqualValue(0)]
        public int MaximumBreedersKept { get; set; } 

        /// <summary>
        /// Minimum number of breeders that can be kept
        /// </summary>
        [Category("Herd size", "Breeding females")]
        [Description("Minimum number of female breeders to be kept")]
        [Core.Display(EnabledCallback = "EnableFemaleNumberProperties")]
        [Required, GreaterThanEqualValue(0)]
        public int MinimumBreedersKept { get; set; }

        /// <summary>
        /// Stop model if breeder herd exceeds maximum breeders times this multiplier
        /// </summary>
        [Category("Herd size", "Breeding females")]
        [Description("Stop model max breeders multiplier")]
        [System.ComponentModel.DefaultValueAttribute(2)]
        [Required, GreaterThanValue(0)]
        public double MaxBreedersMultiplierToStop { get; set; }

        /// <summary>
        /// Include the marking for sale of old breeders in this activity
        /// </summary>
        [Category("Task:Destock", "Breeding females:Breeding females")]
        [Description("Mark old breeding females for sale")]
        [Core.Display(EnabledCallback = "EnableDestockFemaleProperties")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool MarkOldBreedersForSale { get; set; }

        /// <summary>
        /// Maximum breeder age (months) for removal
        /// </summary>
        [Category("Destock", "Breeding females")]
        [Description("Maximum female breeder age (months) before removal")]
        [Required, GreaterThanEqualValue(0)]
        [System.ComponentModel.DefaultValueAttribute(120)]
        [Core.Display(EnabledCallback = "EnableOldFemaleSellProperties")]
        public double MaximumBreederAge { get; set; }

        /// <summary>
        /// Proportion of min breeders in single purchase
        /// </summary>
        [Category("Restock", "Breeding females")]
        [Description("Proportion of min female breeders in single purchase")]
        [System.ComponentModel.DefaultValueAttribute(1)]
        [Required, Proportion, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableStockFemaleProperties")]
        public double MaximumProportionBreedersPerPurchase { get; set; }

        /// <summary>
        /// Proportion of min breeders in single purchase
        /// </summary>
        [Category("Restock", "Breeding females")]
        [Description("Retain pregnant MaxAge individuals if short of breeders")]
        [System.ComponentModel.DefaultValueAttribute(false)]
        [Core.Display(EnabledCallback = "EnableStockFemaleProperties")]
        public bool ReturnPregnantMaxAgeToHerd { get; set; }

        /// <summary>
        /// Retain female replacement breeders marked for sale
        /// </summary>
        [Category("Restock", "Breeding females")]
        [Description("Retain female replacement breeders marked for sale")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        [Core.Display(EnabledCallback = "EnableStockFemaleProperties")]
        public bool RetainFemaleReplacementBreedersFromSaleHerd { get; set; }

        /// <summary>
        /// Manage male breeder numbers
        /// </summary>
        [Category("Task:Herd size", "Breeding males:Breeding males")]
        [Description("Manage male breeder numbers")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        public bool ManageMaleBreederNumbers { get; set; }

        /// <summary>
        /// Maximum number of breeding sires kept
        /// </summary>
        [Category("Herd size", "Breeding males")]
        [Description("Maximum number of male breeders kept")]
        [Required, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableMaleNumberProperties")]
        public double MaximumSiresKept { get; set; }

        /// <summary>
        /// Calculated sires kept
        /// </summary>
        [JsonIgnore]
        public int SiresKept { get; set; }

        /// <summary>
        /// Include the marking for sale of sires in this activity
        /// </summary>
        [Category("Task:Destock", "Breeding males:Breeding males")]
        [Description("Mark old sires for sale")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        [Core.Display(EnabledCallback = "EnableDestockMaleProperties")]
        public bool MarkOldSiresForSale { get; set; }

        /// <summary>
        /// Maximum sire age (months) for removal
        /// </summary>
        [Category("Destock", "Breeding males")]
        [Description("Maximum sire age (months) before removal")]
        [Required, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableOldMaleSellProperties")]
        [System.ComponentModel.DefaultValueAttribute(120)]
        public double MaximumSireAge { get; set; }

        /// <summary>
        /// Allow natural herd replacement of sires
        /// </summary>
        [Category("Restock", "Breeding males")]
        [Description("Allow sire replacement from herd")]
        [Core.Display(EnabledCallback = "EnableStockMaleProperties")]
        [Required]
        public bool AllowSireReplacement { get; set; }

        /// <summary>
        /// Set sire herd to purchase relative to proportion of breeder herd present
        /// </summary>
        [Category("Restock", "Breeding males")]
        [Description("Restock sire numbers relative to proportion of breeders")]
        [Required]
        [Core.Display(EnabledCallback = "EnableStockMaleProperties")]
        public bool RestockSiresRelativeToBreeders { get; set; }

        /// <summary>
        /// Maximum number of sires in a single purchase
        /// </summary>
        [Category("Restock", "Breeding males")]
        [Description("Maximum number of male breeders in a single purchase")]
        [Required, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableStockMaleProperties")]
        public int MaximumSiresPerPurchase { get; set; }

        /// <summary>
        /// Retain male replacement breeders marked for sale
        /// </summary>
        [Category("Restock", "Breeding males")]
        [Description("Retain male replacement breeders marked for sale")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        [Core.Display(EnabledCallback = "EnableStockMaleProperties")]
        public bool RetainMaleReplacementBreedersFromSaleHerd { get; set; }

        /// <summary>
        /// Perfrom growing out of males
        /// </summary>
        [Category("Task:Grow out herd", "Grow out herd:Males")]
        [Description("Perform growing out of young males")]
        [Required]
        public bool GrowOutYoungMales { get; set; }

        /// <summary>
        /// Identify males for sale every time step
        /// </summary>
        [Category("Grow out herd", "General")]
        [Description("Mark those reaching age/weight for sale every time step")]
        [Core.Display(EnabledCallback = "EnableGrowoutProperties")]
        [Required]
        public bool ContinuousGrowOutSales { get; set; }

        /// <summary>
        /// Include the marking for sale of males reaching age or weight
        /// </summary>
        [Category("Task:Grow out herd", "Grow out herd:Males")]
        [Description("Mark grow out males for sale")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        [Core.Display(EnabledCallback = "EnableGrowoutMaleProperties")]
        public bool MarkAgeWeightMalesForSale { get; set; }

        /// <summary>
        /// Castrate grow out males (steers, bullocks)
        /// </summary>
        [Category("Task:Grow out herd", "Grow out herd:Males")]
        [Description("Castrate young males")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        [Core.Display(EnabledCallback = "EnableGrowoutMaleProperties")]
        public bool CastrateMales { get; set; }

        /// <summary>
        /// Male selling age (months)
        /// </summary>
        [Category("Grow out herd", "Males")]
        [Description("Grow out male selling age (months)")]
        [System.ComponentModel.DefaultValueAttribute(24)]
        [Required, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableGrowoutMaleSellProperties")]
        public double MaleSellingAge { get; set; }

        /// <summary>
        /// Male selling weight (kg)
        /// </summary>
        [Category("Grow out herd", "Males")]
        [Description("Grow out male selling weight (kg)")]
        [Required, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableGrowoutMaleSellProperties")]
        public double MaleSellingWeight { get; set; }

        /// <summary>
        /// Perform selling of young females the same as males
        /// </summary>
        [Category("Task:Grow out herd", "Grow out herd:Females")]
        [Description("Perform growing out of young females")]
        [Required]
        public bool GrowOutYoungFemales { get; set; }

        /// <summary>
        /// Female selling age (months)
        /// </summary>
        [Category("Grow out herd", "Females")]
        [Description("Grow out female selling age (months)")]
        [System.ComponentModel.DefaultValueAttribute(24)]
        [Required, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableGrowoutFemaleSellProperties")]
        public double FemaleSellingAge { get; set; }

        /// <summary>
        /// Female selling weight (kg)
        /// </summary>
        [Category("Grow out herd", "Females")]
        [Description("Grow out female selling weight (kg)")]
        [Required, GreaterThanEqualValue(0)]
        [Core.Display(EnabledCallback = "EnableGrowoutFemaleSellProperties")]
        public double FemaleSellingWeight { get; set; }

        /// <summary>
        /// Include the marking for sale of females reaching age or weight
        /// </summary>
        [Category("Task:Grow out herd", "Grow out herd:Females")]
        [Description("Mark grow out females for sale")]
        [System.ComponentModel.DefaultValueAttribute(true)]
        [Core.Display(EnabledCallback = "EnableGrowoutFemaleProperties")]
        public bool MarkAgeWeightFemalesForSale { get; set; }

        /// <summary>
        /// GrazeFoodStore (paddock) to place purchased sires in for grazing
        /// </summary>
        [Category("Restock", "Pasture")]
        [Description("GrazeFoodStore (paddock) to place purchased sires in")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { "Not specified - general yards", typeof(GrazeFoodStore) } }, EnabledCallback = "EnableStockMaleProperties")]
        [System.ComponentModel.DefaultValue("Not specified - general yards")]
        public string GrazeFoodStoreNameSires { get; set; }

        /// <summary>
        /// GrazeFoodStore (paddock) to place purchased breeders in for grazing
        /// </summary>
        [Category("Restock", "Pasture")]
        [Description("GrazeFoodStore (paddock) to place purchased breeders in")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { "Not specified - general yards", typeof(GrazeFoodStore) } }, EnabledCallback = "EnableStockFemaleProperties")]
        [System.ComponentModel.DefaultValue("Not specified - general yards")]
        public string GrazeFoodStoreNameBreeders { get; set; }

        /// <summary>
        /// GrazeFoodStore (paddock) to place grow out heifers in for grazing
        /// </summary>
        [Category("Grow out herd", "Pasture")]
        [Description("GrazeFoodStore (paddock) to place grow out females in")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { "Not specified - general yards", typeof(GrazeFoodStore) } }, EnabledCallback = "EnableGrowoutFemaleProperties")]
        [System.ComponentModel.DefaultValue("Not specified - general yards")]
        public string GrazeFoodStoreNameGrowOutFemales { get; set; }

        /// <summary>
        /// GrazeFoodStore (paddock) to place grow out young males in for grazing
        /// </summary>
        [Category("Grow out herd", "Pasture")]
        [Description("GrazeFoodStore (paddock) to place grow out males in")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new object[] { "Not specified - general yards", typeof(GrazeFoodStore) } }, EnabledCallback = "EnableGrowoutMaleProperties")]
        [System.ComponentModel.DefaultValue("Not specified - general yards")]
        public string GrazeFoodStoreNameGrowOutMales { get; set; }

        /// <summary>
        /// Minimum pasture (kg/ha) before restocking if placed in paddock
        /// </summary>
        [Category("Restock", "Pasture")]
        [Description("Minimum pasture (kg/ha) before restocking if placed in paddock")]
        [Required, Range(0, double.MaxValue, ErrorMessage = "Value must be a greter than or equal to 0")]
        [Core.Display(EnabledCallback = "EnableStockProperties")]
        public double MinimumPastureBeforeRestock { get; set; }

        /// <summary>
        /// Adjust breeding females up to required amount at start-up
        /// </summary>
        [Category("Task:Start up", "Start up:Breeding females")]
        [Description("Adjust breeding female cohorts at start-up")]
        public bool AdjustBreedingFemalesAtStartup { get; set; }

        /// <summary>
        /// Adjust all non breeding females in proportion to breeding females at start-up
        /// </summary>
        [Category("Task:Start up", "Start up:Breeding females")]
        [Description("Adjust all cohorts in proportion to breeding females at start-up")]
        [Core.Display(EnabledCallback = "PerformingFemaleBreederAdjustment")]
        public bool AdjustOthersToFemaleBreedersPropAtStartup { get; set; }

        /// <summary>
        /// Adjust breeding males up to required amount at start-up
        /// </summary>
        [Category("Task:Start up", "Start up:Breeding males")]
        [Description("Adjust breeding sire cohorts at start-up")]
        public bool AdjustBreedingMalesAtStartup { get; set; }

        /// <summary>
        /// Determine where Female breeder number properties should be displayed
        /// </summary>
        public bool EnableFemaleNumberProperties() { return ManageFemaleBreederNumbers && (PerformFemaleDestocking || PerformFemaleStocking); }

        /// <summary>
        /// Determine where Female breeder properties for destocking should be displayed
        /// </summary>
        public bool EnableDestockFemaleProperties() { return ManageFemaleBreederNumbers && PerformFemaleDestocking; }

        /// <summary>
        /// Determine where sell old Female breeder properties should be displayed
        /// </summary>
        public bool EnableOldFemaleSellProperties() { return EnableDestockFemaleProperties() & MarkOldBreedersForSale; }

        /// <summary>
        /// Determine where Female breeder number properties for stocking should be displayed
        /// </summary>
        public bool EnableStockFemaleProperties() { return ManageFemaleBreederNumbers && PerformFemaleStocking; }

        /// <summary>
        /// Determine where Male breeder number properties should be displayed
        /// </summary>
        public bool EnableMaleNumberProperties() { return ManageMaleBreederNumbers && (PerformMaleDestocking || PerformMaleStocking); }

        /// <summary>
        /// Determine where Male breeder properties for destocking should be displayed
        /// </summary>
        public bool EnableDestockMaleProperties() { return ManageMaleBreederNumbers && PerformMaleDestocking; }

        /// <summary>
        /// Determine where sell old Male breeder properties should be displayed
        /// </summary>
        public bool EnableOldMaleSellProperties() { return EnableDestockMaleProperties() & MarkOldSiresForSale; }

        /// <summary>
        /// Determine where Male breeder number properties for stocking should be displayed
        /// </summary>
        public bool EnableStockMaleProperties() { return ManageMaleBreederNumbers && PerformMaleStocking; }

        /// <summary>
        /// Determine where Male breeder number properties for stocking should be displayed
        /// </summary>
        public bool EnableStockProperties() { return EnableStockFemaleProperties() | EnableStockMaleProperties(); }

        /// <summary>
        /// Determine where Male grow out properties should be displayed
        /// </summary>
        public bool EnableGrowoutMaleProperties() { return GrowOutYoungMales; }

        /// <summary>
        /// Determine where Female grow out properties should be displayed
        /// </summary>
        public bool EnableGrowoutFemaleProperties() { return GrowOutYoungFemales; }

        /// <summary>
        /// Determine where Male grow out properties should be displayed
        /// </summary>
        public bool EnableGrowoutMaleSellProperties() { return MarkAgeWeightMalesForSale & GrowOutYoungMales; }

        /// <summary>
        /// Determine where Female grow out properties should be displayed
        /// </summary>
        public bool EnableGrowoutFemaleSellProperties() { return MarkAgeWeightFemalesForSale & GrowOutYoungFemales; }

        /// <summary>
        /// Determine where Female grow out properties should be displayed
        /// </summary>
        public bool EnableGrowoutProperties() { return GrowOutYoungFemales | GrowOutYoungMales; }

        /// <summary>
        /// Determine where Female grow out properties should be displayed
        /// </summary>
        public bool PerformingFemaleBreederAdjustment() { return AdjustBreedingFemalesAtStartup; }

        /// <summary>
        /// Constructor
        /// </summary>
        public RuminantActivityManage()
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
                        identifiers: new List<string>() {
                            "RemoveOldFemalesFromHerd",
                            "RemoveOldSiresFromHerd",
                            "RemoveSiresFromPurchases",
                            "RemoveBreedersFromPurchases",
                            "RemoveSiresFromHerd",
                            "RemoveBreedersFromHerd",
                            "RemoveFemaleReplacementBreeders",
                            "SelectSiresFromSales",
                            "SelectYoungMalesFromSales",
                            "SelectYoungFemalesFromSales",
                            "SelectBreedersFromSales",
                            "SelectYoungMalesFromGrowOut",
                            "SelectYoungFemalesFromGrowOut",
                            "SelectMalesForGrowOut",
                            "SelectFemalesForGrowOut",
                            "SelectUnweanedFemalesAsReplacements"
                        },
                        measures: new List<string>()
                        );
                case "ActivityFee":
                case "LabourRequirement":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>() {
                            "Herd size - before activity",
                            "Adjust - new grow out males",
                            "Adjust - new grow out females",
                            "Destock - grow out males",
                            "Destock - grow out females",
                            "Destock - old sires",
                            "Destock - old female breeders",
                            "Destock - excess female breeders",
                            "Destock - excess sires",
                            "Stock - female breeder purchases",
                            "Stock - sire purchases",
                        },
                        measures: new List<string>() {
                            "fixed",
                            "per head",
                        }
                        );
                default:
                    return new LabelsForCompanionModels();
            }
        }

        /// <summary>An event handler to allow us to reset initial cohort sizes before crearting the herd.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("StartOfSimulation")]
        private new void OnStartOfSimulation(object sender, EventArgs e)
        {
            // reset min breeders if set greater than max breeders
            // this allows multi run versions to only consider maxbreeders
            minBreeders = (this.MinimumBreedersKept > this.MaximumBreedersKept) ? this.MaximumBreedersKept : this.MinimumBreedersKept;
            maxBreeders = Math.Max(this.MaximumBreedersKept, minBreeders);

            if (MathUtilities.IsLessThan(MaximumSiresKept, 1) & MathUtilities.IsPositive(MaximumSiresKept))
                SiresKept = Convert.ToInt32(Math.Ceiling(maxBreeders * MaximumSiresKept), CultureInfo.InvariantCulture);
            else
                SiresKept = Convert.ToInt32(Math.Truncate(MaximumSiresKept), CultureInfo.InvariantCulture);

            if (AdjustBreedingMalesAtStartup | AdjustBreedingFemalesAtStartup)
            {
                HerdResource = Resources.FindResourceGroup<RuminantHerd>();
                IEnumerable<RuminantType> herds = HerdResource.FindAllChildren<RuminantType>();

                // NOTE: this only currently works for single herd simulations.

                if (herds.Count() == 1)
                {
                    string warn = "";
                    List<string> information = new List<string>();
                    // calculate the number of breeders, sucklings, weaners and prebreeders
                    IEnumerable<RuminantTypeCohort> cohorts = herds.FirstOrDefault().FindAllDescendants<RuminantTypeCohort>();

                    if (cohorts.Any())
                    {
                        if (AdjustBreedingFemalesAtStartup)
                        {
                            var breederf = cohorts.Where(a => a.Sex == Sex.Female && a.Age >= herds.FirstOrDefault().MinimumAge1stMating);
                            if (breederf.Any())
                            {
                                // if no breeders numbers defined we need to bring them up to 
                                // distribute evently across breeder cohorts
                                double totalf = breederf.Sum(a => a.Number);
                                if (MathUtilities.IsLessThanOrEqual(totalf, 0))
                                {
                                    foreach (var item in breederf)
                                        item.Number = Math.Truncate(minBreeders / breederf.Count() + 0.5);

                                    if (AdjustOthersToFemaleBreedersPropAtStartup)
                                    {
                                        warn = $"Unable to [Adjust all cohorts in proportion to breeding females at start-up] using [a={this.Name}].{Environment.NewLine}No individuals (Number = 0 in all cohorts) were identified in cohorts for non-breeders. Breeder female numbers have been adjusted to [MinBreeders] but unable to determine an adjustment factor for the remiander of the herd.";
                                        Warnings.CheckAndWrite(warn, Summary, this, MessageType.Warning);
                                    }
                                }
                                else
                                {
                                    double adjustment = 1;
                                    if (totalf > maxBreeders)
                                        adjustment = maxBreeders / totalf;
                                    else if (totalf < minBreeders)
                                        adjustment = minBreeders / totalf;

                                    if (MathUtilities.IsGreaterThan(adjustment, 500))
                                    {
                                        warn = $"Problem [Adjust breeding female cohorts at start-up] using [a={this.Name}]. Adjustment factor [{adjustment:F1}] from initial cohort numbers limited to 500x. Add more individuals to initial cohorts (set Number) to allow ensure a more efficient adjustment.";
                                        Warnings.CheckAndWrite(warn, Summary, this, MessageType.Warning);
                                    }
                                    if (adjustment != 1)
                                    {
                                        foreach (var item in breederf)
                                            item.Number = Math.Truncate(item.Number * adjustment + 0.5);

                                        information.Add($"[Adjust breeding female cohorts at start-up] {(adjustment<1?"reduced":"increased")} the female breeder numbers from [{totalf}] to the [{(adjustment<1?"Maximum breeders":"Minimum breeders")} kept] of [{(adjustment < 1 ? maxBreeders : minBreeders)}]");

                                        if (AdjustOthersToFemaleBreedersPropAtStartup)
                                        {
                                            foreach (var item in cohorts.Where(a => !breederf.Contains(a) && a.Sire == false))
                                                item.Number = Math.Truncate(item.Number * adjustment + 0.5);
                                            information.Add($"[Adjust all cohorts based on breeders at start-up] {(adjustment < 1 ? "reduced" : "increased")} all non-breeder numbers by a factor of [{adjustment:F3}]");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                warn = $"Unable to [Adjust breeding female cohorts at start-up] using [a={this.Name}]. No cohorts representing female breeders were found.";
                                Warnings.CheckAndWrite(warn, Summary, this, MessageType.Warning);
                            }
                        }

                        if (AdjustBreedingMalesAtStartup)
                        {
                            var breederm = cohorts.Where(a => a.Sex == Sex.Male && a.Sire == true);
                            if(breederm.Any())
                            {
                                double totalm = breederm.Sum(a => a.Number);

                                if (SiresKept > 0 && MathUtilities.IsPositive(totalm))
                                {
                                    double sireAdjustment = SiresKept / totalm;
                                    foreach (var item in breederm)
                                        item.Number = Math.Truncate(item.Number * sireAdjustment + 0.5);

                                    information.Add($"[Adjust breeding male (sire) cohorts at start-up] {(sireAdjustment < 1 ? "reduced" : "increased")} the male breeder (sire) numbers from [{totalm:F0}] to [{SiresKept}]{(MathUtilities.IsLessThan(MaximumSiresKept, 1)?" based on specified proportion of the adjusted female breeder herd":"")} ");
                                }
                            }
                            else
                            {
                                warn = $"Unable to [Adjust breeding male (sire) cohorts at start-up] using [a={this.Name}]. No cohorts representing male breeders (sires) were found.";
                                Warnings.CheckAndWrite(warn, Summary, this, MessageType.Warning);
                            }
                        }
                        if(information.Any())
                        {
                            information.Insert(0, $"The numbers in initial cohorts for [{cohorts.FirstOrDefault().FindAncestor<RuminantType>().Breed}] were adjusted by [a={this.Name}] at start of the simulation.");
                            Warnings.CheckAndWrite(string.Join(Environment.NewLine, information), Summary, this, MessageType.Information);
                        }
                    }
                }
            }
        }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            this.InitialiseHerd(false, true);
            breedParams = Resources.FindResourceType<RuminantHerd, RuminantType>(this, this.PredictedHerdName, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop) as RuminantType;

            if (FindAllChildren<RuminantActivityGroup>().Any())
            {
                selectHerdAvailable = new List<Ruminant>();
            }

            // get the mortality rate for the herd if available or assume zero
            mortalityRate = breedParams.MortalityBase;

            // check GrazeFoodStoreExists for breeders
            grazeStoreBreeders = "";
            if((ManageFemaleBreederNumbers & PerformFemaleStocking) && GrazeFoodStoreNameBreeders != null && !GrazeFoodStoreNameBreeders.StartsWith("Not specified"))
            {
                grazeStoreBreeders = GrazeFoodStoreNameBreeders.Split('.').Last();
                foodStoreBreeders = Resources.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreNameBreeders, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
            }

            var ah = this.FindInScope<ActivitiesHolder>();
            // check for managed paddocks and warn if breeders placed in yards.
            if ((ManageFemaleBreederNumbers & PerformFemaleStocking) && grazeStoreBreeders == "" && this.MaximumProportionBreedersPerPurchase > 0)
            {
                if(ah.FindAllDescendants<PastureActivityManage>().Any())
                    Summary.WriteMessage(this, $"Breeders purchased by [a={this.Name}] are currently placed in [Not specified - general yards] while a managed pasture is available. These animals will not graze until moved and will require feeding while in yards.\r\nSolution: Set the [GrazeFoodStore to place purchase in] located in the properties [General].[PastureDetails]", MessageType.Warning);
            }
            
            // check GrazeFoodStoreExists for sires
            grazeStoreSires = "";
            if ((ManageMaleBreederNumbers & PerformMaleStocking) && GrazeFoodStoreNameSires != null && !GrazeFoodStoreNameSires.StartsWith("Not specified"))
            {
                grazeStoreSires = GrazeFoodStoreNameSires.Split('.').Last();
                foodStoreSires = Resources.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreNameSires, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);
            }

            // check for managed paddocks and warn if sires placed in yards.
            if (grazeStoreSires == "" && this.SiresKept > 0)
            {
                if (ah.FindAllDescendants<PastureActivityManage>().Any())
                    Summary.WriteMessage(this, $"Sires purchased by [a={this.Name}] are currently placed in [Not specified - general yards] while a managed pasture is available. These animals will not graze until moved and will require feeding while in yards.\r\nSolution: Set the [GrazeFoodStore to place purchase in] located in the properties [General].[PastureDetails]", MessageType.Warning);
            }

            // check GrazeFoodStoreExists for grow out males
            if (GrowOutYoungMales)
            {
                if (GrazeFoodStoreNameGrowOutMales != null && !GrazeFoodStoreNameGrowOutMales.StartsWith("Not specified"))
                    grazeStoreGrowOutMales = GrazeFoodStoreNameGrowOutMales.Split('.').Last();

                // check for managed paddocks and warn if sires placed in yards.
                if (grazeStoreGrowOutMales == "")
                {
                    if (ah.FindAllDescendants<PastureActivityManage>().Any())
                        Summary.WriteMessage(this, $"Males grown out before sale by [a={this.Name}] are currently placed in [Not specified - general yards] while a managed pasture is available. These animals will not graze until moved and will require feeding while in yards.\r\nSolution: Set the [GrazeFoodStore to place purchase in] located in the properties [General].[PastureDetails]", MessageType.Warning);
                }
            }

            if (GrowOutYoungFemales)
            {
                if (GrazeFoodStoreNameGrowOutFemales != null && !GrazeFoodStoreNameGrowOutFemales.StartsWith("Not specified"))
                    grazeStoreGrowOutFemales = GrazeFoodStoreNameGrowOutFemales.Split('.').Last();

                // check for managed paddocks and warn if sires placed in yards.
                if (grazeStoreGrowOutFemales == "")
                {
                    if (ah.FindAllDescendants<PastureActivityManage>().Any())
                        Summary.WriteMessage(this, $"Females grown out before sale by [a={this.Name}] are currently placed in [Not specified - general yards] while a managed pasture is available. These animals will not graze until moved and will require feeding while in yards.\r\nSolution: Set the [GrazeFoodStore to place purchase in] located in the properties [General].[PastureDetails]", MessageType.Warning);
                }
            }

            // get list of replacement individuals
            purchaseDetails = this.FindAllChildren<SpecifyRuminant>().Cast<SpecifyRuminant>().Select((a, index) => new SpecifiedRuminantListItem() { Index = index, ExampleRuminant = a.ExampleIndividual, SpecifyRuminantComponent = a}).Cast<SpecifiedRuminantListItem>().ToList();
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            // reset for this time step
            numberMaleSiresInHerd = 0;
            numberMaleSiresInPurchases = 0;
            numberFemaleInPurchases = 0;
            numberFemalePreBreedersInHerd = 0;
            siresPresent = 0;
            numberFemaleBreedingInHerd = 0;
            excessBreeders = 0;
            sufficientFoodBreeders = true;
            sufficientFoodSires = true;

            // if this activity has determined where is a RuminantFilterGroup apply the rules to define individuals available this time-step
            // this allows a filter to determine what individuals are available to manage e.g. incomplete muster.
            if (selectHerdAvailable != null)
            {
                selectHerdAvailable = GetIndividuals<Ruminant>();
            }

            // calculate numbers for current herd
            if (ManageFemaleBreederNumbers | ManageMaleBreederNumbers)
            {
                List<Ruminant> nonGrowOutHerd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.NotMarkedForSale, includeCheckHerdMeetsCriteria: true).Where(a => !a.Attributes.Exists("GrowOut")).ToList();

                // ensure pasture limits are ok before purchases
                if (foodStoreBreeders != null)
                    sufficientFoodBreeders = (foodStoreBreeders.TonnesPerHectare * 1000) >= MinimumPastureBeforeRestock;

                if (foodStoreSires != null)
                    sufficientFoodSires = (foodStoreSires.TonnesPerHectare * 1000) >= MinimumPastureBeforeRestock;

                if (ManageMaleBreederNumbers)
                {
                    // MALES
                    // check for sires after sale of old individuals and buy/sell
                    numberMaleSiresInHerd = nonGrowOutHerd.OfType<RuminantMale>().Where(a => a.SaleFlag == HerdChangeReason.None && a.IsSire).Count();
                    numberMaleSiresInPurchases = HerdResource.PurchaseIndividuals.OfType<RuminantMale>().Where(a => a.Breed == this.PredictedHerdBreed && a.IsSire).Count();

                    int numberFemaleTotalInHerd = nonGrowOutHerd.OfType<RuminantFemale>().Where(a => a.SaleFlag == HerdChangeReason.None && a.IsBreeder).Count();

                    siresPresent = numberMaleSiresInHerd + numberMaleSiresInPurchases;
                    if (MathUtilities.IsLessThan(MaximumSiresKept, 1) & MathUtilities.IsPositive(MaximumSiresKept))
                    {
                        if (numberFemaleTotalInHerd > MaximumBreedersKept)
                            SiresKept = Convert.ToInt32(Math.Ceiling(MaximumBreedersKept * MaximumSiresKept), CultureInfo.InvariantCulture);
                        else
                            SiresKept = Convert.ToInt32(Math.Ceiling(MaximumBreedersKept * MaximumSiresKept), CultureInfo.InvariantCulture);
                    }
                }

                if (ManageFemaleBreederNumbers)
                {
                    // Number of females needed to check stop simulation rule
                    numberFemaleBreedingInHerd = nonGrowOutHerd.OfType<RuminantFemale>().Where(a => a.SaleFlag == HerdChangeReason.None && a.IsBreeder).Count();

                    numberFemaleInPurchases = HerdResource.PurchaseIndividuals.OfType<RuminantFemale>().Where(a => a.Breed == this.PredictedHerdBreed && a.IsBreeder).Count();

                    // these are the breeders already marked for sale
                    // don't include those marked as max age sale as these can't be considered excess female
                    int numberFemaleMarkedForSale = nonGrowOutHerd.OfType<RuminantFemale>().Where(a => a.IsBreeder && a.ReadyForSale && a.SaleFlag != HerdChangeReason.MaxAgeSale).Count();

                    // defined heifers here as weaned and will be a breeder in the next year
                    // we should not include those individuals > 12 months before reaching breeder age
                    List<RuminantFemale> preBreeders = nonGrowOutHerd.OfType<RuminantFemale>().Where(a => a.IsPreBreeder && (a.Age - a.BreedParams.MinimumAge1stMating > -11) & !a.Attributes.Exists("GrowOut")).ToList();
                    numberFemalePreBreedersInHerd = preBreeders.Count();
                    int numberFemalePreBreedersInPurchases = HerdResource.PurchaseIndividuals.OfType<RuminantFemale>().Where(a => a.Breed == this.PredictedHerdBreed && a.IsPreBreeder).Count();

                    // prevent runaway population growth in individual based model by a check against max breeders
                    //if (numberFemaleBreedingInHerd > Math.Max(MaximumBreedersKept, MinimumBreedersKept) * MaxBreedersMultiplierToStop)
                    //    throw new ApsimXException(this, $"The breeder herd [{numberFemaleBreedingInHerd}] has exceeded the maximum number of breeders [{Math.Max(MaximumBreedersKept, MinimumBreedersKept)}] x the stop model max breeders multiplier [{MaxBreedersMultiplierToStop}]{System.Environment.NewLine}This is a safety mechanism to limit runaway population growth in the individual-based ruminant model. Adjust [Maximum breeders kept] or the [Stop model max breeders multiplier] if this population was intended");

                    // shortfall between actual and desired numbers of breeders (-ve for shortfall)
                    excessBreeders = numberFemaleBreedingInHerd + numberFemaleInPurchases - maxBreeders;

                    // IAT-NABSA adjusts to account for the old animals that will be sold in the next year
                    // This is not required in CLEM as they have been sold in this method, and it won't be until this method is called again that the next lot are sold.
                    // Like IAT-NABSA we will account for mortality losses in the next year in our breeder purchases
                    // Account for whole individuals only.

                    // calculate the mortality of the remaining + purchases
                    int numberDyingInNextYear = 0;
                    numberDyingInNextYear += Convert.ToInt32(Math.Floor((Math.Max(0, numberFemaleBreedingInHerd + excessBreeders)) * mortalityRate), CultureInfo.InvariantCulture);
                    //  include mortality of heifers added
                    numberDyingInNextYear += Convert.ToInt32(Math.Floor(Math.Max(0, numberFemalePreBreedersInHerd - ((excessBreeders > 0) ? -excessBreeders : 0)) * mortalityRate), CultureInfo.InvariantCulture);

                    // account for heifers already in the herd
                    // These are the next cohort that will become breeders in the next 12 months (before this method is called again)
                    excessBreeders += numberFemalePreBreedersInHerd;

                    // adjust for future mortality over 1 year
                    excessBreeders -= numberDyingInNextYear;
                }

                if (ManageMaleBreederNumbers)
                {
                    double propOfBreeders = 1;
                    if (RestockSiresRelativeToBreeders && maxBreeders > 0)
                        propOfBreeders = Math.Max(1, (double)numberFemaleBreedingInHerd / (double)maxBreeders);

                    int sires = Convert.ToInt32(Math.Ceiling(SiresKept * propOfBreeders), CultureInfo.InvariantCulture);
                    maleBreedersRequired = Math.Min(MaximumSiresPerPurchase, Math.Max(0, sires - numberMaleSiresInHerd - numberMaleSiresInPurchases));
                }
            }

            // provide updated measure for companion models
            foreach (var valueToSupply in valuesForCompanionModels)
            {
                int number = -99999;
                switch (valueToSupply.Key.identifier)
                {
                    case "Herd size - before activity":
                        number = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.AllOnFarm).Count();
                        break;
                    case "Adjust - new grow out males":
                        if (GrowOutYoungMales)
                        {
                            var filters = GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectMalesForGrowOut");
                            var uniqueIndividuals = GetUniqueIndividuals<RuminantMale>(filters, GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Weaned && !a.ReplacementBreeder && !a.IsSire && !a.Attributes.Exists("GrowOut")));
                            number = uniqueIndividuals.Count();
                        }
                        break;
                    case "Adjust - new grow out females":
                        if (GrowOutYoungFemales)
                        {
                            var filters = GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectFemalesForGrowOut");
                            var uniqueIndividuals = GetUniqueIndividuals<RuminantFemale>(filters, GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => !a.ReplacementBreeder && a.IsPreBreeder && !a.Attributes.Exists("GrowOut")));
                            number = uniqueIndividuals.Count(); 
                        }
                        break;
                    case "Destock - grow out males":
                        if (GrowOutYoungMales && PerformMaleDestocking & MarkAgeWeightMalesForSale)
                        {
                            var filters = GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectMalesForGrowOut");
                            var uniqueIndividuals = GetUniqueIndividuals<RuminantMale>(filters, GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Attributes.Exists("GrowOut") && ((a.Age >= MaleSellingAge) || a.Weight >= MaleSellingWeight)));
                            number = uniqueIndividuals.Count();
                        }
                        break;
                    case "Destock - grow out females":
                        if (GrowOutYoungFemales && PerformFemaleDestocking)
                        {
                            var filters = GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectFemalesForGrowOut");
                            var uniqueIndividuals = GetUniqueIndividuals<RuminantFemale>(filters, GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Attributes.Exists("GrowOut") && ((a.Age >= FemaleSellingAge) || a.Weight >= FemaleSellingWeight)));
                            number = uniqueIndividuals.Count();
                        }
                        break;
                    case "Destock - old sires":
                        if (MarkOldSiresForSale && PerformMaleDestocking)
                        {
                            var filters = GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveOldSiresFromHerd");
                            var uniqueIndividuals = GetUniqueIndividuals<RuminantMale>(filters, GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.IsSire && a.Age >= MaximumSireAge));
                            number = uniqueIndividuals.Count();
                        }
                        break;
                    case "Destock - old female breeders":
                        if (MarkOldBreedersForSale && PerformFemaleDestocking)
                        {
                            var filters = GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveOldFemalesFromHerd");
                            var uniqueIndividuals = GetUniqueIndividuals<RuminantFemale>(filters, GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Age >= MaximumBreederAge));
                            number = uniqueIndividuals.Count();
                        }
                        break;
                    case "Destock - excess female breeders":
                        if (ManageFemaleBreederNumbers && PerformFemaleDestocking && excessBreeders > 0)
                        {
                            number = excessBreeders;
                        }
                        break;
                    case "Destock - excess sires":
                        if (ManageMaleBreederNumbers && PerformMaleDestocking && siresPresent > SiresKept)
                        {
                            number = Math.Min(numberMaleSiresInHerd + numberMaleSiresInPurchases - SiresKept, numberMaleSiresInPurchases);
                        }
                        break;
                    case "Stock - female breeder purchases":
                        if (ManageFemaleBreederNumbers && PerformFemaleStocking && excessBreeders < 0 && sufficientFoodBreeders)
                        {
                            number = excessBreeders * -1;
                        }
                        break;
                    case "Stock - sire purchases":
                        if (ManageMaleBreederNumbers && PerformFemaleDestocking && siresPresent < SiresKept && sufficientFoodSires)
                        {
                            number = maleBreedersRequired;
                        }
                        break;
                    default:
                        throw new NotImplementedException(UnknownUnitsErrorText(this, valueToSupply.Key));
                }

                valuesForCompanionModels[valueToSupply.Key] = valueToSupply.Key.unit switch
                {
                    "fixed" => (double?)1,
                    "per head" => number,
                    _ => (double?)0,
                };
            }
            return null;
        }

        /// <inheritdoc/>
        protected override void AdjustResourcesForTimestep()
        {
            // No resource shortfalls are allowed to influence this activity at present.
            IEnumerable<ResourceRequest> shortfalls = MinimumShortfallProportion();
            if (shortfalls.Any())
            {
                string warn = $"Resource shortfalls found in child components with Insufficient resources action set to [UseAvailableWithImplications] will not influence the tasks performed by [a={NameWithParent}].{Environment.NewLine}All [UseAvailableWithImplications] settings have been ignored.";
                Warnings.CheckAndWrite(warn, Summary, this, MessageType.Warning);
                Status = ActivityStatus.Warning;
                AddStatusMessage("Shortfalls ignored. See CLEM Messages");
            }
        }

        /// <inheritdoc/>
        [EventSubscribe("CLEMAnimalManage")]
        protected override void OnGetResourcesPerformActivity(object sender, EventArgs e)
        {
            ManageActivityResourcesAndTasks();
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            // assumed Timing is OK from base class handling of resource provision.

            List<Ruminant> nonGrowOutHerd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.NotMarkedForSale, includeCheckHerdMeetsCriteria: true).Where(a => !a.Attributes.Exists("GrowOut")).ToList();

            // grow out management - extended version if NABSA
            // Allows sell off of age/weight grow out males and females in any month or only activity timer month
            // NABSA MALES - weaners, 1-2, 2-3 and 3-4 yo, we check for any male weaned and not a breeding sire.
            // if SellYoungFemalesLikeMales then all apply to both sexes else only males.
            // SellFemalesLikeMales will grow out excess heifers until age/weight rather than sell immediately.
            if (GrowOutYoungMales | GrowOutYoungFemales)
            {
                Status = (Status == ActivityStatus.NoTask) ? ActivityStatus.NotNeeded : Status;
                // tag all grow out and move to pasture specified
                bool growoutOccurred = false;

                // select females for growing out
                if (GrowOutYoungFemales)
                    foreach (var removalFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectFemalesForGrowOut"))
                        foreach (var ind in removalFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => !a.ReplacementBreeder && a.IsPreBreeder && !a.Attributes.Exists("GrowOut"))).ToList())
                        {
                            ind.Location = grazeStoreGrowOutFemales;
                            ind.Attributes.Add("GrowOut");
                            if (!ind.ReadyForSale)
                                growoutOccurred = true;
                        }

                // select old males for growing out
                if (GrowOutYoungMales)
                    foreach (var removalFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectMalesForGrowOut"))
                        foreach (var ind in removalFilter.Filter(GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Weaned && !a.ReplacementBreeder && !a.IsSire && !a.Attributes.Exists("GrowOut"))).ToList())
                        {
                            ind.Location = grazeStoreGrowOutMales;
                            ind.Attributes.Add("GrowOut");
                            if (!ind.ReadyForSale)
                                growoutOccurred = true;
                            // do not castrate here as we may need to keep some of this months pool as replacement breeders
                            // see replacement sire section in timingOK
                        }

                if (growoutOccurred)
                    nonGrowOutHerd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.NotMarkedForSale, includeCheckHerdMeetsCriteria: true).Where(a => !a.Attributes.Exists("GrowOut")).ToList();


                if (PerformMaleDestocking)
                {
                    // identify those ready for sale
                    foreach (var ind in GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Attributes.Exists("GrowOut") && MarkAgeWeightMalesForSale && (a.Age >= MaleSellingAge || a.Weight >= MaleSellingWeight)))
                    {
                        this.Status = ActivityStatus.Success;
                        ind.SaleFlag = HerdChangeReason.AgeWeightSale;
                    } 
                }
                if (PerformFemaleDestocking)
                {
                    // identify those ready for sale
                    foreach (var ind in GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Attributes.Exists("GrowOut") && MarkAgeWeightFemalesForSale && (a.Age >= FemaleSellingAge || a.Weight >= FemaleSellingWeight)))
                    {
                        this.Status = ActivityStatus.Success;
                        ind.SaleFlag = HerdChangeReason.AgeWeightSale;
                    }
                }

            }

            this.Status = ActivityStatus.NotNeeded;

            // select old females for sale
            if (MarkOldBreedersForSale && PerformFemaleDestocking)
                foreach (var removalFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveOldFemalesFromHerd"))
                    foreach (var ind in removalFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Age >= MaximumBreederAge)).ToList())
                    {
                        ind.SaleFlag = HerdChangeReason.MaxAgeSale;
                        this.Status = ActivityStatus.Success;
                    }

            // select old males for sale
            if (MarkOldSiresForSale && PerformMaleDestocking)
                foreach (var removalFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveOldSiresFromHerd"))
                    foreach (var ind in removalFilter.Filter(GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.IsSire && a.Age >= MaximumSireAge)).ToList())
                    {
                        ind.SaleFlag = HerdChangeReason.MaxAgeSale;
                        this.Status = ActivityStatus.Success;
                    }

            // Number of females needed to check stop simulation rule
            int numberFemaleBreedingInHerd = nonGrowOutHerd.OfType<RuminantFemale>().Where(a => a.SaleFlag == HerdChangeReason.None && a.IsBreeder).Count();

            // prevent runaway population growth in individual based model by a check against max breeders
            if (numberFemaleBreedingInHerd > maxBreeders * MaxBreedersMultiplierToStop)
                throw new ApsimXException(this, $"The breeder herd [{numberFemaleBreedingInHerd}] has exceeded the maximum number of breeders [{maxBreeders}] x the stop model max breeders multiplier [{MaxBreedersMultiplierToStop}]{System.Environment.NewLine}This is a safety mechanism to limit runaway population growth in the individual-based ruminant model. Adjust [Maximum breeders kept] or the [Stop model max breeders multiplier] if this population was intended");

            // if any breeder management to be performed calculate current herd size
            if (ManageFemaleBreederNumbers | ManageMaleBreederNumbers)
            {
                // adjust males sires if managing male breeders
                if (ManageMaleBreederNumbers && siresPresent != SiresKept)
                {
                    if (siresPresent > SiresKept)
                    {
                        // sell excess sires
                        // individuals marked for sale are not considered as they will be lost by sales and we assume all sales will take place

                        // remove excess sires from purchases if any
                        // this value may exceed those in purchases, but that's ok as first step before remiving from herd
                        int numberToRemove = numberMaleSiresInHerd + numberMaleSiresInPurchases - SiresKept;

                        // remove suitable individuals from the purchase list 
                        foreach (var removeFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveSiresFromPurchases"))
                        {
                            int index = 0;
                            var individuals = removeFilter.Filter(HerdResource.PurchaseIndividuals.OfType<RuminantMale>().Where(a => a.Breed == this.PredictedHerdBreed && a.IsSire)).ToList();
                            while (numberToRemove > 0 && index < individuals.Count())
                            {
                                HerdResource.PurchaseIndividuals.Remove(individuals[index]);
                                numberToRemove--;
                                index++;
                            }
                        }

                        if (PerformMaleDestocking)
                        {
                            // remove sires followed by replacement sires in one go
                            if (numberToRemove > 0)
                                foreach (var removalFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveSiresFromHerd"))
                                    foreach (var male in removalFilter.Filter(GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.IsSire || a.ReplacementBreeder)).OrderByDescending(a => a.Class).Take(numberToRemove))
                                    {
                                        male.Location = grazeStoreSires;
                                        male.SaleFlag = HerdChangeReason.ExcessSireSale;
                                        this.Status = ActivityStatus.Success;
                                        male.ReplacementBreeder = false;
                                        numberToRemove--;
                                    }

                            if (numberToRemove > 0)
                            {
                                AddStatusMessage("Excess sires");
                                Status = ActivityStatus.Warning;
                            }

                        }
                    }
                    else
                    {
                        // need to assign/buy sires

                        // get suitable sires marked for sale if not maxage sale
                        foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectSiresFromSales"))
                            foreach (var male in selectFilter.Filter(GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.MarkedForSale, new List<HerdChangeReason>() { HerdChangeReason.MaxAgeSale }).Where(a => a.IsSire)).Take(maleBreedersRequired))
                            {
                                male.SaleFlag = HerdChangeReason.None;
                                male.Location = grazeStoreSires;
                                maleBreedersRequired--;
                            }

                        // herd replacements are permitted regardless of pasture as these animals are already in the herd
                        if (AllowSireReplacement && maleBreedersRequired > 0)
                        {
                            // remove young males from sale herd to replace breeding sires (not those sold because too old)
                            // only consider individuals that will mature in next 12 months and are not castrated
                            foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectYoungMalesFromSales"))
                                // male, saleflag is AgeWeightSale, not castrated and age will mature in next 12 months
                                foreach (RuminantMale male in selectFilter.Filter(GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.MarkedForSale, new List<HerdChangeReason>() { HerdChangeReason.MaxAgeSale }).Where(a => a.Weaned && (a.Age - a.BreedParams.MinimumAge1stMating > -11) && !a.IsCastrated)).Take(maleBreedersRequired))
                                {
                                    male.Location = grazeStoreSires;
                                    male.SaleFlag = HerdChangeReason.None;
                                    male.Attributes.Remove("GrowOut");
                                    male.Attributes.Add("Sire");
                                    male.ReplacementBreeder = true;
                                    numberMaleSiresInHerd++;
                                    maleBreedersRequired--;
                                }

                            // if still insufficent, look into current growing out herd for replacement before they are castrated below
                            // only consider individuals that will mature in next 12 months
                            if (GrowOutYoungMales && maleBreedersRequired > 0)
                            {
                                foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectYoungMalesFromGrowOut"))
                                    foreach (RuminantMale male in selectFilter.Filter(GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => (a.Age - a.BreedParams.MinimumAge1stMating > -11) && a.Attributes.Exists("GrowOut") && !a.IsCastrated)).Take(maleBreedersRequired).ToList())
                                    {
                                        male.Location = grazeStoreSires;
                                        male.SaleFlag = HerdChangeReason.None;
                                        male.Attributes.Remove("GrowOut");
                                        male.Attributes.Add("Sire");
                                        male.ReplacementBreeder = true;
                                        numberMaleSiresInHerd++;
                                        maleBreedersRequired--;
                                    }
                            }

                            // we still don't have enough sires. 
                            // we can now move to buy or if purchasing is off we'll need to set aside a number of younger males and wait for them to grow

                            // remaining males assumed to be too small, so await next time-step
                            // note they will be castrated
                            // we will catch these individuals if they are offered for sale in the not timing OK else block each month
                        }

                        // check pasture before buying new sires
                        if (PerformMaleStocking && (foodStoreSires == null) || (sufficientFoodSires))
                        {
                            // if still insufficient buy sires.
                            // new code to buy sires based on details provided in SpecifyRuminants list already created in initialise and validated

                            var selectedPurchaseDetails = purchaseDetails.Where(a => a.ExampleRuminant is RuminantMale && (a.ExampleRuminant as RuminantMale).IsSire).ToList();
                            int numberToBuy = maleBreedersRequired;
                            int[] totals = new int[selectedPurchaseDetails.Count()];

                            if (numberToBuy > 0 && selectedPurchaseDetails.Any())
                            {
                                for (int i = 0; i < numberToBuy; i++)
                                {
                                    int purchaseIndex = 0;
                                    double rndNumber = RandomNumberGenerator.Generator.NextDouble();
                                    while (rndNumber > selectedPurchaseDetails[purchaseIndex].CummulativeProbability)
                                    {
                                        if (purchaseIndex == selectedPurchaseDetails.Count())
                                            // Cummulative probabilities of the purchase breeders provided have been calculated and checked under validation
                                            throw new ApsimXException(this, $"Cannot assign PurchaseSireDetails in [a={this.NameWithParent}] due to invalid cummulative probabilities. See developers.");
                                        purchaseIndex++;
                                    }
                                    totals[purchaseIndex]++;
                                }
                                for (int i = 0; i < totals.Length; i++)
                                {
                                    RuminantTypeCohort cohort = selectedPurchaseDetails[i].SpecifyRuminantComponent.Details;
                                    cohort.Number = totals[i];
                                    this.Status = ActivityStatus.Success;
                                    var newindividuals = cohort.CreateIndividuals(null, selectedPurchaseDetails[i].SpecifyRuminantComponent.BreedParams);
                                    foreach (var ind in newindividuals)
                                    {
                                        ind.Location = grazeStoreSires;
                                        ind.SaleFlag = HerdChangeReason.SirePurchase;
                                        ind.ID = 0;
                                        ind.PurchaseAge = ind.Age;

                                        // weight will be set to normalised weight as it was assigned 0 at initialisation
                                        ind.PreviousWeight = ind.Weight;

                                        // TODO: supply attributes with new individuals

                                        // this individual must be weaned to be permitted to start breeding.
                                        ind.Wean(false, "Initial");

                                        if (!ind.Attributes.Exists("Sire"))
                                            ind.Attributes.Add("Sire");

                                        // add to purchase request list and await purchase in Buy/Sell
                                        HerdResource.PurchaseIndividuals.Add(ind);
                                        maleBreedersRequired--;
                                    }
                                }
                            }
                            if (maleBreedersRequired > 0 && Status != ActivityStatus.Warning) 
                            {
                                AddStatusMessage("Sire shortfall");
                                Status = ActivityStatus.Partial;
                            }
                        }
                    }
                }

                // FEMALES
                // TODO: allow purchase of pregtested females.
                // Feb2020 - Added ability to provide destocking groups to try and sell non heifer breeders before reverting to heifer sales.
                if (ManageFemaleBreederNumbers)
                {
                    if (excessBreeders > 0) // surplus breeders
                    {
                        // Remove from purchases
                        //
                        // remove suitable individuals from the purchase list 
                        foreach (var removeFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveBreedersFromPurchases"))
                        {
                            int index = 0;
                            var individuals = removeFilter.Filter(HerdResource.PurchaseIndividuals.OfType<RuminantFemale>().Where(a => a.Breed == this.PredictedHerdBreed && a.IsBreeder)).ToList();
                            while (excessBreeders > 0 && index < individuals.Count())
                            {
                                HerdResource.PurchaseIndividuals.Remove(individuals[index]);
                                excessBreeders--;
                                index++;
                            }
                        }

                        if (PerformFemaleDestocking)
                        {
                            // Remove from herd not for sale
                            IEnumerable<RuminantGroup> reduceBreedersFilters = GetCompanionModelsByIdentifier<RuminantGroup>(false, false, "RemoveBreedersFromHerd");
                            if(reduceBreedersFilters is null)
                            {
                                reduceBreedersFilters = GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveBreedersFromHerd");
                                SortByProperty srt = new SortByProperty() { PropertyOfIndividual = "Age", SortDirection = System.ComponentModel.ListSortDirection.Descending };
                                reduceBreedersFilters.FirstOrDefault().Children.Add(srt);
                            }

                            foreach (var removeFilter in reduceBreedersFilters)
                                foreach (RuminantFemale female in removeFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.IsBreeder || (a.IsPreBreeder && (a.Age - a.BreedParams.MinimumAge1stMating > -11)))).Take(excessBreeders).ToList())
                                {
                                    if(female.Class == "PreBreeder" || female.Class == "Weaner")
                                        female.SaleFlag = HerdChangeReason.ExcessPreBreederSale;
                                    else
                                        female.SaleFlag = HerdChangeReason.ExcessBreederSale;
                                    excessBreeders--;
                                }
                        }

                        // any additional excess cannot be solved so herd will be larger than desired

                        if (excessBreeders > 0)
                        {
                            AddStatusMessage("Excess breeders");
                            Status = ActivityStatus.Warning;
                        }

                        // excess breeders were found so there is no need for any replacement breeders to be flagged
                        foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveFemaleReplacementBreeders"))
                            foreach (var female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.ReplacementBreeder)).ToList())
                            {
                                female.ReplacementBreeder = false;
                            }

                    }
                    else if (excessBreeders < 0) // shortfall breeders to buy
                    {
                        double minBreedAge = breedParams.MinimumAge1stMating;
                        femaleBreedersRequired = excessBreeders * -1;

                        // leave purchase alone as they will be added and already accounted for

                        if (femaleBreedersRequired > 0)
                        {
                            // remove females breeders from sale herd to replace breeders (not those sold because too old)
                            foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectBreedersFromSales"))
                                foreach (var female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.MarkedForSale, new List<HerdChangeReason>() { HerdChangeReason.MaxAgeSale }).Where(a => a.ReadyForSale && a.Age >= a.BreedParams.MinimumAge1stMating)).Take(femaleBreedersRequired).ToList())
                                {
                                    female.Attributes.Remove("GrowOut"); // in case grow out
                                    female.SaleFlag = HerdChangeReason.None;
                                    if(grazeStoreBreeders != "")
                                        female.Location = grazeStoreBreeders;
                                    femaleBreedersRequired--;
                                }
                        }

                        // remove grow out breeders from grow out if of breeding in next year age
                        if (GrowOutYoungFemales && femaleBreedersRequired > 0)
                        {
                            foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectYoungFemalesFromGrowOut"))
                                foreach (RuminantFemale female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => (a.Age >= a.BreedParams.MinimumAge1stMating) && a.Attributes.Exists("GrowOut"))).Take(femaleBreedersRequired).ToList())
                                {
                                    female.Attributes.Remove("GrowOut");
                                    if (!female.IsBreeder && !female.IsSterilised)
                                        female.ReplacementBreeder = true;
                                    female.Location = grazeStoreBreeders;
                                    femaleBreedersRequired--;
                                }
                        }


                        // check pasture limits before purchasing new breeders
                        if ((foodStoreBreeders == null) || (sufficientFoodBreeders))
                        {
                            // if still insufficient and permitted, buy breeders.
                            if (PerformFemaleStocking && femaleBreedersRequired > 0 && (MaximumProportionBreedersPerPurchase > 0))
                            {
                                // can only buy up to minbreeders
                                // recalculate based on minbreeders kept for purchases and animals just returned to breeder pool
                                int currentBreederHerdSize = numberFemaleBreedingInHerd + ((excessBreeders * -1) - femaleBreedersRequired);
                                // this limit is only applied to purchases, not herd replacement to max breeders kept
                                int limitedExcessBreeders = Math.Min(femaleBreedersRequired, Math.Max(0, minBreeders - currentBreederHerdSize));

                                // this limit is only applied to purchases, not herd replacement to max breeders kept
                                //int limitedExcessBreeders = Math.Max(0, femaleBreedersRequired - (maxBreeders - minBreeders));

                                // adjust mortality for new level
                                if (limitedExcessBreeders < femaleBreedersRequired)
                                {
                                    int notDead = Convert.ToInt32(Math.Floor((femaleBreedersRequired - limitedExcessBreeders) * mortalityRate), CultureInfo.InvariantCulture);
                                    limitedExcessBreeders = Math.Max(0, limitedExcessBreeders - notDead);
                                }

                                // IAT-NABSA had buy mortality base% more to account for deaths before these individuals grow to breeding age
                                // These individuals are already of breeding age so we will ignore this in CLEM
                                // minimum of (max kept x prop in single purchase) and (the number needed + annual mortality)

                                // new code to buy Breeders based on details provided in SpecifyRuminants list already created in initialise and validated
                                // now looks at individual specified rather than hoping for a specific component name "FemaleBreeder"

                                var purchaseBreederDetails = purchaseDetails
                                    .Where(a => a.ExampleRuminant is RuminantFemale && (a.ExampleRuminant as RuminantFemale).IsBreeder).ToList();

                                int numberToBuy = Math.Min(limitedExcessBreeders, Convert.ToInt32(Math.Ceiling(MaximumProportionBreedersPerPurchase * minBreeders), CultureInfo.InvariantCulture));
                                int[] totals = new int[purchaseBreederDetails.Count()];

                                if (numberToBuy > 0 && purchaseBreederDetails.Any())
                                {
                                    for (int i = 0; i < numberToBuy; i++)
                                    {
                                        int purchaseIndex = 0;
                                        double rndNumber = RandomNumberGenerator.Generator.NextDouble();
                                        while (rndNumber > purchaseBreederDetails[purchaseIndex].CummulativeProbability)
                                        {
                                            if (purchaseIndex == purchaseBreederDetails.Count())
                                                // Cummulative probabilities of the purchase breeders provided have been calculated and checked under validation
                                                throw new ApsimXException(this, $"Cannot assign PurchaseBreederDetails in [a={this.NameWithParent}] due to invalid cummulative probabilities. See developers.");
                                            purchaseIndex++;
                                        }
                                        totals[purchaseIndex]++;
                                    }
                                    for (int i = 0; i < totals.Length; i++)
                                    {
                                        RuminantTypeCohort cohort = purchaseBreederDetails[i].SpecifyRuminantComponent.Details;
                                        cohort.Number = totals[i];
                                        this.Status = ActivityStatus.Success;
                                        var newindividuals = cohort.CreateIndividuals(null, purchaseBreederDetails[i].SpecifyRuminantComponent.BreedParams);
                                        foreach (var ind in newindividuals)
                                        {
                                            ind.Location = grazeStoreBreeders;
                                            ind.SaleFlag = HerdChangeReason.BreederPurchase;
                                            ind.ID = 0;
                                            ind.PurchaseAge = ind.Age;

                                            // weight will be set to normalised weight as it was assigned 0 at initialisation
                                            ind.PreviousWeight = ind.Weight;

                                            // get purchased breeder attributes
                                            // TODO: allow proportion preg-tested - check preganant in pricing test
                                            // TODO: supply attributes with new individuals

                                            // this individual must be weaned to be permitted to start breeding.
                                            ind.Wean(false, "Initial");

                                            // add to purchase request list and await purchase in Buy/Sell
                                            HerdResource.PurchaseIndividuals.Add(ind);
                                            femaleBreedersRequired--;
                                        }
                                    }
                                }
                            }
                        }

                        var replacementsInHerd = GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.ReplacementBreeder);
                        int numberOfReplacements = replacementsInHerd.Count();

                        if(numberOfReplacements >= femaleBreedersRequired)
                        {
                            foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "RemoveFemaleReplacementBreeders"))
                                foreach (var female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.ReplacementBreeder)).Take(numberOfReplacements - femaleBreedersRequired).ToList())
                                {
                                    female.ReplacementBreeder = false;
                                }
                        }
                        else
                        {
                            // still need breeders and couldn't buy them so look at even younger individuals still in sale herd

                            // remove grow out pre-breeders from grow out if of breeding in next year age
                            if (GrowOutYoungFemales && numberOfReplacements < femaleBreedersRequired)
                            {
                                foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectYoungFemalesFromGrowOut"))
                                    foreach (RuminantFemale female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => (a.Age - a.BreedParams.MinimumAge1stMating > -11) && a.Attributes.Exists("GrowOut"))).Take(femaleBreedersRequired- numberOfReplacements).ToList())
                                    {
                                        female.Attributes.Remove("GrowOut");
                                        if (female.IsPreBreeder)
                                            female.ReplacementBreeder = true;
                                        female.Location = grazeStoreBreeders;
                                        numberOfReplacements++;
                                    }
                            }

                            // remove pre breeders maturing in the next 12 months from sales.
                            if (numberOfReplacements < femaleBreedersRequired)
                            {
                                foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectYoungFemalesFromSales"))
                                    foreach (var female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.MarkedForSale, new List<HerdChangeReason>() { HerdChangeReason.MaxAgeSale }).Where(a => a.ReadyForSale && (a.Age - a.BreedParams.MinimumAge1stMating > -11))).Take(femaleBreedersRequired - numberOfReplacements).ToList())
                                    {
                                        female.Attributes.Remove("GrowOut"); // in case grow out
                                        female.SaleFlag = HerdChangeReason.None;
                                        female.Location = grazeStoreBreeders;
                                        if (female.IsPreBreeder)
                                            female.ReplacementBreeder = true;
                                        numberOfReplacements++;
                                    }
                            }

                            // identify even younger individuals as replacements from sales
                            if (numberOfReplacements < femaleBreedersRequired)
                                foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectYoungFemalesFromSales"))
                                    foreach (RuminantFemale female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.MarkedForSale)).Where(a => a.IsPreBreeder || !a.Weaned).OrderByDescending(a => a.Age).Take(femaleBreedersRequired-numberOfReplacements).ToList())
                                    {
                                        female.Attributes.Remove("GrowOut");
                                        // keep by removing any tag for sale.
                                        female.SaleFlag = HerdChangeReason.None;
                                        female.Location = grazeStoreBreeders;
                                        if (!female.IsBreeder)
                                            female.ReplacementBreeder = true;
                                        numberOfReplacements++;
                                    }

                            // remove grow out heifers from grow out if young as these will be future needed replacements
                            if (GrowOutYoungFemales & numberOfReplacements < femaleBreedersRequired)
                                foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectYoungFemalesFromGrowOut"))
                                    foreach (RuminantFemale female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Attributes.Exists("GrowOut"))).OrderByDescending(a => a.Age).Take(femaleBreedersRequired-numberOfReplacements).ToList())
                                    {
                                        female.Attributes.Remove("GrowOut");
                                        female.SaleFlag = HerdChangeReason.None;
                                        female.Location = grazeStoreBreeders;
                                        if (!female.IsBreeder)
                                            female.ReplacementBreeder = true;
                                        numberOfReplacements++;
                                    }

                            // identify some unweaned individuals to be flagged as replacement breeders so avoid selling after weaned
                            if (numberOfReplacements < femaleBreedersRequired)
                                foreach (var selectFilter in GetCompanionModelsByIdentifier<RuminantGroup>(false, true, "SelectUnweanedFemalesAsReplacements"))
                                    foreach (RuminantFemale female in selectFilter.Filter(GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale)).Where(a => !a.Weaned).OrderByDescending(a => a.Age).Take(femaleBreedersRequired-numberOfReplacements).ToList())
                                    {
                                        female.ReplacementBreeder = true;
                                        numberOfReplacements++;
                                    }

                            // remove pregnants female from sales even at max age as calf is valuable this year
                            if (ReturnPregnantMaxAgeToHerd && numberOfReplacements < femaleBreedersRequired)
                                foreach (RuminantFemale female in GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.MarkedForSale).Where(a => a.IsPregnant).Take(femaleBreedersRequired - numberOfReplacements).ToList())
                                {
                                    female.SaleFlag = HerdChangeReason.None;
                                    female.Location = grazeStoreBreeders;
                                    numberOfReplacements++;
                                }
                        }

                        if (femaleBreedersRequired > 0 && Status != ActivityStatus.Warning)
                        {
                            AddStatusMessage("Breeder shortfall");
                            Status = ActivityStatus.Partial;
                        }

                        // identify shortfall in 

                    }

                    // Breeders themselves don't get sold unless specified in ruminant groups (with RemoveBreeders status) below this activity. Sales is with pre-breeders (e.g. Heifers)
                    // Only excess pre-breeders either be marked as GrowOut like males or sold as an excess heifer if becoming a breeder within the next cycle of management
                    // Breeders can be sold in seasonal and ENSO destocking.
                    // The destocking groups will define the order individuals are sold
                    // All keeping and removing tasks now obey the ruminant groups provided specifying individuals and order
                }
            }

            // time to castrate any males that have not been assigned as replacement breeders from this years young male pool
            // get grow-out males that are not castrated and not marked as replacement breeder
            if (CastrateMales)
                foreach (RuminantMale male in GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => !a.ReplacementBreeder && !a.IsCastrated && a.Attributes.Exists("GrowOut")).ToList())
                    male.Attributes.Add("Castrated");
            
        }

        /// <summary>An event handler to call for all herd management activities</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMAnimalManage")]
        private void OnCLEMAnimalManageManageGrowOuts(object sender, EventArgs e)
        {
            // perform in months where the the timer allowed other tasks
            if (!TimingOK)
            {
                // allow sale of grow outs in any month
                List<Ruminant> nonGrowOutHerd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.NotMarkedForSale, includeCheckHerdMeetsCriteria: true).Where(a => !a.Attributes.Exists("GrowOut")).ToList();

                if (ContinuousGrowOutSales)
                {
                    if (PerformMaleDestocking & GrowOutYoungMales)
                    {
                        // identify those ready for sale
                        foreach (var ind in GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Attributes.Exists("GrowOut") && MarkAgeWeightMalesForSale && (a.Age >= MaleSellingAge || a.Weight >= MaleSellingWeight)))
                        {
                            this.Status = ActivityStatus.Success;
                            ind.SaleFlag = HerdChangeReason.AgeWeightSale;
                        }
                    }
                    if (PerformFemaleDestocking & GrowOutYoungFemales)
                    {
                        // identify those ready for sale
                        foreach (var ind in GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Attributes.Exists("GrowOut") && MarkAgeWeightFemalesForSale && (a.Age >= FemaleSellingAge || a.Weight >= FemaleSellingWeight)))
                        {
                            this.Status = ActivityStatus.Success;
                            ind.SaleFlag = HerdChangeReason.AgeWeightSale;
                        }
                    }

                    // identify those ready for sale
                    foreach (var ind in GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.NotMarkedForSale).Where(a => a.Attributes.Exists("GrowOut") && ((a is RuminantMale) ? MarkAgeWeightMalesForSale : MarkAgeWeightFemalesForSale) && (a.Age >= ((a is RuminantMale) ? MaleSellingAge : FemaleSellingAge) || a.Weight >= ((a is RuminantMale) ? MaleSellingWeight : FemaleSellingWeight))))
                    {
                        this.Status = ActivityStatus.Success;
                        ind.SaleFlag = HerdChangeReason.AgeWeightSale;
                    }
                }

                // This is not a managment month based on timers 
                // Need to see if any suitable breeder replacements have emerged from herd that are due to be sold this time step
                // These will only be from births and weaning males or females being sold or truckout out as normally weaners would be maintained 
                // and castration and grow-out is only handled in this activity in the management month. 

                if (PerformFemaleStocking & RetainFemaleReplacementBreedersFromSaleHerd)
                    IdentifyMaleReplacementBreedersFromSaleHerd();
                if (PerformMaleStocking & RetainMaleReplacementBreedersFromSaleHerd)
                    IdentifyFemaleReplacementBreedersFromSaleHerd();
            }
        }

        private void IdentifyMaleReplacementBreedersFromSaleHerd()
        {
            if (maleBreedersRequired > 0)
            {
                // any suitable males for sale will be flagged as a prebreeder
                // male, readyForSale, not maxAgeSale, not castrated
                foreach (var individual in GetIndividuals<RuminantMale>(GetRuminantHerdSelectionStyle.MarkedForSale, new List<HerdChangeReason>() { HerdChangeReason.MaxAgeSale }).Where(a => !a.IsCastrated).Take(maleBreedersRequired).ToList())
                {
                    individual.Attributes.Remove("GrowOut");
                    individual.Attributes.Add("Sire");
                    individual.ReplacementBreeder = true;
                    individual.SaleFlag = HerdChangeReason.None;
                    maleBreedersRequired--;
                }
            }
        }

        private void IdentifyFemaleReplacementBreedersFromSaleHerd()
        {
            if (femaleBreedersRequired > 0)
            {
                // any suitable females for sale will be flagged as a prebreeder 
                // female, readyForSale, not maxAgeSale, notDryBreeder
                foreach (var individual in GetIndividuals<RuminantFemale>(GetRuminantHerdSelectionStyle.MarkedForSale, new List<HerdChangeReason>() { HerdChangeReason.MaxAgeSale, HerdChangeReason.DryBreederSale }).Take(femaleBreedersRequired).ToList())
                {
                    individual.Attributes.Remove("GrowOut");
                    if (!(individual as RuminantFemale).IsBreeder)
                        individual.ReplacementBreeder = true;
                    individual.SaleFlag = HerdChangeReason.None;
                    femaleBreedersRequired--;
                }
            }
        }

        /// <summary>An event handler to call for all herd management activities</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMEndOfTimeStep")]
        private void OnCLEMEndOfTimeStep(object sender, EventArgs e)
        {
            // check for outstanding sales and purchases not made and report error
            if(((PerformFemaleStocking | PerformMaleStocking) && HerdResource.PurchaseIndividuals.Any()) || ((PerformFemaleDestocking | PerformMaleDestocking) && GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.MarkedForSale).Any()))
            {
                string warn = $"Purchases or sales were outstanding at the end of a timestep{Environment.NewLine}Ensure you include a RuminantBuySell component and it has a suitable timer to ensure this outcome is accepted.{Environment.NewLine}Purchases and sales do not carry over between timesteps and this information is lost.";
                Warnings.CheckAndWrite(warn, Summary, this, MessageType.Warning);
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

            // check validity of all purchase individuals specified

            if (purchaseDetails.Count() == 0)
            {
                if ((ManageMaleBreederNumbers & PerformMaleStocking) && MaximumSiresPerPurchase > 0)
                {
                    string[] memberNames = new string[] { "Specify purchased individuals' details" };
                    results.Add(new ValidationResult($"No purchase individual details have been specified by [r=SpecifyRuminant] components below [a={this.Name}]{Environment.NewLine}Add [SpecifyRuminant] components for new Sires or disable purchases [PerformMaleStocking] = False or set [MaximumSiresPerPurchase] to [0]", memberNames));
                }
                if ((ManageFemaleBreederNumbers & PerformFemaleStocking) && MaximumProportionBreedersPerPurchase > 0)
                {
                    string[] memberNames = new string[] { "Specify purchased individuals' details" };
                    results.Add(new ValidationResult($"No purchase individual details have been specified by [r=SpecifyRuminant] components below [a={this.Name}]{Environment.NewLine}Add [SpecifyRuminant] components for new female Breeders or disable purchases [PerformFemaleStocking] = False or set [MaximumProportionBreedersPerPurchase] to [0]", memberNames));
                }
            }
            else
            {
                if (ManageMaleBreederNumbers && PerformMaleStocking)
                {
                    if(ValidateSires() is ValidationResult sires)
                        results.Add(sires);
                    if (GrazeFoodStoreNameSires.Contains("."))
                    {
                        ResourcesHolder resHolder = FindInScope<ResourcesHolder>();
                        if (resHolder is null || resHolder.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreNameSires) is null)
                        {
                            string[] memberNames = new string[] { "GrazeStoreType (paddock) to place purchased sires in" };
                            results.Add(new ValidationResult($"The location where purchased ruminant sires are to be placed [r={GrazeFoodStoreNameSires}] is not found.{Environment.NewLine}Ensure [r=GrazeFoodStore] is present and the [GrazeFoodStoreType] is present", memberNames));
                        }
                    }

                }

                if (ManageFemaleBreederNumbers && PerformFemaleStocking)
                {
                    if (ValidateBreeders() is ValidationResult breeders)
                        results.Add(breeders);
                    if (GrazeFoodStoreNameBreeders.Contains("."))
                    {
                        ResourcesHolder resHolder = FindInScope<ResourcesHolder>();
                        if (resHolder is null || resHolder.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreNameBreeders) is null)
                        {
                            string[] memberNames = new string[] { "GrazeStoreType (paddock) to place purchased breeders in" };
                            results.Add(new ValidationResult($"The location where purchased ruminant breeders are to be placed [r={GrazeFoodStoreNameBreeders}] is not found.{Environment.NewLine}Ensure [r=GrazeFoodStore] is present and the [GrazeFoodStoreType] is present", memberNames));
                        }
                    }
                }

                // unknown entries
                var unknownPurchases = purchaseDetails.Where(f => (f.ExampleRuminant is RuminantFemale) ? !(f.ExampleRuminant as RuminantFemale).IsBreeder : !(f.ExampleRuminant as RuminantMale).IsSire);

                if (unknownPurchases.Any())
                    foreach (var item in unknownPurchases)
                    {
                        string[] memberNames = new string[] { "Invalid purchase details provided" };
                        results.Add(new ValidationResult($"The [r=SpecifyRuminant] component [r={item.SpecifyRuminantComponent.Name}] does not represent a breeding male (sire) or female in [a={this.Name}].{Environment.NewLine}Check this component and remove from the list if unneeded", memberNames));
                    }
            }

            if (EnableGrowoutMaleProperties())
            {
                if (GrazeFoodStoreNameGrowOutMales.Contains("."))
                {
                    ResourcesHolder resHolder = FindInScope<ResourcesHolder>();
                    if (resHolder is null || resHolder.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreNameGrowOutMales) is null)
                    {
                        string[] memberNames = new string[] { "GrazeStoreType (paddock) to place grow out males in" };
                        results.Add(new ValidationResult($"The location where grow out male ruminants are to be moved [r={GrazeFoodStoreNameGrowOutMales}] is not found.{Environment.NewLine}Ensure [r=GrazeFoodStore] is present and the [GrazeFoodStoreType] is present", memberNames));
                    }
                }
            }
            if (EnableGrowoutFemaleProperties())
            {
                if (GrazeFoodStoreNameGrowOutFemales.Contains("."))
                {
                    ResourcesHolder resHolder = FindInScope<ResourcesHolder>();
                    if (resHolder is null || resHolder.FindResourceType<GrazeFoodStore, GrazeFoodStoreType>(this, GrazeFoodStoreNameGrowOutFemales) is null)
                    {
                        string[] memberNames = new string[] { "GrazeStoreType (paddock) to place purchased sires in" };
                        results.Add(new ValidationResult($"The location where grow out female ruminants are to be moved [r={GrazeFoodStoreNameGrowOutFemales}] is not found.{Environment.NewLine}Ensure [r=GrazeFoodStore] is present and the [GrazeFoodStoreType] is present", memberNames));
                    }
                }
            }


            return results;
        }

        private ValidationResult ValidateSires()
        {
            var purchases = purchaseDetails.Where(a => a.ExampleRuminant is RuminantMale && (a.ExampleRuminant as RuminantMale).IsSire);

            if (purchases.Any())
            {
                double sumProportions = 0;
                foreach (var item in purchases)
                {
                    sumProportions += item.SpecifyRuminantComponent.Proportion;
                    item.CummulativeProbability = sumProportions;
                }
                if (Math.Round(sumProportions, 4) != 1)
                {
                    string[] memberNames = new string[] { "Invalid proportions set" };
                    string error = $"The proportions set in each [r=SpecifyRuminant] representing breeding males in [a={this.Name}] do not add up to 1.";
                    return new ValidationResult(error, memberNames);
                }
            }
            else
            {
                if (MaximumSiresKept > 0 && MaximumSiresPerPurchase > 0)
                {
                    string[] memberNames = new string[] { "No breeding males specified" };
                    string error = $"No purchases specified by [r=SpecifyRuminant] in [a={this.Name}] represent sires required by this simulation.{Environment.NewLine}If the purchase of males is not permitted then set [MaximumSiresPerPurchase] to [0] or turn off manage breeding males";
                    return new ValidationResult(error, memberNames);
                }
            }
            return null;
        }

        private ValidationResult ValidateBreeders()
        {
            var purchases = purchaseDetails.Where(a => a.ExampleRuminant is RuminantFemale && (a.ExampleRuminant as RuminantFemale).IsBreeder);

            if (purchases.Any())
            {
                double sumProportions = 0;
                foreach (var item in purchases)
                {
                    sumProportions += item.SpecifyRuminantComponent.Proportion;
                    item.CummulativeProbability = sumProportions;
                }
                if (Math.Round(sumProportions, 4) != 1)
                {
                    string[] memberNames = new string[] { "Invalid proportions set" };
                    var error = $"The proportions set in each [r=SpecifyRuminant] representing breeding females in [a={this.Name}] do not add up to 1.";
                    return new ValidationResult(error, memberNames);
                }
            }
            else
            {
                if (MaximumProportionBreedersPerPurchase > 0)
                {
                    string[] memberNames = new string[] { "No breeding females specified" };
                    var error = $"No purchases specified by [r=SpecifyRuminant] in [a={this.Name}] represent breeding females required by this simulation.{Environment.NewLine}If the purchase of females is not permitted then set [MaximumProportionBreedersPerPurchase] to [0] or turn off manage breeding females";
                    return new ValidationResult(error, memberNames);
                }
            }
            return null;
        }

        #endregion

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            List<string> notBeingMarked = new List<string>();

            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write("\r\n<div class=\"activityentry\">");
                htmlWriter.Write($"This activity will stop the simulation if the number of breeders exceeds the <i>Maximum number of breeders to stop multiplier</i> <span class=\"setvalue\">{MaxBreedersMultiplierToStop:#0.###}</span> x <i>Maximum number of breeders</i> which equates to <span class=\"setvalue\">{MaxBreedersMultiplierToStop * MaximumBreedersKept:#,###}</span> individuals.");
                htmlWriter.Write("</div>");

                // adjust herd
                if (AdjustBreedingFemalesAtStartup | AdjustBreedingMalesAtStartup)
                {
                    htmlWriter.Write("\r\n<div class=\"activitybannerlight\">Initial herd</div>");
                    htmlWriter.Write("\r\n<div class=\"activitycontentlight\">");
                    string adjusted = "";
                    if (AdjustBreedingFemalesAtStartup & AdjustBreedingMalesAtStartup)
                        adjusted = "females and males";
                    else
                    {
                        if (AdjustBreedingFemalesAtStartup)
                            adjusted = "females";
                        else
                            adjusted = "males";
                    }
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write($"The number of breeding <span class=\"setvalue\">{adjusted}</span> will be adjusted to the maximum herd by scaling the initial ruminant cohorts at the start of the simulation.");
                    htmlWriter.Write("</div>");
                    htmlWriter.Write("</div>");
                }

                htmlWriter.Write("\r\n<div class=\"activitybannerlight\">Breeding females</div>");
                htmlWriter.Write("\r\n<div class=\"activitycontentlight\">");

                // does controlled mating exist in simulation
                var zone = this.FindAncestor<Zone>();
                bool cmate = zone?.FindDescendant<RuminantActivityControlledMating>() != null;

                if (ManageFemaleBreederNumbers && (PerformFemaleStocking | PerformFemaleDestocking))
                {
                    double minimumBreedersKept = Math.Min(MinimumBreedersKept, MaximumBreedersKept);
                    int maxBreed = Math.Max(MinimumBreedersKept, MaximumBreedersKept);
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("The herd will be maintained");
                    if (!PerformFemaleStocking | (minimumBreedersKept == 0))
                        htmlWriter.Write(" using only natural recruitment up to <span class=\"setvalue\">" + MaximumBreedersKept.ToString("#,###") + "</span> breeders");
                    else
                    { 
                        if (minimumBreedersKept == maxBreed)
                            htmlWriter.Write(" with breeder purchases and natural recruitment up to <span class=\"setvalue\">" + minimumBreedersKept.ToString("#,###") + "</span > breeders");
                        else
                            htmlWriter.Write(" with breeder purchases up to <span class=\"setvalue\">" + minimumBreedersKept.ToString("#,###") + "</span > and only natural recruitment to <span class=\"setvalue\">" + maxBreed.ToString("#,###") + "</span> breeders");
                    }
                    if(!PerformFemaleDestocking)
                    {
                        htmlWriter.Write(" with <b>No</b> destocking through sales");
                    }
                    htmlWriter.Write("</div>");

                    if (PerformFemaleDestocking)
                    {
                        htmlWriter.Write("\r\n<div class=\"activityentry\">");
                        if (MarkOldBreedersForSale)
                        {
                            htmlWriter.Write("Individuals will be sold when over <span class=\"setvalue\">" + MaximumBreederAge.ToString("###") + "</span> months old");
                            if (ReturnPregnantMaxAgeToHerd)
                                htmlWriter.Write(" unless pregant and the herd is below the required level");
                        }
                        else
                        {
                            htmlWriter.Write($"Old breeders will <b>NOT</b> be marked for sale<sup>*</sup>");
                            notBeingMarked.Add("Sell old female breeders");
                        }
                        htmlWriter.Write("</div>");
                    }

                    if(PerformFemaleStocking)
                    { 
                        if (MaximumProportionBreedersPerPurchase < 1 & minimumBreedersKept > 0)
                        {
                            htmlWriter.Write("\r\n<div class=\"activityentry\">");
                            htmlWriter.Write("A maximum of <span class=\"setvalue\">" + MaximumProportionBreedersPerPurchase.ToString("#0.##%") + "</span> of the Minimum Breeders Kept equal to <span class=\"setvalue\">" + (MaximumProportionBreedersPerPurchase * minimumBreedersKept).ToString("#,###") + "</span > can be purchased in a single transaction");
                            htmlWriter.Write("</div>");
                        }
                        htmlWriter.Write("\r\n<div class=\"activityentry\">");
                        htmlWriter.Write("Purchased breeders will be placed in ");

                        if (GrazeFoodStoreNameBreeders == null || GrazeFoodStoreNameBreeders == "")
                            htmlWriter.Write("<span class=\"resourcelink\">General yards</span>");
                        else
                        {
                            htmlWriter.Write("<span class=\"resourcelink\">" + GrazeFoodStoreNameBreeders + "</span>");
                            if (MinimumPastureBeforeRestock > 0)
                                htmlWriter.Write($" with no restocking while pasture is below <span class=\"setvalue\">{MinimumPastureBeforeRestock}</span> kg/ha");
                        }
                        htmlWriter.Write("</div>");

                        if (!cmate && GrazeFoodStoreNameBreeders != "" && GrazeFoodStoreNameBreeders == GrazeFoodStoreNameSires)
                            htmlWriter.Write($"<div class=\"warningbanner\">Uncontrolled mating will occur as soon as Breeders and Sires are placed in <span class=\"resourcelink\">{GrazeFoodStoreNameBreeders}</span>.</div>");
                    }
                    else
                    {
                        htmlWriter.Write("\r\n<div class=\"activityentry\">");
                        htmlWriter.Write("This activity is <b>NOT</b> stocking any breeding females");
                        htmlWriter.Write("</div>");
                    }
                }
                else
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("This activity is <b>NOT</b> currently managing breeding females");
                    htmlWriter.Write("</div>");
                }

                htmlWriter.Write("</div>");

                htmlWriter.Write("\r\n<div class=\"activitybannerlight\">Breeding males (sires/rams etc)</div>");
                htmlWriter.Write("\r\n<div class=\"activitycontentlight\">");

                if (ManageMaleBreederNumbers && (PerformMaleStocking | PerformMaleDestocking))
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    if (MaximumSiresKept == 0)
                        htmlWriter.Write("No breeding sires will be kept");
                    else if (MaximumSiresKept < 1)
                        htmlWriter.Write("The number of breeding males will be determined as <span class=\"setvalue\">" + MaximumSiresKept.ToString("###%") + "</span> of the maximum female breeder herd. Currently <span class=\"setvalue\">" + (Convert.ToInt32(Math.Ceiling(MaximumBreedersKept * MaximumSiresKept), CultureInfo.InvariantCulture).ToString("#,##0")) + "</span> individuals");
                    else
                        htmlWriter.Write("A maximum of <span class=\"setvalue\">" + MaximumSiresKept.ToString("#,###") + "</span> will be maintained");
                    htmlWriter.Write("</div>");

                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    if (PerformMaleDestocking)
                    {
                        if (MarkOldSiresForSale)
                            htmlWriter.Write("Individuals will be sold when over <span class=\"setvalue\">" + MaximumSireAge.ToString("###") + "</span> months old");
                        else
                        {
                            htmlWriter.Write($"Old sires will <b>NOT</b> be marked for sale<sup>*</sup>{((MaximumSiresKept == 0) ? " as maximum sires kept is class=\"setvalue\">" + MaximumSiresKept.ToString("#,###") + "</span>" : "")}");
                            notBeingMarked.Add("Sell old male breeders");
                        }
                    }
                    else
                        htmlWriter.Write("This activity is <b>NOT</b> destocking any breeding males");
                    htmlWriter.Write("</div>");

                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    if (PerformMaleDestocking)
                    {
                        htmlWriter.Write("\r\n<div class=\"activityentry\">");
                        htmlWriter.Write("Purchased sires will be placed in ");
                        if (GrazeFoodStoreNameSires == null || GrazeFoodStoreNameSires == "")
                            htmlWriter.Write("<span class=\"resourcelink\">General yards</span>");
                        else
                        {
                            htmlWriter.Write("<span class=\"resourcelink\">" + GrazeFoodStoreNameSires + "</span>");
                            if (MinimumPastureBeforeRestock > 0)
                                htmlWriter.Write($" with no restocking while pasture is below <span class=\"setvalue\">{MinimumPastureBeforeRestock}</span> kg/ha");
                        }
                    }
                    else
                        htmlWriter.Write("This activity is <b>NOT</b> stocking any breeding males");
                    htmlWriter.Write("</div>");
                }
                else
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("This activity is <b>NOT</b> currently managing breeding males");
                    htmlWriter.Write("</div>");
                }
                htmlWriter.Write("</div>");

                htmlWriter.Write("\r\n<div class=\"activitybannerlight\">General herd</div>");
                htmlWriter.Write("\r\n<div class=\"activitycontentlight\">");
                if(GrowOutYoungMales)
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write($"Young males will be managed for grow out");
                    htmlWriter.Write("</div>");

                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    if (!PerformMaleDestocking | !MarkAgeWeightMalesForSale)
                    {
                        htmlWriter.Write("Grow out males are <b>NOT</b> marked for sale");
                        notBeingMarked.Add("Sell grow out males reaching age or weight");
                    }
                    else
                    {
                        if(MaleSellingAge + MaleSellingWeight > 0)
                        {
                            htmlWriter.Write("Grow out males will be sold when <span class=\"setvalue\">" + MaleSellingAge.ToString("###") + "</span> months old or <span class=\"setvalue\">" + MaleSellingWeight.ToString("#,###") + "</span> kg");
                        }
                        else
                        {
                            htmlWriter.Write($"Grow out males will <b>NOT</b> be marked for sale<sup>*</sup> {((MaleSellingAge + MaleSellingWeight > 0) ? " as no age or weight for sale has been defined" : "")}");
                            notBeingMarked.Add("Sell grow out males reaching age or weight");
                        }
                    }
                    htmlWriter.Write("</div>");

                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("Grow-out males will be placed in ");
                    if (GrazeFoodStoreNameGrowOutMales == null || GrazeFoodStoreNameGrowOutMales == "")
                        htmlWriter.Write("<span class=\"resourcelink\">General yards</span>");
                    else
                        htmlWriter.Write("<span class=\"resourcelink\">" + GrazeFoodStoreNameGrowOutMales + "</span>");
                    htmlWriter.Write("</div>");
                }
                else
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("Growing out and sale of young males is <b>NOT</b> being performed by this activity");
                    htmlWriter.Write("</div>");
                }

                if (GrowOutYoungFemales)
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write($"Young females will be managed as grow out or a breeder reserve {((GrowOutYoungMales)?" with the grow out males":"")}");
                    htmlWriter.Write("</div>");

                    if (!PerformFemaleDestocking | !MarkAgeWeightFemalesForSale)
                    {
                        htmlWriter.Write("Grow out females are <b>NOT</b> marked for sale</div>");
                        notBeingMarked.Add("Sell grow out females reaching age or weight");
                    }
                    else
                    {
                        if (FemaleSellingAge + FemaleSellingWeight > 0)
                        {
                            htmlWriter.Write("Grow out females will be sold when <span class=\"setvalue\">" + FemaleSellingAge.ToString("###") + "</span> months old or <span class=\"setvalue\">" + FemaleSellingWeight.ToString("#,###") + "</span> kg");
                        }
                        else
                        {
                            htmlWriter.Write($"Grow out females will <b>NOT</b> be marked for sale<sup>*</sup> {((FemaleSellingAge + FemaleSellingWeight > 0) ? " as no age or weight for sale has been defined" : "")}");
                            notBeingMarked.Add("Sell grow out females reaching age or weight");
                        }
                    }

                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("Grow-out females will be placed in ");
                    if (GrazeFoodStoreNameGrowOutFemales == null || GrazeFoodStoreNameGrowOutFemales == "")
                        htmlWriter.Write("<span class=\"resourcelink\">General yards</span>");
                    else
                        htmlWriter.Write("<span class=\"resourcelink\">" + GrazeFoodStoreNameGrowOutFemales + "</span>");
                    htmlWriter.Write("</div>");

                    if (!cmate && GrazeFoodStoreNameGrowOutFemales != "" & !CastrateMales && GrazeFoodStoreNameGrowOutFemales == GrazeFoodStoreNameGrowOutMales)
                        htmlWriter.Write($"<div class=\"warningbanner\">Uncontrolled mating may occur in grow out females and males if allowed to mature before sales as they are placed in <span class=\"resourcelink\">{GrazeFoodStoreNameGrowOutFemales}</span> if using natural mating.</div>");
                }
                else
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("Growing out and sale of young females is <b>NOT</b> being performed by this activity");
                    htmlWriter.Write("</div>");
                }


                if (GrowOutYoungFemales | GrowOutYoungMales)
                {
                    if (ContinuousGrowOutSales)
                    {
                        htmlWriter.Write("\r\n<div class=\"activityentry\">");
                        htmlWriter.Write("Grow out age/weight sales will be performed in any month where conditions are met");
                        htmlWriter.Write("</div>");
                    }
                    else
                    {
                        htmlWriter.Write("\r\n<div class=\"activityentry\">");
                        htmlWriter.Write("Grow out age/weight sales will only be performed when this activity is due");
                        htmlWriter.Write("</div>");
                    }
                }

                if (GrowOutYoungMales & CastrateMales)
                {
                    htmlWriter.Write("\r\n<div class=\"activityentry\">");
                    htmlWriter.Write("Young males will be castrated (e.g. create steers or bullocks)");
                    htmlWriter.Write("</div>");
                }

                htmlWriter.Write("</div>");

                if (notBeingMarked.Any())
                    htmlWriter.Write($"<div class=\"clearfix warningbanner\">* This activity is not performing all mark for sale tasks. The following tasks can be enabled or handled elsewhere: <span class=\"highlightdiv\">{string.Join("</span><span class=\"highlightdiv\">", notBeingMarked)}</span></div>");

                return htmlWriter.ToString(); 
            }
        }

        /// <inheritdoc/>
        public override List<(IEnumerable<IModel> models, bool include, string borderClass, string introText, string missingText)> GetChildrenInSummary()
        {
            var childList = new List<(IEnumerable<IModel> models, bool include, string borderClass, string introText, string missingText)>();

            var identifiers = LocateCompanionModels<RuminantGroup>().Select(a => a.Key);
            foreach (var identifier in identifiers)
            {
                childList.Add((FindAllChildren<RuminantGroup>().Where(a => a.Identifier == identifier), true, "activitygroupsborder", $"Individuals selected for {identifier} will be futher refined by:", ""));
            }
            childList.Add((FindAllChildren<SpecifyRuminant>(), true, "childgroupactivityborder", "The following SpecifyRuminant components will define the individuals to be purchased (Breeding males and females):", ""));
            return childList;   
        }

        /// <inheritdoc/>
        public override string ModelSummaryInnerClosingTags()
        {
            return "";
        }

        /// <inheritdoc/>
        public override string ModelSummaryInnerOpeningTags()
        {
            return "";
        } 
        #endregion

    }
}
