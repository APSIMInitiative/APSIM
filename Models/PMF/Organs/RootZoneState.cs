using System;
using System.Linq;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.Soils;

namespace Models.PMF.Organs
{
    /// <summary>The state of each zone that root knows about.</summary>
    [Serializable]
    public class ZoneState : Model, IRootGeometryData
    {
        /// <summary>The soil in this zone</summary>
        public Soil Soil { get; set; }

        /// <summary>The soilcrop in this zone</summary>
        public SoilCrop SoilCrop { get; private set; }

        /// <summary>The soil in this zone</summary>
        public IPhysical Physical { get; set; }

        /// <summary>The water balance in this zone</summary>
        public ISoilWater WaterBalance { get; set; }

        /// <summary>The NO3 solute.</summary>
        public ISolute NO3 = null;

        /// <summary>The NH4 solute.</summary>
        public ISolute NH4 = null;

        /// <summary>The parent plant</summary>
        public Plant plant { get; set; }

        /// <summary>The root organ</summary>
        public Root root = null;

        /// <summary>Is the Weirdo model present in the simulation?</summary>
        public bool IsWeirdoPresent { get; set; }

        /// <summary>The root front velocity function</summary>
        private IFunction rootFrontVelocity;

        /// <summary>The Maximum Root Depth</summary>
        private IFunction maximumRootDepth = null;

        /// <summary>The cost for remobilisation</summary>
        private IFunction remobilisationCost = null;

        /// <summary>The water uptake</summary>
        public double[] WaterUptake { get; set; }

        /// <summary>The delta n h4</summary>
        public double[] DeltaNH4 { get; set; }

        /// <summary>The delta n o3</summary>
        public double[] DeltaNO3 { get; set; }

        /// <summary>Holds actual DM allocations to use in allocating N to structural and Non-Structural pools</summary>
        [Units("g/2")]
        public double[] DMAllocated { get; set; }

        /// <summary>Holds potential DM allocations to use in allocating N to structural and Non-Structural pools</summary>
        [Units("g/2")]
        public double[] PotentialDMAllocated { get; set; }

        /// <summary>Demand for structural N, set when Ndemand is called and used again in N allocation</summary>
        [Units("g/2")]
        public double[] StructuralNDemand { get; set; }

        /// <summary>Demand for Non-structural N, set when Ndemand is called and used again in N allocation</summary>
        [Units("g/m2")]
        public double[] StorageNDemand { get; set; }

        /// <summary>Demand for Metabolic N, set when Ndemand is called and used again in N allocation</summary>
        [Units("g/m2")]
        public double[] MetabolicNDemand { get; set; }

        /// <summary>The Nuptake</summary>
        public double[] NitUptake { get; set; }

        /// <summary>Gets or sets the nuptake supply.</summary>
        public double NuptakeSupply { get; set; }

        /// <summary>Gets or sets the layer live.</summary>
        public Biomass[] LayerLive { get; set; }

        /// <summary>Gets or sets the layer dead.</summary>
        public Biomass[] LayerDead { get; set; }

        /// <summary>Gets or sets the depth.</summary>
        [Units("mm")]
        public double Depth { get; set; }

        /// <summary>Gets the RootFront</summary>
        public double RootLength { get { return Depth - plant.SowingData.Depth; } }

        /// <summary>Gets the RootFront</summary>
        public double RootFront { get; set; }
        /// <summary>Gets the RootFront</summary>
        public double RootSpread { get; set; }
        /// <summary>Gets the RootFront</summary>
        public double LeftDist { get; set; }
        /// <summary>Gets the RootFront</summary>
        public double RightDist { get; set; }

        /// <summary>Gets the RootProportions</summary>
        public double[] RootProportions { get; set; }

        /// <summary>
        /// Proportion of the layer volume occupied by root, for each layer.
        /// </summary>
        public double[] RootProportionVolume { get; set; }

        /// <summary>Gets the LLModifier for leaf angles != RootAngleBase</summary>
        public double[] LLModifier { get; set; }

        /// <summary>Soil area occipied by roots</summary>
        [Units("m2")]
        public double RootArea { get; set; }

        /// <summary>Gets or sets AvailableSW during SW Uptake
        /// Old Sorghum does actual uptake at end of day
        /// PMF does actual uptake before N uptake</summary>
        public double[] AvailableSW { get; set; }

        /// <summary>Gets or sets PotentialAvailableSW during SW Uptake</summary>
        public double[] PotentialAvailableSW { get; set; }

        /// <summary>Record the Water level before </summary>
        public double[] StartWater { get; set; }

        /// <summary>Record the Water Supply before </summary>
        public double[] Supply { get; set; }

        /// <summary>Gets or sets MassFlow during NitrogenUptake Calcs</summary>
        public double[] MassFlow { get; set; }

        /// <summary>Gets or sets Diffusion during NitrogenUptake Calcs</summary>
        public double[] Diffusion { get; set; }


        /// <summary>Constructor</summary>
        /// <param name="Plant">The parant plant</param>
        /// <param name="Root">The parent root organ</param>
        /// <param name="soil">The soil in the zone.</param>
        /// <param name="depth">Root depth (mm)</param>
        /// <param name="initialDM">Initial dry matter</param>
        /// <param name="population">plant population</param>
        /// <param name="maxNConc">maximum n concentration</param>
        /// <param name="rfv">Root front velocity</param>
        /// <param name="mrd">Maximum root depth</param>
        /// <param name="remobCost">Remobilisation cost</param>
        public ZoneState(Plant Plant, Root Root, Soil soil, double depth,
                         NutrientPoolFunctions initialDM, double population, double maxNConc,
                         IFunction rfv, IFunction mrd, IFunction remobCost)
        {
            this.Soil = soil;
            this.plant = Plant;
            this.root = Root;
            this.rootFrontVelocity = rfv;
            this.maximumRootDepth = mrd;
            this.remobilisationCost = remobCost;
            Physical = soil.FindChild<IPhysical>();
            WaterBalance = soil.FindChild<ISoilWater>();
            IsWeirdoPresent = soil.FindChild("Weirdo") != null;
            SoilCrop = Soil.FindDescendant<SoilCrop>(plant.Name + "Soil");
            if (SoilCrop == null)
                throw new Exception($"Cannot find a soil crop parameterisation called {plant.Name + "Soil"}");

            Clear();
            Zone zone = soil.FindAncestor<Zone>();
            if (zone == null)
                throw new Exception("Soil " + soil + " is not in a zone.");
            NO3 = zone.FindInScope<ISolute>("NO3");
            NH4 = zone.FindInScope<ISolute>("NH4");
            Name = zone.Name;
            Initialise(depth, initialDM, population, maxNConc);
        }

        /// <summary>Initialise the zone.</summary>
        /// <param name="depth">Root depth (mm)</param>
        /// <param name="initialDM">Initial dry matter</param>
        /// <param name="population">plant population</param>
        /// <param name="maxNConc">maximum n concentration</param>
        public void Initialise(double depth, NutrientPoolFunctions initialDM, double population, double maxNConc)
        {
            Depth = depth;
            RootFront = depth;
            //distribute root biomass evenly through root depth
            double[] fromLayer = new double[1] { depth };
            double[] fromStructural = new double[1] { initialDM.Structural.Value() };
            double[] toStructural = SoilUtilities.MapMass(fromStructural, fromLayer, Physical.Thickness);
            double[] fromMetabolic = new double[1] { initialDM.Metabolic.Value() };
            double[] toMetabolic = SoilUtilities.MapMass(fromMetabolic, fromLayer, Physical.Thickness);
            double[] fromStorage = new double[1] { initialDM.Storage.Value() };
            double[] toStorage = SoilUtilities.MapMass(fromStorage, fromLayer, Physical.Thickness);

            for (int layer = 0; layer < Physical.Thickness.Length; layer++)
            {
                LayerLive[layer].StructuralWt = toStructural[layer] * population;
                LayerLive[layer].MetabolicWt = toMetabolic[layer] * population;
                LayerLive[layer].StorageWt = toStorage[layer] * population;
                LayerLive[layer].StructuralN = LayerLive[layer].StructuralWt * maxNConc;
                LLModifier[layer] = 1;
            }

            if (plant.SowingData != null)
            {
                if (plant.SowingData.SkipType == 0)
                {
                    LeftDist = plant.SowingData.RowSpacing * 0.5;
                    RightDist = plant.SowingData.RowSpacing * 0.5;
                }
                if (plant.SowingData.SkipType == 1)
                {
                    LeftDist = plant.SowingData.RowSpacing * 1.0;
                    RightDist = plant.SowingData.RowSpacing * 1.0;
                }
                if (plant.SowingData.SkipType == 2)
                {
                    LeftDist = plant.SowingData.RowSpacing * 1.0;
                    RightDist = plant.SowingData.RowSpacing * 0.5;
                }
                if (plant.SowingData.SkipType == 3)
                {
                    LeftDist = plant.SowingData.RowSpacing * 1.5;
                    RightDist = plant.SowingData.RowSpacing * 0.5;
                }
            }
            root.RootShape.CalcRootProportionInLayers(this);
        }

        /// <summary>Clears this instance.</summary>
        public void Clear()
        {
            WaterUptake = null;
            NitUptake = null;
            DeltaNO3 = new double[Physical.Thickness.Length];
            DeltaNH4 = new double[Physical.Thickness.Length];
            RootProportions = new double[Physical.Thickness.Length];
            RootProportionVolume = new double[Physical.Thickness.Length];
            LLModifier = new double[Physical.Thickness.Length];

            Depth = 0.0;

            if (LayerLive == null || LayerLive.Length == 0)
            {
                LayerLive = new Biomass[Physical.Thickness.Length];
                LayerDead = new Biomass[Physical.Thickness.Length];
                for (int i = 0; i < Physical.Thickness.Length; i++)
                {
                    LayerLive[i] = new Biomass();
                    LayerDead[i] = new Biomass();
                }
            }
            else
            {
                for (int i = 0; i < Physical.Thickness.Length; i++)
                {
                    LayerLive[i].Clear();
                    LayerDead[i].Clear();
                }
            }
        }

        /// <summary>
        /// Growth depth of roots in this zone
        /// </summary>
        public void GrowRootDepth()
        {
            // Do Root Front Advance
            int RootLayer = SoilUtilities.LayerIndexOfDepth(Physical.Thickness, Depth);
            var rootfrontvelocity1 = rootFrontVelocity.Value(RootLayer);
            int NextLayer = Math.Min(RootLayer + 1, Physical.Thickness.Length-1);
            var rootfrontvelocity2 = rootFrontVelocity.Value(NextLayer);

            double wf = 0; // weighting factor
            if (RootLayer > 0)
                wf = (Depth - Physical.ThicknessCumulative[RootLayer-1])/Physical.Thickness[RootLayer];
            else
                wf = Depth / Physical.Thickness[0];

            

            var rootfrontvelocity = wf * rootfrontvelocity2 + (1 - wf) * rootfrontvelocity1;

            double MaxDepth;
            double[] xf = null;
            if (!IsWeirdoPresent)
            {
                var soilCrop = Soil.FindDescendant<SoilCrop>(plant.Name + "Soil");
                if (soilCrop == null)
                    throw new Exception($"Cannot find a soil crop parameterisation called {plant.Name}Soil");

                xf = soilCrop.XF;

                Depth = Depth + rootfrontvelocity * xf[RootLayer];
                MaxDepth = 0;
                // Limit root depth for impeded layers
                for (int i = 0; i < Physical.Thickness.Length; i++)
                {
                    if (xf[i] > 0)
                        MaxDepth += Physical.Thickness[i];
                    else
                        break;
                }
            }
            else
            {
                Depth = Depth + rootfrontvelocity;
                MaxDepth = Physical.Thickness.Sum();
            }

            // Limit root depth for the crop specific maximum depth
            MaxDepth = Math.Min(maximumRootDepth.Value(), MaxDepth);
            Depth = Math.Min(Depth, MaxDepth);

            RootFront = Depth;
            root.RootShape.CalcRootProportionInLayers(this);
            root.RootShape.CalcRootVolumeProportionInLayers(this);
        }
        /// <summary>
        /// Calculate Root Activity Values for water and nitrogen
        /// </summary>
        public double[] CalculateRootActivityValues()
        {
            int currentLayer = SoilUtilities.LayerIndexOfDepth(Physical.Thickness, Depth);
            double[] RAw = new double[Physical.Thickness.Length];
            for (int layer = 0; layer < Physical.Thickness.Length; layer++)
            {
                if (layer <= currentLayer)
                    if (LayerLive[layer].Wt > 0)
                    {
                        RAw[layer] = -WaterUptake[layer] / LayerLive[layer].Wt
                                   * Physical.Thickness[layer]
                                   * SoilUtilities.ProportionThroughLayer(Physical.Thickness, layer, Depth);
                        RAw[layer] = Math.Max(RAw[layer], 1e-20);  // Make sure small numbers to avoid lack of info for partitioning
                    }
                    else if (layer > 0)
                        RAw[layer] = RAw[layer - 1];
                    else
                        RAw[layer] = 0;
            }
            return RAw;
        }

        /// <summary>
        /// Partition root mass into layers
        /// </summary>
        public void PartitionRootMass(double TotalRAw, Biomass TotalDMAllocated)
        {
            DMAllocated = new double[Physical.Thickness.Length];

            if (Depth > 0)
            {
                double[] RAw = CalculateRootActivityValues();

                for (int layer = 0; layer < Physical.Thickness.Length; layer++)
                    if (TotalRAw > 0)
                    {
                        LayerLive[layer].StructuralWt += TotalDMAllocated.StructuralWt * RAw[layer] / TotalRAw;
                        LayerLive[layer].StorageWt += TotalDMAllocated.StorageWt * RAw[layer] / TotalRAw;
                        LayerLive[layer].MetabolicWt += TotalDMAllocated.MetabolicWt * RAw[layer] / TotalRAw;

                        DMAllocated[layer] += (TotalDMAllocated.StructuralWt +
                                               TotalDMAllocated.StorageWt +
                                               TotalDMAllocated.MetabolicWt) * RAw[layer] / TotalRAw;
                    }
            }
        }
    }


}
