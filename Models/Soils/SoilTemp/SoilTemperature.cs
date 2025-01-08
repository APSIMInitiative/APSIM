using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Interfaces;

namespace Models.Soils.SoilTemp
{
    /// <summary>
    /// The soil temperature model includes functionality for simulating the heat flux and temperatures over
    /// the soil profile, includes temperature on the soil surface. The processes are described below, most
    /// are based on Campbell, 1985. "Soil physics with BASIC: Transport models for soil-plant systems"
    /// </summary>
    /// <remarks>
    /// Since temperature changes rapidly near the soil surface and very little at depth, the best simulation
    /// will be obtained with short elements (shallow layers) near the soil surface and longer ones deeper in
    /// the soil. Ideally, the element lengths should follow a geometric progression...
    /// Ten to twelve nodes are probably sufficient for short-term simulations (daily or weekly). Fifteen nodes
    /// would probably be sufficient for annual cycle simulation where a deeper grid is needed.
    /// p36, Campbell, G.S. (1985) "Soil physics with BASIC: Transport models for soil-plant systems"
    /// --------------------------------------------------------------------------------------------------------
    /// -----------------------------------------------IMPORTANT NOTE-------------------------------------------
    /// Due to FORTRAN's 'flexibility' with arrays that are not present in C#, few modifications have been done
    /// to array sizes in this version of SoilTemp. Here, all arrays are forcibly 0-based, so to deal with the
    /// fact that the original module had both 0- and 1-based arrays, all arrays have been increased in size by
    /// one. With this approach, the indexing does not need to change in those processes that in the FORTRAN code
    /// that processed 1-based arrays, here the first (0th) element would simply not be used...
    /// This is actually rather convenient. In these arrays, the element 0 now refers to the air (airNode),
    /// the element 1 refers to the soil surface (surfaceNode), and from elements 2 (topsoilNode) to numNodes+1
    /// all nodes refer the middle of layers within the soil.
    /// ----------------------------------------------------------------------------------------------------------
    /// </remarks>
    /// <structure>
    /// In the soil temperature model, the soil profile is represented (abstracted) by two schema:
    ///  a) Soil layers, each with a top and bottom boundary and a thickness (mm). Their index ranges from
    ///  one to numLayers;
    ///  b) Temperature nodes, all dimensionless, one at the centre of each layer, plus additional nodes to
    ///  represent a node below the bottom layer and a node above the top layer (in the atmosphere). Index
    ///  ranges from 0 to numLayers + 1;
    /// </structure>
    [Serializable]
    [ValidParent(ParentType = typeof(Soil))]
    [ViewName("ApsimNG.Resources.Glade.ProfileView.glade")]
    [PresenterName("UserInterface.Presenters.ProfilePresenter")]
    public class SoilTemperature : Model, ISoilTemperature
    {
        [Link]
        private IWeather weather = null;

        [Link]
        private IClock clock = null;

        [Link(IsOptional = true)]   // Simulations without plants don't have a micro climate instance
        private MicroClimate microClimate = null;

        [Link]
        private Physical physical = null;

        [Link]
        private Organic organic = null;

        [Link]
        ISoilWater waterBalance = null;

        #region Table of properties for soil constituents   - - - - - - - - - - - - - - - - - - - -

        /// <summary>Particle density of organic matter (Mg/m3), from Campbell (1985)</summary>
        private double pom = 1.3;   // CHECK, should come from soil physical

        /// <summary>Particle density of soil fines (Mg/m3)</summary>
        private double ps = 2.63;   // CHECK, should come from soil physical

        /// <summary>List of names of soil constituents</summary>
        private string[] soilConstituentNames = { "Rocks", "OrganicMatter", "Sand", "Silt", "Clay", "Water", "Ice", "Air" };

        /// <summary>Gets the volumetric specific heat of soil constituents (MJ/m3/K)</summary>
        /// <param name="name">The name of the constituent</param>
        /// <param name="layer">The layer index</param>
        private double volumetricSpecificHeat(string name, int layer)
        {
            double specificHeatRocks = 7.7;
            double specificHeatOM = 0.25;
            double specificHeatSand = 7.7;
            double specificHeatSilt = 2.74;
            double specificHeatClay = 2.92;
            double specificHeatWater = 0.57;
            // CHECK, value seems wrong, wikipedia gives 4.18 MJ/m3/K.
            // Also, water has one of the highest values for specific heat, and here it isn't
            double specificHeatIce = 2.18;
            double specificHeatAir = 0.025;

            double result = 0.0;

            if (name == "Rocks")
            {
                result = specificHeatRocks;
            }
            else if (name == "OrganicMatter")
            {
                result = specificHeatOM;
            }
            else if (name == "Sand")
            {
                result = specificHeatSand;
            }
            else if (name == "Silt")
            {
                result = specificHeatSilt;
            }
            else if (name == "Clay")
            {
                result = specificHeatClay;
            }
            else if (name == "Water")
            {
                result = specificHeatWater;
            }
            else if (name == "Ice")
            {
                result = specificHeatIce;
            }
            else if (name == "Air")
            {
                result = specificHeatAir;
            }
            else
            {
                throw new Exception("Cannot return specific heat for " + name);
            }

            return result;
        }

        /// <summary>Gets the thermal conductance of soil constituents (W/K)</summary>
        /// <param name="name">The name of the constituent</param>
        /// <param name="layer">The layer index</param>
        private double ThermalConductance(string name, int layer)
        {
            double thermalConductanceRocks = 0.182;
            double thermalConductanceOM = 2.50;
            double thermalConductanceSand = 0.182;
            double thermalConductanceSilt = 2.39;
            double thermalConductanceClay = 1.39;
            double thermalConductanceWater = 4.18;  // CHECK, this value seems to be the specific heat
            double thermalConductanceIce = 1.73;
            double thermalConductanceAir = 0.0012;

            double result = 0.0;

            if (name == "Rocks")
            {
                result = thermalConductanceRocks;
            }
            else if (name == "OrganicMatter")
            {
                result = thermalConductanceOM;
            }
            else if (name == "Sand")
            {
                result = thermalConductanceSand;
            }
            else if (name == "Silt")
            {
                result = thermalConductanceSilt;
            }
            else if (name == "Clay")
            {
                result = thermalConductanceClay;
            }
            else if (name == "Water")
            {
                result = thermalConductanceWater;
            }
            else if (name == "Ice")
            {
                result = thermalConductanceIce;
            }
            else if (name == "Air")
            {
                result = thermalConductanceAir;
            }
            else if (name == "Minerals")
            {
                result = Math.Pow(thermalConductanceRocks, volumetricFractionRocks(layer)) *
                         Math.Pow(thermalConductanceSand, volumetricFractionSand(layer)) +
                         Math.Pow(thermalConductanceSilt, volumetricFractionSilt(layer)) +
                         Math.Pow(thermalConductanceClay, volumetricFractionClay(layer));
                // CHECK, this function seem odd (wrong), why power function, why multiply and add???
            }
            else
            {
                throw new Exception("Cannot return thermal conductance for " + name);
            }

            result = volumetricSpecificHeat(name, layer);  // This is wrong, but is was in the code...
            return result;
        }

        /// <summary>Gets the shape factor of soil constituents (W/m/K - CHECK, unit)</summary>
        /// <param name="name">The name of the constituent</param>
        /// <param name="layer">The layer index</param>
        private double shapeFactor(string name, int layer)
        {
            double shapeFactorRocks = 0.182;
            double shapeFactorOM = 0.5;
            double shapeFactorSand = 0.182;
            double shapeFactorSilt = 0.125;
            double shapeFactorClay = 0.007755;
            double shapeFactorWater = 1.0;
            //double shapeFactorIce = 0.0;
            //double shapeFactorAir = double.NaN;

            double result = 0.0;

            if (name == "Rocks")
            {
                result = shapeFactorRocks;
            }
            else if (name == "OrganicMatter")
            {
                result = shapeFactorOM;
            }
            else if (name == "Sand")
            {
                result = shapeFactorSand;
            }
            else if (name == "Silt")
            {
                result = shapeFactorSilt;
            }
            else if (name == "Clay")
            {
                result = shapeFactorClay;
            }
            else if (name == "Water")
            {
                result = shapeFactorWater;
            }
            else if (name == "Ice")
            {
                result = 0.333 - 0.333 * volumetricFractionIce(layer) /
                         (volumetricFractionWater(layer) + volumetricFractionIce(layer) + volumetricFractionAir(layer));
                // CHECK, the value of shapeFactorIce is not used...?
                return result; // CHECK, not right but replicate what was in the code
            }
            else if (name == "Air")
            {
                result = 0.333 - 0.333 * volumetricFractionAir(layer) /
                    (volumetricFractionWater(layer) + volumetricFractionIce(layer) + volumetricFractionAir(layer));
                // CHECK, the value of shapeFactorAir is not used...?
                return result; // CHECK, not right but replicate what was in the code
            }
            else if (name == "Minerals")
            {
                result = shapeFactorRocks * volumetricFractionRocks(layer) +
                         shapeFactorSand * volumetricFractionSand(layer) +
                         shapeFactorSilt * volumetricFractionSilt(layer) +
                         shapeFactorClay * volumetricFractionClay(layer);
            }
            else
            {
                throw new Exception("Cannot return thermal conductance for " + name);
            }

            result = volumetricSpecificHeat(name, layer);  // This is wrong, but is was in the code...
            return result;
        }

        /// <summary>Volumetric fraction of rocks in the soil (m3/m3)</summary>
        private double volumetricFractionRocks(int layer) => rocks[layer] / 100.0;

        /// <summary>Volumetric fraction of organic matter in the soil (m3/m3)</summary>
        private double volumetricFractionOrganicMatter(int layer) => carbon[layer] / 100.0 * 2.5 * bulkDensity[layer] / pom;

        /// <summary>Volumetric fraction of sand in the soil (m3/m3)</summary>
        private double volumetricFractionSand(int layer) => (1 - volumetricFractionOrganicMatter(layer) - volumetricFractionRocks(layer)) *
                                                       sand[layer] / 100.0 * bulkDensity[layer] / ps;

        /// <summary>Volumetric fraction of silt in the soil (m3/m3)</summary>
        private double volumetricFractionSilt(int layer) => (1 - volumetricFractionOrganicMatter(layer) - volumetricFractionRocks(layer)) *
                                                       silt[layer] / 100.0 * bulkDensity[layer] / ps;

        /// <summary>Volumetric fraction of clay in the soil (m3/m3)</summary>
        private double volumetricFractionClay(int layer) => (1 - volumetricFractionOrganicMatter(layer) - volumetricFractionRocks(layer)) *
                                                       clay[layer] / 100.0 * bulkDensity[layer] / ps;

        /// <summary>Volumetric fraction of water in the soil (m3/m3)</summary>
        private double volumetricFractionWater(int layer) => (1 - volumetricFractionOrganicMatter(layer)) * soilWater[layer];

        /// <summary>Volumetric fraction of ice in the soil (m3/m3)</summary>
        /// <remarks>
        /// Not implemented yet, might be simulated in the future. Something like:
        ///  (1 - VolumetricFractionOrganicMatter(i)) * waterBalance.Ice[i];
        /// </remarks>
        private double volumetricFractionIce(int layer) => 0.0;

        /// <summary>Volumetric fraction of air in the soil (m3/m3)</summary>
        private double volumetricFractionAir(int layer)
        {
            return 1.0 - volumetricFractionRocks(layer) -
                         volumetricFractionOrganicMatter(layer) -
                         volumetricFractionSand(layer) -
                         volumetricFractionSilt(layer) -
                         volumetricFractionClay(layer) -
                         volumetricFractionWater(layer) -
                         volumetricFractionIce(layer);
        }

        /// <summary>Calculate the density of air at a given environmental conditions</summary>
        /// <param name="temperature">temperature (oC)</param>
        /// <param name="AirPressure">air pressure (hPa)</param>
        /// <returns>density of air (kg/m3)</returns>
        /// <remarks>From - CHECK</remarks>
        private double airDensity(double temperature, double AirPressure)
        {
            const double MWair = 0.02897;     // molecular weight air (kg/mol)
            const double RGAS = 8.3143;       // universal gas constant (J/mol/K)
            const double HPA2PA = 100.0;      // hectoPascals to Pascals

            return MathUtilities.Divide(MWair * AirPressure * HPA2PA, kelvinT(temperature) * RGAS, 0.0);
        }

        #endregion  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        #region constants   - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        /// <summary>Internal time-step (minutes)</summary>
        private double timestep = 24.0 * 60.0 * 60.0;

        /// <summary>Latent heat of vapourisation for water (J/kg)</summary>
        /// <remarks>Evaporation of 1.0 mm/day = 1 kg/m^2/day requires 2.45*10^6 J/m^2</remarks>
        private const double latentHeatOfVapourisation = 2465000.0;

        /// <summary>The Stefan-Boltzmann constant (W/m2/K4)</summary>
        /// <remarks>Power per unit area emitted by a black body as a function of its temperature</remarks>
        private const double stefanBoltzmannConstant = 0.0000000567;

        /// <summary>The index of the node in the atmosphere (aboveground)</summary>
        private const int airNode = 0;

        /// <summary>The index of the node on the soil surface (depth = 0)</summary>
        private const int surfaceNode = 1;

        /// <summary>The index of the first node within the soil (top layer)</summary>
        private const int topsoilNode = 2;

        /// <summary>The number of phantom nodes below the soil profile</summary>
        /// <remarks>These are needed for the lower BC to work properly, invisible externally</remarks>
        private const int numPhantomNodes = 5;

        /// <summary>Boundary layer conductance, if constant (K/W)</summary>
        /// <remarks>From Program 12.2, p140, Campbell, Soil Physics with Basic</remarks>
        private const double constantBoundaryLayerConductance = 20;

        /// <summary>Number of iterations to calculate atmosphere boundary layer conductance</summary>
        private const int numIterationsForBoundaryLayerConductance = 1;

        /// <summary>Time of maximum temperature (h)</summary>
        private double defaultTimeOfMaximumTemperature = 14.0;  // FIXME, there should be an input ot calculation for non-default (also fot minT)

        /// <summary>Default instrument height (m)</summary>
        private const double defaultInstrumentHeight = 1.2;

        /// <summary>Roughness element height of bare soil (mm)</summary>
        private const double bareSoilRoughness = 57;

        #endregion  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        #region Internal variables for this model   - - - - - - - - - - - - - - - - - - - - - - - -

        /// <summary>Flag whether initialisation is needed</summary>
        private bool doInitialisationStuff = false;

        /// <summary>Internal time-step (s)</summary>
        private double internalTimeStep = 0.0;

        /// <summary>Time of day from midnight (s)</summary>
        private double timeOfDaySecs = 0.0;

        /// <summary>Number of nodes over the soil profile</summary>
        private int numNodes = 0;

        /// <summary>Number of layers in the soil profile</summary>
        private int numLayers = 0;

        /// <summary>Depths of nodes (m)</summary>
        private double[] nodeDepth;

        /// <summary>Parameter 1 for computing thermal conductivity of soil solids</summary>
        private double[] thermCondPar1;

        /// <summary>Parameter 2 for computing thermal conductivity of soil solids</summary>
        private double[] thermCondPar2;

        /// <summary>Parameter 3 for computing thermal conductivity of soil solids</summary>
        private double[] thermCondPar3;

        /// <summary>Parameter 4 for computing thermal conductivity of soil solids</summary>
        private double[] thermCondPar4;

        /// <summary>Volumetric specific heat over the soil profile (J/K/m3)</summary>
        private double[] volSpecHeatSoil;

        /// <summary>Temperature of each soil layer (oC)</summary>
        private double[] soilTemp;

        /// <summary>Soil temperature over the soil profile at morning (oC)</summary>
        private double[] morningSoilTemp;

        /// <summary>CP, heat storage between nodes (J/K) - index is same as upper node</summary>
        private double[] heatStorage;

        /// <summary>K, conductance of element between nodes (W/K) - index is same as upper node</summary>
        private double[] thermalConductance;

        /// <summary>thermal conductivity (W.m/K)</summary>
        private double[] thermalConductivity;

        /// <summary>Average daily atmosphere boundary layer conductance</summary>
        private double boundaryLayerConductance = 0.0;

        /// <summary>Soil temperature at the end of this iteration (oC)</summary>
        private double[] newTemperature;

        /// <summary>Air temperature (oC)</summary>
        private double airTemperature = 0.0;

        /// <summary>Yesterday's maximum daily air temperature (oC)</summary>
        private double maxTempYesterday = 0.0;

        /// <summary>Yesterday's minimum daily air temperature (oC)</summary>
        private double minTempYesterday = 0.0;

        /// <summary>Volumetric water content of each soil layer (mm3/mm3)</summary>
        private double[] soilWater;

        /// <summary>Minimum soil temperature (oC)</summary>
        private double[] minSoilTemp;

        /// <summary>Maximum soil temperature (oC)</summary>
        private double[] maxSoilTemp;

        /// <summary>Average soil temperature (oC)</summary>
        private double[] aveSoilTemp;

        /// <summary>Thickness of each soil, includes phantom layer (mm)</summary>
        private double[] thickness;

        /// <summary>Stores the value of MathUtilities.Sum(thickness, 1, layer) for each layer.</summary>
        private double[] depthAbove;

        /// <summary>Soil bulk density, includes phantom layer (g/cm3)</summary>
        private double[] bulkDensity;

        /// <summary>Volumetric fraction of rocks in each soil layer (%)</summary>
        private double[] rocks;

        /// <summary>Volumetric fraction of carbon (CHECK, OM?) in each soil layer (%)</summary>
        private double[] carbon;

        /// <summary>Volumetric fraction of sand in each soil layer (%)</summary>
        private double[] sand;

        /// <summary>Volumetric fraction of silt in each soil layer (%)</summary>
        private double[] silt;

        /// <summary>Volumetric fraction of clay in each soil layer (%)</summary>
        private double[] clay;

        /// <summary>Height of soil roughness (mm)</summary>
        private double soilRoughnessHeight = 0.0;

        /// <summary>Height of instruments above soil surface (m)</summary>
        private double instrumentHeight = 0.0;

        /// <summary>Net radiation per internal time-step (MJ)</summary>
        private double netRadiation = 0.0;

        /// <summary>Height of canopy above ground (m)</summary>
        private double canopyHeight = 0.0;

        /// <summary>Height of instruments above ground</summary>
        private double instrumHeight = 0.0;  // FIXME, this should be read in (or deleted, use default)

        //        /// <summary>Altitude at site (m)</summary>
        //        private double altitude = 0.0;   // FIXME, this should be read from weather(?)

        /// <summary>Forward/backward differencing coefficient (0-1)</summary>
        /// <remarks>
        /// This is a weighting factor used to determined how the heat flux is to be computed.
        /// The numerical procedure that results from setting this to zero is called a forward difference
        /// of the explicit method. If set to 0.5, the average of the old and new temperatures is used to
        /// compute heat flux. This is called a time-centred or Crank-Nicholson scheme.
        /// The equation for computing T(j+1) is an 'implicit' scheme for any choice of this parameter
        /// (except zero, which is called the 'explicit' scheme). This is because T(j+1) depends on the
        /// values of the new temperatures at the nodes i+1 and i-1.
        /// Most heat flow models use either 0.0 or 0.5. However, the best value for this parameters is
        /// determined by consideration of numerical stability and accuracy.
        /// The explicit scheme predicts more heat transfer between nodes than would actually occur, and
        /// can therefore become unstable if time steps are too large. Stable numerical solutions are only
        /// obtained when deltaT is less than CH*(deltaZ)^2 / 2*lambda (Simonson, 1975).
        /// When using 0.5, stable solutions to the heat flow problem will always be obtained, while if
        /// set to a too small value, the solutions may oscillate. The reason for this is that simulated
        /// heat transfer between nodes 'overshoots' (thus in the next time step the excess heat must be
        /// transferred back). If the parameter is closer to one, the temperature difference will be too
        /// small and not enough heat will be transferred. Simulated temperatures will never oscillate in
        /// these conditions, but the simulation will underestimate heat flux. Best accuracy is obtained
        /// with a value around 0.4, while best stability is at one. A typical compromise is nu=0.6.
        /// </remarks>
        private double nu = 0.6;

        /// <summary>Flag whether boundary layer conductance is calculated or gotten from input</summary>
        private string boundarLayerConductanceSource = "calc";

        /// <summary>Flag whether net radiation is calculated or gotten from input</summary>
        private string netRadiationSource = "calc";

        #endregion  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        #region Input for this model  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        /// <summary>Depth strings. Wrapper around Thickness</summary>
        /// <remarks>Use for display only, values taken from thickness</remarks>
        [Display]
        [Summary]
        [Units("mm")]
        [JsonIgnore]
        public string[] Depth
        {
            get => SoilUtilities.ToDepthStrings(Thickness);
            set => Thickness = SoilUtilities.ToThickness(value);
        }

        /// <summary>Thickness of soil layers (mm)</summary>
        public double[] Thickness { get; set; }

        /// <summary>Initial values for the temperature in each layer (oC)</summary>
        [Summary]
        [Display(Format = "N2")]
        [Units("oC")]
        public double[] InitialValues { get; set; }

        /// <summary>Depth down to the constant temperature (mm)</summary>
        [JsonIgnore]   //FIXME, should this be in GUI?
        [Units("mm")]
        public double DepthToConstantTemperature { get; set; } = 10000.0;

        #endregion  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        #region Outputs from this model - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        /// <summary>Temperatures over the soil profile at end of a day, i.e. at midnight</summary>
        [Units("oC")]
        public double[] FinalSoilTemperature
        {
            get
            {
                double[] result = new double[numLayers];
                Array.ConstrainedCopy(soilTemp, topsoilNode, result, 0, numLayers);
                return result;
            }
        }

        /// <summary>Temperature of soil surface at end of a day, i.e. at midnight</summary>
        [Units("oC")]
        public double FinalSoilSurfaceTemperature { get { return soilTemp[surfaceNode]; } }

        /// <summary>Temperature of soil layers averaged over a day</summary>
        /// <remarks>Mandatory for ISoilTemperature interface. For now, just return average daily values - CHECK</remarks>
        [Units("oC")]
        public double[] Value { get { return AverageSoilTemperature; } }

        /// <summary>Temperatures over soil profile averaged over a day</summary>
        /// <remarks>If called during init1, this will return an array of length 100 with all elements as 0.0</remarks>
        [Units("oC")]
        public double[] AverageSoilTemperature
        {
            get
            {
                double[] result = new double[numLayers];
                Array.ConstrainedCopy(aveSoilTemp, topsoilNode, result, 0, numLayers);
                return result;
            }
        }

        /// <summary>Temperature of soil surface averaged over a day</summary>
        [Units("oC")]
        public double AverageSoilSurfaceTemperature { get { return aveSoilTemp[surfaceNode]; } }

        /// <summary>Minimum temperatures over the soil profile within a day</summary>
        [Units("oC")]
        public double[] MinimumSoilTemperature
        {
            get
            {
                double[] result = new double[numLayers];
                Array.ConstrainedCopy(minSoilTemp, topsoilNode, result, 0, numLayers);
                return result;
            }
        }

        /// <summary>Minimum temperatures of soil surface within a day</summary>
        [Units("oC")]
        public double MinimumSoilSurfaceTemperature { get { return minSoilTemp[surfaceNode]; } }

        /// <summary>Maximum temperatures over the soil profile within a day</summary>
        [Units("oC")]
        public double[] MaximumSoilTemperature
        {
            get
            {
                double[] result = new double[numLayers];
                Array.ConstrainedCopy(maxSoilTemp, topsoilNode, result, 0, numLayers);
                return result;
            }
        }

        /// <summary>Maximum temperatures of soil surface within a day</summary>
        [Units("oC")]
        public double MaximumSoilSurfaceTemperature { get { return maxSoilTemp[surfaceNode]; } }

        /// <summary>Atmosphere boundary layer conductance averaged over a day</summary>
        [Units("W/K")]
        public double BoundaryLayerConductance { get { return boundaryLayerConductance; } }

        /// <summary>Thermal conductivity over the soil profile</summary>
        [Units("W.m/K")]
        public double[] ThermalConductivity
        {
            get
            {
                double[] result = new double[numNodes];
                Array.ConstrainedCopy(thermalConductivity, 1, result, 0, numNodes);  // FIXME - should index be 2?
                return result;
            }
        }

        /// <summary>Volumetric heat capacity over the soil profile</summary>
        [Units("J/K/m3")]
        public double[] HeatCapacity
        {
            get
            {
                double[] result = new double[numNodes];
                Array.ConstrainedCopy(volSpecHeatSoil, surfaceNode, result, 0, numNodes);  // FIXME - should index be 2?
                return result;
            }
        }

        /// <summary>Heat storage over the soil profile</summary>
        [Units("J/K")]
        public double[] HeatStore
        {
            get
            {
                double[] result = new double[numNodes];
                Array.ConstrainedCopy(heatStorage, surfaceNode, result, 0, numNodes);  // FIXME - should index be 2?
                return result;
            }
        }

        /// <summary>FIXME</summary>
        [Units("oC")]
        public double[] Thr_profile  // CHECK, needed anywhere??
        {
            get
            {
                double[] result = new double[numNodes + 1 + 1];
                morningSoilTemp.CopyTo(result, 0);
                return result;
            }
        }

        #endregion  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        #region Events invoked or subscribed by this model  - - - - - - - - - - - - - - - - - - - -

        /// <summary>Event invoke when the soil temperature has changed</summary>
        public event EventHandler SoilTemperatureChanged;

        /// <summary>Performs the tasks to initialise the model</summary>
        [EventSubscribe("StartOfSimulation")]
        private void OnStartOfSimulation(object sender, EventArgs e)
        {
            doInitialisationStuff = true;
            getIniVariables();
            getProfileVariables();
            readParam();
        }

        [EventSubscribe("EndOfSimulation")]
        private void OnEndOfSimulation(object sender, EventArgs e)
        {
            InitialValues = null;
        }

        /// <summary>Performs the tasks to simulate soil temperature</summary>
        [EventSubscribe("DoSoilTemperature")]
        private void OnProcess(object sender, EventArgs e)
        {
            getOtherVariables();       // FIXME - note: Need to set yesterday's MaxTg and MinTg to today's at initialisation

            if (doInitialisationStuff)
            {
                if (MathUtilities.ValuesInArray(InitialValues))
                {
                    soilTemp = new double[numNodes + 1 + 1];
                    Array.ConstrainedCopy(InitialValues, 0, soilTemp, topsoilNode, InitialValues.Length);
                }
                else
                {
                    // set t and tnew values to TAve. soil_temp is currently not used
                    calcSoilTemperature(ref soilTemp);
                    InitialValues = new double[numLayers];
                    Array.ConstrainedCopy(soilTemp, topsoilNode, InitialValues, 0, numLayers);
                }

                soilTemp[airNode] = weather.MeanT;
                soilTemp[surfaceNode] = calcSurfaceTemperature();

                // initialise the phantom nodes
                for (int i = numNodes + 1; i < soilTemp.Length; i++)
                    soilTemp[i] = weather.Tav;

                // gT_zb(gNz + 1) = gT_zb(gNz)
                soilTemp.CopyTo(newTemperature, 0);

                // 'gTAve = tav
                // 'For node As Integer = AIRNODE To gNz + 1      ' FIXME - need here until variable passing on init2 enabled
                // '    gT_zb(node) = gTAve                  ' FIXME - need here until variable passing on init2 enabled
                // '    gTNew_zb(node) = gTAve                 ' FIXME - need here until variable passing on init2 enabled
                // 'Next node
                maxTempYesterday = weather.MaxT;
                minTempYesterday = weather.MinT;
                doInitialisationStuff = false;
            }

            doProcess();

            SoilTemperatureChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -

        /// <summary>Performs the tasks to reset the model</summary>
        /// <param name="values">The value of soil temperature to set the model (optional)</param>
        public void Reset(double[] values = null)
        {
            if (values == null)
            {
                Array.ConstrainedCopy(InitialValues, 0, soilTemp, topsoilNode, InitialValues.Length);
            }
            else
            {
                int expectedNumValues = soilTemp.Length - numPhantomNodes - 2;
                if (values.Length != expectedNumValues)
                {
                    throw new Exception($"Not enough values specified when resetting soil temperature. There needs to be {expectedNumValues} values.");
                }

                Array.ConstrainedCopy(values, 0, soilTemp, topsoilNode, values.Length);
            }

            soilTemp[airNode] = weather.MeanT;
            soilTemp[surfaceNode] = calcSurfaceTemperature();
            int firstPhantomNode = topsoilNode + InitialValues.Length;
            for (int i = firstPhantomNode; i < firstPhantomNode + numPhantomNodes; i++)
            {
                soilTemp[i] = weather.Tav;
            }

            soilTemp.CopyTo(newTemperature, 0);
        }

        /// <summary>Initialise global variables to initial values</summary>
        /// <remarks></remarks>
        private void getIniVariables()
        {
            boundCheck(weather.Tav, -30.0, 50.0, "tav (oC)");

            if ((instrumHeight > 0.00001))
                instrumentHeight = instrumHeight;
            else
                instrumentHeight = defaultInstrumentHeight;
        }

        /// <summary>Set soil variables, from layers to nodes over the soil profile</summary>
        /// <remarks>
        /// mapping of nodes to layers:
        ///  node 0 is in the air, node 1 is on the soil surface,
        ///  node 2 at the middle of layer 1, node 3 in layer 3 and so on,
        ///  the last node is below the soil profile, thus at NumLayers + 1
        /// </remarks>
        private void getProfileVariables()
        {
            numLayers = physical.Thickness.Length;
            numNodes = numLayers + numPhantomNodes;

            // set internal thickness array, add layers for zone below bottom layer plus one for surface
            thickness = new double[numLayers + numPhantomNodes + 1];
            physical.Thickness.CopyTo(thickness, 1);

            // add enough to make last node at 9-10 meters - should always be enough to assume constant temperature
            // Depth added below profile to take last node to constant temperature zone (m)
            double belowProfileDepth = Math.Max(DepthToConstantTemperature - MathUtilities.Sum(thickness, 1, numLayers), 1000.0);

            double thicknessForPhantomNodes = belowProfileDepth * 2.0 / numPhantomNodes; // double depth so that bottom node at mid-point is at the ConstantTempDepth
            int firstPhantomNode = numLayers;
            for (int i = firstPhantomNode; i < firstPhantomNode + numPhantomNodes; i++)
                thickness[i] = thicknessForPhantomNodes;
            depthAbove = new double[thickness.Length];
            for (int i = 1; i < thickness.Length; i++)
                depthAbove[i] = MathUtilities.Sum(thickness, 1, i);
            var oldDepth = nodeDepth;
            nodeDepth = new double[numNodes + 1 + 1];

            // Set the node depths to approx middle of soil layers
            if (oldDepth != null)
                Array.Copy(oldDepth, nodeDepth, Math.Min(numNodes + 1 + 1, oldDepth.Length));      // Z_zb dimensioned for nodes 0 to Nz + 1 extra for zone below bottom layer
            nodeDepth[airNode] = 0.0;
            nodeDepth[surfaceNode] = 0.0;
            nodeDepth[topsoilNode] = 0.5 * thickness[1] / 1000.0;
            for (int node = topsoilNode; node <= numNodes; node++)
                nodeDepth[node + 1] = (MathUtilities.Sum(thickness, 1, node - 1) + 0.5 * thickness[node]) / 1000.0;

            // BD
            var oldBulkDensity = bulkDensity;
            bulkDensity = new double[numLayers + 1 + numPhantomNodes];
            if (oldBulkDensity != null)
                Array.Copy(oldBulkDensity, bulkDensity, Math.Min(numLayers + 1 + numPhantomNodes, oldBulkDensity.Length));     // Rhob dimensioned for layers 1 to gNumlayers + extra for zone below bottom layer
            physical.BD.CopyTo(bulkDensity, 1);
            bulkDensity[numNodes] = bulkDensity[numLayers];
            for (int layer = numLayers + 1; layer <= numLayers + numPhantomNodes; layer++)
                bulkDensity[layer] = bulkDensity[numLayers];

            // SW
            var oldSoilWater = soilWater;
            soilWater = new double[numLayers + 1 + numPhantomNodes];
            if (oldSoilWater != null)
                Array.Copy(oldSoilWater, soilWater, Math.Min(numLayers + 1 + numPhantomNodes, oldSoilWater.Length));     // SW dimensioned for layers 1 to gNumlayers + extra for zone below bottom layer
            if (waterBalance.SW != null)
            {
                for (int layer = 1; layer <= numLayers; layer++)
                    soilWater[layer] = MathUtilities.Divide(waterBalance.SWmm[layer - 1], thickness[layer], 0);
                for (int layer = numLayers + 1; layer <= numLayers + numPhantomNodes; layer++)
                    soilWater[layer] = soilWater[numLayers];
            }

            // Carbon
            carbon = new double[numLayers + 1 + numPhantomNodes];
            for (int layer = 1; layer <= numLayers; layer++)
                carbon[layer] = organic.Carbon[layer - 1];
            for (int layer = numLayers + 1; layer <= numLayers + numPhantomNodes; layer++)
                carbon[layer] = carbon[numLayers];

            // Rocks
            rocks = new double[numLayers + 1 + numPhantomNodes];
            for (int layer = 1; layer <= numLayers; layer++)
                rocks[layer] = physical.Rocks[layer - 1];
            for (int layer = numLayers + 1; layer <= numLayers + numPhantomNodes; layer++)
                rocks[layer] = rocks[numLayers];

            // Sand
            sand = new double[numLayers + 1 + numPhantomNodes];
            for (int layer = 1; layer <= numLayers; layer++)
                sand[layer] = physical.ParticleSizeSand[layer - 1];
            for (int layer = numLayers + 1; layer <= numLayers + numPhantomNodes; layer++)
                sand[layer] = sand[numLayers];

            // Silt
            silt = new double[numLayers + 1 + numPhantomNodes];
            for (int layer = 1; layer <= numLayers; layer++)
                silt[layer] = physical.ParticleSizeSilt[layer - 1];
            for (int layer = numLayers + 1; layer <= numLayers + numPhantomNodes; layer++)
                silt[layer] = silt[numLayers];

            // Clay
            clay = new double[numLayers + 1 + numPhantomNodes];
            for (int layer = 1; layer <= numLayers; layer++)
                clay[layer] = physical.ParticleSizeClay[layer - 1];
            for (int layer = numLayers + 1; layer <= numLayers + numPhantomNodes; layer++)
                clay[layer] = clay[numLayers];

            maxSoilTemp = new double[numLayers + 1 + numPhantomNodes];
            minSoilTemp = new double[numLayers + 1 + numPhantomNodes];
            aveSoilTemp = new double[numLayers + 1 + numPhantomNodes];
            volSpecHeatSoil = new double[numNodes + 1];
            soilTemp = new double[numNodes + 1 + 1];
            morningSoilTemp = new double[numNodes + 1 + 1];
            newTemperature = new double[numNodes + 1 + 1];
            thermalConductivity = new double[numNodes + 1];
            heatStorage = new double[numNodes + 1];
            thermalConductance = new double[numNodes + 1 + 1];
        }

        /// <summary>Set parameter values and check validity</summary>
        /// <remarks></remarks>
        private void readParam()
        {
            doThermalConductivityCoeffs();

            // set t and tn values to TAve. soil_temp is currently not used
            calcSoilTemperature(ref soilTemp);     // FIXME - returns as zero here because initialisation is not complete.
            soilTemp.CopyTo(newTemperature, 0);
            soilRoughnessHeight = bareSoilRoughness;
        }

        /// <summary>Calculate the coefficients for thermal conductivity equation</summary>
        /// <remarks>This is equation 4.20 (Campbell, 1985) for a typical low-quartz, mineral soil</remarks>
        private void doThermalConductivityCoeffs()
        {
            var oldGC1 = thermCondPar1;
            thermCondPar1 = new double[numNodes + 1];
            if (oldGC1 != null)
                Array.Copy(oldGC1, thermCondPar1, Math.Min(numNodes + 1, oldGC1.Length));     // C1 dimensioned for nodes 0 to Nz
            var oldGC2 = thermCondPar2;
            thermCondPar2 = new double[numNodes + 1];
            if (oldGC2 != null)
                Array.Copy(oldGC2, thermCondPar2, Math.Min(numNodes + 1, oldGC2.Length));     // C2 dimensioned for nodes 0 to Nz
            var oldGC3 = thermCondPar3;
            thermCondPar3 = new double[numNodes + 1];
            if (oldGC3 != null)
                Array.Copy(oldGC3, thermCondPar3, Math.Min(numNodes + 1, oldGC3.Length));     // C3 dimensioned for nodes 0 to Nz
            var oldGC4 = thermCondPar4;
            thermCondPar4 = new double[numNodes + 1];
            if (oldGC4 != null)
                Array.Copy(oldGC4, thermCondPar4, Math.Min(numNodes + 1, oldGC4.Length));     // C4 dimensioned for nodes 0 to Nz

            for (int layer = 1; layer <= numLayers + 1; layer++)
            {
                int element = layer;
                // first coefficient C1 - For many mineral soils, the quartz fraction can be taken as nil, and the equation can
                // be approximated by this equation - 4.27 Campbell.
                thermCondPar1[element] = 0.65 - 0.78 * bulkDensity[layer] + 0.6 * Math.Pow(bulkDensity[layer], 2);      // A approximation to e
                                                                                                                        // The coefficient C2 (B in Campbell 4.25) can be evaluated from data and for mineral soils is approximated by -
                thermCondPar2[element] = 1.06 * bulkDensity[layer];                              // * SW[i]; //B for mineral soil - assume (is there missing text here??)
                                                                                                 // The coefficient C3 (C in Caqmpbell 4.28) determines the water content where thermal conductivity begins to
                                                                                                 // increase rapidly and is highly correlated with clay content. The following correlation appears to fit data well.
                thermCondPar3[element] = 1.0D + MathUtilities.Divide(2.6, Math.Sqrt(clay[layer]), 0);             // C is the water content where co (is there missing text here??)
                                                                                                                  // Coefficient C4 (D in Campbell 4.22) is the thermal conductivity when volumetric water content=0.
                                                                                                                  // For mineral soils with a particle density of 2.65 Mg/m3 the equation becomes the following.
                thermCondPar4[element] = 0.03 + 0.1 * Math.Pow(bulkDensity[layer], 2);           // D assume mineral soil particle d (is there missing text here??)
            }
        }

        /// <summary>Update global variables with external states and check validity of values</summary>
        /// <remarks></remarks>
        private void getOtherVariables()
        {
            waterBalance.SW.CopyTo(soilWater, 1);
            soilWater[numNodes] = soilWater[numLayers];
            if (microClimate != null)
            {
                canopyHeight = Math.Max(microClimate.CanopyHeight, soilRoughnessHeight) / 1000.0;
            }
            else
            {
                canopyHeight = 0.0;
            }

            // Vals HACK. Should be recalculating wind profile.
            instrumentHeight = Math.Max(instrumentHeight, canopyHeight + 0.5);
        }

        /// <summary>Perform actions for current day</summary>
        private void doProcess()
        {
            const int interactionsPerDay = 48;     // number of iterations in a day

            double cva = 0.0;
            double cloudFr = 0.0;
            double[] solarRadn = new double[49];   // Total incoming short wave solar radiation per time-step
            doNetRadiation(ref solarRadn, ref cloudFr, ref cva, interactionsPerDay);

            // zero the temperature profiles
            MathUtilities.Zero(minSoilTemp);
            MathUtilities.Zero(maxSoilTemp);
            MathUtilities.Zero(aveSoilTemp);
            boundaryLayerConductance = 0.0;

            // calc dt
            internalTimeStep = Math.Round(timestep / interactionsPerDay);

            // These two call used to be inside the time-step loop. I've taken them outside,
            // as the results do not appear to vary over the course of the day.
            // The results would vary if soil water content were to vary, so if future versions
            // to more communication within sub-daily time steps, these may need to be moved
            // back into the loop. EZJ March 2014
            doVolumetricSpecificHeat();      // RETURNS volSpecHeatSoil() (volumetric heat capacity of nodes)
            doThermalConductivity();     // RETURNS gThermConductivity_zb()

            for (int timeStepIteration = 1; timeStepIteration <= interactionsPerDay; timeStepIteration++)
            {
                timeOfDaySecs = internalTimeStep * System.Convert.ToDouble(timeStepIteration);
                if (timestep < 24.0 * 60.0 * 60.0)  // CHECK, this seem wrong, shouldn't be '>'?
                    airTemperature = weather.MeanT;
                else
                    airTemperature = interpolateTemperature(timeOfDaySecs / 3600.0);
                // Convert to hours //most of the arguments in FORTRAN version are global vars so
                // do not need to pass them here, they can be accessed inside InterpTemp
                newTemperature[airNode] = airTemperature;

                netRadiation = interpolateNetRadiation(solarRadn[timeStepIteration], cloudFr, cva);

                switch (boundarLayerConductanceSource)
                {
                    case "constant":
                        {
                            thermalConductivity[airNode] = constantBoundaryLayerConductance;
                            break;
                        }

                    case "calc":
                        {
                            // When calculating the boundary layer conductance it is important to iterate the entire
                            // heat flow calculation at least once, since surface temperature depends on heat flux to
                            // the atmosphere, but heat flux to the atmosphere is determined, in part, by the surface
                            // temperature.
                            thermalConductivity[airNode] = getBoundaryLayerConductance(ref newTemperature);
                            for (int iteration = 1; iteration <= numIterationsForBoundaryLayerConductance; iteration++)
                            {
                                doThomas(ref newTemperature);        // RETURNS TNew_zb()
                                thermalConductivity[airNode] = getBoundaryLayerConductance(ref newTemperature);
                            }

                            break;
                        }
                }
                // Now start again with final atmosphere boundary layer conductance
                doThomas(ref newTemperature);
                doUpdate(interactionsPerDay);
                if (Math.Abs(timeOfDaySecs - 5.0 * 3600.0) <= Math.Min(timeOfDaySecs, 5.0 * 3600.0) * 0.0001)
                {
                    soilTemp.CopyTo(morningSoilTemp, 0);
                }
            }

            minTempYesterday = weather.MinT;
            maxTempYesterday = weather.MaxT;
        }

        /// <summary>Calculate the volumetric specific heat capacity (Cv, J/K/m3) of each soil layer</summary>
        /// <remarks>
        /// Modified from Campbell, G.S. (1985). Soil physics with BASIC: Transport models for soil-plant systems
        /// </remarks>
        private void doVolumetricSpecificHeat()
        {
            double[] volSpecHeatSoil = new double[numNodes + 1];

            for (int node = 1; node <= numNodes; node++)
            {
                volSpecHeatSoil[node] = 0;
                foreach (var constituentName in soilConstituentNames.Except(new string[] { "Minerals" }))
                {
                    volSpecHeatSoil[node] += volumetricSpecificHeat(constituentName, node) * 1000000.0 * soilWater[node];
                }
            }
            // now get weighted average for soil elements between the nodes. i.e. map layers to nodes
            mapLayer2Node(volSpecHeatSoil, ref this.volSpecHeatSoil);
        }

        /// <summary>Calculate the thermal conductivity of each soil layer (K.m/W)</summary>
        private void doThermalConductivity()
        {
            double[] thermCondLayers = new double[numNodes + 1];

            for (int node = 1; node <= numNodes; node++)
            {
                double numerator = 0.0;
                double denominator = 0.0;
                foreach (var constituentName in soilConstituentNames)
                {
                    double shapeFactorConstituent = shapeFactor(constituentName, node);
                    double thermalConductanceConstituent = ThermalConductance(constituentName, node);
                    double thermalConductanceWater = ThermalConductance("Water", node);
                    double k = (2.0 / 3.0) * Math.Pow(1 + shapeFactorConstituent * (thermalConductanceConstituent / thermalConductanceWater - 1.0), -1) +
                               (1.0 / 3.0) * Math.Pow(1 + shapeFactorConstituent * (thermalConductanceConstituent / thermalConductanceWater - 1.0) * (1 - 2 * shapeFactorConstituent), -1);
                    numerator += thermalConductanceConstituent * soilWater[node] * k;
                    denominator += soilWater[node] * k;
                }

                thermCondLayers[node] = numerator / denominator;
            }

            // now get weighted average for soil elements between the nodes. i.e. map layers to nodes
            mapLayer2Node(thermCondLayers, ref thermalConductivity);
        }

        private void mapLayer2Node(double[] layerArray, ref double[] nodeArray)
        {
            // now get weighted average for soil elements between the nodes. i.e. map layers to nodes
            for (int node = surfaceNode; node <= numNodes; node++)
            {
                int layer = node - 1;     // node n lies at the centre of layer n-1
                double d1 = depthAbove[layer] - (nodeDepth[node] * 1000.0);
                double d2 = nodeDepth[node + 1] * 1000.0 - depthAbove[layer];
                double dSum = d1 + d2;

                nodeArray[node] = MathUtilities.Divide(layerArray[layer] * d1, dSum, 0)
                                + MathUtilities.Divide(layerArray[layer + 1] * d2, dSum, 0);
            }
        }

        /// <summary>Numerical solution of the differential equations based on Thomas algorithm</summary>
        /// <remarks>
        /// Solves the tri_diagonal matrix using the Thomas algorithm
        /// Thomas, L.H. (1946). Elliptic problems in linear difference equations over a network
        /// John Hargreaves' version from Campbell Program 4.1
        /// </remarks>
        private void doThomas(ref double[] newTemps)
        {
            double[] a = new double[numNodes + 1 + 1];    // A; thermal conductance at next node (W/m/K)
            double[] b = new double[numNodes + 1];        // B; heat storage at node (W/K)
            double[] c = new double[numNodes + 1];        // C; thermal conductance at node (W/m/K)
            double[] d = new double[numNodes + 1];        // D; heat flux at node (w/m) and then temperature
                                                          // nu = F; Nz = M; 1-nu = G; T_zb = T; newTemps = TN;

            thermalConductance[airNode] = thermalConductivity[airNode];
            // The first node gZ_zb(1) is at the soil surface (Z = 0)
            for (int node = surfaceNode; node <= numNodes; node++)
            {
                // volume of soil around node (m3/m2)
                double volumeOfSoilAtNode = 0.5 * (nodeDepth[node + 1] - nodeDepth[node - 1]);   // Volume of soil around node (m^3), assuming area is 1 m^2
                heatStorage[node] = MathUtilities.Divide(volSpecHeatSoil[node] * volumeOfSoilAtNode, internalTimeStep, 0);       // Joules/s/K or W/K
                                                                                                                                 // rate of heat
                                                                                                                                 // convert to thermal conductance
                double elementLength = nodeDepth[node + 1] - nodeDepth[node];             // (m)
                thermalConductance[node] = MathUtilities.Divide(thermalConductivity[node], elementLength, 0);  // (W/m/K)
            }

            // John's version
            double g = 1 - nu;
            for (int node = surfaceNode; node <= numNodes; node++)
            {
                c[node] = (-nu) * thermalConductance[node];
                a[node + 1] = c[node];             // Eqn 4.13
                b[node] = nu * (thermalConductance[node] + thermalConductance[node - 1]) + heatStorage[node];    // Eqn 4.12
                                                                                                                 // Eqn 4.14
                d[node] = g * thermalConductance[node - 1] * soilTemp[node - 1]
                        + (heatStorage[node] - g * (thermalConductance[node] + thermalConductance[node - 1])) * soilTemp[node]
                        + g * thermalConductance[node] * soilTemp[node + 1];
            }
            a[surfaceNode] = 0.0;

            // The boundary condition at the soil surface is more complex since convection and radiation may be important.
            // When radiative and latent heat transfer are unimportant, then D(1) = D(1) + nu*K(0)*TN(0).
            // d(SURFACEnode) += nu * thermalConductance(AIRnode) * newTemps(AIRnode)       ' Eqn. 4.16
            double sensibleHeatFlux = nu * thermalConductance[airNode] * newTemps[airNode];       // Eqn. 4.16

            // When significant radiative and/or latent heat transfer occur they are added as heat sources at node 1
            // to give D(1) = D(1) = + nu*K(0)*TN(0) - Rn + LE, where Rn is net radiation at soil surface and LE is the
            // latent heat flux. Eqn. 4.17

            // get net radiation flux, Rn (J/m2/s or W/m2)
            double radnNet = 0.0;
            switch (netRadiationSource)
            {
                case "calc":
                    {
                        // interpolated from actual net radiation
                        radnNet = MathUtilities.Divide(netRadiation * 1000000.0, internalTimeStep, 0);
                        break;
                    }

                case "eos":
                    {
                        // estimated using Rn = Eos*L
                        radnNet = MathUtilities.Divide(waterBalance.Eos * latentHeatOfVapourisation, timestep, 0);
                        break;
                    }
            }

            // get latent heat flux, LE (W/m)
            double latentHeatFlux = MathUtilities.Divide(waterBalance.Es * latentHeatOfVapourisation, timestep, 0);

            // get the surface heat flux, from Rn = G + H + LE (W/m)
            double soilSurfaceHeatFlux = sensibleHeatFlux + radnNet - latentHeatFlux;
            d[surfaceNode] += soilSurfaceHeatFlux;        // FIXME JNGH testing alternative net radn

            // last line is unfulfilled soil water evaporation
            // The boundary condition at the bottom of the soil column is usually specified as remaining at some constant,
            // measured temperature, TN(M+1). The last value for D is therefore -

            d[numNodes] += nu * thermalConductance[numNodes] * newTemps[numNodes + 1];
            // For a no-flux condition, K(M) = 0, so nothing is added.

            // The Thomas algorithm
            // Calculate coeffs A, B, C, D for intermediate nodes
            for (int node = surfaceNode; node <= numNodes - 1; node++)
            {
                c[node] = MathUtilities.Divide(c[node], b[node], 0);
                d[node] = MathUtilities.Divide(d[node], b[node], 0);
                b[node + 1] -= a[node + 1] * c[node];
                d[node + 1] -= a[node + 1] * d[node];
            }
            newTemps[numNodes] = MathUtilities.Divide(d[numNodes], b[numNodes], 0);  // do temperature at bottom node

            // Do temperatures at intermediate nodes from second bottom to top in soil profile
            for (int node = numNodes - 1; node >= surfaceNode; node += -1)
            {
                newTemps[node] = d[node] - c[node] * newTemps[node + 1];
                if (MathUtilities.IsGreaterThan(newTemps[node], 100) || MathUtilities.IsLessThan(newTemps[node], -50))
                    throw new Exception($"newTemps({node}) is outside range of -50.0 to 100.0");
            }
        }

        /// <summary>Interpolate air temperature</summary>
        /// <param name="timeHours">time of day at which air temperature is required</param>
        /// <returns>Interpolated air temperature for specified time of day (oC)</returns>
        /// <remarks>
        ///  Between midnight and MinT_time just a linear interpolation between
        ///  yesterday's midnight temperature and today's MinTg.
        ///  For the rest of the day use a sin function.
        /// Note: This can result in the Midnight temperature being lower than the following minimum.
        /// </remarks>
        private double interpolateTemperature(double timeHours)
        {
            double time = timeHours / 24.0;           // Current time of day as a fraction of a day
            double maxT_time = defaultTimeOfMaximumTemperature / 24.0;     // Time of maximum temperature as a fraction of a day
            double minT_time = maxT_time - 0.5;       // Time of minimum temperature as a fraction of a day

            if (time < minT_time)
            {
                // Current time of day is between midnight and time of minimum temperature
                double midnightT = Math.Sin((0.0 + 0.25 - maxT_time) * 2.0 * Math.PI)
                                        * (maxTempYesterday - minTempYesterday) / 2.0
                                        + (maxTempYesterday + minTempYesterday) / 2.0;
                double tScale = (minT_time - time) / minT_time;

                // set bounds for t_scale (0 <= tScale <= 1)
                if (tScale > 1.0)
                    tScale = 1.0;
                else if (tScale < 0)
                    tScale = 0;

                double currentTemperature = weather.MinT + tScale * (midnightT - weather.MinT);
                return currentTemperature;
            }
            else
            {
                // Current time of day is at time of minimum temperature or after it up to midnight.
                double currentTemperature = Math.Sin((time + 0.25 - maxT_time) * 2.0 * Math.PI)
                                          * (weather.MaxT - weather.MinT) / 2.0
                                          + weather.MeanT;
                return currentTemperature;
            }
        }

        /// <summary>Determine min, max, and average soil temperature from the half-hourly iterations</summary>
        /// <param name="numInterationsPerDay">number of times in a day the function is called</param>
        /// <remarks></remarks>
        private void doUpdate(int numInterationsPerDay)
        {
            // Now transfer to old temperature array
            newTemperature.CopyTo(soilTemp, 0);

            // initialise the min & max to soil temperature if this is the first iteration
            if (timeOfDaySecs < internalTimeStep * 1.2)
            {
                for (int node = surfaceNode; node <= numNodes; node++)
                {
                    minSoilTemp[node] = soilTemp[node];
                    maxSoilTemp[node] = soilTemp[node];
                }
            }

            for (int node = surfaceNode; node <= numNodes; node++)
            {
                if (soilTemp[node] < minSoilTemp[node])
                    minSoilTemp[node] = soilTemp[node];
                else if (soilTemp[node] > maxSoilTemp[node])
                    maxSoilTemp[node] = soilTemp[node];
                aveSoilTemp[node] += MathUtilities.Divide(soilTemp[node], numInterationsPerDay, 0);
            }
            boundaryLayerConductance += MathUtilities.Divide(thermalConductivity[airNode], numInterationsPerDay, 0);
        }

        /// <summary>Calculate atmospheric boundary layer conductance</summary>
        /// <returns>thermal conductivity of surface layer (K/W)</returns>
        /// <remarks>
        /// From Program 12.2, p140, Campbell, Soil Physics with Basic.
        /// During first stage drying, evaporation prevents the surface from becoming hot,
        /// so stability corrections are small. Once the surface dries and becomes hot, boundary layer
        /// resistance is relatively unimportant in determining evaporation rate.
        /// A dry soil surface reaches temperatures well above air temperatures during the day, and can be well
        /// below air temperature on a clear night. Thermal stratification on a clear night can be strong enough
        /// to reduce sensible heat exchange between the soil surface and the air to almost nothing. If stability
        /// corrections are not made, soil temperature profiles can have large errors.
        /// </remarks>
        private double getBoundaryLayerConductance(ref double[] TNew_zb)
        {
            const double vonKarmanConstant = 0.41;                 // VK; von Karman's constant
            const double gravitationalConstant = 9.8;    // GR; gravitational constant (m/s/s)
            const double specificHeatOfAir = 1010.0;               // (J/kg/K) Specific heat of air at constant pressure
            const double surfaceEmissivity = 0.98;
            double SpecificHeatAir = specificHeatOfAir * airDensity(airTemperature, weather.AirPressure); // CH; volumetric specific heat of air (J/m3/K) (1200 at 200C at sea level)
                                                                                                          // canopy_height, instrum_ht (Z) = 1.2m, AirPressure = 1010
                                                                                                          // gTNew_zb = TN; gAirT = TA;


            // VOS 11Nov24 note that these calculations are been effectively disabled just before the return
            //               this is a temporary fix for the instability that was resulting
            //               once there is an energy balance through residue adn evaporation it should be
            //               reinstated and tested more





            // Zero plane displacement and roughness parameters depend on the height, density and shape of
            // surface roughness elements. For typical crop surfaces, the following empirical correlations have
            // been obtained. (Extract from Campbell p138.). Canopy height is the height of the roughness elements.
            double roughnessFactorMomentum = 0.13 * canopyHeight;    // ZM; surface roughness factor for momentum
            double roughnessFactorHeat = 0.2 * roughnessFactorMomentum;  // ZH; surface roughness factor for heat
            double d = 0.77 * canopyHeight;                       // D; zero plane displacement for the surface

            double surfaceTemperature = TNew_zb[surfaceNode];    // surface temperature (oC)

            // To calculate the radiative conductance term of the boundary layer conductance, we need to account for canopy and residue cover
            // Calculate a diffuse penetration constant (KL Bristow, 1988. Aust. J. Soil Res, 26, 269-80. The Role of Mulch and its Architecture
            // in modifying soil temperature). Here we estimate this using the Soilwat algorithm for calculating EOS from EO and the cover effects,
            // assuming the cover effects on EO are similar to Bristow's diffuse penetration constant - 0.26 for horizontal mulch treatment and 0.44
            // for vertical mulch treatment.
            double diffusePenetrationConstant = Math.Max(0.1, waterBalance.Eos) / Math.Max(0.1, waterBalance.Eo);

            // Campbell, p136, indicates the radiative conductance is added to the boundary layer conductance to form a combined conductance for
            // heat transfer in the atmospheric boundary layer. Eqn 12.9 modified for residue and plant canopy cover
            double radiativeConductance = 4.0 * stefanBoltzmannConstant * surfaceEmissivity * diffusePenetrationConstant
                                               * Math.Pow(kelvinT(airTemperature), 3);    // Campbell uses air temperature in leiu of surface temperature

            // Zero iteration variables
            double frictionVelocity = 0.0;        // FV; UStar
            double boundaryLayerCond = 0.0;       // KH; sensible heat flux in the boundary layer;(OUTPUT) thermal conductivity  (W/m2/K)
            double stabilityParammeter = 0.0;          // SP; Index of the relative importance of thermal and mechanical turbulence in boundary layer transport.
            double stabilityCorrectionMomentum = 0.0;    // PM; stability correction for momentum
            double stabilityCorrectionHeat = 0.0;        // PH; stability correction for heat
            double heatFluxDensity = 0.0;         // H; sensible heat flux in the boundary layer

            // Since the boundary layer conductance is a function of the heat flux density, an iterative method must be used to find the boundary layer conductance.
            for (int iteration = 1; iteration <= 3; iteration++)
            {
                // Heat and water vapour are transported by eddies in the turbulent atmosphere above the crop.
                // Boundary layer conductance would therefore be expected to vary depending on the wind speed and level
                // of turbulence above the crop. The level of turbulence, in turn, is determined by the roughness of the surface,
                // the distance from the surface and the thermal stratification of the boundary layer.
                // Eqn 12.11 Campbell
                frictionVelocity = MathUtilities.Divide(weather.Wind * vonKarmanConstant,
                                                        Math.Log(MathUtilities.Divide(instrumentHeight - d + roughnessFactorMomentum,
                                                                                      roughnessFactorMomentum,
                                                                                      0)) + stabilityCorrectionMomentum,
                                                        0);
                // Eqn 12.10 Campbell
                boundaryLayerCond = MathUtilities.Divide(SpecificHeatAir * vonKarmanConstant * frictionVelocity,
                                                         Math.Log(MathUtilities.Divide(instrumentHeight - d + roughnessFactorHeat,
                                                                                       roughnessFactorHeat, 0)) + stabilityCorrectionHeat,
                                                         0);

                boundaryLayerCond += radiativeConductance; // * (1.0 - sunAngleAdjust())

                heatFluxDensity = boundaryLayerCond * (surfaceTemperature - airTemperature);
                // Eqn 12.14
                stabilityParammeter = MathUtilities.Divide(-vonKarmanConstant * instrumentHeight * gravitationalConstant * heatFluxDensity,
                                                      SpecificHeatAir * kelvinT(airTemperature) * Math.Pow(frictionVelocity, 3.0)
                                                      , 0);

                // The stability correction parameters correct the boundary layer conductance for the effects
                // of buoyancy in the atmosphere. When the air near the surface is hotter than the air above,
                // the atmosphere becomes unstable, and mixing at a given wind speed is greater than would occur
                // in a neutral atmosphere. If the air near the surface is colder than the air above, the atmosphere
                // is unstable and mixing is suppressed.

                if (stabilityParammeter > 0.0)
                {
                    // Stable conditions, when surface temperature is lower than air temperature, the sensible heat flux
                    // in the boundary layer is negative and stability parameter is positive.
                    // Eqn 12.15
                    stabilityCorrectionHeat = 4.7 * stabilityParammeter;
                    stabilityCorrectionMomentum = stabilityCorrectionHeat;
                }
                else
                {
                    // Unstable conditions, when surface temperature is higher than air temperature, sensible heat flux in the
                    // boundary layer is positive and stability parameter is negative.
                    stabilityCorrectionHeat = -2.0 * Math.Log((1.0 + Math.Sqrt(1.0 - 16.0 * stabilityParammeter)) / 2.0);    // Eqn 12.16
                    stabilityCorrectionMomentum = 0.6 * stabilityCorrectionHeat;                // Eqn 12.17
                }
            }


            // VOS 11Nov24 a temporary fix until there is a full connection of the energy balance.
            boundaryLayerCond = 20;


            return boundaryLayerCond;   // thermal conductivity  (W/m2/K)
        }

        /// <summary>Convert degrees Celcius to Kelvin</summary>
        /// <param name="celciusT">Temperature (oC)</param>
        /// <returns>Temperature (K)</returns>
        private double kelvinT(double celciusT)
        {
            const double celciusToKelvin = 273.18;
            return celciusT + celciusToKelvin;
        }

        /// <summary>Computes the long-wave radiation emitted by a body</summary>
        /// <param name="emissivity">The emissivity of a body</param>
        /// <param name="tDegC">The temperature of the body</param>
        /// <returns>The radiation emitted (W/m2)</returns>
        private double longWaveRadn(double emissivity, double tDegC)
        {
            return stefanBoltzmannConstant * emissivity * Math.Pow(kelvinT(tDegC), 4);
        }

        /// <summary>Calculates average soil temperature at the centre of each layer</summary>
        /// <param name="soilTempIO">temperature of each layer in profile</param>
        private void calcSoilTemperature(ref double[] soilTempIO) // CHECK, is byRef a good idea?
        {
            double[] cumulativeDepth = SoilUtilities.ToCumThickness(thickness);
            double w = 2 * Math.PI / (365.25 * 24 * 3600);
            double dh = 0.6;   // this needs to be in mm a default value for a loam at field capacity - consider makeing this settable
            double zd = Math.Sqrt(2 * dh / w);
            double offset = 0.25;  // moves the "0" and rise in the sin to the spring equinox for southern latitudes
            if (weather.Latitude > 0.0)  // to cope with the northern summer
                offset = -0.25;

            double[] soilTemp = new double[numNodes + 1 + 1];
            for (int nodes = 1; nodes <= numNodes; nodes++)
            {
                soilTemp[nodes] = weather.Tav + weather.Amp * Math.Exp(-1 * cumulativeDepth[nodes] / zd) *
                                                              Math.Sin((clock.Today.DayOfYear / 365.0 + offset) * 2.0 * Math.PI - cumulativeDepth[nodes] / zd);
            }

            Array.ConstrainedCopy(soilTemp, 0, soilTempIO, surfaceNode, numNodes);
        }

        /// <summary>Gets the average soil temperature for each soil layer</summary>
        /// <param name="depthLag">The lag factor for depth (radians)</param>
        /// <param name="alx">The time of a g_year from hottest instance (radians)</param>
        /// <param name="deltaTemp">The change in surface soil temperature since the hottest day (oC)</param>
        /// <returns>The temperature of each soil layer (oC)</returns>
        /// <remarks>
        /// The difference in temperature between surface and subsurface layers is an exponential function
        /// of the ratio of the depth at the bottom of the layer and the temperature damping depth of the soil
        /// </remarks>
        private double calcLayerTemperature(double depthLag, double alx, double deltaTemp)
        {
            return weather.Tav + (weather.Amp / 2.0 * Math.Cos(alx - depthLag) + deltaTemp) * Math.Exp(-depthLag);
        }

        /// <summary>Calculate initial soil surface temperature</summary>
        /// <returns>The initial soil surface temperature (oC)</returns>
        private double calcSurfaceTemperature()
        {
            double surfaceT = (1.0 - waterBalance.Salb) * (weather.MeanT + (weather.MaxT - weather.MeanT) * Math.Sqrt(Math.Max(weather.Radn, 0.1) * 23.8846 / 800.0)) + waterBalance.Salb * weather.MeanT;
            boundCheck(surfaceT, -100.0, 100.0, "Initial surfaceT");
            return surfaceT;
        }

        /// <summary>Calculate initial variables for net radiation per time-step</summary>
        /// <param name="solarRadn"></param>
        /// <param name="cloudFr"></param>
        /// <param name="cva"></param>
        /// <param name="ITERATIONSperDAY"></param>
        /// <remarks></remarks>
        private void doNetRadiation(ref double[] solarRadn, ref double cloudFr, ref double cva, int ITERATIONSperDAY)
        {
            double TSTEPS2RAD = MathUtilities.Divide(2.0 * Math.PI, Convert.ToDouble(ITERATIONSperDAY), 0);          // convert timestep of day to radians
            const double solarConstant = 1360.0;     // W/M^2
            double solarDeclination = 0.3985 * Math.Sin(4.869 + (clock.Today.DayOfYear * 2.0 * Math.PI / 365.25) + 0.03345 * Math.Sin(6.224 + (clock.Today.DayOfYear * 2.0 * Math.PI / 365.25)));
            double cD = Math.Sqrt(1.0 - solarDeclination * solarDeclination);
            double[] m1 = new double[ITERATIONSperDAY + 1];
            double m1Tot = 0.0;
            for (int timestepNumber = 1; timestepNumber <= ITERATIONSperDAY; timestepNumber++)
            {
                m1[timestepNumber] = (solarDeclination * Math.Sin(weather.Latitude * Math.PI / 180.0) + cD * Math.Cos(weather.Latitude * Math.PI / 180.0) * Math.Cos(TSTEPS2RAD * (timestepNumber - ITERATIONSperDAY / 2.0))) * 24.0 / ITERATIONSperDAY;
                if (m1[timestepNumber] > 0.0)
                    m1Tot += m1[timestepNumber];
                else
                    m1[timestepNumber] = 0.0;
            }

            double psr = m1Tot * solarConstant * 3600.0 / 1000000.0;   // potential solar radiation for the day (MJ/m^2)
            double fr = MathUtilities.Divide(Math.Max(weather.Radn, 0.1), psr, 0);               // ratio of potential to measured daily solar radiation (0-1)
            cloudFr = 2.33 - 3.33 * fr;    // fractional cloud cover (0-1)
            cloudFr = Math.Min(Math.Max(cloudFr, 0.0), 1.0);

            for (int timestepNumber = 1; timestepNumber <= ITERATIONSperDAY; timestepNumber++)
                solarRadn[timestepNumber] = Math.Max(weather.Radn, 0.1) *
                                            MathUtilities.Divide(m1[timestepNumber], m1Tot, 0);

            // cva is vapour concentration of the air (g/m^3)
            cva = Math.Exp(31.3716 - 6014.79 / kelvinT(weather.MinT) - 0.00792495 * kelvinT(weather.MinT)) / kelvinT(weather.MinT);
        }

        /// <summary>Calculate the net radiation at the soil surface</summary>
        /// <param name="solarRadn"></param>
        /// <param name="cloudFr"></param>
        /// <param name="cva"></param>
        /// <returns>Net radiation (SW and LW) for timestep (MJ)</returns>
        /// <remarks></remarks>
        private double interpolateNetRadiation(double solarRadn, double cloudFr, double cva)
        {
            const double surfaceEmissivity = 0.96;    // Campbell Eqn. 12.1
            double w2MJ = internalTimeStep / 1000000.0;      // convert W to MJ

            // Eqns 12.2 & 12.3
            double emissivityAtmos = (1 - 0.84 * cloudFr) * 0.58 * Math.Pow(cva, (1.0 / 7.0)) + 0.84 * cloudFr;
            // To calculate the longwave radiation out, we need to account for canopy and residue cover
            // Calculate a penetration constant. Here we estimate this using the Soilwat algorithm for calculating EOS from EO and the cover effects.
            double PenetrationConstant = MathUtilities.Divide(Math.Max(0.1, waterBalance.Eos),
                                                              Math.Max(0.1, waterBalance.Eo), 0);

            // Eqn 12.1 modified by cover.
            double lwRinSoil = longWaveRadn(emissivityAtmos, airTemperature) * PenetrationConstant * w2MJ;

            double lwRoutSoil = longWaveRadn(surfaceEmissivity, soilTemp[surfaceNode]) * PenetrationConstant * w2MJ; // _
                                                                                                                     // + longWaveRadn(emissivityAtmos, (gT_zb(SURFACEnode) + gAirT) * 0.5) * (1.0 - PenetrationConstant) * w2MJ

            // Ignore (mulch/canopy) temperature and heat balance
            double lwRnetSoil = lwRinSoil - lwRoutSoil;

            double swRin = solarRadn;
            double swRout = waterBalance.Salb * solarRadn;
            // Dim swRout As Double = (salb + (1.0 - salb) * (1.0 - sunAngleAdjust())) * solarRadn   'FIXME temp test
            double swRnetSoil = (swRin - swRout) * PenetrationConstant;
            return swRnetSoil + lwRnetSoil;
        }

        /// <summary>Checks whether a variable within lower and upper bounds</summary>
        /// <param name="VariableValue">value to be validated</param>
        /// <param name="Lower">lower limit of value</param>
        /// <param name="Upper">upper limit of value</param>
        /// <param name="VariableName">variable name to be validated</param>
        private void boundCheck(double VariableValue, double Lower, double Upper, string VariableName)
        {
            const double precisionMargin = 0.00001;          // margin for precision err of lower
            double lowerBound = Lower - precisionMargin;       // calculate a margin for precision err of lower.
            double upperBound = Upper + precisionMargin;   // calculate a margin for precision err of upper.

            if ((VariableValue > upperBound | VariableValue < lowerBound))
            {
                if ((lowerBound > upperBound))
                    throw new Exception("Lower bound (" + Lower.ToString() + ") exceeds upper bound (" + Upper.ToString() + ") in bounds checking: Variable is not checked");
                else
                    throw new Exception(VariableName + " = " + VariableValue.ToString() + " is outside range of " + Lower.ToString() + " to " + Upper.ToString());
            }
            else
            {
            }

            return;
        }
    }
}
