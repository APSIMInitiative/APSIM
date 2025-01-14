using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF.Interfaces;
using Models.PMF.Organs;
using Models.PMF.Phen;
using Newtonsoft.Json;

namespace Models.PMF
{
    /// <summary>
    /// The model has been developed using the Plant Modelling Framework (PMF) of [brown_plant_2014]. This
    /// new framework provides a library of plant organ and process submodels that can be coupled, at runtime, to construct a
    /// model in much the same way that models can be coupled to construct a simulation.This means that dynamic composition
    /// of lower level process and organ classes(e.g.photosynthesis, leaf) into larger constructions(e.g.maize, wheat,
    /// sorghum) can be achieved by the model developer without additional coding.
    /// </summary>
    [ValidParent(ParentType = typeof(Zone))]
    [Serializable]
    [ScopedModel]
    public class Plant : Model, IPlant, IPlantDamage
    {
        /// <summary>The summary</summary>
        [Link]
        private ISummary summary = null;

        /// <summary> Clock </summary>
        [Link]
        private IClock clock = null;

        /// <summary>The plant mortality rate</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("")]
        private IFunction mortalityRate = null;

        /// <summary>The seed mortality rate.</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("")]
        private IFunction seedMortalityRate = null;

        /// <summary>The phenology</summary>
        [Link(Type = LinkType.Child)]
        public Phenology Phenology = null;

        /// <summary>The arbitrator</summary>
        [Link(IsOptional = true)]
        public IArbitrator Arbitrator = null;

        /// <summary>The structure</summary>
        [Link(IsOptional = true)]
        public IStructure structure = null;

        /// <summary>The leaf</summary>
        [Link(IsOptional = true)]
        public ICanopy Leaf = null;

        /// <summary>The root</summary>
        [Link(IsOptional = true)]
        public IRoot Root = null;

        /// <summary>Above ground weight</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        public IBiomass AboveGround { get; set; }

        /// <summary>Plant organs.</summary>
        [Link]
        private IOrgan[] Organs { get; set; }

        /// <summary>Above ground weight</summary>
        public IBiomass AboveGroundHarvestable { get { return AboveGround; } }

        /// <summary>Used by several organs to determine the type of crop.</summary>
        public string PlantType { get; set; }

        /// <summary>The sowing data</summary>
        [JsonIgnore]
        public SowingParameters SowingData { get; set; } = new SowingParameters();

        /// <summary>Current cultivar.</summary>
        private Cultivar cultivarDefinition = null;


        /// <summary>Gets a list of cultivar names</summary>
        public string[] CultivarNames
        {
            get
            {
                return new SortedSet<string>(FindAllDescendants<Cultivar>().SelectMany(c => c.GetNames())).ToArray();
            }
        }

        /// <summary>Holds the number of plants.</summary>
        private double plantPopulation = 0.0;
        /// <summary>
        /// Holds the date of sowing
        /// </summary>
        [JsonIgnore]
        public DateTime SowingDate { get; set; }

        /// <summary>Gets or sets the plant population.</summary>
        [JsonIgnore]
        [Description("Number of plants per meter2")]
        [Units("/m2")]
        public double Population
        {
            get { return plantPopulation; }
            set
            {
                double InitialPopn = plantPopulation;
                if (IsAlive && value <= 0.01)
                    EndCrop();  // the plant is dying due to population decline
                else
                {
                    plantPopulation = value;
                    if (structure != null)
                    {
                        structure.DeltaPlantPopulation = InitialPopn - value;
                        structure.ProportionPlantMortality = 1 - (value / InitialPopn);
                    }
                }
            }
        }

        /// <summary>Return true if plant is alive and in the ground.</summary>
        public bool IsAlive { get; private set; }

        /// <summary>Return true if plant has emerged</summary>
        public bool IsEmerged
        {
            get
            {
                if (Phenology is Phenology phenology)
                    return phenology.Emerged;
                return false;
            }
        }

        /// <summary>Returns true if the crop is ready for harvesting</summary>
        public bool IsReadyForHarvesting
        {
            get
            {
                return Phenology.CurrentPhase is EndPhase;
            }
        }

        /// <summary>
        /// Number of days after sowing.
        /// </summary>
        [Units("d")]
        public int DaysAfterSowing
        {
            get
            {
                if (SowingData == null || SowingDate == DateTime.MinValue)
                    return 0;

                return Convert.ToInt32((clock.Today - SowingDate).TotalDays);
            }
        }

        /// <summary>
        /// Total plant green cover from all organs
        /// </summary>
        [Units("-")]
        public double CoverGreen
        {
            get
            {
                double cover = 0;
                foreach (ICanopy canopy in this.FindAllDescendants<ICanopy>())
                    cover = 1 - (1.0 - cover) * (1.0 - canopy.CoverGreen);
                return cover;
            }
        }

        /// <summary>
        /// Total plant cover from all organs
        /// </summary>
        [Units("-")]
        public double CoverTotal
        {
            get
            {
                double cover = 0;
                foreach (ICanopy canopy in this.FindAllDescendants<ICanopy>())
                    cover = 1 - (1.0 - cover) * (1.0 - canopy.CoverTotal);
                return cover;
            }
        }
        /// <summary>Leaf area index.</summary>
        [Units("m^2/m^2")]
        public double LAI
        {
            get
            {
                var leaf = Organs.FirstOrDefault(o => o is Leaf) as Leaf;
                if (leaf != null)
                    return leaf.LAI;

                var simpleLeaf = Organs.FirstOrDefault(o => o is SimpleLeaf) as SimpleLeaf;
                if (simpleLeaf != null)
                    return simpleLeaf.LAI;

                var perennialLeaf = Organs.FirstOrDefault(o => o is PerennialLeaf) as PerennialLeaf;
                if (perennialLeaf != null)
                    return perennialLeaf.LAI;

                return 0;
            }
        }

        /// <summary>The sw uptake</summary>
        public IReadOnlyList<double> WaterUptake => Root == null ? null : Root.SWUptakeLayered;

        /// <summary>The nitrogen uptake</summary>
        public IReadOnlyList<double> NitrogenUptake => Root == null ? null : Root.NUptakeLayered;

        /// <summary>Occurs when a plant is about to be sown.</summary>
        public event EventHandler Sowing;
        /// <summary>Occurs when a plant is sown.</summary>
        public event EventHandler<SowingParameters> PlantSowing;
        /// <summary>Occurs when a plant is about to be harvested so that values can be reported.</summary>
        public event EventHandler Harvesting;
        /// <summary>Occurs when a plant is harvested, this event should trigger functions that remove biomass from organs and reset values</summary>
        public event EventHandler PostHarvesting;
        /// <summary>Occurs when a plant is ended via EndCrop.</summary>
        public event EventHandler PlantEnding;
        /// <summary>Occurs when a plant is about to flower</summary>
        public event EventHandler Flowering;
        /// <summary>Occurs when a plant is about to start pod development</summary>
        public event EventHandler StartPodDevelopment;

        /// <summary>Things the plant model does when the simulation starts</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            Clear();
            IEnumerable<string> duplicates = CultivarNames.GroupBy(x => x).Where(g => g.Count() > 1).Select(x => x.Key);
            if (duplicates.Count() > 0)
                throw new Exception("Duplicate Names in " + this.Name + " has duplicate cultivar names " + string.Join(",", duplicates));
        }

        /// <summary>Called when [phase changed].</summary>
        /// <param name="phaseChange">The phase change.</param>
        /// <param name="sender">Sender plant.</param>
        [EventSubscribe("PhaseChanged")]
        private void OnPhaseChanged(object sender, PhaseChangedType phaseChange)
        {
            if (Phenology.CurrentPhase.Start == "Flowering")
                Flowering?.Invoke(this, null);
            if (Phenology.CurrentPhase.Start == "StartPodDevelopment")
                StartPodDevelopment?.Invoke(this, null);
        }

        /// <summary>Event from sequencer telling us to do our potential growth.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        private void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            //Reduce plant population in case of mortality
            if (Population > 0.0)
                Population -= Population * mortalityRate.Value();

            // Seed mortality
            if (!IsEmerged && SowingData != null && SowingData.Seeds > 0)
                Population -= Population * seedMortalityRate.Value();
        }

        /// <summary>Sow the crop with the specified parameters.</summary>
        /// <param name="cultivar">The cultivar.</param>
        /// <param name="population">The final plant population at emergence.</param>
        /// <param name="depth">The depth mm.</param>
        /// <param name="rowSpacing">The row spacing mm.</param>
        /// <param name="maxCover">The maximum cover.</param>
        /// <param name="budNumber">The bud number.</param>
        /// <param name="rowConfig">SkipRow configuration.</param>
        /// <param name="seeds">The number of seeds sown (/m2).</param>
        /// <param name="tillering">tillering method (-1, 0, 1).</param>
        /// <param name="ftn">Fertile Tiller Number.</param>
        public void Sow(string cultivar, double population, double depth, double rowSpacing, double maxCover = 1, double budNumber = 1, double rowConfig = 0, double seeds = 0, int tillering = 0, double ftn = 0.0)
        {
            SowingDate = clock.Today;

            SowingData = new SowingParameters();
            SowingData.Plant = this;
            SowingData.Population = population;
            SowingData.Depth = depth;
            SowingData.Cultivar = cultivar;
            SowingData.MaxCover = maxCover;
            SowingData.BudNumber = budNumber;
            SowingData.RowSpacing = rowSpacing;
            SowingData.SkipType = rowConfig;
            SowingData.Seeds = seeds;
            SowingData.TilleringMethod = tillering;
            SowingData.FTN = ftn;

            if (SowingData.Seeds != 0 && SowingData.Population != 0)
                throw new Exception("Cannot specify both plant population and number of seeds when sowing.");

            if (SowingData.TilleringMethod < -1 || SowingData.TilleringMethod > 1)
                throw new Exception("Invalid TilleringMethod set in sowingData.");

           // if (SowingData.TilleringMethod != 0 && SowingData.FTN > 0.0)
            //    throw new Exception("Cannot set a FertileTillerNumber when TilleringMethod is not set to FixedTillering.");

            if (rowConfig == 0)
            {
                // No skip row
                SowingData.SkipPlant = 1.0;
                SowingData.SkipRow = 0.0;
            }
            if (rowConfig == 1)
            {
                // Alternate rows (plant 1 – skip 1)
                SowingData.SkipPlant = 1.0;
                SowingData.SkipRow = 1.0;
            }
            if (rowConfig == 2)
            {
                // Planting two rows and skipping one row (plant 2 – skip 1)
                SowingData.SkipPlant = 2.0;
                SowingData.SkipRow = 1.0;
            }
            if (rowConfig == 3)
            {
                // Alternate pairs of rows (plant 2 – skip 2)
                SowingData.SkipPlant = 2.0;
                SowingData.SkipRow = 2.0;
            }

            // Adjusting number of plant per meter in each row
            SowingData.SkipDensityScale = 1.0 + SowingData.SkipRow / SowingData.SkipPlant;

            IsAlive = true;

            if (population > 0)
                this.Population = population;
            else
                this.Population = SowingData.Population = seeds;

            // Find cultivar and apply cultivar overrides.
            cultivarDefinition = FindAllDescendants<Cultivar>().FirstOrDefault(c => c.IsKnownAs(SowingData.Cultivar));
            if (cultivarDefinition == null)
                throw new ApsimXException(this, $"Cannot find a cultivar definition for '{SowingData.Cultivar}'");

            cultivarDefinition.Apply(this);

            // Invoke an AboutToSow event.
            if (Sowing != null)
                Sowing.Invoke(this, new EventArgs());

            // Invoke a sowing event.
            if (PlantSowing != null)
                PlantSowing.Invoke(this, SowingData);

            summary.WriteMessage(this, string.Format("A crop of " + PlantType + " (cultivar = " + cultivar + ") was sown today at a population of " + Population + " plants/m2 with " + budNumber + " buds per plant at a row spacing of " + rowSpacing + " mm and a depth of " + depth + " mm"), MessageType.Information);
        }

        /// <summary>Harvest the crop.</summary>
        public void Harvest(bool removeBiomassFromOrgans = true)
        {
            Phenology.SetToEndStage();
            Harvesting?.Invoke(this, EventArgs.Empty);
            if (removeBiomassFromOrgans)
                PostHarvesting?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>End the crop.</summary>
        public void EndCrop()
        {
            if (IsAlive == false)
                throw new Exception("EndCrop method called when no crop is planted.  Either your planting rule is not working or your end crop is happening at the wrong time");
            summary.WriteMessage(this, "Crop ending", MessageType.Information);

            // Undo cultivar changes.
            cultivarDefinition.Unapply();
            // Invoke a plant ending event.
            if (PlantEnding != null)
                PlantEnding.Invoke(this, new EventArgs());

            Clear();
        }

        /// <summary>Clears this instance.</summary>
        private void Clear()
        {
            SowingData = new SowingParameters();
            plantPopulation = 0.0;
            IsAlive = false;
            SowingDate = DateTime.MinValue;
        }

        /// <summary>
        /// Reduce the plant population.
        /// </summary>
        /// <param name="newPlantPopulation">The new plant population.</param>
        public void ReducePopulation(double newPlantPopulation)
        {
            double InitialPopn = plantPopulation;
            if (IsAlive && newPlantPopulation <= 0.01)
                EndCrop();  // the plant is dying due to population decline
            else
            {
                plantPopulation = newPlantPopulation;
                if (structure != null)
                {
                    structure.DeltaPlantPopulation = InitialPopn - newPlantPopulation;
                    structure.ProportionPlantMortality = 1 - (newPlantPopulation / InitialPopn);
                }
            }
        }
    }
}
