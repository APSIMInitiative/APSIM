﻿

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Models.Core;
using Models;
using System.Xml.Serialization;
using Models.PMF;
using System.Runtime.Serialization;
using Models.SurfaceOM;
using Models.Soils;
using Models.Soils.SoilWaterBackend;
using Models.Interfaces;
using APSIM.Shared.Utilities;
using Models.PMF.Functions;

namespace Models.Soils
{

    /// <summary>
    ///
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.ProfileView")]
    [PresenterName("UserInterface.Presenters.ProfilePresenter")]
    [ValidParent(ParentType = typeof(Soil))]
    public class MultiPoreWater : Model, ISoilWater
    {
        #region IsoilInterface
        /// <summary>The amount of rainfall intercepted by surface residues</summary>
        [XmlIgnore]
        public double residueinterception { get { return ResidueWater; } set { } }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double catchment_area { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double CN2Bare { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double CNCov { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double CNRed { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double DiffusConst { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double DiffusSlope { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double discharge_width { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] dlayer { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] dlt_sw { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] dlt_sw_dep { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double Drainage { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] DULmm { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double Eo { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double Eos { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double Es { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double ESW { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] flow { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] flow_nh4 { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] flow_no3 { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] flow_urea { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] flux { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double gravity_gradient { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double Infiltration { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] KLAT { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double LeachNH4 { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double LeachNO3 { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double LeachUrea { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] LL15mm { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double max_pond { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] outflow_lat { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double pond { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double pond_evap { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double Runoff { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double Salb { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] SATmm { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double slope { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] solute_flow_eff { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] solute_flux_eff { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double specific_bd { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double SummerCona { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public string SummerDate { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double SummerU { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] SW { get; set; }
        ///<summary> Who knows</summary>

        public double[] SWCON { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double[] SWmm { get; set; }
        ///<summary> Who knows</summary>
        public double[] Thickness { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double WaterTable { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double WinterCona { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public string WinterDate { get; set; }
        ///<summary> Who knows</summary>
        [XmlIgnore]
        public double WinterU { get; set; }
        ///<summary> Who knows</summary>
        public void SetSWmm(int Layer, double NewSWmm) { }
        ///<summary> Who knows</summary>
        public void SetWater_frac(double[] New_SW) { }
        ///<summary> Who knows</summary>
        public void Reset() { }
        ///<summary> Who knows</summary>
        public void SetWaterTable(double InitialDepth) { }
        ///<summary> Who knows</summary>
        public void Tillage(TillageType Data) { }
        ///<summary> Who knows</summary>
        public void Tillage(string DefaultTillageName) { }
        #endregion

        #region Class Dependancy Links
        [Link]
        private Water Water = null;
        [Link]
        private Soil Soil = null;
        [Link]
        private SurfaceOrganicMatter SurfaceOM = null;
        [Link]
        private Weather Met = null;
        [Link]
        private HydraulicProperties HyProps = null;
        #endregion

        #region Class Events
        /// <summary>Occurs when a plant is about to be sown.</summary>
        public event EventHandler ReportDetails;
        #endregion

        #region Structures
        /// <summary>
        /// This is the data structure that represents the soils layers and pore cagatories in each layer
        /// </summary>
        public Pore[][] Pores;
        /// <summary>
        /// Contains data extrapolated out to hourly values
        /// </summary>
        public HourlyData Hourly;
        /// <summary>
        /// Contains parameters specific to each layer in the soil
        /// </summary>
        public ProfileParameters ProfileParams = null;
        #endregion

        #region Parameters
        /// <summary>
        /// The maximum diameter of pore compartments
        /// </summary>
        [Units("nm")]
        [Description("The pore diameters that seperate modeled pore compartments")]
        public double[] PoreBounds { get; set; }
        /// <summary>
        /// The hydraulic conductance below the bottom of the specified profile
        /// </summary>
        [Units("mm/h")]
        [Description("The amount of water that will pass the bottom of the profile")]
        public double SubProfileConductance { get; set; }
        /// <summary>
        /// The depth of the water table below the surface, important for gravitational water potential
        /// </summary>
        [Units("m")]
        [Description("The depth of the water table below the surface")]
        public double WaterTableDepth { get; set; }
        /// <summary>
        /// Allow infiltration processes to be switched off from the UI
        /// </summary>
        [Description("Calculate infiltration processes?.  Normally yes, this is for testing")]
        public bool CalculateInfiltration { get; set; }
        /// <summary>
        /// Allow drainage processes to be switched off from the UI
        /// </summary>
        [Description("Calculate draiange processes.  Normally yes, this is for testing")]
        public bool CalculateDrainage { get; set; }
        /// <summary>
        /// Allow output of soil water content of all pores at each time step
        /// </summary>
        [Description("Report SW at all timesteps.  lots of data")]
        public bool ReportDetail { get; set; }
        /// <summary>
        /// The number of time steps to run calculations for each day
        /// </summary>
        [Description("Number of time steps each day.  Not implemented yet")]
        public int TimeSteps { get; set; }
        #endregion

        #region Outputs
        /// <summary>
        /// The amount of water stored in the surface residue
        /// </summary>
        public double ResidueWater { get; set; }
        /// <summary>
        /// Data object to put the water content of each pore into
        /// </summary>
        public double[][] PoreWater { get; set; }
        /// <summary>
        /// Describes the process just completed
        /// </summary>
        public string Process { get; set; }
        /// <summary>
        /// the current hour in the process
        /// </summary>
        public int Hour { get; set; }
        /// <summary>
        /// The layer that is current encountering water flux
        /// </summary>
        public int ReportLayer { get; set; }
        /// <summary>
        /// Number of times water deltas have occured
        /// </summary>
        public int TimeStep { get; set; }
        /// <summary>
        /// Change in pond depth for the day
        /// </summary>
        public double DeltaPond { get { return SODPondDepth - EODPondDepth; } }
        /// <summary>
        /// Hydraulic concutivitiy into each pore
        /// </summary>
        [Units("mm/h")]
        [Summary]
        [Description("The hydraulic conducitivity of water into the pore")]
        public double[][] HydraulicConductivityIn { get; set; }
        /// <summary>
        /// Hydraulic concutivitiy out of each pore
        /// </summary>
        [Units("mm/h")]
        [Summary]
        [Description("The hydraulic conducitivity of water out of the pore")]
        public double[][] HydraulicConductivityOut { get; set; }
        /// <summary>
        /// The water potential when this pore space is full and larger pores are empty
        /// </summary>
        [Units("cm")]
        [Summary]
        [Description("Layer water potential when these pore spaces are full and larger pores are empty")]
        public double[][] PsiUpper { get; set; }
        /// <summary>
        /// The relative water water filled porosity when this pore space if full and larger pores are empty
        /// </summary>
        [Units("0-1")]
        [Summary]
        [Description("Layer relative water water filled porosity when there pores are full and larger pores are empty")]
        public double[][] RelativePoreVolume { get; set; }
        #endregion

        #region Properties
        /// <summary>
        /// The number of layers in the soil profile
        /// </summary>
        private int ProfileLayers { get; set; }
        /// <summary>
        /// The number of compartments the soils porosity is divided into
        /// </summary>
        private int PoreCompartments { get; set; }
        /// <summary>
        /// How much of the current air filled volume of a layer may be water filled in the comming hour
        /// </summary>
        [Units("mm")]
        private double[] AdsorptionCapacity { get; set; }
        /// <summary>
        /// How much water may pass through the current pore in the comming hour
        /// </summary>
        [Units("mm/h")]
        private double[] TransmissionCapacity { get; set; }
        /// <summary>
        /// How much water can the profile below this layer absorb in the comming hour
        /// </summary>
        [Units("mm")]
        private double[] AdsorptionCapacityBelow { get; set; }
        /// <summary>
        /// The amount of water that may flow into and through the profile below this layer in the comming hour
        /// </summary>
        [Units("mm")]
        private double[] PercolationCapacityBelow { get; set; }
        /// <summary>
        /// The amount of water that may enter the surface of the soil each hour
        /// </summary>
        private double PotentialInfiltration { get; set; }
        /// <summary>
        /// The distance down to the nearest zero potential body of water, for calculating gravitational potential
        /// </summary>
        [Units("m")]
        private double[] LayerHeight { get; set; }
        /// <summary>
        /// The depth of the specificed soil profile
        /// </summary>
        [Units("m")]
        private double ProfileDepth { get; set; }

        #endregion

        #region Event Handlers
        /// <summary>
        /// Called when [simulation commencing].
        /// Goes through and creates instances of all the properties of MultiPoreWater model
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        /// <exception cref="ApsimXException">
        /// SoilWater module has detected that the Soil has no layers.
        /// </exception>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            ProfileLayers = Water.Thickness.Length;
            PoreCompartments = PoreBounds.Length - 1;
            AdsorptionCapacity = new double[ProfileLayers];
            TransmissionCapacity = new double[ProfileLayers];
            AdsorptionCapacityBelow = new double[ProfileLayers];
            PercolationCapacityBelow = new double[ProfileLayers];
            LayerHeight = new double[ProfileLayers];
            SWmm = new double[ProfileLayers];
            SW = new double[ProfileLayers];
            ProfileParams = new ProfileParameters(ProfileLayers);

            Pores = new Pore[ProfileLayers][];
            PoreWater = new double[ProfileLayers][];
            HydraulicConductivityIn = new double[ProfileLayers][];
            HydraulicConductivityOut = new double[ProfileLayers][];
            PsiUpper = new double[ProfileLayers][];
            RelativePoreVolume = new double[ProfileLayers][];
            for (int l = 0; l < ProfileLayers; l++)
            {
                Pores[l] = new Pore[PoreCompartments];
                PoreWater[l] = new double[PoreCompartments];
                HydraulicConductivityIn[l] = new double[PoreCompartments];
                HydraulicConductivityOut[l] = new double[PoreCompartments];
                PsiUpper[l] = new double[PoreCompartments];
                RelativePoreVolume[l] = new double[PoreCompartments];
                for (int c = PoreCompartments - 1; c >= 0; c--)
                {
                    Pores[l][c] = new Pore();
                    PoreWater[l][c] = new double();
                    HydraulicConductivityIn[l][c] = new double();
                    HydraulicConductivityOut[l][c] = new double();
                    PsiUpper[l][c] = new double();
                    RelativePoreVolume[l][c] = new double();
                }
            }

            SetSoilProperties(); //Calls a function that applies soil parameters to calculate and set the properties for the soil
           

            Hourly = new HourlyData();
            ProfileSaturation = MathUtilities.Sum(ProfileParams.SaturatedWaterDepth);
            
            if (ReportDetail) { DoDetailReport("Initialisation", 0, 0); }
        }

        /// <summary>
        /// Called at the start of each daily timestep
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [EventSubscribe("DoDailyInitialisation")]
        private void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            Irrigation = 0;
            IrrigationDuration = 0;
            Rainfall = 0;
            Drainage = 0;
            Infiltration = 0;
            Array.Clear(Hourly.Irrigation, 0, 24);
            Array.Clear(Hourly.Rainfall, 0, 24);
            Array.Clear(Hourly.Drainage, 0, 24);
            Array.Clear(Hourly.Infiltration, 0, 24);
        }
        /// <summary>
        /// Called when the model is ready to work out daily soil water deltas
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [EventSubscribe("DoSoilWaterMovement")]
        private void OnDoSoilWaterMovement(object sender, EventArgs e)
        {
            //First we work out how much water is reaching the soil surface each hour
            doPrecipitation();
            SODPondDepth = pond;
            double SoilWaterContentSOD = MathUtilities.Sum(SWmm);
            for (int h = 0; h < 24; h++)
            {
                InitialProfileWater = MathUtilities.Sum(SWmm);
                InitialPondDepth = pond;
                InitialResidueWater = ResidueWater;
                doGravitionalPotential();
                //Update the depth of Surface water that may infiltrate this hour
                pond += Hourly.Rainfall[h] + Hourly.Irrigation[h];
                DoDetailReport("UpdatePond",0,h);
                //Then we work out how much of this may percolate into the profile each hour
                doPercolationCapacity();
                //Now we know how much water can infiltrate into the soil, lets put it there if we have some
                double HourlyInfiltration = Math.Min(pond, PotentialInfiltration);
                if ((HourlyInfiltration>0)&&(CalculateInfiltration))
                    doInfiltration(HourlyInfiltration,h);
                //Next we redistribute water down the profile for draiange processes
                if (CalculateDrainage)
                doDrainage(h);
                doEvaporation();
                doTranspiration();
                doDownwardDiffusion();
                doUpwardDiffusion();
            }
            EODPondDepth = pond;
            Infiltration = MathUtilities.Sum(Hourly.Infiltration);
            Drainage = MathUtilities.Sum(Hourly.Drainage);
            double SoilWaterContentEOD = MathUtilities.Sum(SWmm);
            double DeltaSWC = SoilWaterContentSOD - SoilWaterContentEOD;
            double CheckMass = DeltaSWC + Infiltration - Drainage;
            if (Math.Abs(CheckMass) > FloatingPointTolerance)
                throw new Exception(this + " Mass balance violated");
        }
        /// <summary>
        /// Adds irrigation events into daily total
        /// </summary>
        /// <param name="sender">Irrigation</param>
        /// <param name="IrrigationData">The irrigation data.</param>
        [EventSubscribe("Irrigated")]
        private void OnIrrigated(object sender, Models.Soils.IrrigationApplicationType IrrigationData)
        {
            ResidueWater = ResidueWater + ResidueInterception(IrrigationData.Amount);
            Irrigation += IrrigationData.Amount - ResidueInterception(IrrigationData.Amount);
            //Fix me.  Need to subtract out canopy interception also
            IrrigationDuration += IrrigationData.Duration;
        }
        /// <summary>
        /// sets up daily met data
        /// </summary>
        [EventSubscribe("PreparingNewWeatherData")]
        private void OnPreparingNewWeatherData(object sender, EventArgs e)
        {
            if (Met.Rain > 0)
            {
                ResidueWater = ResidueWater + ResidueInterception(Met.Rain);
                double DailyRainfall = Met.Rain - ResidueInterception(Met.Rain);
                //Fix me.  Need to subtract out canopy interception also
            }
        }
        #endregion

        #region Internal States
        private double FloatingPointTolerance = 0.0000000001;
        /// <summary>
        /// This is the Irrigation ariving at the soil surface, less what has been intercepted by residue
        /// </summary>
        private double Irrigation {get;set; }
        private double IrrigationDuration { get; set; }
        /// <summary>
        /// This is the rainfall ariving at the soil surface, less what has been intercepted by residue
        /// </summary>
        private double Rainfall { get; set; }
        /// <summary>
        /// Variable used for checking mass balance
        /// </summary>
        private double InitialProfileWater { get; set;  }
        /// <summary>
        /// Variable used for checking mass balance
        /// </summary>
        private double InitialPondDepth { get; set; }
        /// <summary>
        /// Variable used for checking mass balance
        /// </summary>
        private double InitialResidueWater { get; set; }
        private double ProfileSaturation { get; set; }
        private double SODPondDepth { get; set; }
        private double EODPondDepth { get; set; }
        #endregion

        #region Internal Properties and Methods
        /// <summary>
        /// Goes through all profile and pore properties and updates their values using soil parameters.  
        /// Must be called after any soil parameters are chagned if the effect of the changes is to work correctly.
        /// </summary>
        private void SetSoilProperties()
        {
            for (int l = 0; l < ProfileLayers; l++)
            {
                ProfileDepth += Water.Thickness[l] / 1000;
                ProfileParams.Ksat[l] = Water.KS[l] / 24; //Convert daily values to hourly
                ProfileParams.SaturatedWaterDepth[l] = Water.SAT[l] * Water.Thickness[l];
            }

            HyProps.SetHydraulicProperties();
            doGravitionalPotential();
            pond = 0;
            for (int l = 0; l < ProfileLayers; l++)
            {
                double AccumWaterVolume = 0;
                for (int c = PoreCompartments - 1; c >= 0; c--)
                {
                    Pores[l][c].Layer = l;
                    Pores[l][c].Compartment = c;
                    Pores[l][c].DiameterUpper = PoreBounds[c];
                    Pores[l][c].DiameterLower = PoreBounds[c + 1];
                    Pores[l][c].Thickness = Water.Thickness[l];
                    Pores[l][c].ThetaUpper = HyProps.SimpleTheta(l, Pores[l][c].PsiUpper);
                    Pores[l][c].ThetaLower = HyProps.SimpleTheta(l, Pores[l][c].PsiLower);
                    double PoreWaterFilledVolume = Math.Min(Pores[l][c].Volume, Soil.InitialWaterVolumetric[l] - AccumWaterVolume);
                    AccumWaterVolume += PoreWaterFilledVolume;
                    Pores[l][c].WaterDepth = PoreWaterFilledVolume * Water.Thickness[l];
                    Pores[l][c].HydraulicConductivityUpper = HyProps.SimpleK(l, Pores[l][c].PsiUpper) * 10;
                    Pores[l][c].HydraulicConductivityLower = HyProps.SimpleK(l, Pores[l][c].PsiLower) * 10;
                    HydraulicConductivityIn[l][c] = Pores[l][c].HydraulicConductivityIn;
                    HydraulicConductivityOut[l][c] = Pores[l][c].HydraulicConductivityOut;
                    PsiUpper[l][c] = Pores[l][c].PsiUpper;
                }
                if (Math.Abs(AccumWaterVolume - Soil.InitialWaterVolumetric[l]) > FloatingPointTolerance)
                    throw new Exception(this + " Initial water content has not been correctly partitioned between pore compartments in layer" + l);
                SWmm[l] = LayerSum(Pores[l], "WaterDepth");
                SW[l] = LayerSum(Pores[l], "WaterDepth") / Water.Thickness[l];
                ProfileSaturation += Water.SAT[l] * Water.Thickness[1];
            }
            for (int l = 0; l < ProfileLayers; l++)
            {
                for (int c = PoreCompartments - 1; c >= 0; c--)
                {
                    RelativePoreVolume[l][c] = Pores[l][c].ThetaUpper / Pores[l][0].ThetaUpper;
                }
            }
        }
        private double ResidueInterception(double Precipitation)
        {
            double ResidueWaterCapacity = 0.0002 * SurfaceOM.Wt; //Fixme coefficient should be obtained from surface OM
            return Math.Min(Precipitation * SurfaceOM.Cover, ResidueWaterCapacity - ResidueWater);
        }
        /// <summary>
        /// Potential gradients moves water out of layers each time step
        /// </summary>
        private double Infiltrate(Pore P)
        {
            double PotentialAdsorbtion = Math.Min(P.HydraulicConductivityIn, P.AirDepth);
            return PotentialAdsorbtion;
        }
        private void doPrecipitation()
        {
            if (Irrigation > 0)
            { //On days when irrigation is applies spread it out into hourly increments
                if (IrrigationDuration > 24)
                    throw new Exception(this + " daily irrigation duration exceeds 24 hours.  There are only 24 hours in each day so it is not really possible to irrigate for longer that this");
                int Irrighours = (int)IrrigationDuration;
                double IrrigationRate = Irrigation / IrrigationDuration;

                for (int h = 0; h < Irrighours; h++)
                {
                    Hourly.Irrigation[h] = IrrigationRate;
                }
                if (Math.Abs(MathUtilities.Sum(Hourly.Irrigation) - Irrigation) > FloatingPointTolerance)
                    throw new Exception(this + " hourly irrigation partition has gone wrong.  Check you are specifying a Duration > 0 in the irrigation method call");
            }
            if (Met.Rain > 0)
            {  //On days when rainfall occurs put it into hourly increments
                int RainHours = (int)Met.RainfallHours;
                double RainRate = Met.Rain / RainHours;
                for (int h = 0; h < RainHours; h++)
                {
                    Hourly.Rainfall[h] = RainRate;
                }
                if (Math.Abs(MathUtilities.Sum(Hourly.Rainfall) - Met.Rain) > FloatingPointTolerance)
                    throw new Exception(this + " hourly rainfall partition has gone wrong");
            }
        }
        /// <summary>
        /// Carries out infiltration processes at each time step
        /// </summary>
        private void doPercolationCapacity()
        {
            for (int l = 0; l < ProfileLayers; l++)
            {//Step through each layer
                double PotentialAbsorption = 0;
                double PotentialTransmission = 0;
                for (int c = PoreCompartments - 1; c >= 0; c--)
                {//Workout how much water may be adsorbed into and transmitted from each pore
                    PotentialAbsorption += Math.Min(Pores[l][c].HydraulicConductivityIn, Pores[l][c].AirDepth);
                    PotentialTransmission += Pores[l][c].HydraulicConductivityOut; 
                }
                AdsorptionCapacity[l] = PotentialAbsorption;
                TransmissionCapacity[l] = PotentialTransmission;
            }
            for (int l = ProfileLayers-1; l >=0; l--)
            {//Then step through each layer and work out how much water the profile below can take
                if (l == ProfileLayers - 1)
                {
                    //In the bottom layer of the profile absorption capaicity below is the amount of water this layer can absorb
                    AdsorptionCapacityBelow[l] = AdsorptionCapacity[l];
                    //In the bottom layer of the profile percolation capacity below is the conductance of the bottom of the profile
                    PercolationCapacityBelow[l] = SubProfileConductance;
                }
                else
                {
                    //For subsequent layers up the profile absorpbion capacity below adds the current layer to the sum of the layers below
                    AdsorptionCapacityBelow[l] = AdsorptionCapacityBelow[l + 1] + AdsorptionCapacity[l];
                    //For subsequent layers up the profile the percolation capacity below is the amount that the layer below may absorb
                    //plus the minimum of what may drain through the layer below (ksat of layer below) and what may potentially percolate
                    //Into the rest of the profile below that
                    PercolationCapacityBelow[l] = AdsorptionCapacity[l + 1] + Math.Min(TransmissionCapacity[l + 1],PercolationCapacityBelow[l+1]);
                }
            }
            //The amount of water that may percolate below the surface layer plus what ever the surface layer may absorb
            PotentialInfiltration = AdsorptionCapacity[0] + Math.Min(PercolationCapacityBelow[0],TransmissionCapacity[0]);
        }
        /// <summary>
        /// Calculates the gravitational potential in each layer from its height to the nearest zero potential layer
        /// </summary>
        private void doGravitionalPotential()
        {
            for (int l = ProfileLayers - 1; l >= 0; l--)
            {//Step through each layer from the bottom up and calculate the height
                if (l == ProfileLayers - 1)
                {//For the bottom layer height is equal to the depth of the water table below the bottom of the profile
                    if (SubProfileConductance == 0)
                        LayerHeight[l] = 0;
                    else
                        LayerHeight[l] = Math.Max(0,WaterTableDepth - ProfileDepth);
                }
                else
                {
                    if ((ProfileParams.Ksat[l + 1] < 0.001) || (SW[l + 1] == Water.SAT[l + 1]))
                        LayerHeight[l] = 0;
                    else
                        LayerHeight[l] = LayerHeight[l + 1] + Water.Thickness[l + 1]/1000;
                }
                for (int c = PoreCompartments - 1; c >= 0; c--)
                {//Step through each pore and assign the gravitational potential for the layer
                    Pores[l][c].GravitationalPotential = LayerHeight[l] / -0.1022;
                }
            }
        }
        private void doInfiltration(double WaterToInfiltrate, int h)
        {
            //Do infiltration processes each hour
            double RemainingInfiltration = WaterToInfiltrate;
            for (int l = 0; l < ProfileLayers && RemainingInfiltration > 0; l++)
            { //Start process in the top layer
                DistributWaterInFlux(l, ref RemainingInfiltration);
                DoDetailReport("Infiltrate",l,h);
            }
            //Add infiltration to daily sum for reporting
            Hourly.Infiltration[h] = WaterToInfiltrate;
            pond -= WaterToInfiltrate;

            Hourly.Drainage[h] += RemainingInfiltration;
            //Error checking for debugging.  To be removed when model complted
            UpdateProfileValues();
            CheckMassBalance("Infiltration",h);
        }
        /// <summary>
        /// Gravity moves mobile water out of layers each time step
        /// </summary>
        private void doDrainage(int h)
        {
            for (int l = 0; l < ProfileLayers; l++)
            {//Step through each layer from the top down
                double PotentialDrainage = 0;
                for (int c = PoreCompartments - 1; c >= 0; c--)
                {//Step through each pore compartment and work out how much may drain
                    PotentialDrainage += Math.Min(Pores[l][c].HydraulicConductivityOut, Pores[l][c].WaterDepth);
                }
                //Limit drainage to that of what the layer may drain and that of which the provile below will allow to drain
                double OutFluxCurrentLayer = Math.Min(PotentialDrainage, PercolationCapacityBelow[l]);
                //Catch the drainage from this layer to be the InFlux to the next Layer down the profile
                double InFluxLayerBelow = OutFluxCurrentLayer;
                //Discharge water from current layer
                for (int c = 0; c < PoreCompartments && OutFluxCurrentLayer > 0; c++)
                {//Step through each pore compartment and remove the water that drains starting with the largest pores
                    double drain = Math.Min(OutFluxCurrentLayer, Math.Min(Pores[l][c].WaterDepth,Pores[l][c].HydraulicConductivityOut));
                    Pores[l][c].WaterDepth -= drain;
                    OutFluxCurrentLayer -= drain;
                    DoDetailReport("Drain", l, h);
                }
                if (Math.Abs(OutFluxCurrentLayer) > FloatingPointTolerance)
                    throw new Exception("Error in drainage calculation");

                //Distribute water from this layer into the profile below and record draiange out the bottom
                //Bring the layer below up to its maximum absorption then move to the next
                for (int l1 = l + 1; l1 < ProfileLayers + 1 && InFluxLayerBelow > 0; l1++)
                {
                    //Any water not stored by this layer will flow to the layer below as saturated drainage
                    if (l1 < ProfileLayers)
                    {
                        DoDetailReport("Redistribute", l1, h);
                        DistributWaterInFlux(l1, ref InFluxLayerBelow);
                    }
                    //If it is the bottom layer, any discharge recorded as drainage from the profile
                    else
                    {
                        Hourly.Drainage[h] += InFluxLayerBelow;
                    }
                }
            }
            //Error checking for debugging.  To be removed when model complted
            UpdateProfileValues();
            CheckMassBalance("Drainage",h); 
        }
        /// <summary>
        /// Potential gradients moves water out of layers each time step
        /// </summary>
        private void doEvaporation()
        {
            //Evaporate water from top layer
        }
        /// <summary>
        /// Potential gradients moves water out of layers each time step
        /// </summary>
        private void doTranspiration()
        {
            //write some temporary stuff to be replaced by arbitrator at some stage
        }
        /// <summary>
        /// Potential gradients moves water out of layers each time step
        /// </summary>
        private void doDownwardDiffusion()
        {
            //Move water down into lower layers if they are dryer than above
        }
        /// <summary>
        /// Potential gradients moves water out of layers each time step
        /// </summary>
        private void doUpwardDiffusion()
        {
            //Move water up into lower layers if they are dryer than below
        }
        /// <summary>
        /// Utility to sum the specified propertie from all pore compartments in the pore layer input 
        /// </summary>
        /// <param name="Compartments"></param>
        /// <param name="Property"></param>
        /// <returns>sum</returns>
        private double LayerSum(Pore[] Compartments, string Property)
        {
            double Sum = 0;
            foreach (Pore P in Compartments)
            {
                object o = ReflectionUtilities.GetValueOfFieldOrProperty(Property, P);
                if (o == null)
                    throw new NotImplementedException();
                Sum += (double)o;
            }
            return Sum;
        }
        /// <summary>
        /// Method takes water flowing into a layer and distributes it between the pore compartments in that layer
        /// </summary>
        /// <param name="l"></param>
        /// <param name="InFlux"></param>
        private void DistributWaterInFlux(int l, ref double InFlux)
        {
            for (int c = PoreCompartments - 1; c >= 0 && InFlux > 0; c--)
            {//Absorb Water onto samllest pores first followed by larger ones
                double PotentialAdsorbtion = Math.Min(Pores[l][c].HydraulicConductivityIn, Pores[l][c].AirDepth);
                double Absorbtion = Math.Min(InFlux, PotentialAdsorbtion);
                Pores[l][c].WaterDepth += Absorbtion;
                InFlux -= Absorbtion;
            }
            if ((LayerSum(Pores[l], "WaterDepth") - ProfileParams.SaturatedWaterDepth[l])>FloatingPointTolerance)
                throw new Exception("Water content of layer " + l + " exceeds saturation.  This is not really possible");
        }
        private void CheckMassBalance(string Process, int h)
        {
            double WaterIn = InitialProfileWater + InitialPondDepth + InitialResidueWater 
                             + Hourly.Rainfall[h] + Hourly.Irrigation[h];
            double ProfileWaterAtCalcEnd = MathUtilities.Sum(SWmm);
            double WaterOut = ProfileWaterAtCalcEnd + pond + ResidueWater + Hourly.Drainage[h];
            if (Math.Abs(WaterIn - WaterOut) > FloatingPointTolerance)
                throw new Exception(this + " " + Process + " calculations are violating mass balance");           
        }
        /// <summary>
        /// Function to update profile summary values
        /// </summary>
        private void UpdateProfileValues()
        {
            for (int l = ProfileLayers - 1; l >= 0; l--)
            {
                SWmm[l] = LayerSum(Pores[l], "WaterDepth");
                SW[l] = LayerSum(Pores[l], "WaterDepth") / Water.Thickness[l];
            }
        }

        private void DoDetailReport(string CallingProcess,int Layer,int hour)
        {
            if (ReportDetail)
            {
                for (int l = 0; l < ProfileLayers; l++)
                {
                    for (int c = 0; c < PoreCompartments; c++)
                    {
                        if (Pores[l][c].WaterFilledVolume == 0)
                            PoreWater[l][c] = 0;
                        else
                            PoreWater[l][c] = Pores[l][c].RelativeWaterContent;
                    }
                }
                Process = CallingProcess;
                ReportLayer = Layer;
                Hour = hour;
                TimeStep += 1;
                ReportDetails.Invoke(this, new EventArgs());
            }
        }
        #endregion
    }
}