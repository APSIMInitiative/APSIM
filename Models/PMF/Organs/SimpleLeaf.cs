using System;
using System.Collections.Generic;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF.Interfaces;
using Models.PMF.Library;
using Models.PMF.Phen;
using Newtonsoft.Json;

namespace Models.PMF.Organs
{

    /// <summary>
    /// This organ is simulated using a SimpleLeaf organ type.  It provides the core functions of intercepting radiation, producing biomass
    ///  through photosynthesis, and determining the plant's transpiration demand.  The model also calculates the growth, senescence, and
    ///  detachment of leaves.  SimpleLeaf does not distinguish leaf cohorts by age or position in the canopy.
    /// 
    /// Radiation interception and transpiration demand are computed by the MicroClimate model.  This model takes into account
    ///  competition between different plants when more than one is present in the simulation.  The values of canopy Cover, LAI, and plant
    ///  Height (as defined below) are passed daily by SimpleLeaf to the MicroClimate model.  MicroClimate uses an implementation of the
    ///  Beer-Lambert equation to compute light interception and the Penman-Monteith equation to calculate potential evapotranspiration.  
    ///  These values are then given back to SimpleLeaf which uses them to calculate photosynthesis and soil water demand.
    /// </summary>
    /// <remarks>
    /// SimpleLeaf has two options to define the canopy: the user can either supply a function describing LAI or a function describing canopy cover directly.  From either of these functions SimpleLeaf can obtain the other property using the Beer-Lambert equation with the specified value of extinction coefficient.
    /// The effect of growth rate on transpiration is captured by the Fractional Growth Rate (FRGR) function, which is passed to the MicroClimate model.
    /// </remarks>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class SimpleLeaf : Model, ICanopy, IHasWaterDemand, IOrgan, IArbitration, IOrganDamage, IHasDamageableBiomass
    {
        /// <summary>
        /// The met data
        /// </summary>
        [Link]
        public IWeather MetData = null;

        /// <summary>
        /// The plant
        /// </summary>
        [Link]
        private Plant plant = null;

        /// <summary>Link to summary instance.</summary>
        [Link]
        private ISummary summary = null;

        /// <summary>
        /// The surface organic matter model.
        /// </summary>
        [Link]
        private ISurfaceOrganicMatter surfaceOrganicMatter = null;

        /// <summary>
        /// Relative growth rate factor.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction frgr = null;

        /// <summary>
        /// The effect of CO2 on stomatal conductance.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction stomatalConductanceCO2Modifier = null;

        /// <summary>
        /// The photosynthesis function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction photosynthesis = null;

        /// <summary>
        /// The height function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction heightFunction = null;

        /// <summary>
        /// The lai dead function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction laiDead = null;

        /// <summary>
        /// Carbon concentration.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction carbonConcentration = null;

        /// <summary>
        /// Water Demand Function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private IFunction waterDemand = null;

        /// <summary>
        /// The cover function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private IFunction cover = null;

        /// <summary>
        /// The lai function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private IFunction area = null;

        /// <summary>
        /// The extinction coefficient function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private IFunction extinctionCoefficient = null;

        /// <summary>
        /// The height of the base of the canopy.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private IFunction baseHeight = null;

        /// <summary>
        /// The with of a single plant.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private IFunction wideness = null;

        /// <summary>
        /// Link to biomass removal model.
        /// </summary>
        [Link(Type = LinkType.Child)]
        private BiomassRemoval biomassRemovalModel = null;

        /// <summary>
        /// The senescence rate function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        protected IFunction senescenceRate = null;

        /// <summary>
        /// The detachment rate function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        private IFunction detachmentRate = null;

        /// <summary>
        /// The N retranslocation factor.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        protected IFunction nRetranslocationFactor = null;

        /// <summary>
        /// The N reallocation factor.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        protected IFunction nReallocationFactor = null;

        /// <summary>
        /// The DM retranslocation factor.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        private IFunction dmRetranslocationFactor = null;

        /// <summary>
        /// The DM reallocation factor.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        private IFunction dmReallocationFactor = null;

        /// <summary>
        /// The DM demand function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/m2/d")]
        private NutrientDemandFunctions dmDemands = null;

        /// <summary>
        /// The N demand function.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/m2/d")]
        private NutrientDemandFunctions nDemands = null;

        /// <summary>
        /// The initial biomass dry matter weight.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/m2")]
        private IFunction initialWt = null;

        /// <summary>
        /// The maximum N concentration.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/g")]
        private IFunction maximumNConc = null;

        /// <summary>
        /// The minimum N concentration.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/g")]
        private IFunction minimumNConc = null;

        /// <summary>
        /// The critical N concentration.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/g")]
        private IFunction criticalNConc = null;

        /// <summary>
        /// The proportion of biomass respired each day.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        private IFunction maintenanceRespiration = null;

        /// <summary>
        /// Dry matter conversion efficiency.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        private IFunction dmConversionEfficiency = null;

        /// <summary>
        /// The cost for remobilisation.
        /// </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("")]
        private IFunction remobilisationCost = null;

        /// <summary>
        /// Tolerance for biomass comparisons.
        /// </summary>
        private const double biomassToleranceValue = 0.0000000001;

        /// <summary>
        /// The live biomass state at start of the computation round.
        /// </summary>
        private Biomass startLive = null;

        /// <summary>
        /// Is leaf initialised?
        /// </summary>
        private bool leafInitialised = false;

        /// <summary>
        /// Constructor.
        /// </summary>
        public SimpleLeaf()
        {
            Live = new Biomass();
            Dead = new Biomass();
        }

        /// <summary>
        /// Albedo.
        /// </summary>
        [Description("Albedo")]
        public double Albedo { get; set; }

        /// <summary>
        /// Maximum stomatal conductance at CO2 concentration of WeatherConstants.DEFAULT_CO2 ppm (m/s).
        /// </summary>
        [Units("m/s")]
        [Description("Maximum stomatal conductance at CO2 concentration of 420 ppm")]
        public double Gsmax350 { get; set; }

        /// <summary>
        /// Radiation level at which stomatal conductance is half the maximum value.
        /// </summary>
        [Description("Radiation level at which stomatal conductance is half the maximum value")]
        public double R50 { get; set; }

        /// <summary>
        /// The Stage that leaves are initialised on.
        /// </summary>
        [Description("The Stage that leaves are initialised on")]
        public string LeafInitialisationStage { get; set; } = "Emergence";

        /// <summary>
        /// Leaf area index.
        /// </summary>
        [JsonIgnore]
        [Units("m^2/m^2")]
        public double LAI { get; set; }

        /// <summary>
        /// Gets or sets the height.
        /// </summary>
        [Units("mm")]
        public double Height { get; set; }

        /// <summary>
        /// Gets or sets the height.
        /// </summary>
        [Units("mm")]
        public double BaseHeight { get; set; }

        /// <summary>
        /// The width of an individual plant
        /// </summary>
        [Units("mm")]
        public double Width { get; set; }

        /// <summary>
        /// Gets or sets the FRGR.
        /// </summary>
        [Units("mm")]
        public double FRGR { get; set; }

        /// <summary>
        /// Sets the potential evapotranspiration. Set by micro cliamte.
        /// </summary>
        [Units("mm")]
        public double PotentialEP { get; set; }

        /// <summary>Sets the actual water demand.</summary>
        [Units("mm")]
        public double WaterDemand { get; set; }

        /// <summary>
        /// Sets the light profile. Set by MICROCLIMATE.
        /// </summary>
        public CanopyEnergyBalanceInterceptionlayerType[] LightProfile { get; set; }

        /// <summary>
        /// Extinction coefficient (dead).
        /// </summary>
        [Description("Extinction coefficient for Dead Leaf")]
        public double KDead { get; set; }

        /// <summary>
        /// Gets or sets the lai dead.
        /// </summary>
        public double LAIDead { get; set; }

        /// <summary>
        /// The dry matter supply.
        /// </summary>
        public BiomassSupplyType DMSupply { get; set; }

        /// <summary>
        /// The nitrogen supply.
        /// </summary>
        public BiomassSupplyType NSupply { get; set; }

        /// <summary>
        /// The dry matter demand.
        /// </summary>
        public BiomassPoolType DMDemand { get; set; }

        /// <summary>
        /// Structural nitrogen demand.
        /// </summary>
        public BiomassPoolType NDemand { get; set; }

        /// <summary>
        /// The dry matter potentially being allocated.
        /// </summary>
        public BiomassPoolType potentialDMAllocation { get; set; }

        /// <summary>
        /// The live biomass.
        /// </summary>
        [JsonIgnore]
        public Biomass Live { get; private set; }

        /// <summary>
        /// The dead biomass.
        /// </summary>
        [JsonIgnore]
        public Biomass Dead { get; private set; }

        /// <summary>
        /// Gets the total biomass.
        /// </summary>
        [JsonIgnore]
        public Biomass Total { get { return Live + Dead; } }

        /// <summary>
        /// Gets the biomass allocated (represented actual growth).
        /// </summary>
        [JsonIgnore]
        public Biomass Allocated { get; private set; }

        /// <summary>
        /// Gets the biomass senesced (transferred from live to dead material).
        /// </summary>
        [JsonIgnore]
        public Biomass Senesced { get; private set; }

        /// <summary>
        /// Gets the biomass detached (sent to soil/surface organic matter).
        /// </summary>
        [JsonIgnore]
        public Biomass Detached { get; private set; }

        /// <summary>
        /// Gets the biomass removed from the system (harvested, grazed, etc.).
        /// </summary>
        [JsonIgnore]
        public Biomass Removed { get; private set; }

        /// <summary>Gets or sets the amount of mass lost each day from maintenance respiration</summary>
        [JsonIgnore]
        public double MaintenanceRespiration { get; private set; }

        /// <summary>
        /// Growth Respiration.
        /// </summary>
        [JsonIgnore]
        public double GrowthRespiration { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the biomass is above ground or not.
        /// </summary>
        public bool IsAboveGround { get { return true; } }

        /// <summary>
        /// Gets the potential DM allocation for this computation round.
        /// </summary>
        public BiomassPoolType DMPotentialAllocation
        {
            get
            {
                return potentialDMAllocation;
            }
        }

        /// <summary>
        /// Gets or sets the n fixation cost.
        /// </summary>
        [JsonIgnore]
        public virtual double NFixationCost
        {
            get
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the maximum N concentration.
        /// </summary>
        [JsonIgnore]
        public double MaxNconc
        {
            get
            {
                return maximumNConc.Value();
            }
        }

        /// <summary>
        /// Gets the minimum N concentration.
        /// </summary>
        [JsonIgnore]
        public double MinNconc
        {
            get
            {
                return minimumNConc.Value();
            }
        }

        /// <summary>
        /// Gets the minimum N concentration.
        /// </summary>
        [JsonIgnore]
        public double CritNconc
        {
            get
            {
                return criticalNConc.Value();
            }
        }

        /// <summary>
        /// Gets the total (live + dead) dry matter weight.
        /// </summary>
        [JsonIgnore]
        [Units("g/m^2")]
        public double Wt
        {
            get
            {
                return Live.Wt + Dead.Wt;
            }
        }

        /// <summary>
        /// Gets the total (live + dead) N amount.
        /// </summary>
        [JsonIgnore]
        [Units("g/m^2")]
        public double N
        {
            get
            {
                return Live.N + Dead.N;
            }
        }

        /// <summary>
        /// Gets the total (live + dead) N concentration.
        /// </summary>
        [JsonIgnore]
        [Units("g/g")]
        public double Nconc
        {
            get
            {
                if (Wt > 0.0)
                    return N / Wt;
                else
                    return 0.0;
            }
        }

        /// <summary>
        /// Gets the transpiration.
        /// </summary>
        public double Transpiration
        {
            get
            {
                return WaterAllocation;
            }
        }

        /// <summary>
        /// Gets the depth.
        /// </summary>
        [Units("mm")]
        public double Depth
        {
            get
            {
                return Math.Max(0, Height - BaseHeight);
            }
        }

        /// <summary>
        /// Gets the canopy. Should return null if no canopy present.
        /// </summary>
        public string CanopyType
        {
            get
            {
                return plant.PlantType;
            }
        }

        /// <summary>
        /// Gets the LAI live + dead.
        /// </summary>
        [Units("m^2/m^2")]
        public double LAITotal
        {
            get
            {
                return LAI + LAIDead;
            }
        }

        /// <summary>
        /// Water stress factor.
        /// </summary>
        public double Fw
        {
            get
            {
                return MathUtilities.Divide(WaterAllocation, PotentialEP, 1);
            }
        }

        /// <summary>
        /// Gets the cover total.
        /// </summary>
        [Units("0-1")]
        public double CoverTotal
        {
            get
            {
                return 1.0 - (1 - CoverGreen) * (1 - CoverDead);
            }
        }

        /// <summary>
        /// Gets the cover green.
        /// </summary>
        [Units("0-1")]
        public double CoverGreen
        {
            get
            {
                if (plant.IsAlive)
                {
                    double greenCover = 0.0;
                    if (cover == null)
                        greenCover = 1.0 - Math.Exp(-extinctionCoefficient.Value() * LAI);
                    else
                        greenCover = cover.Value();
                    return MathUtilities.Bound(greenCover, 0.0, 0.999999999); // limiting to within 10^-9, so MicroClimate doesn't complain
                }
                else
                    return 0.0;

            }
        }

        /// <summary>
        /// Gets the nitrogen factor.
        /// </summary>
        public double Fn
        {
            get
            {
                double factor = 0.0;
                if (Live != null)
                    factor = MathUtilities.Divide(Live.N, Live.Wt * MaxNconc, 1);
                return Math.Min(1.0,factor);
            }
        }

        /// <summary>
        /// Gets the metabolic N concentration factor.
        /// </summary>
        public double FNmetabolic
        {
            get
            {
                double factor = 0.0;
                if (Live != null)
                    factor = MathUtilities.Divide(Live.N - Live.StructuralN, Live.Wt * (CritNconc - MinNconc), 1.0);
                return Math.Min(1.0, factor);
            }
        }

        /// <summary>
        /// Gets the cover dead.
        /// </summary>
        public double CoverDead
        {
            get
            {
                return 1.0 - Math.Exp(-KDead * LAIDead);
            }
        }

        /// <summary>
        /// Intercepted radiation value that is passed to the RUE class to calculate DM supply.
        /// </summary>
        [Units("MJ/m^2/day")]
        public double RadiationIntercepted
        {
            get
            {
                if (LightProfile == null)
                    return 0;

                double totalRadn = 0;
                for (int i = 0; i < LightProfile.Length; i++)
                    totalRadn += LightProfile[i].AmountOnGreen;
                return totalRadn;
            }
        }

        /// <summary>
        /// Radiation intercepted by the dead components of the canopy.
        /// </summary>
        [Units("MJ/m^2/day")]
        public double RadiationInterceptedByDead
        {
            get
            {
                if (LightProfile == null)
                    return 0;

                double totalRadn = 0;
                for (int i = 0; i < LightProfile.Length; i++)
                    totalRadn += LightProfile[i].AmountOnDead;
                return totalRadn;
            }
        }



        /// <summary>
        /// Daily maximum stomatal conductance.
        /// </summary>
        [Units("m/s")]
        public double Gsmax
        {
            get
            {
                return Gsmax350 * FRGR * stomatalConductanceCO2Modifier.Value();
            }
        }

        /// <summary>
        /// Gets or sets the water allocation.
        /// </summary>
        [JsonIgnore]
        public double WaterAllocation { get; set; }

        /// <summary>A list of material (biomass) that can be damaged.</summary>
        public IEnumerable<DamageableBiomass> Material
        {
            get
            {
                yield return new DamageableBiomass($"{Parent.Name}.{Name}", Live, true);
                yield return new DamageableBiomass($"{Parent.Name}.{Name}", Dead, false);
            }
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        private void Clear()
        {
            Live = new Biomass();
            Dead = new Biomass();
            DMSupply.Clear();
            NSupply.Clear();
            DMDemand.Clear();
            NDemand.Clear();
            potentialDMAllocation.Clear();
            Height = 0;
            LAI = 0;
            leafInitialised = false;
            GrowthRespiration = 0;
            FRGR = 0;
            LightProfile = null;
            PotentialEP = 0;
            LAIDead = 0;
            WaterDemand = 0;
            WaterAllocation = 0;
        }

        /// <summary>
        /// Clears the transferring biomass amounts.
        /// </summary>
        private void ClearBiomassFlows()
        {
            Allocated.Clear();
            Senesced.Clear();
            Detached.Clear();
            Removed.Clear();
        }

        /// <summary>
        /// Calculate and return the dry matter supply (g/m2).
        /// </summary>
        [EventSubscribe("SetDMSupply")]
        private void SetDMSupply(object sender, EventArgs e)
        {
            DMSupply.ReAllocation = AvailableDMReallocation();
            DMSupply.ReTranslocation = AvailableDMRetranslocation();
            DMSupply.Uptake = 0;
            DMSupply.Fixation = photosynthesis.Value();
        }

        /// <summary>
        /// Called when [phase changed].
        /// </summary>
        [EventSubscribe("PhaseChanged")]
        private void OnPhaseChanged(object sender, PhaseChangedType phaseChange)
        {
            if (phaseChange.StageName == LeafInitialisationStage)
                leafInitialised = true;
            summary.WriteMessage(this, phaseChange.StageName, MessageType.Diagnostic);
            summary.WriteMessage(this, $"LAI = {LAI:f2} (m^2/m^2)", MessageType.Diagnostic);
        }

        /// <summary>
        /// Event from sequencer telling us to do our potential growth.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        private void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            // save current state
            if (plant.IsEmerged)
                startLive = ReflectionUtilities.Clone(Live) as Biomass;
            if (leafInitialised)
            {
                FRGR = frgr.Value();
                if (cover == null && extinctionCoefficient == null)
                    throw new Exception("\"CoverFunction\" or \"ExtinctionCoefficientFunction\" should be defined in " + this.Name);
                if (cover != null)
                    LAI = (Math.Log(1 - CoverGreen) / (extinctionCoefficient.Value() * -1));
                if (area != null)
                    LAI = area.Value();

                Height = heightFunction.Value();
                if (baseHeight == null)
                    BaseHeight = 0;
                else
                    BaseHeight = baseHeight.Value();
                if (wideness == null)
                    Width = 0;
                else
                    Width = wideness.Value();
                LAIDead = laiDead.Value();
            }
        }

        /// <summary>
        /// Calculate and return the nitrogen supply (g/m2).
        /// </summary>
        [EventSubscribe("SetNSupply")]
        protected virtual void SetNSupply(object sender, EventArgs e)
        {
            NSupply.ReAllocation = Math.Max(0, (startLive.StorageN + startLive.MetabolicN) * senescenceRate.Value() * nReallocationFactor.Value());
            if (MathUtilities.IsNegative(NSupply.ReAllocation))
                throw new Exception("Negative N reallocation value computed for " + Name);

            NSupply.ReTranslocation = Math.Max(0, (startLive.StorageN + startLive.MetabolicN) * (1 - senescenceRate.Value()) * nRetranslocationFactor.Value());
            if (MathUtilities.IsNegative(NSupply.ReTranslocation))
                throw new Exception("Negative N retranslocation value computed for " + Name);

            NSupply.Fixation = 0;
            NSupply.Uptake = 0;
        }

        /// <summary>
        /// Calculate and return the dry matter demand (g/m2).
        /// </summary>
        [EventSubscribe("SetDMDemand")]
        protected virtual void SetDMDemand(object sender, EventArgs e)
        {
            if (MathUtilities.IsPositive(dmConversionEfficiency.Value()))
            {
                DMDemand.Structural = MathUtilities.Divide(dmDemands.Structural.Value() , dmConversionEfficiency.Value(),0) + remobilisationCost.Value();
                DMDemand.Storage = Math.Max(0, dmDemands.Storage.Value() / dmConversionEfficiency.Value());
                DMDemand.Metabolic = 0;
                DMDemand.QStructuralPriority = dmDemands.QStructuralPriority.Value();
                DMDemand.QStoragePriority = dmDemands.QStoragePriority.Value();
                DMDemand.QMetabolicPriority = dmDemands.QMetabolicPriority.Value();
            }
            else
            {
                // Conversion efficiency is zero!!!!
                DMDemand.Structural = 0;
                DMDemand.Storage = 0;
                DMDemand.Metabolic = 0;
            }
        }

        /// <summary>
        /// Calculate and return the nitrogen demand (g/m2).
        /// </summary>
        [EventSubscribe("SetNDemand")]
        protected virtual void SetNDemand(object sender, EventArgs e)
        {
            NDemand.Structural = nDemands.Structural.Value();
            NDemand.Metabolic = nDemands.Metabolic.Value();
            NDemand.Storage = nDemands.Storage.Value();
            NDemand.QStructuralPriority = nDemands.QStructuralPriority.Value();
            NDemand.QStoragePriority = nDemands.QStoragePriority.Value();
            NDemand.QMetabolicPriority = nDemands.QMetabolicPriority.Value();
        }

        /// <summary>
        /// Called when [simulation commencing].
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        protected void OnSimulationCommencing(object sender, EventArgs e)
        {
            startLive = new Biomass();
            DMDemand = new BiomassPoolType();
            NDemand = new BiomassPoolType();
            DMSupply = new BiomassSupplyType();
            NSupply = new BiomassSupplyType();
            potentialDMAllocation = new BiomassPoolType();
            Allocated = new Biomass();
            Senesced = new Biomass();
            Detached = new Biomass();
            Removed = new Biomass();
            Height = 0.0;
            LAI = 0.0;
            leafInitialised = false;
            Clear();
        }

        /// <summary>
        /// Called when [do daily initialisation].
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
        protected void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            if (plant.IsAlive)
                ClearBiomassFlows();
        }

        /// <summary>
        /// Called when crop is ending.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        protected void OnPlantSowing(object sender, SowingParameters data)
        {
            if (data.Plant == plant)
            {
                Clear();
                ClearBiomassFlows();
                Live.StructuralWt = initialWt.Value();
                Live.StorageWt = 0.0;
                Live.StructuralN = Live.StructuralWt * minimumNConc.Value();
                Live.StorageN = (initialWt.Value() * maximumNConc.Value()) - Live.StructuralN;
            }
        }

        /// <summary>
        /// Does the nutrient allocations.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoActualPlantGrowth")]
        protected void OnDoActualPlantGrowth(object sender, EventArgs e)
        {
            if (plant.IsAlive)
            {
                // Do senescence
                double senescedFrac = senescenceRate.Value();
                if (Live.Wt * (1.0 - senescedFrac) < biomassToleranceValue)
                    senescedFrac = 1.0;  // remaining amount too small, senesce all
                Biomass Loss = Live * senescedFrac;
                Live.Subtract(Loss);
                Dead.Add(Loss);
                Senesced.Add(Loss);

                // Do detachment
                double detachedFrac = detachmentRate.Value();
                if (Dead.Wt * (1.0 - detachedFrac) < biomassToleranceValue)
                    detachedFrac = 1.0;  // remaining amount too small, detach all
                Biomass detaching = Dead * detachedFrac;
                Dead.Multiply(1.0 - detachedFrac);
                if (detaching.Wt > 0.0)
                {
                    Detached.Add(detaching);
                    surfaceOrganicMatter.Add(detaching.Wt * 10, detaching.N * 10, 0, plant.PlantType, Name);
                }

                // Do maintenance respiration
                if (maintenanceRespiration.Value() > 0)
                {
                    MaintenanceRespiration = (Live.MetabolicWt + Live.StorageWt) * maintenanceRespiration.Value();
                    Live.MetabolicWt *= (1 - maintenanceRespiration.Value());
                    Live.StorageWt *= (1 - maintenanceRespiration.Value());
                }
            }
        }

        /// <summary>
        /// Called when crop is ending.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantEnding")]
        protected void OnPlantEnding(object sender, EventArgs e)
        {
            if (MathUtilities.IsPositive(Wt))
            {
                Detached.Add(Live);
                Detached.Add(Dead);
                surfaceOrganicMatter.Add(Wt * 10, N * 10, 0, plant.PlantType, Name);
            }

            Clear();
        }

        /// <summary>Remove biomass from organ.</summary>
        /// <param name="liveToRemove">Fraction of live biomass to remove from simulation (0-1).</param>
        /// <param name="deadToRemove">Fraction of dead biomass to remove from simulation (0-1).</param>
        /// <param name="liveToResidue">Fraction of live biomass to remove and send to residue pool(0-1).</param>
        /// <param name="deadToResidue">Fraction of dead biomass to remove and send to residue pool(0-1).</param>
        /// <returns>The amount of biomass (live+dead) removed from the plant (g/m2).</returns>
        public double RemoveBiomass(double liveToRemove, double deadToRemove, double liveToResidue, double deadToResidue)
        {
            return biomassRemovalModel.RemoveBiomass(liveToRemove, deadToRemove, liveToResidue, deadToResidue, Live, Dead, Removed, Detached);
        }

        /// <summary>Harvest the organ.</summary>
        /// <returns>The amount of biomass (live+dead) removed from the plant (g/m2).</returns>
        public double Harvest()
        {
            return RemoveBiomass(biomassRemovalModel.HarvestFractionLiveToRemove, biomassRemovalModel.HarvestFractionDeadToRemove,
                                 biomassRemovalModel.HarvestFractionLiveToResidue, biomassRemovalModel.HarvestFractionDeadToResidue);
        }

        /// <summary>
        /// Calculates the water demand.
        /// </summary>
        public double CalculateWaterDemand()
        {
            if (waterDemand != null)
                return waterDemand.Value();

            return WaterDemand;
        }

        /// <summary>
        /// Computes the amount of DM available for retranslocation.
        /// </summary>
        public double AvailableDMRetranslocation()
        {
            double availableDM = Math.Max(0.0, startLive.StorageWt - DMSupply.ReAllocation) * dmRetranslocationFactor.Value();
            if (MathUtilities.IsNegative(availableDM))
                throw new Exception("Negative DM retranslocation value computed for " + Name);

            return availableDM;
        }

        /// <summary>
        /// Computes the amount of DM available for reallocation.
        /// </summary>
        public double AvailableDMReallocation()
        {
            double availableDM = startLive.StorageWt * senescenceRate.Value() * dmReallocationFactor.Value();
            if (MathUtilities.IsNegative(availableDM))
                throw new Exception("Negative DM reallocation value computed for " + Name);

            return availableDM;
        }

        /// <summary>
        /// Sets the dry matter potential allocation.
        /// </summary>
        /// <param name="dryMatter">The potential amount of drymatter allocation</param>
        public void SetDryMatterPotentialAllocation(BiomassPoolType dryMatter)
        {
            potentialDMAllocation.Structural = dryMatter.Structural;
            potentialDMAllocation.Metabolic = dryMatter.Metabolic;
            potentialDMAllocation.Storage = dryMatter.Storage;
        }

        /// <summary>
        /// Sets the dry matter allocation.
        /// </summary>
        /// <param name="dryMatter">The actual amount of drymatter allocation</param>
        public virtual void SetDryMatterAllocation(BiomassAllocationType dryMatter)
        {
            // Check retranslocation
            if (MathUtilities.IsGreaterThan(dryMatter.Retranslocation, startLive.StorageWt))
                throw new Exception("Retranslocation exceeds non structural biomass in organ: " + Name);

            // get DM lost by respiration (growth respiration)
            // GrowthRespiration with unit CO2 
            // GrowthRespiration is calculated as 
            // Allocated CH2O from photosynthesis "1 / DMConversionEfficiency.Value()", converted 
            // into carbon through (12 / 30), then minus the carbon in the biomass, finally converted into 
            // CO2 (44/12).
            double growthRespFactor = ((1.0 / dmConversionEfficiency.Value()) * (12.0 / 30.0) - 1.0 * carbonConcentration.Value()) * 44.0 / 12.0;
            GrowthRespiration = 0.0;

            // allocate structural DM
            Allocated.StructuralWt = Math.Min(dryMatter.Structural * dmConversionEfficiency.Value(), DMDemand.Structural);
            Live.StructuralWt += Allocated.StructuralWt;
            GrowthRespiration += Allocated.StructuralWt * growthRespFactor;

            // allocate non structural DM
            if (MathUtilities.IsGreaterThan(dryMatter.Storage * dmConversionEfficiency.Value(), DMDemand.Storage))
                throw new Exception("Non structural DM allocation to " + Name + " is in excess of its capacity");

            // Allocated.StorageWt = dryMatter.Storage * dmConversionEfficiency.Value();
            double diffWt = dryMatter.Storage - dryMatter.Retranslocation;
            if (MathUtilities.IsPositive(diffWt))
            {
                diffWt *= dmConversionEfficiency.Value();
                GrowthRespiration += diffWt * growthRespFactor;
            }
            Allocated.StorageWt = diffWt;
            Live.StorageWt += diffWt;

            // allocate metabolic DM
            Allocated.MetabolicWt = dryMatter.Metabolic * dmConversionEfficiency.Value();
            GrowthRespiration += Allocated.MetabolicWt * growthRespFactor;

        }

        /// <summary>
        /// Sets the n allocation.
        /// </summary>
        /// <param name="nitrogen">The nitrogen allocation</param>
        public virtual void SetNitrogenAllocation(BiomassAllocationType nitrogen)
        {
            Live.StructuralN += nitrogen.Structural;
            Live.StorageN += nitrogen.Storage;
            Live.MetabolicN += nitrogen.Metabolic;

            Allocated.StructuralN += nitrogen.Structural;
            Allocated.StorageN += nitrogen.Storage;
            Allocated.MetabolicN += nitrogen.Metabolic;

            // Retranslocation
            if (MathUtilities.IsGreaterThan(nitrogen.Retranslocation, startLive.StorageN + startLive.MetabolicN - nitrogen.Reallocation))
                throw new Exception("N retranslocation exceeds storage + metabolic nitrogen in organ: " + Name);
            double storageNRetranslocation = Math.Min(nitrogen.Retranslocation, startLive.StorageN * (1 - senescenceRate.Value()) * nRetranslocationFactor.Value());
            Live.StorageN -= storageNRetranslocation;
            Live.MetabolicN -= (nitrogen.Retranslocation - storageNRetranslocation);
            Allocated.StorageN -= nitrogen.Retranslocation;

            // Reallocation
            if (MathUtilities.IsGreaterThan(nitrogen.Reallocation, startLive.StorageN + startLive.MetabolicN))
                throw new Exception("N reallocation exceeds storage + metabolic nitrogen in organ: " + Name);
            double storageNReallocation = Math.Min(nitrogen.Reallocation, startLive.StorageN * senescenceRate.Value() * nReallocationFactor.Value());
            Live.StorageN -= storageNReallocation;
            Live.MetabolicN -= (nitrogen.Reallocation - storageNReallocation);
            Allocated.StorageN -= nitrogen.Reallocation;
        }

    }
}