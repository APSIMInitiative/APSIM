using System;
using System.Collections.Generic;
using System.Linq;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF.Interfaces;
using Models.PMF.Library;
using Models.Soils;
using Models.Soils.Arbitrator;
using Newtonsoft.Json;

namespace Models.PMF.Organs
{

    ///<summary>
    /// The root model calculates root growth in terms of rooting depth, biomass accumulation and subsequent root length density in each soil layer.
    ///</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Plant))]
    public class Root : Model, IWaterNitrogenUptake, IArbitration, IOrgan, IOrganDamage, IRoot, IHasDamageableBiomass
    {
        /// <summary>Tolerance for biomass comparisons</summary>
        private double BiomassToleranceValue = 0.0000000001;

        /// <summary>The plant</summary>
        [Link]
        protected Plant parentPlant = null;

        /// <summary>The surface organic matter model</summary>
        [Link]
        public ISurfaceOrganicMatter SurfaceOrganicMatter = null;

        /// <summary>The RootShape model</summary>
        [Link(Type = LinkType.Child, ByName = false)]
        public IRootShape RootShape = null;

        /// <summary>Link to biomass removal model</summary>
        [Link(Type = LinkType.Child)]
        private BiomassRemoval biomassRemovalModel = null;

        /// <summary>The DM demand function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/m2/d")]
        private NutrientDemandFunctions dmDemands = null;

        /// <summary>Link to the KNO3 link</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d/ppm")]
        private IFunction kno3 = null;

        /// <summary>Link to the KNH4 link</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d/ppm")]
        private IFunction knh4 = null;

        /// <summary>Soil water factor for N Uptake</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction nUptakeSWFactor = null;

        /// <summary>Initial wt</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        public NutrientPoolFunctions InitialWt = null;

        /// <summary>Gets or sets the specific root length</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("m/g")]
        private IFunction specificRootLength = null;

        /// <summary>The N demand function</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private NutrientDemandFunctions nDemands = null;

        /// <summary>The N reallocation factor</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        [Units("/d")]
        private IFunction nReallocationFactor = null;

        /// <summary>The DM reallocation factor</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        [Units("/d")]
        private IFunction dmReallocationFactor = null;

        /// <summary>The biomass senescence rate</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        private IFunction senescenceRate = null;

        /// <summary>The root front velocity</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("mm/d")]
        private IFunction rootFrontVelocity = null;

        /// <summary>The maximum N concentration</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/g")]
        private IFunction maximumNConc = null;

        /// <summary>The minimum N concentration</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/g")]
        private IFunction minimumNConc = null;

        /// <summary>The critical N concentration</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        [Units("g/g")]
        private IFunction criticalNConc = null;

        /// <summary>The maximum daily N uptake</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("kg N/ha/d")]
        private IFunction maxDailyNUptake = null;

        /// <summary>The kl modifier</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("0-1")]
        private IFunction klModifier = null;

        /// <summary>The Maximum Root Depth</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("mm")]
        private IFunction maximumRootDepth = null;

        /// <summary>Dry matter efficiency function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/g")]
        private IFunction dmConversionEfficiency = null;

        /// <summary>Carbon concentration</summary>
        [Units("g/g")]
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction carbonConcentration = null;

        /// <summary>The cost for remobilisation</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction remobilisationCost = null;

        /// <summary>The proportion of biomass respired each day</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("/d")]
        private IFunction maintenanceRespirationFunction = null;

        /// <summary>Do we need to recalculate (expensive operation) live and dead</summary>
        private bool needToRecalculateLiveDead = true;

        /// <summary>Live biomass</summary>
        private Biomass liveBiomass = new Biomass();

        /// <summary>Dead biomass</summary>
        private Biomass deadBiomass = new Biomass();

        /// <summary>The dry matter supply</summary>
        public BiomassSupplyType DMSupply { get; set; }

        /// <summary>The nitrogen supply</summary>
        public BiomassSupplyType NSupply { get; set; }

        /// <summary>The dry matter demand</summary>
        public BiomassPoolType DMDemand { get; set; }

        /// <summary>Structural nitrogen demand</summary>
        public BiomassPoolType NDemand { get; set; }

        /// <summary>The dry matter potentially being allocated</summary>
        public BiomassPoolType potentialDMAllocation { get; set; }

        /// <summary>Link to the soilCrop</summary>
        public SoilCrop SoilCrop {get; private set;} = null;

        /// <summary>The DM supply for retranslocation</summary>
        private double dmRetranslocationSupply = 0.0;

        /// <summary>The DM supply for reallocation</summary>
        private double dmMReallocationSupply = 0.0;

        /// <summary>The N supply for retranslocation</summary>
        private double nRetranslocationSupply = 0.0;

        /// <summary>The N supply for reallocation</summary>
        private double nReallocationSupply = 0.0;

        /// <summary>Constructor</summary>
        public Root()
        {
            Zones = new List<ZoneState>();
            ZoneNamesToGrowRootsIn = new List<string>();
            ZoneRootDepths = new List<double>();
            ZoneInitialDM = new List<NutrientPoolFunctions>();
        }

        /// <summary>Gets a value indicating whether the biomass is above ground or not</summary>
        public bool IsAboveGround { get { return false; } }

        /// <summary>A list of other zone names to grow roots in</summary>
        [JsonIgnore]
        public List<string> ZoneNamesToGrowRootsIn { get; set; }

        /// <summary>The root depths for each addition zone.</summary>
        [JsonIgnore]
        public List<double> ZoneRootDepths { get; set; }

        /// <summary>The live weights for each addition zone.</summary>
        [JsonIgnore]
        public List<NutrientPoolFunctions> ZoneInitialDM { get; set; }

        /// <summary>A list of all zones to grow roots in</summary>
        [JsonIgnore]
        public List<ZoneState> Zones { get; set; }

        /// <summary>The zone where the plant is growing</summary>
        [JsonIgnore]
        public ZoneState PlantZone { get; set; }

        /// <summary>Gets the live biomass.</summary>
        [JsonIgnore]
        public Biomass Live
        {
            get
            {
                RecalculateLiveDead();
                return liveBiomass;
            }
        }

        /// <summary>Gets the dead biomass.</summary>
        [JsonIgnore]
        public Biomass Dead
        {
            get
            {
                RecalculateLiveDead();
                return deadBiomass;
            }
        }

        /// <summary>Gets the root length density.</summary>
        [Units("mm/mm3")]
        public double[] LengthDensity
        {
            get
            {
                if (PlantZone == null)    // Can be null in autodoc
                    return new double[0];
                double[] value;
                value = new double[PlantZone.Physical.Thickness.Length];
                double SRL = specificRootLength.Value();
                for (int i = 0; i < PlantZone.Physical.Thickness.Length; i++)
                    value[i] = PlantZone.LayerLive[i].Wt * RootLengthDensityModifierDueToDamage * SRL * 1000 / 1000000 / PlantZone.Physical.Thickness[i];
                return value;
            }
        }

        /// <summary>
        /// The wet root fraction. Profile water filled pore space average weighted by root length.
        /// </summary>
        [Units("0-1")]
        public double WetRootFraction
        {
            get
            {
                var rootLength = LengthDensity.Zip(PlantZone.Physical.Thickness, (rld, th) => rld * th).ToArray();
                var rootSum = rootLength.Sum();
                if (rootSum == 0.0)
                    return 0.0;
                return SoilUtilities
                    .WFPS(PlantZone.WaterBalance.SWmm, PlantZone.Physical.SATmm, PlantZone.Physical.DULmm)
                    .Zip(rootLength, (wfps, rl) => wfps * rl / rootSum)
                    .Sum();
            }
        }

        /// <summary>Air filled pore space factor (mm/mm).</summary>
        [Units("mm/mm")]
        public double AirFilledPoreSpace
        {
            get
            {
                var i = SoilUtilities.LayerIndexOfDepth(PlantZone.Physical.Thickness, Depth);
                return MathUtilities.Divide(PlantZone.Physical.SATmm[i] - PlantZone.WaterBalance.SWmm[i], PlantZone.Physical.Thickness[i], 0.0);
            }
        }

        ///<Summary>Total DM demanded by roots</Summary>
        [Units("g/m2")]
        [JsonIgnore]
        public double TotalDMDemand { get; set; }

        ///<Summary>The amount of N taken up after arbitration</Summary>
        [Units("g/m2")]
        [JsonIgnore]
        public double NTakenUp { get; set; }

        /// <summary>Root depth.</summary>
        [JsonIgnore]
        [Units("mm")]
        public double Depth { get { return PlantZone.Depth; } }

        /// <summary>Root length.</summary>
        [JsonIgnore]
        public double Length { get { return PlantZone.RootLength; } }

        /// <summary>Root Area</summary>
        [JsonIgnore]
        public double Area { get { return PlantZone.RootArea; } }

        /// <summary>Live Biomass in each soil layer</summary>
        [JsonIgnore]
        public Biomass[] LayerLive { get { return PlantZone.LayerLive; } }

        /// <summary>Dead Biomass in each soil layer</summary>
        [JsonIgnore]
        public Biomass[] LayerDead { get { return PlantZone.LayerDead; } }

        /// <summary>Gets the water uptake.</summary>
        [Units("mm")]
        public double WaterUptake
        {
            get
            {
                double uptake = 0;
                foreach (ZoneState zone in Zones)
                    uptake = uptake + MathUtilities.Sum(zone.WaterUptake);
                return -uptake;
            }
        }

        /// <summary>Gets the water uptake.</summary>
        [Units("mm")]
        public double[] WaterUptakeByZone
        {
            get
            {
                double[] uptake = new double[Zones.Count];
                int i = 0;
                foreach (ZoneState zone in Zones)
                {
                    uptake[i] = -MathUtilities.Sum(zone.WaterUptake);
                    i += 1;
                }
                return uptake;
            }
        }

        /// <summary>Gets the water uptake.</summary>
        [Units("mm")]
        public double[] SWUptakeLayered
        {
            get
            {
                if (Zones == null || Zones.Count == 0)
                    return null;
                double[] uptake = (double[])Zones[0].WaterUptake;
                for (int i = 1; i != Zones.Count; i++)
                    uptake = MathUtilities.Add(uptake, Zones[i].WaterUptake);
                return MathUtilities.Multiply_Value(uptake, -1); // convert to positive values.
            }
        }

        /// <summary>Gets or sets the N uptake.</summary>
        [Units("kg/ha")]
        public double NUptake
        {
            get
            {
                double uptake = 0;
                foreach (ZoneState zone in Zones)
                    uptake += MathUtilities.Sum(zone.NitUptake);
                return uptake;
            }
        }

        /// <summary>Gets the nitrogen uptake.</summary>
        [Units("mm")]
        public double[] NUptakeLayered
        {
            get
            {
                if (Zones == null || Zones.Count == 0)
                    return null;
                if (Zones.Count > 1)
                    throw new Exception(this.Name + " Can't report layered Nuptake for multiple zones as they may not have the same size or number of layers");
                double[] uptake = new double[Zones[0].Physical.Thickness.Length];
                if (Zones[0].NitUptake != null)
                    uptake = Zones[0].NitUptake;
                return MathUtilities.Multiply_Value(uptake, -1); // convert to positive values.
            }
        }

        /// <summary>Gets or sets relative water content for a soil layer (ie fraction between LL15 and DUL)</summary>
        [JsonIgnore]
        [Units("0-1")]
        public double[] RWC { get; private set; }

        /// <summary>Returns the Fraction of Available Soil Water for the root system (across zones and depths in zones)</summary>
        [Units("unitless")]
        public double FASW
        {
            get
            {
                double fasw = 0;
                double TotalArea = 0;

                foreach (ZoneState Z in Zones)
                {
                    Zone zone = this.FindInScope(Z.Name) as Zone;
                    var soilPhysical = Z.Soil.FindChild<IPhysical>();
                    var waterBalance = Z.Soil.FindChild<ISoilWater>();
                    var soilCrop = Z.Soil.FindDescendant<SoilCrop>(parentPlant.Name + "Soil");
                    double[] paw = APSIM.Shared.APSoil.APSoilUtilities.CalcPAWC(soilPhysical.Thickness, soilCrop.LL, waterBalance.SW, soilCrop.XF);
                    double[] pawmm = MathUtilities.Multiply(paw, soilPhysical.Thickness);
                    double[] pawc = APSIM.Shared.APSoil.APSoilUtilities.CalcPAWC(soilPhysical.Thickness, soilCrop.LL, soilPhysical.DUL, soilCrop.XF);
                    double[] pawcmm = MathUtilities.Multiply(pawc, soilPhysical.Thickness);
                    TotalArea += zone.Area;

                    fasw += MathUtilities.Sum(pawmm) / MathUtilities.Sum(pawcmm) * zone.Area;
                }
                fasw = fasw / TotalArea;
                return fasw;
            }
        }

        /// <summary>Gets a factor to account for root zone Water tension weighted for root mass.</summary>
        [Units("0-1")]
        public double WaterTensionFactor
        {
            get
            {
                if (PlantZone == null)
                    return 0;

                double MeanWTF = 0;

                double liveWt = Live.Wt;
                if (liveWt > 0)
                    foreach (ZoneState Z in Zones)
                    {
                        var soilPhysical = Z.Soil.FindChild<IPhysical>();
                        var waterBalance = Z.Soil.FindChild<ISoilWater>();
                        double[] paw = waterBalance.PAW;
                        double[] pawc = soilPhysical.PAWC;
                        Biomass[] layerLiveForZone = Z.LayerLive;
                        for (int i = 0; i < Z.LayerLive.Length; i++)
                            if (pawc[i] > 0)
                            {
                                MeanWTF += layerLiveForZone[i].Wt / liveWt * MathUtilities.Bound(2 * paw[i] / pawc[i], 0, 1);
                            }
                    }

                return MeanWTF;
            }
        }

        /// <summary>Gets a factor to account for root zone Water tension weighted for root mass.</summary>
        [Units("0-1")]
        public double PlantWaterPotentialFactor
        {
            get
            {
                if (PlantZone == null)
                    return 0;

                double MeanWTF = 0;

                double liveWt = Live.Wt;
                if (liveWt > 0)
                    foreach (ZoneState Z in Zones)
                    {
                        var soilPhysical = Z.Soil.FindChild<IPhysical>();
                        var waterBalance = Z.Soil.FindChild<ISoilWater>();

                        double[] paw = waterBalance.PAW;
                        double[] pawc = soilPhysical.PAWC;
                        Biomass[] layerLiveForZone = Z.LayerLive;
                        for (int i = 0; i < Z.LayerLive.Length; i++)
                            MeanWTF += layerLiveForZone[i].Wt / liveWt * MathUtilities.Bound(paw[i] / pawc[i], 0, 1);
                    }

                return MeanWTF;
            }
        }

        /// <summary>Gets or sets the minimum nconc.</summary>
        [Units("g/g")]
        public double MinNconc { get { return minimumNConc.Value(); } }

        /// <summary>Gets the critical nconc.</summary>
        [Units("g/g")]
        public double CritNconc { get { return criticalNConc.Value(); } }

        /// <summary>Gets the total biomass</summary>
        public Biomass Total { get { return Live + Dead; } }

        /// <summary>Gets the total grain weight</summary>
        [Units("g/m2")]
        public double Wt { get { return Total.Wt; } }

        /// <summary>Gets the total grain N</summary>
        [Units("g/m2")]
        public double N { get { return Total.N; } }

        /// <summary>Gets the total (live + dead) N concentration (g/g)</summary>
        [JsonIgnore]
        [Units("g/g")]
        public double Nconc { get { return MathUtilities.Divide(N, Wt, 0.0); } }

        /// <summary>Gets or sets the n fixation cost.</summary>
        [JsonIgnore]
        public double NFixationCost { get { return 0; } }

        /// <summary>Growth Respiration</summary>
        /// [Units("CO_2")]
        public double GrowthRespiration { get; set; }

        /// <summary>The amount of mass lost each day from maintenance respiration</summary>
        public double MaintenanceRespiration { get; set; }

        /// <summary>Gets the biomass allocated (represented actual growth)</summary>
        [JsonIgnore]
        public Biomass Allocated { get; set; }

        /// <summary>Gets the biomass senesced (transferred from live to dead material)</summary>
        [JsonIgnore]
        public Biomass Senesced { get; set; }

        /// <summary>Gets the DM amount detached (sent to soil/surface organic matter) (g/m2)</summary>
        [JsonIgnore]
        public Biomass Detached { get; set; }

        /// <summary>Gets the DM amount removed from the system (harvested, grazed, etc) (g/m2)</summary>
        [JsonIgnore]
        public Biomass Removed { get; set; }

        /// <summary>Gets the potential DM allocation for this computation round.</summary>
        public BiomassPoolType DMPotentialAllocation { get { return potentialDMAllocation; } }

        /// <summary>Gets or sets the root length modifier due to root damage (0-1).</summary>
        [JsonIgnore]
        public double RootLengthDensityModifierDueToDamage { get; set; } = 1.0;

        /// <summary>Does the water uptake.</summary>
        /// <param name="Amount">The amount.</param>
        /// <param name="zoneName">Zone name to do water uptake in</param>
        public void DoWaterUptake(double[] Amount, string zoneName)
        {
            ZoneState zone = Zones.Find(z => z.Name == zoneName);
            if (zone == null)
                throw new Exception("Cannot find a zone called " + zoneName);

            zone.WaterUptake = MathUtilities.Multiply_Value(Amount, -1.0);
            zone.WaterBalance.RemoveWater(Amount);
        }

        /// <summary>Does the Nitrogen uptake.</summary>
        /// <param name="zonesFromSoilArbitrator">List of zones from soil arbitrator</param>
        public void DoNitrogenUptake(List<ZoneWaterAndN> zonesFromSoilArbitrator)
        {
            foreach (ZoneWaterAndN thisZone in zonesFromSoilArbitrator)
            {
                ZoneState zone = Zones.Find(z => z.Name == thisZone.Zone.Name);
                if (zone != null)
                {
                    zone.NO3.SetKgHa(SoluteSetterType.Plant, MathUtilities.Subtract(zone.NO3.kgha, thisZone.NO3N));
                    zone.NH4.SetKgHa(SoluteSetterType.Plant, MathUtilities.Subtract(zone.NH4.kgha, thisZone.NH4N));

                    zone.NitUptake = MathUtilities.Multiply_Value(MathUtilities.Add(thisZone.NO3N, thisZone.NH4N), -1);
                }
            }
        }

        /// <summary>Calculate and return the dry matter supply (g/m2)</summary>
        [EventSubscribe("SetDMSupply")]
        private void SetDMSupply(object sender, EventArgs e)
        {
            DMSupply.Fixation = 0.0;
            DMSupply.ReTranslocation = dmRetranslocationSupply;
            DMSupply.ReAllocation = dmMReallocationSupply;
        }

        /// <summary>Calculate and return the nitrogen supply (g/m2)</summary>
        [EventSubscribe("SetNSupply")]
        private void SetNSupply(object sender, EventArgs e)
        {
            NSupply.Fixation = 0.0;
            NSupply.Uptake = 0.0;
            NSupply.ReTranslocation = nRetranslocationSupply;
            NSupply.ReAllocation = nReallocationSupply;
        }

        /// <summary>Calculate and return the dry matter demand (g/m2)</summary>
        [EventSubscribe("SetDMDemand")]
        private void SetDMDemand(object sender, EventArgs e)
        {
            if (parentPlant.SowingData?.Depth <= PlantZone.Depth)
            {
                double dMCE = dmConversionEfficiency.Value();

                if (dMCE > 0.0)
                {
                    DMDemand.Structural = (dmDemands.Structural.Value() / dMCE + remobilisationCost.Value());
                    DMDemand.Storage = Math.Max(0, dmDemands.Storage.Value() / dMCE);
                    DMDemand.Metabolic = 0;
                    DMDemand.QStructuralPriority = dmDemands.QStructuralPriority.Value();
                    DMDemand.QMetabolicPriority = dmDemands.QMetabolicPriority.Value();
                    DMDemand.QStoragePriority = dmDemands.QStoragePriority.Value();
                }
                else
                    throw new Exception("dmConversionEfficiency should be greater than zero in " + Name);
            }
        }

        /// <summary>Calculate and return the nitrogen demand (g/m2)</summary>
        [EventSubscribe("SetNDemand")]
        private void SetNDemand(object sender, EventArgs e)
        {
            NDemand.Structural = nDemands.Structural.Value();
            if (NDemand.Structural < 0)
                throw new Exception("Structural N demand function in root returning negative, check parameterisation");
            NDemand.Metabolic = nDemands.Metabolic.Value();
            if (NDemand.Metabolic < 0)
                throw new Exception("Structural N demand function in root returning negative, check parameterisation");
            NDemand.Storage = nDemands.Storage.Value();
            if (NDemand.Storage < 0)
                throw new Exception("Structural N demand function in root returning negative, check parameterisation");
            NDemand.QStructuralPriority = nDemands.QStructuralPriority.Value();
            NDemand.QStoragePriority = nDemands.QStoragePriority.Value();
            NDemand.QMetabolicPriority = nDemands.QMetabolicPriority.Value();

            foreach (ZoneState Z in Zones)
            {
                Z.StructuralNDemand = new double[Z.Physical.Thickness.Length];
                Z.StorageNDemand = new double[Z.Physical.Thickness.Length];
                Z.MetabolicNDemand = new double[Z.Physical.Thickness.Length];
                //Note: MetabolicN is assumed to be zero

                double[] RAw = Z.CalculateRootActivityValues();
                for (int i = 0; i < Z.LayerLive.Length; i++)
                {
                    Z.StructuralNDemand[i] = NDemand.Structural;
                    Z.StorageNDemand[i] = NDemand.Storage;
                    Z.MetabolicNDemand[i] = NDemand.Metabolic;
                }
            }
        }

        /// <summary>Sets the dry matter potential allocation.</summary>
        public void SetDryMatterPotentialAllocation(BiomassPoolType dryMatter)
        {

            if (PlantZone.WaterUptake == null)
                throw new Exception("No water and N uptakes supplied to root. Is Soil Arbitrator included in the simulation?");

            if (PlantZone.Depth <= 0)
                return; //cannot allocate growth where no length

            if (DMDemand.Structural == 0 && dryMatter.Structural > 0.000000000001)
                throw new Exception("Invalid allocation of potential DM in" + Name);


            double TotalRAw = 0;
            foreach (ZoneState Z in Zones)
                TotalRAw += Z.CalculateRootActivityValues().Sum();

            if (TotalRAw == 0 && dryMatter.Structural > 0)
                throw new Exception("Error trying to partition potential root biomass");

            if (TotalRAw > 0)
            {
                foreach (ZoneState Z in Zones)
                {
                    double[] RAw = Z.CalculateRootActivityValues();
                    Z.PotentialDMAllocated = new double[Z.Physical.Thickness.Length];

                    for (int layer = 0; layer < Z.Physical.Thickness.Length; layer++)
                        Z.PotentialDMAllocated[layer] = dryMatter.Structural * RAw[layer] / TotalRAw;
                }
                needToRecalculateLiveDead = true;
            }

            potentialDMAllocation.Structural = dryMatter.Structural;
            potentialDMAllocation.Metabolic = dryMatter.Metabolic;
            potentialDMAllocation.Storage = dryMatter.Storage;
        }

        /// <summary>Sets the dry matter allocation.</summary>
        public void SetDryMatterAllocation(BiomassAllocationType dryMatter)
        {
            double TotalRAw = 0;
            foreach (ZoneState Z in Zones)
                TotalRAw += Z.CalculateRootActivityValues().Sum();

            double dMCE = dmConversionEfficiency.Value();

            Allocated.StructuralWt = dryMatter.Structural * dMCE;
            Allocated.StorageWt = dryMatter.Storage * dMCE;
            Allocated.MetabolicWt = dryMatter.Metabolic * dMCE;
            // GrowthRespiration with unit CO2
            // GrowthRespiration is calculated as
            // Allocated CH2O from photosynthesis "1 / dMCE", converted
            // into carbon through (12 / 30), then minus the carbon in the biomass, finally converted into
            // CO2 (44/12).
            double growthRespFactor = ((1.0 / dMCE) * (12.0 / 30.0) - 1.0 * carbonConcentration.Value()) * 44.0 / 12.0;
            GrowthRespiration = (Allocated.StructuralWt + Allocated.StorageWt + Allocated.MetabolicWt) * growthRespFactor;
            if (TotalRAw == 0 && Allocated.Wt > 0)
                throw new Exception("Error trying to partition root biomass");

            foreach (ZoneState Z in Zones)
                Z.PartitionRootMass(TotalRAw, Allocated);
            needToRecalculateLiveDead = true;
        }

        /// <summary>Gets the nitrogen supply from the specified zone.</summary>
        /// <param name="zone">The zone.</param>
        /// <param name="NO3Supply">The returned NO3 supply</param>
        /// <param name="NH4Supply">The returned NH4 supply</param>
        public void CalculateNitrogenSupply(ZoneWaterAndN zone, ref double[] NO3Supply, ref double[] NH4Supply)
        {
            ZoneState myZone = Zones.Find(z => z.Name == zone.Zone.Name);
            if (myZone != null)
            {
                if (RWC == null || RWC.Length != myZone.Physical.Thickness.Length)
                    RWC = new double[myZone.Physical.Thickness.Length];

                double NO3Uptake = 0;
                double NH4Uptake = 0;

                double[] thickness = myZone.Physical.Thickness;
                double[] water = myZone.WaterBalance.SWmm;
                double[] ll15mm = myZone.Physical.LL15mm;
                double[] dulmm = myZone.Physical.DULmm;
                double[] bd = myZone.Physical.BD;

                double accuDepth = 0;

                double maxNUptake = maxDailyNUptake.Value();
                for (int layer = 0; layer < thickness.Length; layer++)
                {
                    accuDepth += thickness[layer];
                    if (myZone.LayerLive[layer].Wt > 0)
                    {
                        double factorRootDepth = Math.Max(0, Math.Min(1, 1 - (accuDepth - Depth) / thickness[layer]));
                        RWC[layer] = (water[layer] - ll15mm[layer]) / (dulmm[layer] - ll15mm[layer]);
                        RWC[layer] = Math.Max(0.0, Math.Min(RWC[layer], 1.0));
                        double SWAF = nUptakeSWFactor.Value(layer);

                        double kno3 = this.kno3.Value(layer);
                        double NO3ppm = zone.NO3N[layer] * (100.0 / (bd[layer] * thickness[layer]));
                        NO3Supply[layer] = Math.Min(zone.NO3N[layer] * kno3 * NO3ppm * SWAF * factorRootDepth, (maxNUptake - NO3Uptake));
                        NO3Uptake += NO3Supply[layer];

                        double knh4 = this.knh4.Value(layer);
                        double NH4ppm = zone.NH4N[layer] * (100.0 / (bd[layer] * thickness[layer]));
                        NH4Supply[layer] = Math.Min(zone.NH4N[layer] * knh4 * NH4ppm * SWAF * factorRootDepth, (maxNUptake - NH4Uptake));
                        NH4Uptake += NH4Supply[layer];
                    }
                }

            }
        }

        /// <summary>Sets the n allocation.</summary>
        public void SetNitrogenAllocation(BiomassAllocationType nitrogen)
        {
            double totalStructuralNDemand = 0;
            double totalStorageNDemand = 0;
            double totalMetabolicDemand = 0;

            foreach (ZoneState Z in Zones)
            {
                totalStructuralNDemand += Z.StructuralNDemand.Sum();
                totalStorageNDemand += Z.StorageNDemand.Sum();
                totalMetabolicDemand += Z.MetabolicNDemand.Sum();
            }
            NTakenUp = nitrogen.Uptake;
            Allocated.StructuralN = nitrogen.Structural;
            Allocated.StorageN = nitrogen.Storage;
            Allocated.MetabolicN = nitrogen.Metabolic;

            double surplus = Allocated.N - totalStructuralNDemand - totalStorageNDemand;
            if (surplus > 0.000000001)
                throw new Exception("N Allocation to roots exceeds Demand");
            double NAllocated = 0;

            foreach (ZoneState Z in Zones)
            {
                for (int i = 0; i < Z.LayerLive.Length; i++)
                {
                    if (totalStructuralNDemand > 0)
                    {
                        double StructFrac = Z.StructuralNDemand[i] / totalStructuralNDemand;
                        Z.LayerLive[i].StructuralN += nitrogen.Structural * StructFrac;
                        NAllocated += nitrogen.Structural * StructFrac;
                    }

                    if (totalStorageNDemand > 0)
                    {
                        double NonStructFrac = Z.StorageNDemand[i] / totalStorageNDemand;
                        Z.LayerLive[i].StorageN += nitrogen.Storage * NonStructFrac;
                        NAllocated += nitrogen.Storage * NonStructFrac;
                    }

                    if (totalMetabolicDemand > 0)
                    {
                        double MetabolFrac = Z.MetabolicNDemand[i] / totalMetabolicDemand;
                        Z.LayerLive[i].MetabolicN += nitrogen.Metabolic * MetabolFrac;
                        NAllocated += nitrogen.Metabolic * MetabolFrac;
                    }
                }
            }
            needToRecalculateLiveDead = true;

            if (!MathUtilities.FloatsAreEqual(NAllocated - Allocated.N, 0.0))
                throw new Exception("Error in N Allocation: " + Name);
        }

        /// <summary>Gets or sets the water supply.</summary>
        /// <param name="zone">The zone.</param>
        public double[] CalculateWaterSupply(ZoneWaterAndN zone)
        {
            ZoneState myZone = Zones.Find(z => z.Name == zone.Zone.Name);
            if (myZone == null)
                return null;

            var currentLayer = SoilUtilities.LayerIndexOfDepth(myZone.Physical.Thickness, myZone.Depth);

            double[] kl = myZone.SoilCrop.KL;
            double[] ll = myZone.SoilCrop.LL;

            double[] supply = new double[myZone.Physical.Thickness.Length];
            for (int layer = 0; layer < myZone.Physical.Thickness.Length; layer++)
            {
                if (layer <= currentLayer)
                {
                    double available = zone.Water[layer] - ll[layer] * myZone.Physical.Thickness[layer] * myZone.LLModifier[layer];

                    supply[layer] = Math.Max(0.0, kl[layer] * klModifier.Value(layer) * available * myZone.RootProportions[layer]);
                }
            }
            return supply;
        }

        //------------------------------------------------------------------------------------------------
        // sorghum specific variables

        /// <summary>The kgha2gsm</summary>
        protected const double kgha2gsm = 0.1;

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
        public double RemoveBiomass(double liveToRemove = 0, double deadToRemove = 0, double liveToResidue = 0, double deadToResidue = 0)
        {
            double amountRemoved = biomassRemovalModel.RemoveBiomassToSoil(liveToRemove, liveToResidue, PlantZone.LayerLive, PlantZone.LayerDead, Removed, Detached);
            needToRecalculateLiveDead = true;

            // Commented out code below because about 10 validation files failed on Jenkins
            // e.g. Chicory, Oats
            //if (biomassRemoveType != null && biomassRemoveType != "Harvest")
            //    IsKLModiferDueToDamageActive = true;

            return amountRemoved;
        }

        /// <summary>Harvest the organ.</summary>
        /// <returns>The amount of biomass (live+dead) removed from the plant (g/m2).</returns>
        public double Harvest()
        {
            return RemoveBiomass(biomassRemovalModel.HarvestFractionLiveToRemove, biomassRemovalModel.HarvestFractionDeadToRemove,
                                 biomassRemovalModel.HarvestFractionLiveToResidue, biomassRemovalModel.HarvestFractionDeadToResidue);
        }

        /// <summary>Initialise all zones.</summary>
        private void InitialiseZones()
        {
            Zones.Clear();
            Zones.Add(PlantZone);
            if (ZoneRootDepths.Count != ZoneNamesToGrowRootsIn.Count ||
                ZoneRootDepths.Count != ZoneInitialDM.Count)
                throw new Exception("The root zone variables (ZoneRootDepths, ZoneNamesToGrowRootsIn, ZoneInitialDM) need to have the same number of values");

            for (int i = 0; i < ZoneNamesToGrowRootsIn.Count; i++)
            {
                Zone zone = this.FindInScope(ZoneNamesToGrowRootsIn[i]) as Zone;
                if (zone != null)
                {
                    Soil soil = zone.FindInScope<Soil>();
                    if (soil == null)
                        throw new Exception("Cannot find soil in zone: " + zone.Name);
                    ZoneState newZone = new ZoneState(parentPlant, this, soil, ZoneRootDepths[i], ZoneInitialDM[i], parentPlant.Population, maximumNConc.Value(),
                                                      rootFrontVelocity, maximumRootDepth, remobilisationCost);
                    Zones.Add(newZone);
                }
            }
            needToRecalculateLiveDead = true;
        }

        /// <summary>Clears this instance.</summary>
        private void Clear()
        {
            Live.Clear();
            Dead.Clear();
            PlantZone.Clear();
            Zones.Clear();
            needToRecalculateLiveDead = true;
            GrowthRespiration = 0;
        }

        /// <summary>Clears the transferring biomass amounts.</summary>
        private void ClearBiomassFlows()
        {
            Allocated.Clear();
            Senesced.Clear();
            Detached.Clear();
            Removed.Clear();
        }

        /// <summary>Recalculate live and dead biomass if necessary</summary>
        private void RecalculateLiveDead()
        {
            if (needToRecalculateLiveDead)
            {
                needToRecalculateLiveDead = false;
                liveBiomass.Clear();
                deadBiomass.Clear();
                if (PlantZone != null)
                {
                    foreach (Biomass b in PlantZone.LayerLive)
                        liveBiomass.Add(b);
                    foreach (Biomass b in PlantZone.LayerDead)
                        deadBiomass.Add(b);
                }
            }
        }

        /// <summary>Computes the DM and N amounts that are made available for new growth</summary>
        private void DoSupplyCalculations()
        {
            dmMReallocationSupply = AvailableDMReallocation();
            nReallocationSupply = AvailableNReallocation();
        }

        /// <summary>Computes root total water supply.</summary>
        public double TotalExtractableWater()
        {

            double[] LL = SoilCrop.LL;
            double[] KL = SoilCrop.KL;
            double[] SWmm = PlantZone.WaterBalance.SWmm;
            double[] DZ = PlantZone.Physical.Thickness;

            double supply = 0;
            for (int layer = 0; layer < LL.Length; layer++)
            {
                if (layer <= SoilUtilities.LayerIndexOfDepth(PlantZone.Physical.Thickness, Depth))
                {
                    double available = Math.Max(SWmm[layer] - LL[layer] * DZ[layer] * PlantZone.LLModifier[layer], 0);

                    supply += Math.Max(0.0, KL[layer] * klModifier.Value(layer) * available * PlantZone.RootProportions[layer]);
                }
            }
            return supply;
        }

        /// <summary>Computes the amount of DM available for reallocation.</summary>
        private double AvailableDMReallocation()
        {
            if (dmReallocationFactor != null)
            {
                double rootLiveStorageWt = 0.0;
                foreach (ZoneState Z in Zones)
                    for (int i = 0; i < Z.LayerLive.Length; i++)
                        rootLiveStorageWt += Z.LayerLive[i].StorageWt;

                double availableDM = rootLiveStorageWt * senescenceRate.Value() * dmReallocationFactor.Value();
                if (availableDM < -BiomassToleranceValue)
                    throw new Exception("Negative DM reallocation value computed for " + Name);
                return availableDM;
            }
            // By default reallocation is turned off!!!!
            return 0.0;
        }

        /// <summary>Computes the N amount available for reallocation.</summary>
        private double AvailableNReallocation()
        {
            if (nReallocationFactor != null)
            {
                double rootLiveStorageN = 0.0;
                foreach (ZoneState Z in Zones)
                    for (int i = 0; i < Z.LayerLive.Length; i++)
                        rootLiveStorageN += Z.LayerLive[i].StorageN;

                double availableN = rootLiveStorageN * senescenceRate.Value() * nReallocationFactor.Value();
                if (availableN < -BiomassToleranceValue)
                    throw new Exception("Negative N reallocation value computed for " + Name);

                return availableN;
            }
            else
            {  // By default reallocation is turned off!!!!
                return 0.0;
            }
        }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        /// <exception cref="ApsimXException">Cannot find a soil crop parameterisation for  + Name</exception>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            Soil soil = this.FindInScope<Soil>();
            if (soil == null)
                throw new Exception("Cannot find soil");
            PlantZone = new ZoneState(parentPlant, this, soil, 0, InitialWt, parentPlant.Population, maximumNConc.Value(),
                                      rootFrontVelocity, maximumRootDepth, remobilisationCost);

            SoilCrop = soil.FindDescendant<SoilCrop>(parentPlant.Name + "Soil");
            if (SoilCrop == null)
                throw new Exception("Cannot find a soil crop parameterisation for " + parentPlant.Name);

            Zones = new List<ZoneState>();
            DMDemand = new BiomassPoolType();
            NDemand = new BiomassPoolType();
            DMSupply = new BiomassSupplyType();
            NSupply = new BiomassSupplyType();
            potentialDMAllocation = new BiomassPoolType();
            Allocated = new Biomass();
            Senesced = new Biomass();
            Detached = new Biomass();
            Removed = new Biomass();
            Clear();
        }

        /// <summary>Called when [do daily initialisation].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
        private void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            ClearBiomassFlows();
        }

        /// <summary>Called when crop is sown</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        private void OnPlantSowing(object sender, SowingParameters data)
        {
            if (data.Plant == parentPlant)
            {
                //sorghum calcs
                PlantZone.Initialise(parentPlant.SowingData.Depth, InitialWt, parentPlant.Population, maximumNConc.Value());
                InitialiseZones();

                needToRecalculateLiveDead = true;
            }
        }


        /// <summary>Event from sequencer telling us to do our potential growth.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        private void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            if (parentPlant.IsEmerged)
            {
                DoSupplyCalculations(); //TODO: This should be called from the Arbitrator, OnDoPotentialPlantPartioning
            }
        }

        /// <summary>Does the nutrient allocations.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoActualPlantGrowth")]
        private void OnDoActualPlantGrowth(object sender, EventArgs e)
        {
            if (parentPlant.IsAlive)
            {
                foreach (ZoneState Z in Zones)
                    Z.GrowRootDepth();

                // Do Root Senescence
                RemoveBiomass(liveToResidue: senescenceRate.Value());

                // Do maintenance respiration
                if (maintenanceRespirationFunction.Value() > 0)
                {
                    MaintenanceRespiration = (Live.MetabolicWt + Live.StorageWt) * maintenanceRespirationFunction.Value();
                    Live.MetabolicWt *= (1 - maintenanceRespirationFunction.Value());
                    Live.StorageWt *= (1 - maintenanceRespirationFunction.Value());
                }
                needToRecalculateLiveDead = true;
            }
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantEnding")]
        private void OnPlantEnding(object sender, EventArgs e)
        {
            if (Wt > 0.0)
                RemoveBiomass(liveToResidue: 1.0);

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
    }
}
