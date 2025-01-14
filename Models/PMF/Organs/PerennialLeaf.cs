using System;
using System.Collections.Generic;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF.Interfaces;
using Models.PMF.Library;
using Models.PMF.Phen;
using Models.PMF.Struct;
using Models.Soils.Arbitrator;
using Newtonsoft.Json;

namespace Models.PMF.Organs
{
    /// <summary>
    /// This organ is parameterised using a simple leaf organ type which provides the core functions of intercepting radiation, providing a photosynthesis supply and a transpiration demand.  It also calculates the growth, senescence and detachment of leaves.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class PerennialLeaf : Model, IOrgan, ICanopy, IArbitration, IHasWaterDemand, IOrganDamage, IHasDamageableBiomass
    {
        /// <summary>The met data</summary>
        [Link]
        public IWeather MetData = null;

        /// <summary>
        /// The plant
        /// </summary>
        [Link]
        private Plant plant = null;

        /// <summary>Carbon concentration</summary>
        /// [Units("-")]
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction CarbonConcentration = null;

        /// <summary>Gets the cohort live.</summary>
        [JsonIgnore]
        [Units("g/m^2")]
        public Biomass Live => cohort.GetLive();

        /// <summary>Gets the cohort live.</summary>
        [JsonIgnore]
        [Units("g/m^2")]
        public Biomass Dead => cohort.GetDead();

        /// <summary>Gets a value indicating whether the biomass is above ground or not</summary>
        public bool IsAboveGround { get { return true; } }

        /// <summary>The plant</summary>
        [Link]
        protected Plant Plant = null;

        /// <summary>The surface organic matter model</summary>
        [Link]
        protected ISurfaceOrganicMatter SurfaceOrganicMatter = null;

        /// <summary>The summary</summary>
        [Link]
        protected ISummary Summary = null;

        /// <summary>Link to biomass removal model</summary>
        [Link(Type = LinkType.Child)]
        public BiomassRemoval biomassRemovalModel = null;

        /// <summary>The dry matter supply</summary>
        public BiomassSupplyType DMSupply { get; set; }

        /// <summary>The nitrogen supply</summary>
        public BiomassSupplyType NSupply { get; set; }

        /// <summary>The dry matter demand</summary>
        public BiomassPoolType DMDemand { get; set; }

        /// <summary>Structural nitrogen demand</summary>
        public BiomassPoolType NDemand { get; set; }

        /// <summary>The amount of mass lost each day from maintenance respiration</summary>
        public double MaintenanceRespiration { get; set; }

        /// <summary>Growth Respiration</summary>
        public double GrowthRespiration { get; set; }

        /// <summary>Gets the DM amount removed from the system (harvested, grazed, etc) (g/m2)</summary>
        [JsonIgnore]
        public Biomass Removed = new Biomass();

        /// <summary>Gets the DM amount detached (sent to soil/surface organic matter) (g/m2)</summary>
        [JsonIgnore]
        public Biomass Detached { get; set; }

        #region Canopy interface

        /// <summary>Gets the canopy. Should return null if no canopy present.</summary>
        public string CanopyType { get { return Plant.PlantType; } }

        /// <summary>Albedo.</summary>
        [Description("Albedo")]
        public double Albedo { get; set; }

        /// <summary>Gets or sets the gsmax.</summary>
        [Description("Daily maximum stomatal conductance(m/s)")]
        public double Gsmax
        {
            get { return Gsmax350 * FRGR * StomatalConductanceCO2Modifier.Value(); }
        }

        /// <summary>Gets or sets the gsmax.</summary>
        [Description("Maximum stomatal conductance at CO2 concentration of 350 ppm (m/s)")]
        public double Gsmax350 { get; set; }

        /// <summary>Gets or sets the R50.</summary>
        [Description("R50")]
        public double R50 { get; set; }

        /// <summary>Gets the LAI</summary>
        [Units("m^2/m^2")]
        public double LAI
        {
            get
            {
                return cohort.Lai;
            }
            set
            {
                cohort.SetLai(value);
            }
        }

        /// <summary>Gets the LAI live + dead (m^2/m^2)</summary>
        public double LAITotal { get { return LAI + LAIDead; } }

        /// <summary>Gets the SLA</summary>
        public double SpecificLeafArea { get { return MathUtilities.Divide(LAI, Live.Wt, 0.0); } }

        /// <summary>Gets the cover green.</summary>
        [Units("0-1")]
        public double CoverGreen
        {
            get
            {
                if (Plant.IsAlive)
                {
                    double greenCover = 1.0 - Math.Exp(-ExtinctionCoefficient.Value() * LAI);
                    return Math.Min(Math.Max(greenCover, 0.0), 0.999999999); // limiting to within 10^-9, so MicroClimate doesn't complain
                }
                else
                    return 0.0;
            }
        }

        /// <summary>Gets the cover total.</summary>
        [Units("0-1")]
        public double CoverTotal
        {
            get { return 1.0 - (1 - CoverGreen) * (1 - CoverDead); }
        }

        /// <summary>Gets or sets the height.</summary>
        [Units("mm")]
        public double Height { get; set; }
        /// <summary>Gets the depth.</summary>
        [Units("mm")]
        public double Depth { get { return Height; } }

        /// <summary>Gets the width of the canopy (mm).</summary>
        public double Width { get { return 0; } }

        /// <summary>Gets or sets the FRGR.</summary>
        [Units("mm")]
        public double FRGR { get; set; }

        private double _PotentialEP = 0;
        /// <summary>Sets the potential evapotranspiration. Set by MICROCLIMATE.</summary>
        [Units("mm")]
        public double PotentialEP
        {
            get { return _PotentialEP; }
            set { _PotentialEP = value; }
        }

        /// <summary>Sets the actual water demand.</summary>
        [Units("mm")]
        public double WaterDemand { get; set; }


        /// <summary>Sets the light profile. Set by MICROCLIMATE.</summary>
        public CanopyEnergyBalanceInterceptionlayerType[] LightProfile { get; set; }
        #endregion

        #region Parameters
        /// <summary>The FRGR function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction FRGRFunction = null;
        /// <summary>The effect of CO2 on stomatal conductance</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction StomatalConductanceCO2Modifier = null;



        /// <summary>The DM demand function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/m2/d")]
        private NutrientPoolFunctions dmDemands = null;

        /// <summary>The N demand function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/m2/d")]
        private NutrientPoolFunctions nDemands = null;

        /// <summary>The extinction coefficient function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction ExtinctionCoefficient = null;
        /// <summary>The extinction coefficient function for dead leaves</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction ExtinctionCoefficientDead = null;
        /// <summary>The photosynthesis</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction Photosynthesis = null;
        /// <summary>The height function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction HeightFunction = null;
        /// <summary>Leaf Residence Time</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction LeafResidenceTime = null;
        /// <summary>Leaf Development Rate</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction LeafDevelopmentRate = null;
        /// <summary>Leaf Death</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction LeafKillFraction = null;
        /// <summary>Minimum LAI</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction MinimumLAI = null;
        /// <summary>Leaf Detachment Time</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction LeafDetachmentTime = null;
        /// <summary>SpecificLeafArea</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction SpecificLeafAreaFunction = null;

        /// <summary>
        /// This encapsulates the leaves.
        /// </summary>
        private Cohorts cohort = new Cohorts();

        /// <summary>The structure</summary>
        [Link(IsOptional = true)]
        public Structure Structure = null;
        /// <summary>The phenology</summary>
        [Link(IsOptional = true)]
        public Phenology Phenology = null;

        #endregion

        #region States and variables

        /// <summary>Calculate the water demand.</summary>
        public double CalculateWaterDemand()
        {
            return WaterDemand;
        }
        /// <summary>Gets the transpiration.</summary>
        public double Transpiration { get { return WaterAllocation; } }

        /// <summary>Gets or sets the n fixation cost.</summary>
        [JsonIgnore]
        public double NFixationCost { get { return 0; } }
        /// <summary>Gets or sets the water supply.</summary>
        /// <param name="zone">The zone.</param>
        public double[] WaterSupply(ZoneWaterAndN zone) { return null; }
        /// <summary>Does the water uptake.</summary>
        /// <param name="Amount">The amount.</param>
        /// <param name="zoneName">Zone name to do water uptake in</param>
        public void DoWaterUptake(double[] Amount, string zoneName) { }
        /// <summary>Gets the nitrogen supply from the specified zone.</summary>
        /// <param name="zone">The zone.</param>
        /// <param name="NO3Supply">The returned NO3 supply</param>
        /// <param name="NH4Supply">The returned NH4 supply</param>
        public void CalcNSupply(ZoneWaterAndN zone, out double[] NO3Supply, out double[] NH4Supply)
        {
            NO3Supply = null;
            NH4Supply = null;
        }

        /// <summary>Does the Nitrogen uptake.</summary>
        /// <param name="zonesFromSoilArbitrator">List of zones from soil arbitrator</param>
        public void DoNitrogenUptake(List<ZoneWaterAndN> zonesFromSoilArbitrator) { }
        /// <summary>Gets the fw.</summary>
        public double Fw { get { return MathUtilities.Divide(WaterAllocation, PotentialEP, 1); } }

        /// <summary>Gets the function.</summary>
        public double Fn
        {
            get
            {
                double value = MathUtilities.Divide(Live.NConc - MinimumNConc.Value(), MaximumNConc.Value() - MinimumNConc.Value(), 1);
                value = MathUtilities.Bound(value, 0, 1);
                return value;
            }
        }

        /// <summary>Gets the LAI</summary>
        [Units("m^2/m^2")]
        public double LAIDead => cohort.LaiDead;

        /// <summary>Gets the cover dead.</summary>
        public double CoverDead { get { return 1.0 - Math.Exp(-ExtinctionCoefficientDead.Value() * LAIDead); } }

        /// <summary>Gets the total radiation intercepted.</summary>
        [Units("MJ/m^2/day")]
        [Description("This is the intercepted radiation value that is passed to the RUE class to calculate DM supply")]
        public double RadiationIntercepted
        {
            get
            {
                if (LightProfile == null)
                    return 0;
                double TotalRadn = 0;
                for (int i = 0; i < LightProfile.Length; i++)
                    TotalRadn += LightProfile[i].AmountOnGreen;
                return TotalRadn;
            }
        }

        /// <summary>Apex number by age</summary>
        /// <param name="age">Threshold age</param>
        public double ApexNumByAge(double age) { return 0; }
        #endregion

        #region Arbitrator Methods

        /// <summary>Calculate and return the dry matter supply (g/m2)</summary>
        [EventSubscribe("SetDMSupply")]
        private void SetDMSupply(object sender, EventArgs e)
        {
            DMSupply.Fixation = Photosynthesis.Value();
            DMSupply.ReTranslocation = StartLive.StorageWt * DMRetranslocationFactor.Value();
            DMSupply.ReAllocation = 0.0;
        }

        /// <summary>Calculate and return the nitrogen supply (g/m2)</summary>
        [EventSubscribe("SetNSupply")]
        private void SetNSupply(object sender, EventArgs e)
        {
            double LabileN = Math.Max(0, StartLive.StorageN - StartLive.StorageWt * MinimumNConc.Value());
            double lrt = LeafResidenceTime.Value();
            double senescingStorageN = cohort.SelectWhere(leaf => leaf.Age >= lrt, l => l.Live.StorageN);

            NSupply.ReAllocation = senescingStorageN * NReallocationFactor.Value();
            NSupply.ReTranslocation = (LabileN - StartNReallocationSupply) * NRetranslocationFactor.Value();
            NSupply.Uptake = 0.0;
        }

        /// <summary>Calculate and return the dry matter demand (g/m2)</summary>
        [EventSubscribe("SetDMDemand")]
        private void SetDMDemand(object sender, EventArgs e)
        {
            DMDemand.Structural = dmDemands.Structural.Value();
            DMDemand.Storage = 0;
            DMDemand.Metabolic = 0;
        }

        /// <summary>Calculate and return the nitrogen demand (g/m2)</summary>
        [EventSubscribe("SetNDemand")]
        private void SetNDemand(object sender, EventArgs e)
        {
            NDemand.Structural = nDemands.Structural.Value();
            NDemand.Metabolic = 0.0; // nDemands.Metabolic.Value();
            NDemand.Storage = nDemands.Storage.Value();
        }


        /// <summary>Gets or sets the water allocation.</summary>
        [JsonIgnore]
        public double WaterAllocation { get; set; }
        #endregion

        #region Events


        /// <summary>Called when [do daily initialisation].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
        protected void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            if (Phenology != null)
                if (Phenology.OnStartDayOf("Emergence"))
                    if (Structure != null)
                        Structure.LeafTipsAppeared = 1.0;

            if (plant.IsAlive)
                ClearBiomassFlows();
        }
        #endregion

        #region Component Process Functions

        /// <summary>Clears this instance.</summary>
        protected void Clear()
        {
            Height = 0;
            PotentialEP = 0;
            WaterDemand = 0;
            LightProfile = null;
            StartNRetranslocationSupply = 0;
            StartNReallocationSupply = 0;
            LiveFWt = 0;
            DMDemand.Clear();
            DMSupply.Clear();
            NDemand.Clear();
            NSupply.Clear();
            Detached.Clear();
            cohort.Clear();
            GrowthRespiration = 0;
            FRGR = 0;
            if (Structure != null)
                Structure.LeafTipsAppeared = 0;

        }

        /// <summary>
        /// Clears the transferring biomass amounts.
        /// </summary>
        private void ClearBiomassFlows()
        {
            Detached.Clear();
            Removed.Clear();
        }
        #endregion

        #region Top Level time step functions


        #endregion

        // ============================================================
        #region Class Structures
        /// <summary>The start live</summary>
        private Biomass StartLive = new Biomass();
        #endregion

        #region Class Parameter Function Links
        /// <summary>The n reallocation factor</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        IFunction NReallocationFactor = null;

        /// <summary>The n retranslocation factor</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        IFunction NRetranslocationFactor = null;

        /// <summary>The dm retranslocation factor</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        IFunction DMRetranslocationFactor = null;

        /// <summary>The initial wt function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/m2")]
        IFunction InitialWtFunction = null;
        /// <summary>The dry matter content</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        [Units("g/g")]
        IFunction DryMatterContent = null;
        /// <summary>The maximum n conc</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/g")]
        public IFunction MaximumNConc = null;
        /// <summary>The minimum n conc</summary>
        [Units("g/g")]
        [Link(Type = LinkType.Child, ByName = true)]
        public IFunction MinimumNConc = null;
        /// <summary>The proportion of biomass repired each day</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        public IFunction MaintenanceRespirationFunction = null;
        /// <summary>Dry matter conversion efficiency</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        public IFunction DMConversionEfficiency = null;
        #endregion

        #region States
        /// <summary>The start n retranslocation supply</summary>
        private double StartNRetranslocationSupply = 0;
        /// <summary>The start n reallocation supply</summary>
        private double StartNReallocationSupply = 0;
        /// <summary>The dry matter potentially being allocated</summary>
        public BiomassPoolType potentialDMAllocation { get; set; }

        #endregion

        #region Class properties

        /// <summary>Gets or sets the live f wt.</summary>
        [JsonIgnore]
        [Units("g/m^2")]
        public double LiveFWt { get; set; }

        #endregion

        #region Arbitrator methods

        /// <summary>Sets the dry matter potential allocation.</summary>
        public void SetDryMatterPotentialAllocation(BiomassPoolType dryMatter)
        {
            potentialDMAllocation.Metabolic = dryMatter.Metabolic;
            potentialDMAllocation.Structural = dryMatter.Structural;
        }

        /// <summary>Sets the dry matter allocation.</summary>
        public void SetDryMatterAllocation(BiomassAllocationType dryMatter)
        {
            // GrowthRespiration with unit CO2 
            // GrowthRespiration is calculated as 
            // Allocated CH2O from photosynthesis "1 / DMConversionEfficiency.Value()", converted 
            // into carbon through (12 / 30), then minus the carbon in the biomass, finally converted into 
            // CO2 (44/12).
            double growthRespFactor = ((1 / DMConversionEfficiency.Value()) * (12.0 / 30.0) - 1.0 * CarbonConcentration.Value()) * 44.0 / 12.0;
            GrowthRespiration = (dryMatter.Structural + dryMatter.Storage) * growthRespFactor;

            cohort.AddNewLeafMaterial(structuralMass: Math.Min(dryMatter.Structural * DMConversionEfficiency.Value(), DMDemand.Structural),
                               storageMass: dryMatter.Storage * DMConversionEfficiency.Value(),
                               structuralN: 0,
                               storageN: 0,
                               sla: SpecificLeafAreaFunction.Value());

            cohort.DoBiomassRetranslocation(dryMatter.Retranslocation);
        }

        /// <summary>Sets the n allocation.</summary>
        public void SetNitrogenAllocation(BiomassAllocationType nitrogen)
        {
            cohort.AddNewLeafMaterial(structuralMass: 0,
                storageMass: 0,
                structuralN: nitrogen.Structural,
                storageN: nitrogen.Storage,
                sla: SpecificLeafAreaFunction.Value());

            cohort.DoNitrogenRetranslocation(nitrogen.Retranslocation + nitrogen.Reallocation);
        }

        /// <summary>Gets or sets the maximum nconc.</summary>
        public double MaxNconc { get { return MaximumNConc.Value(); } }

        /// <summary>Gets or sets the minimum nconc.</summary>
        public double MinNconc { get { return MinimumNConc.Value(); } }

        /// <summary>Gets the total biomass</summary>
        public Biomass Total { get { return Live + Dead; } }

        /// <summary>Gets the total grain weight</summary>
        [Units("g/m2")]
        public double Wt { get { return Total.Wt; } }

        /// <summary>Gets the total grain N</summary>
        [Units("g/m2")]
        public double N { get { return Total.N; } }

        #endregion

        #region Events and Event Handlers
        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        /// 
        [EventSubscribe("Commencing")]
        protected void OnSimulationCommencing(object sender, EventArgs e)
        {
            DMDemand = new BiomassPoolType();
            NDemand = new BiomassPoolType();
            DMSupply = new BiomassSupplyType();
            NSupply = new BiomassSupplyType();
            potentialDMAllocation = new BiomassPoolType();
            Detached = new Biomass();
            Clear();
        }

        /// <summary>Called when crop is sown</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        protected void OnPlantSowing(object sender, SowingParameters data)
        {
            Clear();
        }

        /// <summary>Kill a fraction of the green leaf</summary>
        /// <param name="fraction">The fraction of leaf to kill</param>
        public void Kill(double fraction)
        {
            Summary.WriteMessage(this, "Killing " + fraction + " of live leaf on plant", MessageType.Diagnostic);
            cohort.KillLeavesUniformly(fraction);
        }

        /// <summary>Event from sequencer telling us to do our potential growth.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        protected void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            if (Plant.IsEmerged)
            {
                Detached.Clear();
                FRGR = FRGRFunction.Value();
                Height = HeightFunction.Value();
                //Initialise biomass and nitrogen

                cohort.AddLeaf(InitialWtFunction.Value(), MinimumNConc.Value(), MaximumNConc.Value(), SpecificLeafAreaFunction.Value());

                double developmentRate = LeafDevelopmentRate.Value();
                cohort.IncreaseAge(developmentRate);

                StartLive = ReflectionUtilities.Clone(Live) as Biomass;
                StartNReallocationSupply = NSupply.ReAllocation;
                StartNRetranslocationSupply = NSupply.ReTranslocation;
            }
        }

        /// <summary>Does the nutrient allocations.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoActualPlantGrowth")]
        protected void OnDoActualPlantGrowth(object sender, EventArgs e)
        {
            if (Plant.IsAlive)
            {
                // Senesce any leaves older than residencce time.
                double lrt = LeafResidenceTime.Value();
                cohort.SenesceWhere(l => l.Age >= lrt);
                double lkf = Math.Max(0.0, Math.Min(LeafKillFraction.Value(), MathUtilities.Divide(1 - MinimumLAI.Value(), LAI, 0.0)));
                if (lkf > 0)
                    cohort.KillLeavesUniformly(lkf);

                // Detach any leaves older than residence time + detachment time.
                double detachmentAge = LeafResidenceTime.Value() + LeafDetachmentTime.Value();
                Detached = cohort.DetachWhere(l => l.Age >= detachmentAge);

                if (Detached.Wt > 0.0)
                    SurfaceOrganicMatter.Add(Detached.Wt * 10, Detached.N * 10, 0, Plant.PlantType, Name);

                MaintenanceRespiration = 0;
                //Do Maintenance respiration
                if (MaintenanceRespirationFunction != null && (Live.MetabolicWt + Live.StorageWt) > 0)
                {
                    MaintenanceRespiration += Live.MetabolicWt * MaintenanceRespirationFunction.Value();
                    cohort.ReduceNonStructuralWt(1 - MaintenanceRespirationFunction.Value());
                    MaintenanceRespiration += Live.StorageWt * MaintenanceRespirationFunction.Value();
                }

                if (DryMatterContent != null)
                    LiveFWt = MathUtilities.Divide(Live.Wt, DryMatterContent.Value(), 0.0);
            }
        }

        #endregion

        /// <summary>Called when crop is ending</summary>
        [EventSubscribe("PlantEnding")]
        protected void OnPlantEnding(object sender, EventArgs e)
        {
            if (Wt > 0.0)
            {
                Detached.Add(Live);
                Detached.Add(Dead);
                SurfaceOrganicMatter.Add(Wt * 10, N * 10, 0, Plant.PlantType, Name);
            }

            Clear();
        }

        /// <summary>Called when crop is harvested</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PostHarvesting")]
        protected void OnPostHarvesting(object sender, EventArgs e)
        {
            Harvest();
        }

        /// <summary>Called when [phase changed].</summary>
        /// <param name="phaseChange">The phase change.</param>
        /// <param name="sender">Sender plant.</param>
        [EventSubscribe("PhaseChanged")]
        private void OnPhaseChanged(object sender, PhaseChangedType phaseChange)
        {
            Summary.WriteMessage(this, phaseChange.StageName, MessageType.Diagnostic);
            Summary.WriteMessage(this, $"LAI = {LAI:f2} (m^2/m^2)", MessageType.Diagnostic);
        }

        /// <summary>A list of material (biomass) that can be damaged.</summary>
        public IEnumerable<DamageableBiomass> Material
        {
            get
            {
                yield return new DamageableBiomass($"{Parent.Name}.{Name}", Live, true);
                yield return new DamageableBiomass($"{Parent.Name}.{Name}", Dead, false);
            }
        }

        /// <summary>Remove biomass from organ.</summary>
        /// <param name="liveToRemove">Fraction of live biomass to remove from simulation (0-1).</param>
        /// <param name="deadToRemove">Fraction of dead biomass to remove from simulation (0-1).</param>
        /// <param name="liveToResidue">Fraction of live biomass to remove and send to residue pool(0-1).</param>
        /// <param name="deadToResidue">Fraction of dead biomass to remove and send to residue pool(0-1).</param>
        /// <returns>The amount of biomass (live+dead) removed from the plant (g/m2).</returns>
        public double RemoveBiomass(double liveToRemove, double deadToRemove, double liveToResidue, double deadToResidue)
        {
            Biomass liveAfterRemoval = Live;
            Biomass deadAfterRemoval = Dead;
            double amountRemoved = biomassRemovalModel.RemoveBiomass(liveToRemove, deadToRemove, liveToResidue, deadToResidue, liveAfterRemoval, deadAfterRemoval, Removed, Detached);

            cohort.ReduceLeavesUniformly(liveFraction: MathUtilities.Divide(liveAfterRemoval.Wt, Live.Wt, 0),
                                         deadFraction: MathUtilities.Divide(deadAfterRemoval.Wt, Dead.Wt, 0));

            double remainingLiveFraction = MathUtilities.Divide(liveAfterRemoval.Wt, Live.Wt, 0);
            double remainingDeadFraction = MathUtilities.Divide(deadAfterRemoval.Wt, Dead.Wt, 0);

            cohort.ReduceLeavesUniformly(remainingLiveFraction, remainingDeadFraction);
            return amountRemoved;
        }

        /// <summary>Harvest the organ.</summary>
        /// <returns>The amount of biomass (live+dead) removed from the plant (g/m2).</returns>
        public double Harvest()
        {
            return RemoveBiomass(biomassRemovalModel.HarvestFractionLiveToRemove, biomassRemovalModel.HarvestFractionDeadToRemove,
                                 biomassRemovalModel.HarvestFractionLiveToResidue, biomassRemovalModel.HarvestFractionDeadToResidue);
        }

    }
}