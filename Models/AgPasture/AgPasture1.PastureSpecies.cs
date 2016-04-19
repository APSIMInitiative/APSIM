﻿//-----------------------------------------------------------------------
// <copyright file="AgPasture1.PastureSpecies.cs" project="AgPasture" solution="APSIMx" company="APSIM Initiative">
//     Copyright (c) ASPIM initiative. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Text;
using System.Linq;
using System.Linq.Expressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Models;
using Models.Core;
using Models.Soils;
using Models.PMF;
using Models.Arbitrator;
using Models.Soils.Arbitrator;
using Models.Interfaces;
using APSIM.Shared.Utilities;

namespace Models.AgPasture1
{
	/// <summary>Describes a pasture species</summary>
	[Serializable]
	[ViewName("UserInterface.Views.GridView")]
	[PresenterName("UserInterface.Presenters.PropertyPresenter")]
	public class PastureSpecies : Model, ICrop, ICrop2, IUptake
	{
		#region Links, events and delegates  -------------------------------------------------------------------------------

		//- Links  ----------------------------------------------------------------------------------------------------

		/// <summary>Link to APSIM's Clock (time information)</summary>
		[Link]
		private Clock myClock = null;

		/// <summary>Link to APSIM's WeatherFile (meteorological information)</summary>
		[Link]
		private IWeather myMetData = null;

		/// <summary>Link to the Soil (soil layers and other information)</summary>
		[Link]
		private Soils.Soil mySoil = null;

		/// <summary>Link to apsim's Resource Arbitrator module</summary>
		[Link(IsOptional=true)]
		private Arbitrator.Arbitrator myApsimArbitrator = null;

        //- Events  ---------------------------------------------------------------------------------------------------

        /// <summary>Reference to a NewCrop event</summary>
        /// <param name="Data">Data about crop type</param>
        public delegate void NewCropDelegate(PMF.NewCropType Data);

		/// <summary>Event to be invoked to tell other models about the existence of this species</summary>
		public event NewCropDelegate NewCrop;

		/// <summary>Event to be invoked when sowing or at initialisation (tell models about existence of this species).</summary>
		public event EventHandler Sowing;

		/// <summary>Reference to a FOM incorporation event</summary>
		/// <param name="Data">The data with soil FOM to be added.</param>
		public delegate void FOMLayerDelegate(Soils.FOMLayerType Data);

		/// <summary>Occurs when plant is depositing senesced roots.</summary>
		public event FOMLayerDelegate IncorpFOM;

		/// <summary>Reference to a BiomassRemoved event</summary>
		/// <param name="Data">The data about biomass deposited by this plant to the soil surface.</param>
		public delegate void BiomassRemovedDelegate(PMF.BiomassRemovedType Data);

		/// <summary>Occurs when plant is depositing litter.</summary>
		public event BiomassRemovedDelegate BiomassRemoved;

		/// <summary>Reference to a WaterChanged event</summary>
		/// <param name="Data">The changes in the amount of water for each soil layer.</param>
		public delegate void WaterChangedDelegate(PMF.WaterChangedType Data);

		/// <summary>Occurs when plant takes up water.</summary>
		public event WaterChangedDelegate WaterChanged;

		/// <summary>Reference to a NitrogenChanged event</summary>
		/// <param name="Data">The changes in the soil N for each soil layer.</param>
		public delegate void NitrogenChangedDelegate(Soils.NitrogenChangedType Data);

		/// <summary>Occurs when the plant takes up soil N.</summary>
		public event NitrogenChangedDelegate NitrogenChanged;

		#endregion

        #region Canopy interface
        /// <summary>Canopy type</summary>
        public string CanopyType { get { return CropType; } }

        /// <summary>Gets the LAI (m^2/m^2)</summary>
        public double LAI { get { return LAIGreen; } }

//        /// <summary>Gets the cover green (0-1)</summary>
//        public double CoverGreen { get { return GreenCover; } }

//        /// <summary>Gets the cover total (0-1)</summary>
//        public double CoverTotal { get { return TotalCover; } }

        /// <summary>Gets the canopy height (mm)</summary>
        [Description("Plants average height")]
        [Units("mm")]
        public double Height
        {
            get { return myPlantHeight; }  // minimum = 20mm  - TODO: update this function
        }

        /// <summary>Gets the canopy depth (mm)</summary>
        public double Depth { get { return Height; } }

        // TODO: have to verify how this works (what exactly is needed by MicroClimate
        /// <summary>Plant growth limiting factor, supplied to another module calculating potential transpiration</summary>
        public double FRGR
        { get { return 1.0; } }

        /// <summary>Potential evapotranspiration, as calculated by MicroClimate</summary>
        [XmlIgnore]
        public double PotentialEP
        {
            get
            {
                return myWaterDemand;
            }
            set
            {
                myWaterDemand = value;
                demandWater = myWaterDemand;
            }
        }

		/// <summary>Gets or sets the light profile for this plant, as calculated by MicroClimate</summary>
		[XmlIgnore]
		public CanopyEnergyBalanceInterceptionlayerType[] LightProfile
		{
			get { return myLightProfile; }
			set
			{
				RadnIntercepted = 0.0;
				for (int s = 0; s < value.Length; s++)
				{
					myLightProfile = value;
					RadnIntercepted += myLightProfile[s].amount;
				}
			}
		}
        #endregion

        #region ICrop implementation  --------------------------------------------------------------------------------------

        /// <summary>
		/// Generic descriptor used by MicroClimate to look up for canopy properties for this plant
		/// </summary>
		[Description("Generic type of crop")]
		[Units("")]
		public string CropType
		{
			get { return Name; }
		}

		/// <summary>Gets a list of cultivar names (not used by AgPasture)</summary>
		public string[] CultivarNames
		{
			get { return null; }
		}



		/// <summary>The intercepted solar radiation</summary>
		public double RadnIntercepted;
		/// <summary>Light profile (energy available for each canopy layer)</summary>
		private CanopyEnergyBalanceInterceptionlayerType[] myLightProfile;

		// TODO: Have to verify how this works, it seems Microclime needs a sow event, not new crop...
		/// <summary>Invokes the NewCrop event (info about this crop type)</summary>
		private void DoNewCropEvent()
		{
			if (NewCrop != null)
			{
				// Send out New Crop Event to tell other modules who I am and what I am
				PMF.NewCropType EventData = new PMF.NewCropType();
				EventData.crop_type = mySpeciesFamily;
				EventData.sender = Name;
				NewCrop.Invoke(EventData);
			}

			if (Sowing != null)
				Sowing.Invoke(this, new EventArgs());
		}

		/// <summary>Sows the plant</summary>
		/// <param name="cultivar"></param>
		/// <param name="population"></param>
		/// <param name="depth"></param>
		/// <param name="rowSpacing"></param>
		/// <param name="maxCover"></param>
		/// <param name="budNumber"></param>
		public void Sow(string cultivar, double population, double depth, double rowSpacing, double maxCover = 1, double budNumber = 1)
		{

		}

		#endregion

		#region ICrop2 implementation  -------------------------------------------------------------------------------------

		///// <summary>
		///// Generic descriptor used by MicroClimate to look up for canopy properties for this plant
		///// </summary>
		//[Description("Generic type of crop")]
		//[Units("")]
		//public string CropType
		//{
		//    get { return speciesFamily; }
		//}

		///// <summary>Gets a list of cultivar names (not used by AgPasture)</summary>
		//public string[] CultivarNames
		//{
		//    get { return null; }
		//}

		/// <summary>Flag whether the plant in the ground</summary>
		[XmlIgnore]
		public bool PlantInGround { get { return true; } }

		/// <summary>Flag whether the plant has emerged</summary>
		[XmlIgnore]
		public bool PlantEmerged { get { return true; } }

		/// <summary>
		/// The set of crop canopy properties used by Arbitrator for light and energy calculations
		/// </summary>
		public CanopyProperties CanopyProperties { get { return myCanopyProperties; } }
		/// <summary>The canopy data for this plant</summary>
		CanopyProperties myCanopyProperties = new CanopyProperties();

		/// <summary>
		/// The set of crop root properties used by Arbitrator for water and nutrient calculations
		/// </summary>
		public RootProperties RootProperties { get { return myRootProperties; } }
		/// <summary>The root data for this plant</summary>
		RootProperties myRootProperties = new RootProperties();

		///// <summary>The intercepted solar radiation</summary>
		//internal double interceptedRadn;
		///// <summary>Light profile (energy available for each canopy layer)</summary>
		//private CanopyEnergyBalanceInterceptionlayerType[] myLightProfile;
		///// <summary>Gets or sets the light profile for this plant, as calculated by MicroClimate</summary>
		//[XmlIgnore]
		//public CanopyEnergyBalanceInterceptionlayerType[] LightProfile
		//{
		//    get { return myLightProfile; }
		//    set
		//    {
		//        interceptedRadn = 0.0;
		//        for (int s = 0; s < value.Length; s++)
		//        {
		//            myLightProfile = value;
		//            interceptedRadn += myLightProfile[s].amount;
		//        }
		//    }
		//}

		/// <summary> Water demand for this plant (mm/day)</summary>
		[XmlIgnore]
		public double demandWater { get; set; }

		/// <summary> The actual supply of water to the plant (mm), values given for each soil layer</summary>
		[XmlIgnore]
		public double[] uptakeWater { get; set; }

		/// <summary>Nitrogen demand for this plant (kgN/ha/day)</summary>
		[XmlIgnore]
		public double demandNitrogen { get; set; }

		/// <summary>
		/// The actual supply of nitrogen (nitrate plus ammonium) to the plant (kgN/ha), values given for each soil layer
		/// </summary>
		[XmlIgnore]
		public double[] uptakeNitrogen { get; set; }

		/// <summary>The proportion of nitrogen uptake from each layer in the form of nitrate (0-1)</summary>
		[XmlIgnore]
		public double[] uptakeNitrogenPropNO3 { get; set; }

		# endregion

		#region Model parameters  ------------------------------------------------------------------------------------------

		// NOTE: default parameters describe a generic perennial ryegrass species

		/// <summary>Family type for this plant species (grass/legume/brassica)</summary>
		private string mySpeciesFamily = "Grass";
		/// <summary>Gets or sets the species family type.</summary>
		/// <value>The species family descriptor.</value>
		[Description("Family type for this plant species [grass/legume/brassica]:")]
		public string SpeciesFamily
		{
			get { return mySpeciesFamily; }
			set
			{
				mySpeciesFamily = value;
				myIsLegume = value.ToLower().Contains("legume");
			}
		}

		/// <summary>Metabolic pathway for C fixation during photosynthesis (C3/C4/CAM)</summary>
		private string myPhotosynthesisPathway = "C3";
		/// <summary>Gets or sets the species photosynthetic pathway.</summary>
		/// <value>The species photo pathway.</value>
		[Description("Metabolic pathway for C fixation during photosynthesis [C3/C4/CAM]:")]
		public string SpeciesPhotoPathway
		{
			get { return myPhotosynthesisPathway; }
			set { myPhotosynthesisPathway = value; }
		}

		/// <summary>The initial DM amount above ground (shoot)</summary>
		private double myIniDMShoot = 2000.0;
		/// <summary>Gets or sets the initial shoot DM.</summary>
		/// <value>The initial DM amount.</value>
		[Description("Initial above ground DM (leaf, stem, stolon, etc) [kg DM/ha]:")]
		[Units("kg/ha")]
		public double InitialDMShoot
		{
			get { return myIniDMShoot; }
			set { myIniDMShoot = value; }
		}

		/// <summary>The ini dm root</summary>
		private double myIniDMRoot = 500.0;
		/// <summary>Gets or sets the initial dm root.</summary>
		/// <value>The initial dm root.</value>
		[Description("Initial below ground DM (roots) [kg DM/ha]:")]
		[Units("kg/ha")]
		public double InitialDMRoot
		{
			get { return myIniDMRoot; }
			set { myIniDMRoot = value; }
		}

		/// <summary>The ini root depth</summary>
		private double myIniRootDepth = 750.0;
		/// <summary>Gets or sets the initial root depth.</summary>
		/// <value>The initial root depth.</value>
		[Description("Initial depth for roots [mm]:")]
		[Units("mm")]
		public double InitialRootDepth
		{
			get { return myIniRootDepth; }
			set { myIniRootDepth = value; }
		}

		// temporary?? initial DM fractions for grass or legume species
		/// <summary>The initial fractions of DM for grass</summary>
		private double[] myInitialDMFractions_grass = new double[] { 0.15, 0.25, 0.25, 0.05, 0.05, 0.10, 0.10, 0.05, 0.00, 0.00, 0.00 };
		/// <summary>The initial fractions of DM for legume</summary>
		private double[] myInitialDMFractions_legume = new double[] { 0.20, 0.25, 0.25, 0.00, 0.02, 0.04, 0.04, 0.00, 0.06, 0.12, 0.12 };

		/// <summary>
		/// Initial DM fractions for each plant tissue (in order: leaf1, leaf2, leaf3, leaf4, stem1, stem2, stem3, stem4, stolon1, stolon2, stolon3)
		/// </summary>
		private double[] myIniDMFraction;
		/// <summary>Gets or sets the initial dm fractions.</summary>
		/// <value>The initial dm fractions.</value>
		[XmlIgnore]
		[Units("0-1")]
		public double[] initialDMFractions
		{
			get { return myIniDMFraction; }
			set
			{
				//make sure we have te right number of values
				Array.Resize(ref value, 12);
				myIniDMFraction = new double[12];
				for (int i = 0; i < 12; i++)
					myIniDMFraction[i] = value[i];
			}
		}

		// - Growth and photosysnthesis  ------------------------------------------------------------------------------

		/// <summary>Reference CO2 assimilation rate during photosynthesis [mg CO2/m2 leaf/s]</summary>
		private double myReferencePhotosynthesisRate = 1.0;
		/// <summary>Reference CO2 assimilation rate during photosynthesis [mg CO2/m2 leaf/s]</summary>
		/// <value>The reference photosynthesis rate.</value>
		[Description("Reference CO2 assimilation rate during photosynthesis [mg CO2/m2/s]:")]
		[Units("mg/m^2/s")]
		public double ReferencePhotosynthesisRate
		{
			get { return myReferencePhotosynthesisRate; }
			set { myReferencePhotosynthesisRate = value; }
		}

		/// <summary>
		/// Maintenance respiration coefficient - Fraction of DM consumed by respiration [0-1]
		/// </summary>
		private double myMaintenanceRespirationCoef = 0.03;
		/// <summary>
		/// Maintenance respiration coefficient - Fraction of DM consumed by respiration [0-1]
		/// </summary>
		/// <value>The maintenance respiration coefficient.</value>
		[Description("Maintenance respiration coefficient [0-1]:")]
		[Units("0-1")]
		public double MaintenanceRespirationCoefficient
		{
			get { return myMaintenanceRespirationCoef; }
			set { myMaintenanceRespirationCoef = value; }
		}

		/// <summary>
		/// Growth respiration coefficient - fraction of photosynthesis CO2 not assimilated [0-1]
		/// </summary>
		private double myGrowthRespirationCoef = 0.25;
		/// <summary>
		/// Growth respiration coefficient - fraction of photosynthesis CO2 not assimilated (0-1)
		/// </summary>
		/// <value>The growth respiration coefficient.</value>
		[Description("Growth respiration coefficient [0-1]:")]
		[Units("0-1")]
		public double GrowthRespirationCoefficient
		{
			get { return myGrowthRespirationCoef; }
			set { myGrowthRespirationCoef = value; }
		}

		/// <summary>Light extinction coefficient [0-1]</summary>
		private double myLightExtentionCoeff = 0.5;
		/// <summary>Light extinction coefficient (0-1)</summary>
		/// <value>The light extention coeff.</value>
		[Description("Light extinction coefficient [0-1]:")]
		[Units("0-1")]
		public double LightExtentionCoeff
		{
			get { return myLightExtentionCoeff; }
			set { myLightExtentionCoeff = value; }
		}

		/// <summary>Minimum temperature for growth [oC]</summary>
		private double myGrowthTmin = 2.0;
		/// <summary>Minimum temperature for growth [oC]</summary>
		/// <value>The growth tmin.</value>
		[Description("Minimum temperature for growth [oC]:")]
		[Units("oC")]
		public double GrowthTmin
		{
			get { return myGrowthTmin; }
			set { myGrowthTmin = value; }
		}

		/// <summary>Maximum temperature for growth [oC]</summary>
		private double myGrowthTmax = 32.0;
		/// <summary>Maximum temperature for growth [oC]</summary>
		/// <value>The growth tmax.</value>
		[Description("Maximum temperature for growth [oC]:")]
		[Units("oC")]
		public double GrowthTmax
		{
			get { return myGrowthTmax; }
			set { myGrowthTmax = value; }
		}

		/// <summary>Optimum temperature for growth [oC]</summary>
		private double myGrowthTopt = 20.0;
		/// <summary>Optimum temperature for growth [oC]</summary>
		/// <value>The growth topt.</value>
		[Description("Optimum temperature for growth [oC]:")]
		[Units("oC")]
		public double GrowthTopt
		{
			get { return myGrowthTopt; }
			set { myGrowthTopt = value; }
		}

		/// <summary>Curve parameter for growth response to temperature</summary>
		private double myGrowthTq = 1.75;
		/// <summary>Curve parameter for growth response to temperature</summary>
		/// <value>The growth tq.</value>
		[Description("Curve parameter for growth response to temperature:")]
		[Units("-")]
		public double GrowthTq
		{
			get { return myGrowthTq; }
			set { myGrowthTq = value; }
		}

		/// <summary>Onset temperature for heat effects on growth [oC]</summary>
		private double myHeatOnsetT = 28.0;
		/// <summary>Onset temperature for heat effects on growth [oC]</summary>
		/// <value>The heat onset t.</value>
		[Description("Onset temperature for heat effects on growth [oC]:")]
		[Units("oC")]
		public double HeatOnsetT
		{
			get { return myHeatOnsetT; }
			set { myHeatOnsetT = value; }
		}

		/// <summary>Temperature for full heat effect on growth (no growth) [oC]</summary>
		private double myHeatFullT = 35.0;
		/// <summary>Temperature for full heat effect on growth (no growth) [oC]</summary>
		/// <value>The heat full t.</value>
		[Description("Temperature for full heat effect on growth [oC]:")]
		[Units("oC")]
		public double HeatFullT
		{
			get { return myHeatFullT; }
			set { myHeatFullT = value; }
		}

		/// <summary>Cumulative degrees for recovery from heat stress [oC]</summary>
		private double myHeatSumT = 30.0;
		/// <summary>Cumulative degrees for recovery from heat stress [oC]</summary>
		/// <value>The heat sum t.</value>
		[Description("Cumulative degrees for recovery from heat stress [oC]:")]
		[Units("oC")]
		public double HeatSumT
		{
			get { return myHeatSumT; }
			set { myHeatSumT = value; }
		}

		/// <summary>Reference temperature for recovery from heat stress [oC]</summary>
		private double myReferenceT4Heat = 25.0;
		/// <summary>Reference temperature for recovery from heat stress [oC]</summary>
		/// <value>The reference t4 heat.</value>
		[Description("Reference temperature for recovery from heat stress [oC]:")]
		[Units("oC")]
		public double ReferenceT4Heat
		{
			get { return myReferenceT4Heat; }
			set { myReferenceT4Heat = value; }
		}

		/// <summary>Onset temperature for cold effects on growth [oC]</summary>
		private double myColdOnsetT = 0.0;
		/// <summary>Onset temperature for cold effects on growth [oC]</summary>
		/// <value>The cold onset t.</value>
		[Description("Onset temperature for cold effects on growth [oC]:")]
		[Units("oC")]
		public double ColdOnsetT
		{
			get { return myColdOnsetT; }
			set { myColdOnsetT = value; }
		}

		/// <summary>Temperature for full cold effect on growth (no growth) [oC]</summary>
		private double myColdFullT = -3.0;
		/// <summary>Temperature for full cold effect on growth (no growth) [oC]</summary>
		/// <value>The cold full t.</value>
		[Description("Temperature for full cold effect on growth [oC]:")]
		[Units("oC")]
		public double ColdFullT
		{
			get { return myColdFullT; }
			set { myColdFullT = value; }
		}

		/// <summary>Cumulative degrees for recovery from cold stress [oC]</summary>
		private double myColdSumT = 20.0;
		/// <summary>Cumulative degrees for recovery from cold stress [oC]</summary>
		/// <value>The cold sum t.</value>
		[Description("Cumulative degrees for recovery from cold stress [oC]:")]
		[Units("oC")]
		public double ColdSumT
		{
			get { return myColdSumT; }
			set { myColdSumT = value; }
		}

		/// <summary>Reference temperature for recovery from cold stress [oC]</summary>
		private double myReferenceT4Cold = 0.0;
		/// <summary>Reference temperature for recovery from cold stress [oC]</summary>
		/// <value>The reference t4 cold.</value>
		[Description("Reference temperature for recovery from cold stress [oC]:")]
		[Units("oC")]
		public double ReferenceT4Cold
		{
			get { return myReferenceT4Cold; }
			set { myReferenceT4Cold = value; }
		}

		/// <summary>Specific leaf area [m^2/kg DM]</summary>
		private double mySpecificLeafArea = 20.0;
		/// <summary>Specific leaf area [m^2/kg DM]</summary>
		/// <value>The specific leaf area.</value>
		[Description("Specific leaf area [m^2/kg DM]:")]
		[Units("m^2/kg")]
		public double SpecificLeafArea
		{
			get { return mySpecificLeafArea; }
			set { mySpecificLeafArea = value; }
		}

		/// <summary>Specific root length [m/g DM]</summary>
		private double mySpecificRootLength = 75.0;
		/// <summary>Specific root length [m/g DM]</summary>
		/// <value>The length of the specific root.</value>
		[Description("Specific root length [m/g DM]:")]
		[Units("m/g")]
		public double SpecificRootLength
		{
			get { return mySpecificRootLength; }
			set { mySpecificRootLength = value; }
		}

		/// <summary>Maximum fraction of DM allocated to roots (from daily growth) [0-1]</summary>
		private double myMaxRootFraction = 0.25;
		/// <summary>Maximum fraction of DM allocated to roots (from daily growth) [0-1]</summary>
		/// <value>The maximum root fraction.</value>
		[Description("Maximum fraction of DM allocated to roots (from daily growth) [0-1]:")]
		[Units("0-1")]
		public double MaxRootFraction
		{
			get { return myMaxRootFraction; }
			set { myMaxRootFraction = value; }
		}

		/// <summary>Factor by which DM allocation to shoot is increased during 'spring'[0-1]</summary>
		private double myShootSeasonalAllocationIncrease = 0.8;
		/// <summary>Factor by which DM allocation to shoot is increased during 'spring' [0-1]</summary>
		/// <value>The shoot seasonal allocation increase.</value>
		/// <remarks>
		/// Allocation to shoot is typically given by 1-maxRootFraction, but for a certain 'spring' period it can be increased to simulate reproductive growth
		/// at this period shoot allocation is corrected by multiplying it by 1 + SeasonShootAllocationIncrease
		/// </remarks>
		[Description("Factor by which DM allocation to shoot is increased during 'spring' [0-1]:")]
		[Units("0-1")]
		public double ShootSeasonalAllocationIncrease
		{
			get { return myShootSeasonalAllocationIncrease; }
			set { myShootSeasonalAllocationIncrease = value; }
		}

		/// <summary>Day for the beginning of the period with higher shoot allocation ('spring')</summary>
		private int myDOYIniHighShoot = 232;
		/// <summary>Day for the beginning of the period with higher shoot allocation ('spring')</summary>
		/// <value>The day initialize higher shoot allocation.</value>
		/// <remarks>Care must be taken as this varies with north or south hemisphere</remarks>
		[Description("Day for the beginning of the period with higher shoot allocation ('spring'):")]
		[Units("-")]
		public int DayInitHigherShootAllocation
		{
			get { return myDOYIniHighShoot; }
			set { myDOYIniHighShoot = value; }
		}

		/// <summary>
		/// Number of days defining the duration of the three phases with higher DM allocation to shoot (onset, sill, return)
		/// </summary>
		private int[] myHigherShootAllocationPeriods = new int[] { 35, 60, 30 };
		/// <summary>
		/// Number of days defining the duration of the three phases with higher DM allocation to shoot (onset, sill, return)
		/// </summary>
		/// <value>The higher shoot allocation periods.</value>
		/// <remarks>
		/// Three numbers are needed, they define the duration of the phases for increase, plateau, and the deacrease in allocation
		/// The allocation to shoot is maximum at the plateau phase, it is 1 + SeasonShootAllocationIncrease times the value of maxSRratio
		/// </remarks>
		[Description("Duration of the three phases of higher DM allocation to shoot [days]:")]
		[Units("days")]
		public int[] HigherShootAllocationPeriods
		{
			get { return myHigherShootAllocationPeriods; }
			set
			{
				for (int i = 0; i < 3; i++)
					myHigherShootAllocationPeriods[i] = value[i];
				// so, if 1 or 2 values are supplied the remainder are not changed, if more values are given, they are ignored
			}
		}

		/// <summary>Fraction of new shoot growth allocated to leaves [0-1]</summary>
		private double myFracToLeaf = 0.7;
		/// <summary>Fraction of new shoot growth allocated to leaves [0-1]</summary>
		/// <value>The frac to leaf.</value>
		[Description("Fraction of new shoot growth allocated to leaves [0-1]:")]
		[Units("0-1")]
		public double FracToLeaf
		{
			get { return myFracToLeaf; }
			set { myFracToLeaf = value; }
		}

		/// <summary>Fraction of new shoot growth allocated to stolons [0-1]</summary>
		private double myFracToStolon = 0.0;
		/// <summary>Fraction of new shoot growth allocated to stolons [0-1]</summary>
		/// <value>The frac to stolon.</value>
		[Description("Fraction of new shoot growth allocated to stolons [0-1]:")]
		[Units("0-1")]
		public double FracToStolon
		{
			get { return myFracToStolon; }
			set { myFracToStolon = value; }
		}

		// Turnover rate  ---------------------------------------------------------------------------------------------

		/// <summary>Daily turnover rate for DM live to dead [0-1]</summary>
		private double myTurnoverRateLive2Dead = 0.025;
		/// <summary>Daily turnover rate for DM live to dead [0-1]</summary>
		/// <value>The turnover rate live2 dead.</value>
		[Description("Daily turnover rate for DM live to dead [0-1]:")]
		[Units("0-1")]
		public double TurnoverRateLive2Dead
		{
			get { return myTurnoverRateLive2Dead; }
			set { myTurnoverRateLive2Dead = value; }
		}

		/// <summary>Daily turnover rate for DM dead to litter [0-1]</summary>
		private double myTurnoverRateDead2Litter = 0.11;
		/// <summary>Daily turnover rate for DM dead to litter [0-1]</summary>
		/// <value>The turnover rate dead2 litter.</value>
		[Description("Daily turnover rate for DM dead to litter [0-1]:")]
		[Units("0-1")]
		public double TurnoverRateDead2Litter
		{
			get { return myTurnoverRateDead2Litter; }
			set { myTurnoverRateDead2Litter = value; }
		}

		/// <summary>Daily turnover rate for root senescence [0-1]</summary>
		private double myTurnoverRateRootSenescence = 0.02;
		/// <summary>Daily turnover rate for root senescence [0-1]</summary>
		/// <value>The turnover rate root senescence.</value>
		[Description("Daily turnover rate for root senescence [0-1]")]
		[Units("0-1")]
		public double TurnoverRateRootSenescence
		{
			get { return myTurnoverRateRootSenescence; }
			set { myTurnoverRateRootSenescence = value; }
		}

		/// <summary>Minimum temperature for tissue turnover [oC]</summary>
		private double myTissueTurnoverTmin = 2.0;
		/// <summary>Minimum temperature for tissue turnover [oC]</summary>
		/// <value>The tissue turnover tmin.</value>
		[Description("Minimum temperature for tissue turnover [oC]:")]
		[Units("oC")]
		public double TissueTurnoverTmin
		{
			get { return myTissueTurnoverTmin; }
			set { myTissueTurnoverTmin = value; }
		}

		/// <summary>Optimum temperature for tissue turnover [oC]</summary>
		private double myTissueTurnoverTopt = 20.0;
		/// <summary>Optimum temperature for tissue turnover [oC]</summary>
		/// <value>The tissue turnover topt.</value>
		[Description("Optimum temperature for tissue turnover [oC]:")]
		[Units("oC")]
		public double TissueTurnoverTopt
		{
			get { return myTissueTurnoverTopt; }
			set { myTissueTurnoverTopt = value; }
		}

		/// <summary>Maximum increase in tissue turnover due to water stress</summary>
		private double myTissueTurnoverWFactorMax = 2.0;
		/// <summary>Maximum increase in tissue turnover due to water stress</summary>
		/// <value>The tissue turnover w factor maximum.</value>
		[Description("Maximum increase in tissue turnover due to water stress:")]
		[Units("-")]
		public double TissueTurnoverWFactorMax
		{
			get { return myTissueTurnoverWFactorMax; }
			set { myTissueTurnoverWFactorMax = value; }
		}

		/// <summary>
		/// Optimum value GLFwater for tissue turnover [0-1] - below this value tissue turnover increases
		/// </summary>
		private double myTissueTurnoverGLFWopt = 0.5;
		/// <summary>
		/// Optimum value GLFwater for tissue turnover [0-1] - below this value tissue turnover increases
		/// </summary>
		/// <value>The tissue turnover GLF wopt.</value>
		[Description("Optimum value GLFwater for tissue turnover [0-1]")]
		[Units("0-1")]
		public double TissueTurnoverGLFWopt
		{
			get { return myTissueTurnoverGLFWopt; }
			set { myTissueTurnoverGLFWopt = value; }
		}

		/// <summary>Stock factor for increasing tissue turnover rate</summary>
		private double myStockParameter = 0.05;
		/// <summary>Stock factor for increasing tissue turnover rate</summary>
		/// <value>The stock parameter.</value>
		[XmlIgnore]
		[Units("-")]
		public double StockParameter
		{
			get { return myStockParameter; }
			set { myStockParameter = value; }
		}

		// - Digestibility values  ------------------------------------------------------------------------------------

		/// <summary>Digestibility of live plant material [0-1]</summary>
		private double myDigestibilityLive = 0.6;
		/// <summary>Digestibility of live plant material [0-1]</summary>
		/// <value>The digestibility live.</value>
		[Description("Digestibility of live plant material [0-1]:")]
		[Units("0-1")]
		public double DigestibilityLive
		{
			get { return myDigestibilityLive; }
			set { myDigestibilityLive = value; }
		}

		/// <summary>Digestibility of dead plant material [0-1]</summary>
		private double myDigestibilityDead = 0.2;
		/// <summary>Digestibility of dead plant material [0-1]</summary>
		/// <value>The digestibility dead.</value>
		[Description("Digestibility of dead plant material [0-1]:")]
		[Units("0-1")]
		public double DigestibilityDead
		{
			get { return myDigestibilityDead; }
			set { myDigestibilityDead = value; }
		}

		// - Minimum DM and preferences when harvesting  --------------------------------------------------------------

		/// <summary>Minimum above ground green DM [kg DM/ha]</summary>
		private double myMinimumGreenWt = 300.0;
		/// <summary>Minimum above ground green DM [kg DM/ha]</summary>
		/// <value>The minimum green DM weight.</value>
		[Description("Minimum above ground green DM [kg DM/ha]:")]
		[Units("kg/ha")]
		public double MinimumGreenWt
		{
			get { return myMinimumGreenWt; }
			set { myMinimumGreenWt = value; }
		}

		/// <summary>Minimum above ground dead DM [kg DM/ha]</summary>
		private double myMinimumDeadWt = 0.0;
		/// <summary>Minimum above ground dead DM [kg DM/ha]</summary>
		/// <value>The minimum dead DM weight.</value>
		[Description("Minimum above ground dead DM [kg DM/ha]")]
		[Units("kg/ha")]
		public double MinimumDeadWt
		{
			get { return myMinimumDeadWt; }
			set { myMinimumDeadWt = value; }
		}

		/// <summary>Preference for green DM during graze (weight factor)</summary>
		private double myPreferenceForGreenDM = 1.0;
		/// <summary>Preference for green DM during graze (weight factor)</summary>
		/// <value>The preference for green dm.</value>
		[Description("Preference for green DM during graze (weight factor):")]
		[Units("-")]
		public double PreferenceForGreenDM
		{
			get { return myPreferenceForGreenDM; }
			set { myPreferenceForGreenDM = value; }
		}

		/// <summary>Preference for dead DM during graze (weight factor)</summary>
		private double myPreferenceForDeadDM = 1.0;
		/// <summary>Preference for dead DM during graze (weight factor)</summary>
		/// <value>The preference for dead dm.</value>
		[Description("Preference for dead DM during graze (weight factor):")]
		[Units("-")]
		public double PreferenceForDeadDM
		{
			get { return myPreferenceForDeadDM; }
			set { myPreferenceForDeadDM = value; }
		}

		// - N concentration  -----------------------------------------------------------------------------------------

		/// <summary>Optimum N concentration in leaves [0-1]</summary>
		private double myLeafNopt = 0.04;
		/// <summary>Optimum N concentration in leaves [%]</summary>
		/// <value>The leaf nopt.</value>
		[Description("Optimum N concentration in young leaves [%]:")]
		[Units("%")]
		public double LeafNopt
		{
			get { return myLeafNopt * 100; }
			set { myLeafNopt = value / 100; }
		}

		/// <summary>Maximum N concentration in leaves (luxury N) [0-1]</summary>
		private double myLeafNmax = 0.05;
		/// <summary>Maximum N concentration in leaves (luxury N) [%]</summary>
		/// <value>The leaf nmax.</value>
		[Description("Maximum N concentration in leaves (luxury N) [%]:")]
		[Units("%")]
		public double LeafNmax
		{
			get { return myLeafNmax * 100; }
			set { myLeafNmax = value / 100; }
		}

		/// <summary>Minimum N concentration in leaves (dead material) [0-1]</summary>
		private double myLeafNmin = 0.012;
		/// <summary>Minimum N concentration in leaves (dead material) [%]</summary>
		/// <value>The leaf nmin.</value>
		[Description("Minimum N concentration in leaves (dead material) [%]:")]
		[Units("%")]
		public double LeafNmin
		{
			get { return myLeafNmin * 100; }
			set { myLeafNmin = value / 100; }
		}

		/// <summary>Concentration of N in stems relative to leaves [0-1]</summary>
		private double myRelativeNStems = 0.5;
		/// <summary>Concentration of N in stems relative to leaves [0-1]</summary>
		/// <value>The relative n stems.</value>
		[Description("Concentration of N in stems relative to leaves [0-1]:")]
		[Units("0-1")]
		public double RelativeNStems
		{
			get { return myRelativeNStems; }
			set { myRelativeNStems = value; }
		}

		/// <summary>Concentration of N in stolons relative to leaves [0-1]</summary>
		private double myRelativeNStolons = 0.0;
		/// <summary>Concentration of N in stolons relative to leaves [0-1]</summary>
		/// <value>The relative n stolons.</value>
		[Description("Concentration of N in stolons relative to leaves [0-1]:")]
		[Units("0-1")]
		public double RelativeNStolons
		{
			get { return myRelativeNStolons; }
			set { myRelativeNStolons = value; }
		}

		/// <summary>Concentration of N in roots relative to leaves [0-1]</summary>
		private double myRelativeNRoots = 0.5;
		/// <summary>Concentration of N in roots relative to leaves [0-1]</summary>
		/// <value>The relative n roots.</value>
		[Description("Concentration of N in roots relative to leaves [0-1]:")]
		[Units("0-1")]
		public double RelativeNRoots
		{
			get { return myRelativeNRoots; }
			set { myRelativeNRoots = value; }
		}

		/// <summary>Concentration of N in tissues at stage 2 relative to stage 1 [0-1]</summary>
		private double myRelativeNStage2 = 1.0;
		/// <summary>Concentration of N in tissues at stage 2 relative to stage 1 [0-1]</summary>
		/// <value>The relative n stage2.</value>
		[Description("Concentration of N in tissues at stage 2 relative to stage 1 [0-1]:")]
		[Units("0-1")]
		public double RelativeNStage2
		{
			get { return myRelativeNStage2; }
			set { myRelativeNStage2 = value; }
		}

		/// <summary>Concentration of N in tissues at stage 3 relative to stage 1 [0-1]</summary>
		private double myRelativeNStage3 = 1.0;
		/// <summary>Concentration of N in tissues at stage 3 relative to stage 1 [0-1]</summary>
		/// <value>The relative n stage3.</value>
		[Description("Concentration of N in tissues at stage 3 relative to stage 1 [0-1]:")]
		[Units("0-1")]
		public double RelativeNStage3
		{
			get { return myRelativeNStage3; }
			set { myRelativeNStage3 = value; }
		}

		// - N fixation  ----------------------------------------------------------------------------------------------

		/// <summary>Minimum fraction of N demand supplied by biologic N fixation [0-1]</summary>
		private double myMinimumNFixation = 0.0;
		/// <summary>Minimum fraction of N demand supplied by biologic N fixation [0-1]</summary>
		/// <value>The minimum n fixation.</value>
		[Description("Minimum fraction of N demand supplied by biologic N fixation [0-1]:")]
		[Units("0-1")]
		public double MinimumNFixation
		{
			get { return myMinimumNFixation; }
			set { myMinimumNFixation = value; }
		}

		/// <summary>Maximum fraction of N demand supplied by biologic N fixation [0-1]</summary>
		private double myMaximumNFixation = 0.0;
		/// <summary>Maximum fraction of N demand supplied by biologic N fixation [0-1]</summary>
		/// <value>The maximum n fixation.</value>
		[Description("Maximum fraction of N demand supplied by biologic N fixation [0-1]:")]
		[Units("0-1")]
		public double MaximumNFixation
		{
			get { return myMaximumNFixation; }
			set { myMaximumNFixation = value; }
		}

		// - Remobilisation and luxury N  -----------------------------------------------------------------------------

		/// <summary>Fraction of luxury N in tissue 2 available for remobilisation [0-1]</summary>
		private double myKappaNRemob2 = 0.0;
		/// <summary>Fraction of luxury N in tissue 2 available for remobilisation [0-1]</summary>
		/// <value>The kappa n remob2.</value>
		[Description("Fraction of luxury N in tissue 2 available for remobilisation [0-1]:")]
		[Units("0-1")]
		public double KappaNRemob2
		{
			get { return myKappaNRemob2; }
			set { myKappaNRemob2 = value; }
		}

		/// <summary>Fraction of luxury N in tissue 3 available for remobilisation [0-1]</summary>
		private double myKappaNRemob3 = 0.0;
		/// <summary>Fraction of luxury N in tissue 3 available for remobilisation [0-1]</summary>
		/// <value>The kappa n remob3.</value>
		[Description("Fraction of luxury N in tissue 3 available for remobilisation [0-1]:")]
		[Units("0-1")]
		public double KappaNRemob3
		{
			get { return myKappaNRemob3; }
			set { myKappaNRemob3 = value; }
		}

		/// <summary>Fraction of non-utilised remobilised N that is returned to dead material [0-1]</summary>
		private double myKappaNRemob4 = 0.0;
		/// <summary>Fraction of non-utilised remobilised N that is returned to dead material [0-1]</summary>
		/// <value>The kappa n remob4.</value>
		[Description("Fraction of non-utilised remobilised N that is returned to dead material [0-1]:")]
		[Units("0-1")]
		public double KappaNRemob4
		{
			get { return myKappaNRemob4; }
			set { myKappaNRemob4 = value; }
		}

		/// <summary>Fraction of senescent DM that is remobilised (as carbohydrate) [0-1]</summary>
		private double myKappaCRemob = 0.0;
		/// <summary>Fraction of senescent DM that is remobilised (as carbohydrate) [0-1]</summary>
		/// <value>The kappa c remob.</value>
		[XmlIgnore]
		[Units("0-1")]
		public double KappaCRemob
		{
			get { return myKappaCRemob; }
			set { myKappaCRemob = value; }
		}

		/// <summary>Fraction of senescent DM (protein) that is remobilised to new growth [0-1]</summary>
		private double myFacCNRemob = 0.0;
		/// <summary>Fraction of senescent DM (protein) that is remobilised to new growth [0-1]</summary>
		/// <value>The fac cn remob.</value>
		[XmlIgnore]
		[Units("0-1")]
		public double FacCNRemob
		{
			get { return myFacCNRemob; }
			set { myFacCNRemob = value; }
		}

		// - Effect of stress on growth  ------------------------------------------------------------------------------

		/// <summary>Curve parameter for the effect of N deficiency on plant growth</summary>
		private double myDillutionCoefN = 0.5;
		/// <summary>Curve parameter for the effect of N deficiency on plant growth</summary>
		/// <value>The dillution coef n.</value>
		[Description("Curve parameter for the effect of N deficiency on plant growth:")]
		[Units("-")]
		public double DillutionCoefN
		{
			get { return myDillutionCoefN; }
			set { myDillutionCoefN = value; }
		}

		/// <summary>Generic growth limiting factor [0-1]</summary>
		private double myGLFGeneric = 1.0;
		/// <summary>Gets or sets a generic growth limiting factor (arbitrary limitation).</summary>
		/// <value>The generic growth limiting factor.</value>
		/// <remarks> This factor is applied at same level as N, so it can be considered a nutrient limitation effect </remarks>
		[Description("Generic growth limiting factor [0-1]:")]
		[Units("0-1")]
		public double GlfGeneric
		{
			get { return myGLFGeneric; }
			set { myGLFGeneric = value; }
		}

		/// <summary>Exponent factor for the water stress function</summary>
		private double myWaterStressExponent = 1.0;
		/// <summary>Exponent factor for the water stress function</summary>
		/// <value>The water stress exponent.</value>
		[Description("Exponent factor for the water stress function:")]
		[Units("-")]
		public double WaterStressExponent
		{
			get { return myWaterStressExponent; }
			set { myWaterStressExponent = value; }
		}

		/// <summary>Maximum reduction in plant growth due to water logging (saturated soil) [0-1]</summary>
		private double myWaterLoggingCoefficient = 0.1;
		/// <summary>Maximum reduction in plant growth due to water logging (saturated soil) [0-1]</summary>
		/// <value>The water logging coefficient.</value>
		[Description("Maximum reduction in plant growth due to water logging (saturated soil) [0-1]:")]
		[Units("0-1")]
		public double WaterLoggingCoefficient
		{
			get { return myWaterLoggingCoefficient; }
			set { myWaterLoggingCoefficient = value; }
		}

		// - CO2 related  ---------------------------------------------------------------------------------------------

		/// <summary>Reference CO2 concentration for photosynthesis [ppm]</summary>
		private double myReferenceCO2 = 380.0;
		/// <summary>Reference CO2 concentration for photosynthesis [ppm]</summary>
		/// <value>The reference c o2.</value>
		[Description("Reference CO2 concentration for photosynthesis [ppm]:")]
		[Units("ppm")]
		public double ReferenceCO2
		{
			get { return myReferenceCO2; }
			set { myReferenceCO2 = value; }
		}

		/// <summary>
		/// Coefficient for the function describing the CO2 effect on photosynthesis [ppm CO2]
		/// </summary>
		private double myCoefficientCO2EffectOnPhotosynthesis = 700.0;
		/// <summary>
		/// Coefficient for the function describing the CO2 effect on photosynthesis [ppm CO2]
		/// </summary>
		/// <value>The coefficient c o2 effect on photosynthesis.</value>
		[Description("Coefficient for the function describing the CO2 effect on photosynthesis [ppm CO2]:")]
		[Units("ppm")]
		public double CoefficientCO2EffectOnPhotosynthesis
		{
			get { return myCoefficientCO2EffectOnPhotosynthesis; }
			set { myCoefficientCO2EffectOnPhotosynthesis = value; }
		}

		/// <summary>Scalling paramenter for the CO2 effects on N uptake [ppm Co2]</summary>
		private double myOffsetCO2EffectOnNuptake = 600.0;
		/// <summary>Scalling paramenter for the CO2 effects on N uptake [ppm Co2]</summary>
		/// <value>The offset c o2 effect on nuptake.</value>
		[Description("Scalling paramenter for the CO2 effects on N requirement [ppm Co2]:")]
		[Units("ppm")]
		public double OffsetCO2EffectOnNuptake
		{
			get { return myOffsetCO2EffectOnNuptake; }
			set { myOffsetCO2EffectOnNuptake = value; }
		}

		/// <summary>Minimum value for the effect of CO2 on N requirement [0-1]</summary>
		private double myMinimumCO2EffectOnNuptake = 0.7;
		/// <summary>Minimum value for the effect of CO2 on N requirement [0-1]</summary>
		/// <value>The minimum c o2 effect on nuptake.</value>
		[Description("Minimum value for the effect of CO2 on N requirement [0-1]:")]
		[Units("0-1")]
		public double MinimumCO2EffectOnNuptake
		{
			get { return myMinimumCO2EffectOnNuptake; }
			set { myMinimumCO2EffectOnNuptake = value; }
		}

		/// <summary>Exponent of the function describing the effect of CO2 on N requirement</summary>
		private double myExponentCO2EffectOnNuptake = 2.0;
		/// <summary>Exponent of the function describing the effect of CO2 on N requirement</summary>
		/// <value>The exponent c o2 effect on nuptake.</value>
		[Description("Exponent of the function describing the effect of CO2 on N requirement:")]
		[Units("-")]
		public double ExponentCO2EffectOnNuptake
		{
			get { return myExponentCO2EffectOnNuptake; }
			set { myExponentCO2EffectOnNuptake = value; }
		}

		// - Root distribution and height  ----------------------------------------------------------------------------

		/// <summary>Root distribution method (Homogeneous, ExpoLinear, UserDefined)</summary>
		private string myRootDistributionMethod = "ExpoLinear";
		/// <summary>Root distribution method (Homogeneous, ExpoLinear, UserDefined)</summary>
		/// <value>The root distribution method.</value>
		/// <exception cref="System.Exception">Root distribution method given ( + value +  is no valid</exception>
		[XmlIgnore]
		public string RootDistributionMethod
		{
			get { return myRootDistributionMethod; }
			set
			{
				switch (value.ToLower())
				{
					case "homogenous":
					case "userdefined":
					case "expolinear":
						myRootDistributionMethod = value;
						break;
					default:
						throw new Exception("Root distribution method given (" + value + " is no valid");
				}
			}
		}

		/// <summary>Fraction of root depth where its proportion starts to decrease</summary>
		private double myExpoLinearDepthParam = 0.12;
		/// <summary>Fraction of root depth where its proportion starts to decrease</summary>
		/// <value>The expo linear depth parameter.</value>
		[Description("Fraction of root depth where its proportion starts to decrease")]
		public double ExpoLinearDepthParam
		{
			get { return myExpoLinearDepthParam; }
			set
			{
				myExpoLinearDepthParam = value;
				if (myExpoLinearDepthParam == 1.0)
					myRootDistributionMethod = "Homogeneous";
			}
		}

		/// <summary>Exponent to determine mass distribution in the soil profile</summary>
		private double myExpoLinearCurveParam = 3.2;
		/// <summary>Exponent to determine mass distribution in the soil profile</summary>
		/// <value>The expo linear curve parameter.</value>
		[Description("Exponent to determine mass distribution in the soil profile")]
		public double ExpoLinearCurveParam
		{
			get { return myExpoLinearCurveParam; }
			set
			{
				myExpoLinearCurveParam = value;
				if (myExpoLinearCurveParam == 0.0)
					myRootDistributionMethod = "Homogeneous";	// It is impossible to solve, but its limit is a homogeneous distribution 
			}
		}

		// - Other parameters  ----------------------------------------------------------------------------------------
		/// <summary>Broken stick type function describing how plant height varies with DM</summary>
		[XmlIgnore]
		public BrokenStick HeightFromMass = new BrokenStick
		{
			X = new double[5] { 0, 1000, 2000, 3000, 4000 },
			Y = new double[5] { 0, 25, 75, 150, 250 }
		};

		/// <summary>The FVPD function</summary>
		[XmlIgnore]
		public BrokenStick FVPDFunction = new BrokenStick
		{
			X = new double[3] { 0.0, 10.0, 50.0 },
			Y = new double[3] { 1.0, 1.0, 1.0 }
		};

		/// <summary>Flag which module will perform the water uptake process</summary>
		internal string myWaterUptakeSource = "species";
		/// <summary>Flag whether the alternative water uptake process will be used</summary>
		internal string useAltWUptake = "no";
		/// <summary>Reference value of Ksat for water availability function</summary>
		internal double ReferenceKSuptake = 1000.0;
		/// <summary>Flag which module will perform the nitrogen uptake process</summary>
		internal string myNitrogenUptakeSource = "species";
		/// <summary>Flag whether the alternative nitrogen uptake process will be used</summary>
		internal string useAltNUptake = "no";
		/// <summary>Availability factor for NH4</summary>
		internal double kuNH4 = 0.50;
		/// <summary>Availability factor for NO3</summary>
		internal double kuNO3 = 0.95;
		/// <summary>Reference value for root length density fot the Water and N availability</summary>
		internal double ReferenceRLD = 2.0;

		/// <summary>the local value for stomatal conductance</summary>
		private double myStomatalConductanceMax = 1.0;
		/// <summary>The value for the maximum stomatal conductance (m/s)</summary>
		/// <value>Maximum stomatal conductance.</value>
		[Description("Maximum stomatal conductance (m/s)")]
		public double MaximumStomatalConductance
		{
			get { return myStomatalConductanceMax; }
			set { myStomatalConductanceMax = value; }
		}

		/// <summary>the local value for KNO3</summary>
		private double myKNO3 = 1.0;
		/// <summary>The value for the nitrate uptake coefficient</summary>
		/// <value>The kNO3 for this plant.</value>
		[Description("Nitrate uptake coefficient")]
		public double KNO3
		{
			get { return myKNO3; }
			set { myKNO3 = value; }
		}

		/// <summary>the local value for KNH4</summary>
		private double myKNH4 = 1.0;
		/// <summary>The value for the ammonium uptake coefficient</summary>
		/// <value>The local kNH4 for this plant.</value>
		[Description("Ammonium uptake coefficient")]
		public double KNH4
		{
			get { return myKNH4; }
			set { myKNH4 = value; }
		}

		#endregion

		#region Model outputs  ---------------------------------------------------------------------------------------------

		/// <summary>
		/// Is the plant alive?
		/// </summary>
		public bool IsAlive
		{
			get { return PlantStatus == "alive"; }
		}

		/// <summary>Gets the plant status.</summary>
		/// <value>The plant status (dead, alive, etc).</value>
		[Description("Plant status (dead, alive, etc)")]
		[Units("")]
		public string PlantStatus
		{
			get
			{
				if (myIsAlive)
					return "alive";
				else
					return "out";
			}
		}

		/// <summary>Gets the index for the plant development stage.</summary>
		/// <value>The stage index.</value>
		[Description("Plant development stage number")]
		[Units("")]
		public int Stage
		{
			get
			{
				if (myIsAlive)
				{
					if (myPhenoStage == 0)
						return 1;    //"sowing & germination";
					else
						return 3;    //"emergence" & "reproductive";
				}
				else
					return 0;
			}
		}

		/// <summary>Gets the name of the plant development stage.</summary>
		/// <value>The name of the stage.</value>
		[Description("Plant development stage name")]
		[Units("")]
		public string StageName
		{
			get
			{
				if (myIsAlive)
				{
					if (myPhenoStage == 0)
						return "sowing";
					else
						return "emergence";
				}
				else
					return "out";
			}
		}

		#region - DM and C amounts  ----------------------------------------------------------------------------------------

		/// <summary>Gets the total plant C content.</summary>
		/// <value>The plant C content.</value>
		[Description("Total amount of C in plants")]
		[Units("kgDM/ha")]
		public double TotalC
		{
			get { return TotalWt * CarbonFractionInDM; }
		}

		/// <summary>Gets the plant total dry matter weight.</summary>
		/// <value>The total DM weight.</value>
		[Description("Total plant dry matter weight")]
		[Units("kgDM/ha")]
		public double TotalWt
		{
			get { return myDMShoot + myDMRoot; }
		}

		/// <summary>Gets the plant DM weight above ground.</summary>
		/// <value>The above ground DM weight.</value>
		[Description("Dry matter weight above ground")]
		[Units("kgDM/ha")]
		public double AboveGroundWt
		{
			get { return myDMShoot; }
		}

		/// <summary>Gets the DM weight of live plant parts above ground.</summary>
		/// <value>The above ground DM weight of live plant parts.</value>
		[Description("Dry matter weight of alive plants above ground")]
		[Units("kgDM/ha")]
		public double AboveGrounLivedWt
		{
			get { return myDMShootGreen; }
		}

		/// <summary>Gets the DM weight of dead plant parts above ground.</summary>
		/// <value>The above ground dead DM weight.</value>
		[Description("Dry matter weight of dead plants above ground")]
		[Units("kgDM/ha")]
		public double AboveGroundDeadWt
		{
			get { return myDMShootDead; }
		}

		/// <summary>Gets the DM weight of the plant below ground.</summary>
		/// <value>The below ground DM weight of plant.</value>
		[Description("Dry matter weight below ground")]
		[Units("kgDM/ha")]
		public double BelowGroundWt
		{
			get { return myDMRoot; }
		}

		/// <summary>Gets the total standing DM weight.</summary>
		/// <value>The DM weight of leaves and stems.</value>
		[Description("Dry matter weight of standing herbage")]
		[Units("kgDM/ha")]
		public double StandingWt
		{
			get { return myLeaves.DMTotal + myStems.DMTotal; }
		}

		/// <summary>Gets the DM weight of standing live plant material.</summary>
		/// <value>The DM weight of live leaves and stems.</value>
		[Description("Dry matter weight of live standing plants parts")]
		[Units("kgDM/ha")]
		public double StandingLiveWt
		{
			get { return myLeaves.DMGreen + myStems.DMGreen; }
		}

		/// <summary>Gets the DM weight of standing dead plant material.</summary>
		/// <value>The DM weight of dead leaves and stems.</value>
		[Description("Dry matter weight of dead standing plants parts")]
		[Units("kgDM/ha")]
		public double StandingDeadWt
		{
			get { return myLeaves.tissue[3].DM + myStems.tissue[3].DM; }
		}

		/// <summary>Gets the total DM weight of leaves.</summary>
		/// <value>The leaf DM weight.</value>
		[Description("Dry matter weight of leaves")]
		[Units("kgDM/ha")]
		public double LeafWt
		{
			get { return myLeaves.DMTotal; }
		}

		/// <summary>Gets the DM weight of green leaves.</summary>
		/// <value>The green leaf DM weight.</value>
		[Description("Dry matter weight of live leaves")]
		[Units("kgDM/ha")]
		public double LeafGreenWt
		{
			get { return myLeaves.DMGreen; }
		}

		/// <summary>Gets the DM weight of dead leaves.</summary>
		/// <value>The dead leaf DM weight.</value>
		[Description("Dry matter weight of dead leaves")]
		[Units("kgDM/ha")]
		public double LeafDeadWt
		{
			get { return myLeaves.tissue[3].DM; }
		}

		/// <summary>Gets the toal DM weight of stems and sheath.</summary>
		/// <value>The stem DM weight.</value>
		[Description("Dry matter weight of stems and sheath")]
		[Units("kgDM/ha")]
		public double StemWt
		{
			get { return myStems.DMTotal; }
		}

		/// <summary>Gets the DM weight of live stems and sheath.</summary>
		/// <value>The live stems DM weight.</value>
		[Description("Dry matter weight of alive stems and sheath")]
		[Units("kgDM/ha")]
		public double StemGreenWt
		{
			get { return myStems.DMGreen; }
		}

		/// <summary>Gets the DM weight of dead stems and sheath.</summary>
		/// <value>The dead stems DM weight.</value>
		[Description("Dry matter weight of dead stems and sheath")]
		[Units("kgDM/ha")]
		public double StemDeadWt
		{
			get { return myStems.tissue[3].DM; }
		}

		/// <summary>Gets the total DM weight od stolons.</summary>
		/// <value>The stolon DM weight.</value>
		[Description("Dry matter weight of stolons")]
		[Units("kgDM/ha")]
		public double StolonWt
		{
			get { return myStolons.DMGreen; }
		}

		/// <summary>Gets the total DM weight of roots.</summary>
		/// <value>The root DM weight.</value>
		[Description("Dry matter weight of roots")]
		[Units("kgDM/ha")]
		public double RootWt
		{
			get { return myRoots.DMGreen; }
		}

		/// <summary>Gets the DM weight of leaves at stage1 (developing).</summary>
		/// <value>The stage1 leaf DM weight.</value>
		[Description("Dry matter weight of leaves at stage 1 (developing)")]
		[Units("kgDM/ha")]
		public double LeafStage1Wt
		{
			get
			{ return myLeaves.tissue[0].DM; }
		}

		/// <summary>Gets the DM weight of leaves stage2 (mature).</summary>
		/// <value>The stage2 leaf DM weight.</value>
		[Description("Dry matter weight of leaves at stage 2 (mature)")]
		[Units("kgDM/ha")]
		public double LeafStage2Wt
		{
			get { return myLeaves.tissue[1].DM; }
		}

		/// <summary>Gets the DM weight of leaves at stage3 (senescing).</summary>
		/// <value>The stage3 leaf DM weight.</value>
		[Description("Dry matter weight of leaves at stage 3 (senescing)")]
		[Units("kgDM/ha")]
		public double LeafStage3Wt
		{
			get { return myLeaves.tissue[2].DM; }
		}

		/// <summary>Gets the DM weight of leaves at stage4 (dead).</summary>
		/// <value>The stage4 leaf DM weight.</value>
		[Description("Dry matter weight of leaves at stage 4 (dead)")]
		[Units("kgDM/ha")]
		public double LeafStage4Wt
		{
			get { return myLeaves.tissue[3].DM; }
		}

		/// <summary>Gets the DM weight stems and sheath at stage1 (developing).</summary>
		/// <value>The stage1 stems DM weight.</value>
		[Description("Dry matter weight of stems at stage 1 (developing)")]
		[Units("kgDM/ha")]
		public double StemStage1Wt
		{
			get { return myStems.tissue[0].DM; }
		}

		/// <summary>Gets the DM weight of stems and sheath at stage2 (mature).</summary>
		/// <value>The stage2 stems DM weight.</value>
		[Description("Dry matter weight of stems at stage 2 (mature)")]
		[Units("kgDM/ha")]
		public double StemStage2Wt
		{
			get { return myStems.tissue[1].DM; }
		}

		/// <summary>Gets the DM weight of stems and sheath at stage3 (senescing)).</summary>
		/// <value>The stage3 stems DM weight.</value>
		[Description("Dry matter weight of stems at stage 3 (senescing)")]
		[Units("kgDM/ha")]
		public double StemStage3Wt
		{
			get { return myStems.tissue[2].DM; }
		}

		/// <summary>Gets the DM weight of stems and sheath at stage4 (dead).</summary>
		/// <value>The stage4 stems DM weight.</value>
		[Description("Dry matter weight of stems at stage 4 (dead)")]
		[Units("kgDM/ha")]
		public double StemStage4Wt
		{
			get { return myStems.tissue[3].DM; }
		}

		/// <summary>Gets the DM weight of stolons at stage1 (developing).</summary>
		/// <value>The stage1 stolon DM weight.</value>
		[Description("Dry matter weight of stolons at stage 1 (developing)")]
		[Units("kgDM/ha")]
		public double StolonStage1Wt
		{
			get { return myStolons.tissue[0].DM; }
		}

		/// <summary>Gets the DM weight of stolons at stage2 (mature).</summary>
		/// <value>The stage2 stolon DM weight.</value>
		[Description("Dry matter weight of stolons at stage 2 (mature)")]
		[Units("kgDM/ha")]
		public double StolonStage2Wt
		{
			get { return myStolons.tissue[1].DM; }
		}

		/// <summary>Gets the DM weight of stolons at stage3 (senescing).</summary>
		/// <value>The stage3 stolon DM weight.</value>
		[Description("Dry matter weight of stolons at stage 3 (senescing)")]
		[Units("kgDM/ha")]
		public double StolonStage3Wt
		{
			get { return myStolons.tissue[2].DM; }
		}

		#endregion

		#region - C and DM flows  ------------------------------------------------------------------------------------------

		/// <summary>Gets the potential carbon assimilation.</summary>
		/// <value>The potential carbon assimilation.</value>
		[Description("Potential C assimilation, corrected for extreme temperatures")]
		[Units("kgC/ha")]
		public double PotCarbonAssimilation
		{
			get { return myPgross; }
		}

		/// <summary>Gets the carbon loss via respiration.</summary>
		/// <value>The carbon loss via respiration.</value>
		[Description("Loss of C via respiration")]
		[Units("kgC/ha")]
		public double CarbonLossRespiration
		{
			get { return myResp_m; }
		}

		/// <summary>Gets the carbon remobilised from senescent tissue.</summary>
		/// <value>The carbon remobilised.</value>
		[Description("C remobilised from senescent tissue")]
		[Units("kgC/ha")]
		public double CarbonRemobilised
		{
			get { return myCRemobilised; }
		}

		/// <summary>Gets the gross potential growth rate.</summary>
		/// <value>The potential C assimilation, in DM equivalent.</value>
		[Description("Gross potential growth rate (potential C assimilation)")]
		[Units("kgDM/ha")]
		public double GrossPotentialGrowthWt
		{
			get { return myPgross / CarbonFractionInDM; }
		}

		/// <summary>Gets the respiration rate.</summary>
		/// <value>The loss of C due to respiration, in DM equivalent.</value>
		[Description("Respiration rate (DM lost via respiration)")]
		[Units("kgDM/ha")]
		public double RespirationWt
		{
			get { return myResp_m / CarbonFractionInDM; }
		}

		/// <summary>Gets the remobilisation rate.</summary>
		/// <value>The C remobilised, in DM equivalent.</value>
		[Description("C remobilisation (DM remobilised from old tissue to new growth)")]
		[Units("kgDM/ha")]
		public double RemobilisationWt
		{
			get { return myCRemobilised / CarbonFractionInDM; }
		}

		/// <summary>Gets the net potential growth rate.</summary>
		/// <value>The net potential growth rate.</value>
		[Description("Net potential growth rate")]
		[Units("kgDM/ha")]
		public double NetPotentialGrowthWt
		{
			get { return myDGrowthPot; }
		}

		/// <summary>Gets the potential growth rate after water stress.</summary>
		/// <value>The potential growth after water stress.</value>
		[Description("Potential growth rate after water stress")]
		[Units("kgDM/ha")]
		public double PotGrowthWt_Wstress
		{
			get { return myDGrowthWstress; }
		}

		/// <summary>Gets the actual growth rate.</summary>
		/// <value>The actual growth rate.</value>
		[Description("Actual growth rate, after nutrient stress")]
		[Units("kgDM/ha")]
		public double ActualGrowthWt
		{
			get { return myDGrowthActual; }
		}

		/// <summary>Gets the effective growth rate.</summary>
		/// <value>The effective growth rate.</value>
		[Description("Effective growth rate, after turnover")]
		[Units("kgDM/ha")]
		public double EffectiveGrowthWt
		{
			get { return myDGrowthEff; }
		}

		/// <summary>Gets the effective herbage growth rate.</summary>
		/// <value>The herbage growth rate.</value>
		[Description("Effective herbage growth rate, above ground")]
		[Units("kgDM/ha")]
		public double HerbageGrowthWt
		{
			get { return myDGrowthShoot; }
		}

		/// <summary>Gets the effective root growth rate.</summary>
		/// <value>The root growth DM weight.</value>
		[Description("Effective root growth rate")]
		[Units("kgDM/ha")]
		public double RootGrowthWt
		{
			get { return myDGrowthRoot; }
		}

		/// <summary>Gets the litter DM weight deposited onto soil surface.</summary>
		/// <value>The litter DM weight deposited.</value>
		[Description("Litter amount deposited onto soil surface")]
		[Units("kgDM/ha")]
		public double LitterWt
		{
			get { return myDLitter; }
		}

		/// <summary>Gets the senesced root DM weight.</summary>
		/// <value>The senesced root DM weight.</value>
		[Description("Amount of senesced roots added to soil FOM")]
		[Units("kgDM/ha")]
		public double RootSenescedWt
		{
			get { return myDRootSen; }
		}

		/// <summary>Gets the gross primary productivity.</summary>
		/// <value>The gross primary productivity.</value>
		[Description("Gross primary productivity")]
		[Units("kgDM/ha")]
		public double GPP
		{
			get { return myPgross / CarbonFractionInDM; }
		}

		/// <summary>Gets the net primary productivity.</summary>
		/// <value>The net primary productivity.</value>
		[Description("Net primary productivity")]
		[Units("kgDM/ha")]
		public double NPP
		{
			get { return (myPgross * (1 - myGrowthRespirationCoef) - myResp_m) / CarbonFractionInDM; }
		}

		/// <summary>Gets the net above-ground primary productivity.</summary>
		/// <value>The net above-ground primary productivity.</value>
		[Description("Net above-ground primary productivity")]
		[Units("kgDM/ha")]
		public double NAPP
		{
			get { return (myPgross * (1 - myGrowthRespirationCoef) - myResp_m) * myFShoot / CarbonFractionInDM; }
		}

		/// <summary>Gets the net below-ground primary productivity.</summary>
		/// <value>The net below-ground primary productivity.</value>
		[Description("Net below-ground primary productivity")]
		[Units("kgDM/ha")]
		public double NBPP
		{
			get { return (myPgross * (1 - myGrowthRespirationCoef) - myResp_m) * (1 - myFShoot) / CarbonFractionInDM; }
		}
		#endregion

		#region - N amounts  -----------------------------------------------------------------------------------------------

		/// <summary>Gets the plant total N content.</summary>
		/// <value>The total N content.</value>
		[Description("Total plant N amount")]
		[Units("kgN/ha")]
		public double TotalN
		{
			get { return myNShoot + myNRoot; }
		}

		/// <summary>Gets the N content in the plant above ground.</summary>
		/// <value>The above ground N content.</value>
		[Description("N amount of plant parts above ground")]
		[Units("kgN/ha")]
		public double AboveGroundN
		{
			get { return myNShoot; }
		}

		/// <summary>Gets the N content in live plant material above ground.</summary>
		/// <value>The N content above ground of live plants.</value>
		[Description("N amount of alive plant parts above ground")]
		[Units("kgN/ha")]
		public double AboveGroundLiveN
		{
			get { return myLeaves.NGreen+myStems.NGreen+myStolons.NGreen; }
		}

		/// <summary>Gets the N content of dead plant material above ground.</summary>
		/// <value>The N content above ground of dead plants.</value>
		[Description("N amount of dead plant parts above ground")]
		[Units("kgN/ha")]
		public double AboveGroundDeadN
		{
			get { return myLeaves.tissue[3].Namount+ myStems.tissue[3].Namount; }
		}

		/// <summary>Gets the N content of plants below ground.</summary>
		/// <value>The below ground N content.</value>
		[Description("N amount of plant parts below ground")]
		[Units("kgN/ha")]
		public double BelowGroundN
		{
			get { return myNRoot; }
		}

		/// <summary>Gets the N content of standing plants.</summary>
		/// <value>The N content of leaves and stems.</value>
		[Description("N amount of standing herbage")]
		[Units("kgN/ha")]
		public double StandingN
		{
			get { return myLeaves.DMTotal+myStems.DMTotal; }
		}

		/// <summary>Gets the N content of standing live plant material.</summary>
		/// <value>The N content of live leaves and stems.</value>
		[Description("N amount of alive standing herbage")]
		[Units("kgN/ha")]
		public double StandingLiveN
		{
			get { return myLeaves.DMGreen + myStems.DMGreen; }
		}

		/// <summary>Gets the N content  of standing dead plant material.</summary>
		/// <value>The N content of dead leaves and stems.</value>
		[Description("N amount of dead standing herbage")]
		[Units("kgN/ha")]
		public double StandingDeadN
		{
			get { return myLeaves.tissue[3].Namount+myStems.tissue[3].Namount; }
		}

		/// <summary>Gets the total N content of leaves.</summary>
		/// <value>The leaf N content.</value>
		[Description("N amount in the plant's leaves")]
		[Units("kgN/ha")]
		public double LeafN
		{
			get { return myLeaves.NTotal; }
		}

		/// <summary>Gets the total N content of stems and sheath.</summary>
		/// <value>The stem N content.</value>
		[Description("N amount in the plant's stems")]
		[Units("kgN/ha")]
		public double StemN
		{
			get { return myStems.NTotal; }
		}

		/// <summary>Gets the total N content of stolons.</summary>
		/// <value>The stolon N content.</value>
		[Description("N amount in the plant's stolons")]
		[Units("kgN/ha")]
		public double StolonN
		{
			get { return myStolons.NGreen; }
		}

		/// <summary>Gets the total N content of roots.</summary>
		/// <value>The root N content.</value>
		[Description("N amount in the plant's roots")]
		[Units("kgN/ha")]
		public double RootN
		{
			get { return myNRoot; }
		}

		/// <summary>Gets the N content of green leaves.</summary>
		/// <value>The green leaf N content.</value>
		[Description("N amount in alive leaves")]
		[Units("kgN/ha")]
		public double LeafGreenN
		{
			get { return myLeaves.NGreen; }
		}

		/// <summary>Gets the N content of dead leaves.</summary>
		/// <value>The dead leaf N content.</value>
		[Description("N amount in dead leaves")]
		[Units("kgN/ha")]
		public double LeafDeadN
		{
			get { return myLeaves.tissue[3].Namount; }
		}

		/// <summary>Gets the N content of green stems and sheath.</summary>
		/// <value>The green stem N content.</value>
		[Description("N amount in alive stems")]
		[Units("kgN/ha")]
		public double StemGreenN
		{
			get { return myStems.NGreen; }
		}

		/// <summary>Gets the N content  of dead stems and sheath.</summary>
		/// <value>The dead stem N content.</value>
		[Description("N amount in dead sytems")]
		[Units("kgN/ha")]
		public double StemDeadN
		{
			get { return myStems.tissue[3].Namount; }
		}

		/// <summary>Gets the N content of leaves at stage1 (developing).</summary>
		/// <value>The stage1 leaf N.</value>
		[Description("N amount in leaves at stage 1 (developing)")]
		[Units("kgN/ha")]
		public double LeafStage1N
		{
			get { return myLeaves.tissue[0].Namount; }
		}

		/// <summary>Gets the N content of leaves at stage2 (mature).</summary>
		/// <value>The stage2 leaf N.</value>
		[Description("N amount in leaves at stage 2 (mature)")]
		[Units("kgN/ha")]
		public double LeafStage2N
		{
			get { return myLeaves.tissue[1].Namount; }
		}

		/// <summary>Gets the N content of leaves at stage3 (senescing).</summary>
		/// <value>The stage3 leaf N.</value>
		[Description("N amount in leaves at stage 3 (senescing)")]
		[Units("kgN/ha")]
		public double LeafStage3N
		{
			get { return myLeaves.tissue[2].Namount; }
		}

		/// <summary>Gets the N content of leaves at stage4 (dead).</summary>
		/// <value>The stage4 leaf N.</value>
		[Description("N amount in leaves at stage 4 (dead)")]
		[Units("kgN/ha")]
		public double LeafStage4N
		{
			get { return myLeaves.tissue[3].Namount; }
		}

		/// <summary>Gets the N content of stems and sheath at stage1 (developing).</summary>
		/// <value>The stage1 stem N.</value>
		[Description("N amount in stems at stage 1 (developing)")]
		[Units("kgN/ha")]
		public double StemStage1N
		{
			get { return myStems.tissue[0].Namount; }
		}

		/// <summary>Gets the N content of stems and sheath at stage2 (mature).</summary>
		/// <value>The stage2 stem N.</value>
		[Description("N amount in stems at stage 2 (mature)")]
		[Units("kgN/ha")]
		public double StemStage2N
		{
			get { return myStems.tissue[1].Namount; }
		}

		/// <summary>Gets the N content of stems and sheath at stage3 (senescing).</summary>
		/// <value>The stage3 stem N.</value>
		[Description("N amount in stems at stage 3 (senescing)")]
		[Units("kgN/ha")]
		public double StemStage3N
		{
			get { return myStems.tissue[2].Namount; }
		}

		/// <summary>Gets the N content of stems and sheath at stage4 (dead).</summary>
		/// <value>The stage4 stem N.</value>
		[Description("N amount in stems at stage 4 (dead)")]
		[Units("kgN/ha")]
		public double StemStage4N
		{
			get { return myStems.tissue[3].Namount; }
		}

		/// <summary>Gets the N content of stolons at stage1 (developing).</summary>
		/// <value>The stage1 stolon N.</value>
		[Description("N amount in stolons at stage 1 (developing)")]
		[Units("kgN/ha")]
		public double StolonStage1N
		{
			get { return myStolons.tissue[0].Namount; }
		}

		/// <summary>Gets the N content of stolons at stage2 (mature).</summary>
		/// <value>The stage2 stolon N.</value>
		[Description("N amount in stolons at stage 2 (mature)")]
		[Units("kgN/ha")]
		public double StolonStage2N
		{
			get { return myStolons.tissue[1].Namount; }
		}

		/// <summary>Gets the N content of stolons as stage3 (senescing).</summary>
		/// <value>The stolon stage3 n.</value>
		[Description("N amount in stolons at stage 3 (senescing)")]
		[Units("kgN/ha")]
		public double StolonStage3N
		{
			get { return myStolons.tissue[1].Namount; }
		}

		#endregion

		#region - N concentrations  ----------------------------------------------------------------------------------------

		/// <summary>Gets the average N concentration of standing plant material.</summary>
		/// <value>The average N concentration of leaves and stems.</value>
		[Description("Average N concentration in standing plant parts")]
		[Units("kgN/kgDM")]
		public double StandingNConc
		{
			get { return MathUtilities.Divide(StandingN, StandingWt, 0.0); }
		}

		/// <summary>Gets the average N concentration of leaves.</summary>
		/// <value>The leaf N concentration.</value>
		[Description("Average N concentration in leaves")]
		[Units("kgN/kgDM")]
		public double LeafNConc
		{
			get { return myLeaves.NconcTotal; }
		}

		/// <summary>Gets the average N concentration of stems and sheath.</summary>
		/// <value>The stem N concentration.</value>
		[Description("Average N concentration in stems")]
		[Units("kgN/kgDM")]
		public double StemNConc
		{
			get { return myStems.NconcTotal; }
		}

		/// <summary>Gets the average N concentration of stolons.</summary>
		/// <value>The stolon N concentration.</value>
		[Description("Average N concentration in stolons")]
		[Units("kgN/kgDM")]
		public double StolonNConc
		{
			get { return myStolons.NconcTotal; }
		}

		/// <summary>Gets the average N concentration of roots.</summary>
		/// <value>The root N concentration.</value>
		[Description("Average N concentration in roots")]
		[Units("kgN/kgDM")]
		public double RootNConc
		{
			get { return myRoots.NconcTotal; }
		}

		/// <summary>Gets the N concentration of leaves at stage1 (developing).</summary>
		/// <value>The stage1 leaf N concentration.</value>
		[Description("N concentration of leaves at stage 1 (developing)")]
		[Units("kgN/kgDM")]
		public double LeafStage1NConc
		{
			get { return myLeaves.tissue[0].Nconc; }
		}

		/// <summary>Gets the N concentration of leaves at stage2 (mature).</summary>
		/// <value>The stage2 leaf N concentration.</value>
		[Description("N concentration of leaves at stage 2 (mature)")]
		[Units("kgN/kgDM")]
		public double LeafStage2NConc
		{
			get { return myLeaves.tissue[1].Nconc; }
		}

		/// <summary>Gets the N concentration of leaves at stage3 (senescing).</summary>
		/// <value>The stage3 leaf N concentration.</value>
		[Description("N concentration of leaves at stage 3 (senescing)")]
		[Units("kgN/kgDM")]
		public double LeafStage3NConc
		{
			get { return myLeaves.tissue[2].Nconc; }
		}

		/// <summary>Gets the N concentration of leaves at stage4 (dead).</summary>
		/// <value>The stage4 leaf N concentration.</value>
		[Description("N concentration of leaves at stage 4 (dead)")]
		[Units("kgN/kgDM")]
		public double LeafStage4NConc
		{
			get { return myLeaves.tissue[3].Nconc; }
		}

		/// <summary>Gets the N concentration of stems at stage1 (developing).</summary>
		/// <value>The stage1 stem N concentration.</value>
		[Description("N concentration of stems at stage 1 (developing)")]
		[Units("kgN/kgDM")]
		public double StemStage1NConc
		{
			get { return myStems.tissue[0].Nconc; }
		}

		/// <summary>Gets the N concentration of stems at stage2 (mature).</summary>
		/// <value>The stage2 stem N concentration.</value>
		[Description("N concentration of stems at stage 2 (mature)")]
		[Units("kgN/kgDM")]
		public double StemStage2NConc
		{
			get { return myStems.tissue[1].Nconc; }
		}

		/// <summary>Gets the N concentration of stems at stage3 (senescing).</summary>
		/// <value>The stage3 stem N concentration.</value>
		[Description("N concentration of stems at stage 3 (senescing)")]
		[Units("kgN/kgDM")]
		public double StemStage3NConc
		{
			get { return myStems.tissue[2].Nconc; }
		}

		/// <summary>Gets the N concentration of stems at stage4 (dead).</summary>
		/// <value>The stage4 stem N concentration.</value>
		[Description("N concentration of stems at stage 4 (dead)")]
		[Units("kgN/kgDM")]
		public double StemStage4NConc
		{
			get { return myStems.tissue[3].Nconc; }
		}

		/// <summary>Gets the N concentration of stolons at stage1 (developing).</summary>
		/// <value>The stage1 stolon N concentration.</value>
		[Description("N concentration of stolons at stage 1 (developing)")]
		[Units("kgN/kgDM")]
		public double StolonStage1NConc
		{
			get { return myStolons.tissue[0].Nconc; }
		}

		/// <summary>Gets the N concentration of stolons at stage2 (mature).</summary>
		/// <value>The stage2 stolon N concentration.</value>
		[Description("N concentration of stolons at stage 2 (mature)")]
		[Units("kgN/kgDM")]
		public double StolonStage2NConc
		{
			get { return myStolons.tissue[1].Nconc; }
		}

		/// <summary>Gets the N concentration of stolons at stage3 (senescing).</summary>
		/// <value>The stage3 stolon N concentration.</value>
		[Description("N concentration of stolons at stage 3 (senescing)")]
		[Units("kgN/kgDM")]
		public double StolonStage3NConc
		{
			get { return myStolons.tissue[2].Nconc; }
		}

		/// <summary>Gets the N concentration in new grown tissue.</summary>
		/// <value>The actual growth N concentration.</value>
		[Description("Concentration of N in new growth")]
		[Units("kgN/kgDM")]
		public double ActualGrowthNConc
		{
			get { return MathUtilities.Divide(myNewGrowthN, myDGrowthActual, 0.0); }
		}


		#endregion

		#region - N flows  -------------------------------------------------------------------------------------------------

		/// <summary>Gets amount of N remobilisable from senesced tissue.</summary>
		/// <value>The remobilisable N amount.</value>
		[Description("Amount of N remobilisable from senesced material")]
		[Units("kgN/ha")]
		public double RemobilisableN
		{
			get { return myNRemobilised; }
		}

		/// <summary>Gets the amount of N remobilised from senesced tissue.</summary>
		/// <value>The remobilised N amount.</value>
		[Description("Amount of N remobilised from senesced material")]
		[Units("kgN/ha")]
		public double RemobilisedN
		{
			get { return myNremob2NewGrowth; }
		}

		/// <summary>Gets the amount of luxury N potentially remobilisable.</summary>
		/// <value>The remobilisable luxury N amount.</value>
		[Description("Amount of luxury N potentially remobilisable")]
		[Units("kgN/ha")]
		public double RemobilisableLuxuryN
		{
			get { return myNLuxury2 + myNLuxury3; }
		}

		/// <summary>Gets the amount of luxury N remobilised.</summary>
		/// <value>The remobilised luxury N amount.</value>
		[Description("Amount of luxury N remobilised")]
		[Units("kgN/ha")]
		public double RemobilisedLuxuryN
		{
			get { return myNFastRemob2 + myNFastRemob3; }
		}

		/// <summary>Gets the amount of luxury N potentially remobilisable from tissue 2.</summary>
		/// <value>The remobilisable luxury N amoount.</value>
		[Description("Amount of luxury N potentially remobilisable from tissue 2")]
		[Units("kgN/ha")]
		public double RemobT2LuxuryN
		{
			get { return myNLuxury2; }
		}

		/// <summary>Gets the amount of luxury N potentially remobilisable from tissue 3.</summary>
		/// <value>The remobilisable luxury N amount.</value>
		[Description("Amount of luxury N potentially remobilisable from tissue 3")]
		[Units("kgN/ha")]
		public double RemobT3LuxuryN
		{
			get { return myNLuxury3; }
		}

		/// <summary>Gets the amount of atmospheric N fixed.</summary>
		/// <value>The fixed N amount.</value>
		[Description("Amount of atmospheric N fixed")]
		[Units("kgN/ha")]
		public double FixedN
		{
			get { return myNfixation; }
		}

		/// <summary>Gets the amount of N required with luxury uptake.</summary>
		/// <value>The required N with luxury.</value>
		[Description("Amount of N required with luxury uptake")]
		[Units("kgN/ha")]
		public double RequiredLuxuryN
		{
			get { return myNdemandLux; }
		}

		/// <summary>Gets the amount of N required for optimum N content.</summary>
		/// <value>The required optimum N amount.</value>
		[Description("Amount of N required for optimum growth")]
		[Units("kgN/ha")]
		public double RequiredOptimumN
		{
			get { return myNdemandOpt; }
		}

		/// <summary>Gets the amount of N demanded from soil.</summary>
		/// <value>The N demand from soil.</value>
		[Description("Amount of N demanded from soil")]
		[Units("kgN/ha")]
		public double DemandSoilN
		{
			get { return mySoilNDemand; }
		}

		/// <summary>Gets the amount of plant available N in the soil.</summary>
		/// <value>The soil available N.</value>
		[Description("Amount of N available in the soil")]
		[Units("kgN/ha")]
		public double[] SoilAvailableN
		{
			get { return mySoilAvailableN; }
		}

		/// <summary>Gets the amount of N taken up from soil.</summary>
		/// <value>The N uptake.</value>
		[Description("Amount of N uptake")]
		[Units("kgN/ha")]
		public double[] UptakeN
		{
			get { return mySoilNitrogenTakenUp; }
		}

		/// <summary>Gets the amount of N deposited as litter onto soil surface.</summary>
		/// <value>The litter N amount.</value>
		[Description("Amount of N deposited as litter onto soil surface")]
		[Units("kgN/ha")]
		public double LitterN
		{
			get { return myDNlitter; }
		}

		/// <summary>Gets the amount of N from senesced roots added to soil FOM.</summary>
		/// <value>The senesced root N amount.</value>
		[Description("Amount of N from senesced roots added to soil FOM")]
		[Units("kgN/ha")]
		public double SenescedRootN
		{
			get { return myDNrootSen; }
		}

		/// <summary>Gets the amount of N in new grown tissue.</summary>
		/// <value>The actual growth N amount.</value>
		[Description("Amount of N in new growth")]
		[Units("kgN/ha")]
		public double ActualGrowthN
		{
			get { return myNewGrowthN; }
		}

		#endregion

		#region - Turnover rates and DM allocation  ------------------------------------------------------------------------

		/// <summary>Gets the turnover rate for live DM (leaves, stems and sheath).</summary>
		/// <value>The turnover rate for live DM.</value>
		[Description("Turnover rate for live DM (leaves and stem)")]
		[Units("0-1")]
		public double LiveDMTurnoverRate
		{
			get { return myGama; }
		}

		/// <summary>Gets the turnover rate for dead DM (leaves, stems and sheath).</summary>
		/// <value>The turnover rate for dead DM.</value>
		[Description("Turnover rate for dead DM (leaves and stem)")]
		[Units("0-1")]
		public double DeadDMTurnoverRate
		{
			get { return myGamaD; }
		}

		/// <summary>Gets the turnover rate for live DM in stolons.</summary>
		/// <value>The turnover rate for stolon DM.</value>
		[Description("DM turnover rate for stolons")]
		[Units("0-1")]
		public double StolonDMTurnoverRate
		{
			get { return myGamaS; }
		}

		/// <summary>Gets the turnover rate for live DM in roots.</summary>
		/// <value>The turnover rate for root DM.</value>
		[Description("DM turnover rate for roots")]
		[Units("0-1")]
		public double RootDMTurnoverRate
		{
			get { return myGamaR; }
		}

		/// <summary>Gets the DM allocation to shoot.</summary>
		/// <value>The shoot DM allocation.</value>
		[Description("Fraction of DM allocated to Shoot")]
		[Units("0-1")]
		public double ShootDMAllocation
		{
			get { return myFShoot; }
		}

		/// <summary>Gets the DM allocation to roots.</summary>
		/// <value>The root dm allocation.</value>
		[Description("Fraction of DM allocated to roots")]
		[Units("0-1")]
		public double RootDMAllocation
		{
			get { return 1 - myFShoot; }
		}

		#endregion

		#region - LAI and cover  -------------------------------------------------------------------------------------------

		/// <summary>Gets the total plant LAI (leaf area index).</summary>
		/// <value>The total LAI.</value>
		[Description("Total leaf area index")]
		[Units("m^2/m^2")]
		public double LAITotal
		{
			get { return myLAIGreen + myLAIDead; }
		}

		/// <summary>Gets the plant's green LAI (leaf area index).</summary>
		/// <value>The green LAI.</value>
		[Description("Leaf area index of green leaves")]
		[Units("m^2/m^2")]
		public double LAIGreen
		{
			get { return myLAIGreen; }
		}

		/// <summary>Gets the plant's dead LAI (leaf area index).</summary>
		/// <value>The dead LAI.</value>
		[Description("Leaf area index of dead leaves")]
		[Units("m^2/m^2")]
		public double LAIDead
		{
			get { return myLAIDead; }
		}

		/// <summary>Gets the irradiance on top of canopy.</summary>
		/// <value>The irradiance on top of canopy.</value>
		[Description("Irridance on the top of canopy")]
		[Units("W.m^2/m^2")]
		public double IrradianceTopCanopy
		{
			get { return myIL; }
		}

		/// <summary>Gets the plant's total cover.</summary>
		/// <value>The total cover.</value>
		[Description("Fraction of soil covered by plants")]
		[Units("%")]
		public double CoverTotal
		{
			get
			{
				if (myLAIGreen + myLAIDead == 0) return 0;
				return (1.0 - (Math.Exp(-myLightExtentionCoeff * (myLAIGreen + myLAIDead))));
			}
		}

		/// <summary>Gets the plant's green cover.</summary>
		/// <value>The green cover.</value>
		[Description("Fraction of soil covered by green leaves")]
		[Units("%")]
		public double CoverGreen
		{
			get
			{
				if (myLAIGreen == 0)
					return 0.0;
				else
					return (1.0 - Math.Exp(-myLightExtentionCoeff * myLAIGreen));
			}
		}

		/// <summary>Gets the plant's dead cover.</summary>
		/// <value>The dead cover.</value>
		[Description("Fraction of soil covered by dead leaves")]
		[Units("%")]
		public double CoverDead
		{
			get
			{
				if (myLAIDead == 0)
					return 0.0;
				else
					return (1.0 - Math.Exp(-myLightExtentionCoeff * myLAIDead));
			}
		}

		#endregion

		#region - Root depth and distribution  -----------------------------------------------------------------------------

		/// <summary>Gets the root depth.</summary>
		/// <value>The root depth.</value>
		[Description("Depth of roots")]
		[Units("mm")]
		public double RootDepth
		{
			get { return myRootDepth; }
		}

		/// <summary>Gets the root frontier.</summary>
		/// <value>The layer at bottom of root zone.</value>
		[Description("Layer at bottom of root zone")]
		[Units("mm")]
		public double RootFrontier
		{
			get { return myRootFrontier; }
		}

		/// <summary>Gets the fraction of root dry matter for each soil layer.</summary>
		/// <value>The root fraction.</value>
		[Description("Fraction of root dry matter for each soil layer")]
		[Units("0-1")]
		public double[] RootWtFraction
		{
			get { return myRootFraction; }
		}

		/// <summary>Gets the plant's root length density for each soil layer.</summary>
		/// <value>The root length density.</value>
		[Description("Root length density")]
		[Units("mm/mm^3")]
		public double[] RLD
		{
			get
			{
				double[] result = new double[myNLayers];
				double Total_Rlength = myDMRoot * mySpecificRootLength;   // m root/ha
				Total_Rlength *= 0.0000001;  // convert into mm root/mm2 soil)
				for (int layer = 0; layer < result.Length; layer++)
				{
					result[layer] = myRootFraction[layer] * Total_Rlength / mySoil.Thickness[layer];    // mm root/mm3 soil
				}
				return result;
			}
		}

		#endregion

		#region - Water amounts  -------------------------------------------------------------------------------------------

		/// <summary>Gets the lower limit of soil water content for plant uptake.</summary>
		/// <value>The water uptake lower limit.</value>
		[Description("Lower limit of soil water content for plant uptake")]
		[Units("mm^3/mm^3")]
		public double[] LL
		{
			get
			{
				SoilCrop soilInfo = (SoilCrop)mySoil.Crop(Name);
				return soilInfo.LL;
			}
		}

		/// <summary>Gets the amount of water demanded by the plant.</summary>
		/// <value>The water demand.</value>
		[Description("Plant water demand")]
		[Units("mm")]
		public double WaterDemand
		{
			get { return myWaterDemand; }
		}

		/// <summary>Gets the amount of soil water available for uptake.</summary>
		/// <value>The soil available water.</value>
		[Description("Plant availabe water")]
		[Units("mm")]
		public double[] SoilAvailableWater
		{
			get { return mySoilAvailableWater; }
		}

		/// <summary>Gets the amount of water taken up by the plant.</summary>
		/// <value>The water uptake.</value>
		[Description("Plant water uptake")]
		[Units("mm")]
		public double[] WaterUptake
		{
			get { return mySoilWaterTakenUp; }
		}

		#endregion

		#region - Growth limiting factors  ---------------------------------------------------------------------------------

		/// <summary>Gets the growth limiting factor due to N availability.</summary>
		/// <value>The growth limiting factor due to N.</value>
		[Description("Growth limiting factor due to nitrogen")]
		[Units("0-1")]
		public double GlfN
		{
			get { return myGLFN; }
		}

		/// <summary>Gets the growth limiting factor due to N concentration in the plant.</summary>
		/// <value>The growth limiting factor due to N concentration.</value>
		[Description("Plant growth limiting factor due to plant N concentration")]
		[Units("0-1")]
		public double GlfNConcentration
		{
			get { return myNCFactor; }
		}

		/// <summary>Gets the growth limiting factor due to temperature.</summary>
		/// <value>The growth limiting factor due to temperature.</value>
		[Description("Growth limiting factor due to temperature")]
		[Units("0-1")]
		public double GlfTemperature
		{
			get { return TemperatureLimitingFactor(myTmeanW); }
		}

		/// <summary>Gets the growth limiting factor due to water availability.</summary>
		/// <value>The growth limiting factor due to water.</value>
		[Description("Growth limiting factor due to water deficit")]
		[Units("0-1")]
		public double GlfWater
		{
			get { return myGLFWater; }
		}

		// TODO: verify that this is really needed
		/// <summary>Gets the vapour pressure deficit factor.</summary>
		/// <value>The vapour pressure deficit factor.</value>
		[Description("Effect of vapour pressure on growth (used by micromet)")]
		[Units("0-1")]
		public double FVPD
		{
			get { return FVPDFunction.Value(VPD()); }
		}

		#endregion

		#region - Harvest variables  ---------------------------------------------------------------------------------------

		/// <summary>Gets the amount of dry matter harvestable (leaf + stem).</summary>
		/// <value>The harvestable DM weight.</value>
		[Description("Amount of dry matter harvestable (leaf+stem)")]
		[Units("kgDM/ha")]
		public double HarvestableWt
		{
			get { return Math.Max(0.0, StandingLiveWt - myMinimumGreenWt) + Math.Max(0.0, StandingDeadWt - myMinimumDeadWt); }
		}

		/// <summary>Gets the amount of dry matter harvested.</summary>
		/// <value>The harvested DM weight.</value>
		[Description("Amount of plant dry matter removed by harvest")]
		[Units("kgDM/ha")]
		public double HarvestedWt
		{
			get { return myDefoliatedDM; }
		}

		/// <summary>Gets the fraction of the plant that was harvested.</summary>
		/// <value>The fraction harvested.</value>
		[Description("Fraction harvested")]
		[Units("0-1")]
		public double HarvestedFraction
		{
			get { return myFractionHarvested; }
		}

		/// <summary>Gets the amount of plant N removed by harvest.</summary>
		/// <value>The harvested N amount.</value>
		[Description("Amount of plant nitrogen removed by harvest")]
		[Units("kgN/ha")]
		public double HarvestedN
		{
			get { return myDefoliatedN; }
		}

		/// <summary>Gets the N concentration in harvested DM.</summary>
		/// <value>The N concentration in harvested DM.</value>
		[Description("average N concentration of harvested material")]
		[Units("kgN/kgDM")]
		public double HarvestedNconc
		{
			get { return MathUtilities.Divide(HarvestedN, HarvestedWt, 0.0); }
		}

		/// <summary>Gets the average herbage digestibility.</summary>
		/// <value>The herbage digestibility.</value>
		[Description("Average digestibility of herbage")]
		[Units("0-1")]
		public double HerbageDigestibility
		{
			get { return myDigestHerbage; }
		}

		// TODO: Digestibility of harvested material should be better calculated (consider fraction actually removed)
		/// <summary>Gets the average digestibility of harvested DM.</summary>
		/// <value>The harvested digestibility.</value>
		[Description("Average digestibility of harvested meterial")]
		[Units("0-1")]
		public double HarvestedDigestibility
		{
			get { return myDigestDefoliated; }
		}

		/// <summary>Gets the average herbage ME (metabolisable energy).</summary>
		/// <value>The herbage ME.</value>
		[Description("Average ME of herbage")]
		[Units("(MJ/ha)")]
		public double HerbageME
		{
			get { return 16 * myDigestHerbage * StandingWt; }
		}

		/// <summary>Gets the average ME (metabolisable energy) of harvested DM.</summary>
		/// <value>The harvested ME.</value>
		[Description("Average ME of harvested material")]
		[Units("(MJ/ha)")]
		public double HarvestedME
		{
			get { return 16 * myDigestDefoliated * HarvestedWt; }
		}

        #endregion

        #endregion

        #region Private variables  -----------------------------------------------------------------------------------------

        /// <summary>The state of leaves (DM and N)</summary>
        private OrganPool myLeaves;

        /// <summary>The state of sheath/stems (DM and N)</summary>
        private OrganPool myStems;

        /// <summary>The state of stolons (DM and N)</summary>
        private OrganPool myStolons;

        /// <summary>The state of roots (DM and N)</summary>
        private OrganPool myRoots;

        /// <summary>Initialises the basic structure of a pasture plant</summary>
        public PastureSpecies()
	    {
            myLeaves = new OrganPool();
            myStems = new OrganPool();
            myStolons = new OrganPool();
            myRoots = new OrganPool();
        }

        /// <summary>The DM of shoot (g/m^2)</summary>
        private double myDMShoot;

        /// <summary>The DM of shoot (g/m^2)</summary>
        private double myDMShootGreen;

        /// <summary>The DM of shoot (g/m^2)</summary>
        private double myDMShootDead;

        /// <summary>The DM of roots (g/m^2)</summary>
        private double myDMRoot;

        /// <summary>The amount of N above ground (shoot)</summary>
        private double myNShoot;

        /// <summary>The amount of N below ground (root)</summary>
        private double myNRoot;

        /// <summary>flag whether several routines are ran by species or are controlled by the Swar</summary>
        internal bool myIsSwardControlled = false;

		/// <summary>flag whether this species is alive (activelly growing)</summary>
		private bool myIsAlive = true;

		// defining the plant type  -----------------------------------------------------------------------------------

		/// <summary>flag this species type, annual or perennial</summary>
		private bool myIsAnnual = false;
		/// <summary>flag whether this species is a legume</summary>
		private bool myIsLegume = false;

		// TODO: do we want to keep this??
		// Parameters for annual species  -----------------------------------------------------------------------------

		/// <summary>The day of year for emergence</summary>
		private int myDayEmerg = 0;
		/// <summary>The monthe of emergence</summary>
		private int myMonEmerg = 0;
		/// <summary>The day of anthesis</summary>
		private int myDayAnth = 0;
		/// <summary>The month of anthesis</summary>
		private int myMonAnth = 0;
		/// <summary>The number of days to mature</summary>
		private int myDaysToMature = 0;
		/// <summary>The number of days between emergence and anthesis</summary>
		private int myDaysEmgToAnth = 0;
		/// <summary>The phenologic stage (0= pre_emergence, 1= vegetative, 2= reproductive)</summary>
		private int myPhenoStage = 1;
		///// <summary>The phenologic factor</summary>
		//private double phenoFactor = 1;
		/// <summary>The number of days from emergence</summary>
		private int myDaysfromEmergence = 0;
		/// <summary>The number of days from anthesis</summary>
		private int myDaysfromAnthesis = 0;

		/// <summary>The daily variation in root depth</summary>
		private double myDRootDepth = 50;
		/// <summary>The maximum root depth</summary>
		private double myMaxRootDepth = 900;


		// N concentration thresholds for various tissues (set relative to leaf N)  -----------------------------------

		/// <summary>The optimum N concentration of stems and sheath</summary>
		private double myNcStemOpt;
		/// <summary>The optimum N concentration of stolons</summary>
		private double myNcStolonOpt;
		/// <summary>The optimum N concentration of roots</summary>
		private double myNcRootOpt;
		/// <summary>The maximum N concentration of stems andd sheath</summary>
		private double myNcStemMax;
		/// <summary>The maximum N concentration of stolons</summary>
		private double myNcStolonMax;
		/// <summary>The maximum N concentration of roots</summary>
		private double myNcRootMax;
		/// <summary>The minimum N concentration of stems and sheath</summary>
		private double myNcStemMin;
		/// <summary>The minimum N concentration of stolons</summary>
		private double myNcStolonMin;
		/// <summary>The minimum N concentration of roots</summary>
		private double myNcRootMin;


		// Amounts and fluxes of N in the plant  ----------------------------------------------------------------------

		/// <summary>The N demand for new growth, with luxury uptake</summary>
		private double myNdemandLux;
		/// <summary>The N demand for new growth, at optimum N content</summary>
		private double myNdemandOpt;
		/// <summary>The amount of N fixation from atmosphere (for legumes)</summary>
		internal double myNfixation = 0.0;
		/// <summary>The amount of N remobilised from senesced tissue</summary>
		private double myNRemobilised = 0.0;

        /// <summary>Some Description</summary>
        internal double myNleaf3Remob = 0.0;

        /// <summary>Some Description</summary>
        internal double myNstem3Remob = 0.0;

        /// <summary>Some Description</summary>
        internal double myNstol3Remob = 0.0;

        /// <summary>Some Description</summary>
        internal double myNrootRemob = 0.0;


        /// <summary>The amount of N actually remobilised to new growth</summary>
        private double myNremob2NewGrowth = 0.0;
		/// <summary>The amount of N used in new growth</summary>
		internal double myNewGrowthN = 0.0;
		/// <summary>The aount of luxury N (above Nopt) in tissue 2 potentially remobilisable</summary>
		private double myNLuxury2;
		/// <summary>The amount of luxury N (above Nopt) in tissue 3 potentially remobilisable</summary>
		private double myNLuxury3;
		/// <summary>The amount of luxury N actually remobilised from tissue 2</summary>
		private double myNFastRemob2 = 0.0;
		/// <summary>The amount of luxury N actually remobilised from tissue 3</summary>
		private double myNFastRemob3 = 0.0;

		// N uptake process  ------------------------------------------------------------------------------------------

		///// <summary>The amount of N demanded for new growth</summary>
		//private double myNitrogenDemand = 0.0;
		/// <summary>The amount of N in the soil available to the plant</summary>
		internal double[] mySoilAvailableN;
		/// <summary>The amount of NH4 in the soil available to the plant</summary>
		internal double[] mySoilNH4available;
		/// <summary>The amount of NO3 in the soil available to the plant</summary>
		internal double[] mySoilNO3available;
		/// <summary>The amount of N demanded from the soil</summary>
		private double mySoilNDemand;
		/// <summary>The amount of N actually taken up</summary>
		internal double mySoilNuptake;
		/// <summary>The amount of N uptake from each soil layer</summary>
		internal double[] mySoilNitrogenTakenUp;

		// water uptake process  --------------------------------------------------------------------------------------

		/// <summary>The amount of water demanded for new growth</summary>
		internal double myWaterDemand = 0.0;
		/// <summary>The amount of soil available water</summary>
		private double[] mySoilAvailableWater;
		/// <summary>The amount of soil water taken up</summary>
		internal double[] mySoilWaterTakenUp;

		// harvest and digestibility  ---------------------------------------------------------------------------------

		/// <summary>The DM amount harvested (defoliated)</summary>
		private double myDefoliatedDM = 0.0;
		/// <summary>The N amount harvested (defoliated)</summary>
		private double myDefoliatedN = 0.0;
		/// <summary>The digestibility of herbage</summary>
		private double myDigestHerbage = 0.0;
		/// <summary>The digestibility of defoliated material</summary>
		private double myDigestDefoliated = 0.0;
		/// <summary>The fraction of standing DM harvested</summary>
		internal double myFractionHarvested = 0.0;

		// Plant height, LAI and cover  -------------------------------------------------------------------------------

		/// <summary>The plant's average height</summary>
		private double myPlantHeight;
		/// <summary>The plant's green LAI</summary>
		private double myLAIGreen;
		/// <summary>The plant's dead LAI</summary>
		private double myLAIDead;

		// root variables  --------------------------------------------------------------------------------------------

		/// <summary>The plant's root depth</summary>
		private double myRootDepth = 0.0;
		/// <summary>The layer at the bottom of the root zone</summary>
		private int myRootFrontier = 1;
		/// <summary>The fraction of roots DM in each layer</summary>
		private double[] myRootFraction;
		/// <summary>The maximum shoot-root ratio</summary>
		private double myMaxSRratio;

		/// <summary>The fraction of each layer that is actually explored by roots (0-1)</summary>
		private double[] myRootExplorationFactor;

		// photosynthesis, growth and turnover  -----------------------------------------------------------------------

		/// <summary>The irradiance on top of canopy</summary>
		private double myIL;
		/// <summary>The gross photosynthesis rate (C assimilation)</summary>
		private double myPgross = 0.0;
		/// <summary>The growth respiration rate (C loss)</summary>
		private double myResp_g = 0.0;
		/// <summary>The maintenance respiration rate (C loss)</summary>
		private double myResp_m = 0.0;
		/// <summary>The amount of C remobilised from senesced tissue</summary>
		private double myCRemobilised = 0.0;

		/// <summary>Daily net growth potential (kgDM/ha)</summary>
		private double myDGrowthPot;
		/// <summary>Daily potential growth after water stress</summary>
		private double myDGrowthWstress;
		/// <summary>Daily growth after nutrient stress (actual growth)</summary>
		private double myDGrowthActual;

		/// <summary>Effective growth of roots</summary>
		private double myDGrowthRoot;
		/// <summary>Effective growth of shoot (herbage growth)</summary>
		private double myDGrowthShoot;
		/// <summary>Effective plant growth (actual growth minus senescence)</summary>
		private double myDGrowthEff;

		/// <summary>Daily litter production (dead to surface OM)</summary>
		private double myDLitter;
		/// <summary>N amount in litter procuded</summary>
		private double myDNlitter;
		/// <summary>Daily root sennesce (added to soil FOM)</summary>
		private double myDRootSen;
		/// <summary>N amount in senesced roots</summary>
		private double myDNrootSen;

		/// <summary>Fraction of growth allocated to shoot (0-1)</summary>
		private double myFShoot;

		/// <summary>The daily DM turnover rate (from tissue 1 to 2, then to 3, then to 4)</summary>
		private double myGama = 0.0;
		/// <summary>The daily DM turnover rate for stolons</summary>
		private double myGamaS = 0.0;	  // for stolons
		/// <summary>The daily DM turnover rate for dead tissue (from tissue 4 to litter)</summary>
		private double myGamaD = 0.0;
		/// <summary>The daily DM turnover rate for roots</summary>
		private double myGamaR = 0.0;

		// growth limiting factors ------------------------------------------------------------------------------------
		/// <summary>The GLF due to water stress</summary>
		internal double myGLFWater = 1.0;
		// private double glfTemp;   //The GLF due to temperature stress
		/// <summary>The GLF due to N stress</summary>
		internal double myGLFN = 0.0;
		/// <summary>The growth factor for N concentration</summary>
		private double myNCFactor = 1.0;

		// auxiliary variables for radiation and temperature stress  --------------------------------------------------

		/// <summary>Growth rate reduction factor due to high temperatures</summary>
		private double myHighTempEffect = 1.0;
		/// <summary>Growth rate reduction factor due to low temperatures</summary>
		private double myLowTempEffect = 1.0;
		/// <summary>Cumulative degress of temperature for recovery from heat damage</summary>
		private double myAccumT4Heat = 0.0;
		/// <summary>Cumulative degress of temperature for recovry from cold damage</summary>
		private double myAccumT4Cold = 0.0;

		// general auxiliary variables  -------------------------------------------------------------------------------

		/// <summary>Number of layers in the soil</summary>
		private int myNLayers = 0;
		/// <summary>Today's average temperature</summary>
		private double myTmean;
		/// <summary>Today's weighted mean temperature</summary>
		private double myTmeanW;
		/// <summary>State for this plant on the previous day</summary>
		private SpeciesState myPrevState;

		// TODO: Hope to get rid of these soon  --------------------------------------------------------
		/// <summary>fraction of Radn intercepted by this species</summary>
		internal double myRadnIntFrac = 1.0;
		/// <summary>Light extintion coefficient for all species</summary>
		internal double mySwardLightExtCoeff;
		/// <summary>average green cover for all species</summary>
		internal double mySwardGreenCover;

		#endregion

		#region Constants  -------------------------------------------------------------------------------------------------

		/// <summary>Average carbon content in plant dry matter</summary>
		const double CarbonFractionInDM = 0.4;

		/// <summary>Factor for converting nitrogen to protein</summary>
		const double NitrogenToProteinFactor = 6.25;

		/// <summary>The C:N ratio of protein</summary>
		const double CNratioProtein = 3.5;

		/// <summary>The C:N ratio of cell wall</summary>
		const double CNratioCellWall = 100.0;

	    /// <summary>Maximum difference between two values of double precision in this model</summary>
	    const double myEpsilon = 0.000001;

        #endregion

        #region Initialisation methods  ------------------------------------------------------------------------------------

        /// <summary>Performs the initialisation procedures for this species (set DM, N, LAI, etc)</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
		private void OnSimulationCommencing(object sender, EventArgs e)
		{
			// get the number of layers in the soil profile
			myNLayers = mySoil.Thickness.Length;

			// initialise soil water and N variables
			InitiliaseSoilArrays();

			// set initial plant state
			SetInitialState();

			// initialise the class which will hold yesterday's plant state
			myPrevState = new SpeciesState();

			// check whether uptake is done here or by another module
			if (myApsimArbitrator != null)
			{
				myWaterUptakeSource = "arbitrator";
				myNitrogenUptakeSource = "arbitrator";
			}

			// tell other modules about the existence of this species
			if (!myIsSwardControlled)
				DoNewCropEvent();
		}

		/// <summary>
		/// Initialise arrays to same length as soil layers
		/// </summary>
		private void InitiliaseSoilArrays()
		{
			mySoilAvailableWater = new double[myNLayers];
			mySoilWaterTakenUp = new double[myNLayers];
			mySoilNH4available = new double[myNLayers];
			mySoilNO3available = new double[myNLayers];
			mySoilAvailableN = new double[myNLayers];
			mySoilNitrogenTakenUp = new double[myNLayers];
		}

		/// <summary>
		/// Set the initial parameters for this plant, including DM and N content of various pools plus plant height and root depth
		/// </summary>
		private void SetInitialState()
		{
			// 1. Initialise DM of various tissue pools, user should supply initial values for shoot and root
			//dmTotal = iniDMShoot + iniDMRoot;

			// set initial DM fractions - Temporary?? TODO
			if (initialDMFractions == null)
			{
				if (myIsLegume)
					initialDMFractions = myInitialDMFractions_legume;
				else
					initialDMFractions = myInitialDMFractions_grass;
			}

            myLeaves.tissue[0].DM = myIniDMFraction[0] * myIniDMShoot;
            myLeaves.tissue[1].DM = myIniDMFraction[1] * myIniDMShoot;
            myLeaves.tissue[2].DM = myIniDMFraction[2] * myIniDMShoot;
            myLeaves.tissue[3].DM = myIniDMFraction[3] * myIniDMShoot;
            myStems.tissue[0].DM = myIniDMFraction[4] * myIniDMShoot;
            myStems.tissue[1].DM = myIniDMFraction[5] * myIniDMShoot;
            myStems.tissue[2].DM = myIniDMFraction[6] * myIniDMShoot;
            myStems.tissue[3].DM = myIniDMFraction[7] * myIniDMShoot;
            myStolons.tissue[0].DM = myIniDMFraction[8] * myIniDMShoot;
            myStolons.tissue[1].DM = myIniDMFraction[9] * myIniDMShoot;
            myStolons.tissue[2].DM = myIniDMFraction[10] * myIniDMShoot;
		    myStolons.tissue[3].DM = 0.0;
            myRoots.tissue[0].DM = myIniDMRoot;
		    myRoots.tissue[1].DM = 0.0;
            myRoots.tissue[2].DM = 0.0;
            myRoots.tissue[3].DM = 0.0;

            // 2. Initialise N content thresholds (optimum, maximum, and minimum)
            myNcStemOpt = myLeafNopt * myRelativeNStems;
			myNcStolonOpt = myLeafNopt * myRelativeNStolons;
			myNcRootOpt = myLeafNopt * myRelativeNRoots;

			myNcStemMax = myLeafNmax * myRelativeNStems;
			myNcStolonMax = myLeafNmax * myRelativeNStolons;
			myNcRootMax = myLeafNmax * myRelativeNRoots;

			myNcStemMin = myLeafNmin * myRelativeNStems;
			myNcStolonMin = myLeafNmin * myRelativeNStolons;
			myNcRootMin = myLeafNmin * myRelativeNRoots;

            // 3. Initialise the N amounts in each pool (assume to be at optimum)
            myLeaves.tissue[0].Nconc =  myLeafNopt;
            myLeaves.tissue[1].Nconc =  myLeafNopt;
            myLeaves.tissue[2].Nconc =  myLeafNopt;
            myLeaves.tissue[3].Nconc =  myLeafNmin;
            myStems.tissue[0].Nconc =  myNcStemOpt;
            myStems.tissue[1].Nconc =  myNcStemOpt;
            myStems.tissue[2].Nconc =  myNcStemOpt;
            myStems.tissue[3].Nconc =  myNcStemMin;
            myStolons.tissue[0].Nconc =  myNcStolonOpt;
            myStolons.tissue[1].Nconc =  myNcStolonOpt;
            myStolons.tissue[2].Nconc =  myNcStolonOpt;
            myRoots.tissue[0].Nconc =  myNcRootOpt;

			// 4. Root depth and distribution
			myRootDepth = myIniRootDepth;
			myRootExplorationFactor = new double[myNLayers];
			double cumDepth = 0.0;
			for (int layer = 0; layer < myNLayers; layer++)
			{
				cumDepth += mySoil.Thickness[layer];
				if (cumDepth <= myRootDepth)
				{
					myRootFrontier = layer;
					myRootExplorationFactor[layer] = LayerFractionWithRoots(layer);
				}
				else
				{
					layer = mySoil.Thickness.Length;
				}
			}
			myRootFraction = RootProfileDistribution();
			InitialiseRootsProperties();

			// 5. Canopy height and related variables
			myPlantHeight = Math.Max(20.0, HeightFromMass.Value(StandingWt));  // TODO:update this approach
			InitialiseCanopy();
			// maximum shoot:root ratio
			myMaxSRratio = (1 - MaxRootFraction) / MaxRootFraction;

			// 6. Set initial phenological stage
			if (myDMShoot+myDMRoot == 0.0)
				myPhenoStage = 0;
			else
				myPhenoStage = 1;

			// 7. aggregated auxiliary DM and N variables
			updateAggregated();

			// 8. Calculate the values for LAI
			EvaluateLAI();
		}

		/// <summary>Initialise the variables in canopy properties</summary>
		private void InitialiseCanopy()
		{
			myCanopyProperties.Name = Name;
			myCanopyProperties.CoverGreen = CoverGreen;
			myCanopyProperties.CoverTot = CoverTotal;
			myCanopyProperties.CanopyDepth = myPlantHeight;
			myCanopyProperties.CanopyHeight = myPlantHeight;
			myCanopyProperties.LAIGreen = myLAIGreen;
			myCanopyProperties.LAItot = LAITotal;
			myCanopyProperties.MaximumStomatalConductance = myStomatalConductanceMax;
			myCanopyProperties.HalfSatStomatalConductance = 200.0;  // TODO: this should be on the UI
			myCanopyProperties.CanopyEmissivity = 0.96;  // TODO: this should be on the UI
			myCanopyProperties.Frgr = FRGR;
		}

		/// <summary>Initialise the variables in root properties</summary>
		private void InitialiseRootsProperties()
		{
			SoilCrop soilCrop = this.mySoil.Crop(Name) as SoilCrop;

			myRootProperties.RootDepth = myRootDepth;
			myRootProperties.KL = soilCrop.KL;
			myRootProperties.MinNO3ConcForUptake = new double[mySoil.Thickness.Length];
			myRootProperties.MinNH4ConcForUptake = new double[mySoil.Thickness.Length];
			myRootProperties.KNO3 = myKNO3;
			myRootProperties.KNH4 = myKNH4;

			myRootProperties.LowerLimitDep = new double[mySoil.Thickness.Length];
			myRootProperties.UptakePreferenceByLayer = new double[mySoil.Thickness.Length];
			myRootProperties.RootExplorationByLayer = new double[mySoil.Thickness.Length];
			for (int layer = 0; layer < mySoil.Thickness.Length; layer++)
			{
				myRootProperties.LowerLimitDep[layer] = soilCrop.LL[layer] * mySoil.Thickness[layer];
				myRootProperties.MinNO3ConcForUptake[layer] = 0.0;
				myRootProperties.MinNH4ConcForUptake[layer] = 0.0;
				myRootProperties.UptakePreferenceByLayer[layer] = 1.0;
				myRootProperties.RootExplorationByLayer[layer] = myRootExplorationFactor[layer];
			}
			myRootProperties.RootLengthDensityByVolume = RLD;
		}

		/// <summary>Calculates the days emg to anth.</summary>
		/// <returns></returns>
		private int CalcDaysEmgToAnth()
		{
			int numbMonths = myMonAnth - myMonEmerg;  //emergence & anthesis in the same calendar year: monEmerg < monAnth
			if (myMonEmerg >= myMonAnth)			  //...across the calendar year
				numbMonths += 12;

			myDaysEmgToAnth = (int)(30.5 * numbMonths + (myDayAnth - myDayEmerg));

			return myDaysEmgToAnth;
		}

		#endregion

		#region Daily processes  -------------------------------------------------------------------------------------------

		/// <summary>EventHandler - preparation befor the main process</summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
		[EventSubscribe("DoDailyInitialisation")]
		private void OnDoDailyInitialisation(object sender, EventArgs e)
		{
			// 1. Zero out several variables
			RefreshVariables();

			// mean air temperature for today
			myTmean = (myMetData.MaxT + myMetData.MinT) * 0.5;
			myTmeanW = (myMetData.MaxT * 0.75) + (myMetData.MinT * 0.25);
		}

		/// <summary>Performs the plant growth calculations</summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
		[EventSubscribe("DoPlantGrowth")]
		private void OnDoPlantGrowth(object sender, EventArgs e)
		{
			if (!myIsSwardControlled)
			{
				if (myIsAlive)
				{
					// stores the current state for this species
					SaveCurrentState();

					// step 01 - preparation and potential growth
					CalcPotentialGrowth();

					// Water demand, supply, and uptake
					DoWaterCalculations();

					// step 02 - Potential growth after water limitations
					CalcGrowthWithWaterLimitations();

					// Nitrogen demand, supply, and uptake
					DoNitrogenCalculations();

					// step 03 - Actual growth after nutrient limitations, but before senescence
					CalcActualGrowthAndPartition();

					// step 04 - Effective growth after all limitations and senescence
					CalcTurnoverAndEffectiveGrowth();

					// Send amounts of litter and senesced roots to other modules
					DoSurfaceOMReturn(myDLitter, myDNlitter);
					DoIncorpFomEvent(myDRootSen, myDNrootSen);
				}
			}
			//else
			//    Growth is controlled by Sward (all species)
		}

		/// <summary>Calculates the potential growth.</summary>
		internal void CalcPotentialGrowth()
		{
			// update root depth (for annuals only)
			EvaluateRootGrowth();

			// Evaluate the phenologic stage, for annuals
			if (myIsAnnual)
				myPhenoStage = annualsPhenology();

			// Compute the potential growth
			if (myPhenoStage == 0 || myLAIGreen == 0.0)
			{
				// Growth before germination is null
				myPgross = 0.0;
				myResp_m = 0.0;
				myResp_g = 0.0;
				myCRemobilised = 0.0;
				myDGrowthPot = 0.0;
			}
			else
			{
				// Gross potential growth (kgC/ha/day)
				myPgross = DailyGrossPotentialGrowth();

				// Respiration (kgC/ha/day)
				myResp_m = DailyMaintenanceRespiration();
				myResp_g = DailyGrowthRespiration();

				// Remobilisation (kgC/ha/day) (got from previous day turnover)

				// Net potential growth (kgDM/ha/day)
				myDGrowthPot = DailyNetPotentialGrowth();
			}
		}

		/// <summary>Calculates the growth with water limitations.</summary>
		internal void CalcGrowthWithWaterLimitations()
		{
			// Potential growth after water limitations
			myDGrowthWstress = myDGrowthPot * Math.Pow(myGLFWater, myWaterStressExponent);

			// allocation of todays growth
			myFShoot = ToShootFraction();
			//   FL = UpdatefLeaf();
		}

		/// <summary>Calculates the actual growth and partition.</summary>
		internal void CalcActualGrowthAndPartition()
		{
			// Actual daily growth
			myDGrowthActual = DailyActualGrowth();

			// Partition growth into various tissues
			PartitionNewGrowth();
		}

		/// <summary>Calculates the turnover and effective growth.</summary>
		internal void CalcTurnoverAndEffectiveGrowth()
		{
			// Compute tissue turnover and remobilisation (C and N)
			TissueTurnoverAndRemobilisation();

			// Effective, or net, growth
			myDGrowthEff = myDGrowthShoot + myDGrowthRoot;

			// Update aggregate variables and digetibility
			updateAggregated();

			// Update LAI
			EvaluateLAI();

			myDigestHerbage = calcDigestibility();
		}

		#region - Handling and auxilary processes  -------------------------------------------------------------------------

		/// <summary>Refresh the value of several variables</summary>
		internal void RefreshVariables()
		{
			// reset some variables
			myDefoliatedDM = 0.0;
			myDefoliatedN = 0.0;
			myDigestDefoliated = 0.0;

			// TODO:
			// these are needed when AgPasture controls the growth (not necessarily the partition of soil stuff)
			// this is not sound and we should get rid of it very soon
			myRadnIntFrac = 1.0;
			if (!myIsSwardControlled)
			{
				mySwardLightExtCoeff = myLightExtentionCoeff;
				mySwardGreenCover = CalcPlantCover(myLAIGreen);
			}
			//else
			//    Sward will set these values

		}

        /// <summary>Stores the current state for this species</summary>
        internal void SaveCurrentState()
        {
            myPrevState.leaves.tissue[0].DM = myLeaves.tissue[0].DM;
            myPrevState.leaves.tissue[1].DM = myLeaves.tissue[1].DM;
            myPrevState.leaves.tissue[2].DM = myLeaves.tissue[2].DM;
            myPrevState.leaves.tissue[3].DM = myLeaves.tissue[3].DM;
            myPrevState.stems.tissue[0].DM = myStems.tissue[0].DM;
            myPrevState.stems.tissue[1].DM = myStems.tissue[1].DM;
            myPrevState.stems.tissue[2].DM = myStems.tissue[2].DM;
            myPrevState.stems.tissue[3].DM = myStems.tissue[3].DM;
            myPrevState.stolons.tissue[0].DM = myStolons.tissue[0].DM;
            myPrevState.stolons.tissue[1].DM = myStolons.tissue[1].DM;
            myPrevState.stolons.tissue[2].DM = myStolons.tissue[2].DM;
            myPrevState.roots.tissue[0].DM = myRoots.tissue[0].DM; // only one pool for roots

            myPrevState.leaves.tissue[0].Namount = myLeaves.tissue[0].Namount;
            myPrevState.leaves.tissue[1].Namount = myLeaves.tissue[1].Namount;
            myPrevState.leaves.tissue[2].Namount = myLeaves.tissue[2].Namount;
            myPrevState.leaves.tissue[3].Namount = myLeaves.tissue[3].Namount;
            myPrevState.stems.tissue[0].Namount = myStems.tissue[0].Namount;
            myPrevState.stems.tissue[1].Namount = myStems.tissue[1].Namount;
            myPrevState.stems.tissue[2].Namount = myStems.tissue[2].Namount;
            myPrevState.stems.tissue[3].Namount = myStems.tissue[3].Namount;
            myPrevState.stolons.tissue[0].Namount = myStolons.tissue[0].Namount;
            myPrevState.stolons.tissue[1].Namount = myStolons.tissue[1].Namount;
            myPrevState.stolons.tissue[2].Namount = myStolons.tissue[2].Namount;
            myPrevState.roots.tissue[0].Namount = myRoots.tissue[0].Namount; // only one pool for roots
        }

        /// <summary>Computes the value of auxiliary variables (aggregates for DM and N content)</summary>
        /// <exception cref="System.Exception">
        /// Loss of mass balance when aggregating plant dry matter after growth
        /// </exception>
        private void updateAggregated()
		{
			// auxiliary DM variables
			myDMShoot = myLeaves.DMTotal + myStems.DMTotal + myStolons.DMTotal;
            myDMShootGreen = myLeaves.DMGreen + myStems.DMGreen + myStolons.DMGreen;
            myDMShootDead = myLeaves.tissue[3].DM + myStems.tissue[3].DM + myStolons.tissue[3].DM;
            myDMRoot = myRoots.DMTotal;

			// auxiliary N variables
		    myNShoot = myLeaves.NTotal + myStems.NTotal + myStolons.NTotal;
		    myNRoot = myRoots.NTotal;
		}

		/// <summary>
		/// Evaluates the phenologic stage of annual plants, plus days from emergence or from anthesis
		/// </summary>
		/// <returns>An integer representing the plant's phenologic stage</returns>
		private int annualsPhenology()
		{
			int result = 0;
			if (myClock.Today.Month == myMonEmerg && myClock.Today.Day == myDayEmerg)
			{
				result = 1;		 //vegetative stage
				myDaysfromEmergence++;
			}
			else if (myClock.Today.Month == myMonAnth && myClock.Today.Day == myDayAnth)
			{
				result = 2;		 //reproductive stage
				myDaysfromAnthesis++;
				if (myDaysfromAnthesis >= myDaysToMature)
				{
					myPhenoStage = 0;
					myDaysfromEmergence = 0;
					myDaysfromAnthesis = 0;
				}
			}
			return result;
		}

		/// <summary>Reduction factor for potential growth due to phenology of annual species</summary>
		/// <returns>A factor to reduce plant growth (0-1)</returns>
		private double annualSpeciesReduction()
		{
			double rFactor = 1.0;
			if (myPhenoStage == 1 && myDaysfromEmergence < 60)  //decline at the begining due to seed bank effects ???
				rFactor = 0.5 + 0.5 * myDaysfromEmergence / 60;
			else if (myPhenoStage == 2)                       //decline of photosynthesis when approaching maturity
				rFactor = 1.0 - (double)myDaysfromAnthesis / myDaysToMature;
			return rFactor;
		}

		/// <summary>
		/// Computes the values of LAI (leaf area index) for green, dead, and total plant material
		/// </summary>
		private void EvaluateLAI()
		{
			double greenTissue = myLeaves.DMGreen + (myStolons.DMGreen * 0.3);  // assuming stolons have 0.3*SLA
			greenTissue /= 10000;   // converted from kg/ha to kg/m2
			myLAIGreen = greenTissue * mySpecificLeafArea;

			// Adjust accounting for resilience after unfavoured conditions
			if (!myIsLegume && myDMShootGreen < 1000)
			{
				greenTissue = myStems.DMGreen / 10000;
				myLAIGreen += greenTissue * mySpecificLeafArea * Math.Sqrt((1000 - myDMShootGreen) / 10000);
			}
			/* 
			 This adjust assumes cover will be bigger for the same amount of DM when DM is low, due to:
			 - light extinction coefficient will be bigger - plant leaves will be more horizontal than in dense high swards
			 - more parts (stems) will turn green for photosysnthesis (?)
			 - quick response of plant shoots to favoured conditions after release of stress
			 » Specific leaf area should be reduced (RCichota2014) - TODO
			 */

			myLAIDead = (myLeaves.tissue[3].DM / 10000) * mySpecificLeafArea;
		}

		/// <summary>Compute the average digestibility of aboveground plant material</summary>
		/// <returns>The digestibility of plant material (0-1)</returns>
		private double calcDigestibility()
		{
			if ((myLeaves.DMTotal + myStems.DMTotal) <= 0.0)
			{
				return 0.0;
			}

			// fraction of sugar (soluble carbohydrates)  - RCichota: this ignores any stored reserves (TODO: revise this approach)
			double fSugar = 0.5 * MathUtilities.Divide(myDGrowthActual, myDMShootGreen, 0.0);

			//Live
			double digestLive = 0.0;
		    double Ngreen = myLeaves.NGreen + myStems.NGreen + myStolons.NGreen;
			if (myDMShootGreen > 0.0 & Ngreen > 0.0)
			{
				double CNlive = MathUtilities.Divide(myDMShootGreen * CarbonFractionInDM, Ngreen, 0.0);   //CN ratio of live shoot tissue
				double ratio1 = CNratioCellWall / CNlive;
				double ratio2 = CNratioCellWall / CNratioProtein;
				double fProteinLive = (ratio1 - (1 - fSugar)) / (ratio2 - 1);          //Fraction of protein in living shoot
				double fWallLive = 1 - fSugar - fProteinLive;                          //Fraction of cell wall in living shoot
				digestLive = fSugar + fProteinLive + (myDigestibilityLive * fWallLive);
			}

			//Dead
			double digestDead = 0;
			if (myDMShootDead > 0.0 && (myLeaves.tissue[3].DM + myStems.tissue[3].DM) > 0.0)
			{
				double CNdead = MathUtilities.Divide(myDMShootDead * CarbonFractionInDM, myLeaves.tissue[3].Namount + myStems.tissue[3].Namount, 0.0);   //CN ratio of standing dead;
				double ratio1 = CNratioCellWall / CNdead;
				double ratio2 = CNratioCellWall / CNratioProtein;
				double fProteinDead = (ratio1 - 1) / (ratio2 - 1);          //Fraction of protein in standing dead
				double fWallDead = 1 - fProteinDead;                        //Fraction of cell wall in standing dead
				digestDead = fProteinDead + myDigestibilityDead * fWallDead;
			}

			double deadFrac = MathUtilities.Divide(myDMShootDead, myLeaves.tissue[3].DM + myStems.tissue[3].DM, 1.0);
			double result = (1 - deadFrac) * digestLive + deadFrac * digestDead;

			return result;
		}

		#endregion

		#region - Plant growth processes  ----------------------------------------------------------------------------------

		/// <summary>
		/// Computes the variations in root depth, including the layer containing the root frontier (for annuals only)
		/// </summary>
		/// <remarks>
		/// For perennials, the root depth and distribution are set at initialisation and do not change throughtout the simulation
		/// </remarks>
		private void EvaluateRootGrowth()
		{
			if (myIsAnnual)
			{
				//considering root distribution change, here?
				myRootDepth = myDRootDepth + (myMaxRootDepth - myDRootDepth) * myDaysfromEmergence / myDaysEmgToAnth;

				// get new layer for root frontier
				double cumDepth = 0.0;
				for (int layer = 0; layer < mySoil.Thickness.Length; layer++)
				{
					cumDepth += mySoil.Thickness[layer];
					if (cumDepth <= myRootDepth)
					{
						myRootFrontier = layer;
						myRootExplorationFactor[layer] = LayerFractionWithRoots(layer);
					}
					else
					{
						layer = mySoil.Thickness.Length;
					}
				}
			}
			// else:  both myRootDepth and myRootFrontier have been set at initialisation and do not change
		}

		/// <summary>Computes the plant's gross potential growth rate</summary>
		/// <returns>The potential amount of C assimilated via photosynthesis (kgC/ha)</returns>
		private double DailyGrossPotentialGrowth()
		{
			// 1. compute photosynthesis rate per leaf area

			// to be moved to parameter section
			// Photochemical, or photosynthetic, efficiency (mg CO2/J) - typically with small variance and little effect
			const double alpha = 0.01;
			// Photosynthesis curvature parameter (J/kg^2/s) - typically with small variance and little effect
			const double theta = 0.8;

			// Temp effects to Pmax
			double effTemp1 = TemperatureLimitingFactor(myTmean);
			double effTemp2 = TemperatureLimitingFactor(myTmeanW);

			// CO2 effects on Pmax
			double efCO2 = PCO2Effects();

			// N effects on Pmax
			myNCFactor = PmxNeffect();

			// Maximum photosynthetic rate (mg CO2/m^2 leaf/s)
			double Pmax_EarlyLateDay = myReferencePhotosynthesisRate * effTemp1 * efCO2 * myNCFactor;
			double Pmax_MiddleDay = myReferencePhotosynthesisRate * effTemp2 * efCO2 * myNCFactor;

			double myDayLength = 3600 * myMetData.CalculateDayLength(-6);  //conversion of hour to seconds

			// Photosynthetically active radiation, PAR = 0.5*Radn, converted from MJ/m2 to J/2 (10^6)
			double myPAR = 0.5 * RadnIntercepted * 1000000;

			// Irradiance, or radiation, on the canopy at the middle of the day (W/m^2)
			//IL = (4.0 / 3.0) * myPAR * swardLightExtCoeff / myDayLength;  TODO: enable this
			myIL = 1.33333 * myPAR * mySwardLightExtCoeff / myDayLength;
			double IL2 = myIL / 2;                      //IL for early & late period of a day

			// Photosynthesis per LAI under full irradiance at the top of the canopy (mg CO2/m^2 leaf/s)
			double photoAux1 = alpha * myIL + Pmax_MiddleDay;
			double photoAux2 = 4 * theta * alpha * myIL * Pmax_MiddleDay;
			double Pl_MiddleDay = (0.5 / theta) * (photoAux1 - Math.Sqrt(Math.Pow(photoAux1, 2.0) - photoAux2));

			photoAux1 = alpha * IL2 + Pmax_EarlyLateDay;
			photoAux2 = 4 * theta * alpha * IL2 * Pmax_EarlyLateDay;
			double Pl_EarlyLateDay = (0.5 / theta) * (photoAux1 - Math.Sqrt(Math.Pow(photoAux1, 2.0) - photoAux2));

			// Photosynthesis per leaf area for the day (mg CO2/m^2 leaf/day)
			double Pl_Daily = myDayLength * (Pl_MiddleDay + Pl_EarlyLateDay) * 0.5;

			// Photosynthesis for whole canopy, per ground area (mg CO2/m^2/day)
			double Pc_Daily = Pl_Daily * mySwardGreenCover * myRadnIntFrac / myLightExtentionCoeff;

			//  Carbon assimilation per leaf area (g C/m^2/day)
			double CarbonAssim = Pc_Daily * 0.001 * (12.0 / 44.0);         // Convert to from mgCO2 to kgC           

			// Base gross photosynthesis, converted to kg C/ha/day)
			double BaseGrossPhotosynthesis = CarbonAssim * 10;             // convertion = 10000 / 1000

			// Consider the extreme temperature effects (in practice only one temp stress factor is < 1)
			double ExtremeTemperatureFactor = HeatStress() * ColdStress();

			// Actual gross photosynthesis (gross potential growth - kg C/ha/day)
			return BaseGrossPhotosynthesis * ExtremeTemperatureFactor;

			// TODO: implement GLFGeneric...
		}

		/// <summary>Computes the plant's loss of C due to respiration</summary>
		/// <returns>The amount of C lost to atmosphere (kgC/ha)</returns>
		private double DailyMaintenanceRespiration()
		{
			// Temperature effects on respiration
			double Teffect = 0;
			if (myTmean > myGrowthTmin)
			{
				if (myTmean < myGrowthTopt)
				{
					Teffect = TemperatureLimitingFactor(myTmean);
				}
				else
				{
					Teffect = Math.Min(1.25, myTmean / myGrowthTopt);		// Using growthTopt as reference temperature, and maximum of 1.25
					Teffect *= TemperatureLimitingFactor(myGrowthTopt);
				}
			}

			// Total DM converted to C (kg/ha)
			double dmLive = (myDMShootGreen + myDMRoot) * CarbonFractionInDM;
			double result = dmLive * myMaintenanceRespirationCoef * Teffect * myNCFactor;
			return Math.Max(0.0, result);
		}

		/// <summary>Computes the plant's loss of C due to growth respiration</summary>
		/// <returns>The amount of C lost to atmosphere (kgC/ha)</returns>
		private double DailyGrowthRespiration()
		{
			return myPgross * myGrowthRespirationCoef;
		}
		/// <summary>Compute the plant's net potential growth</summary>
		/// <returns>The net potential growth (kg DM/ha)</returns>
		private double DailyNetPotentialGrowth()
		{
			// Net potential growth (C assimilation) for the day (excluding respiration)
			double NetPotGrowth = 0.0;
			NetPotGrowth = (1 - myGrowthRespirationCoef) * (myPgross + myCRemobilised - myResp_m);  // TODO: the respCoeff should only multiply Pgross
			//NetPotGrowth = Pgross + CRemobilised - Resp_g - Resp_m;
			NetPotGrowth = Math.Max(0.0, NetPotGrowth);

			// Net daily potential growth (kg DM/ha)
			NetPotGrowth /= CarbonFractionInDM;

			// phenologically related reduction in growth of annual species (from IJ)
			if (myIsAnnual)
				NetPotGrowth *= annualSpeciesReduction();

			return NetPotGrowth;
		}

		/// <summary>Computes the plant's potential growth rate</summary>
		/// <returns></returns>
		private double DailyActualGrowth()
		{
			// Adjust GLF due to N deficiency. Many plants (grasses) can grow more by reducing the N concentration
			//  in its tissues. This is represented here by reducing the effect of N deficiency using a power function,
			//  when exponent is 1.0, the reduction in growth is proportional to N deficiency; for many plants the value
			//  should be smaller than that. For grasses, the exponent is typically around 0.5.
			double glfNit = Math.Pow(myGLFN, myDillutionCoefN);

			// The generic limitation factor is assumed to be equivalent to a nutrient deficiency, so it is considered here
			myDGrowthActual = myDGrowthWstress * Math.Min(glfNit, myGLFGeneric);   // TODO: uptade the use of GLFGeneric

			return myDGrowthActual;
		}

		/// <summary>Update DM and N amounts of all tissues accounting for the new growth (plus leftover remobilisation)</summary>
		/// <exception cref="System.Exception">
		/// Mass balance lost on partition of new growth DM
		/// or
		/// Mass balance lost on partition of new growth N
		/// </exception>
		private void PartitionNewGrowth()
		{
			// TODO: implement fLeaf
			// Leaf appearance rate, as modified by temp & water stress  -  Not really used, should it??
			//double effTemp = TemperatureLimitingFactor(Tmean);
			//double effWater = Math.Pow(glfWater, 0.33333);
			//double rateLeafGrowth = leafRate * effTemp * effWater;
			//rateLeafGrowth = Math.Max(0.0, Math.Min(1.0, rateLeafGrowth));

			if (myDGrowthActual > 0.0)
			{
				// Fractions of new growth for each plant part (fShoot was calculated in DoPlantGrowth)
				double toLeaf = myFShoot * myFracToLeaf;
				double toStem = myFShoot * (1.0 - myFracToStolon - myFracToLeaf);
				double toStolon = myFShoot * myFracToStolon;
				double toRoot = 1.0 - myFShoot;

				// Checking mass balance
				double ToAll = toLeaf + toStolon + toStem + toRoot;
				if (Math.Abs(ToAll - 1.0) > 0.0001)
					throw new Exception("Mass balance lost on partition of new growth DM");

                // New growth is allocated to the first tissue pools
                myLeaves.tissue[0].DM += toLeaf * myDGrowthActual;
                myStems.tissue[0].DM += toStem * myDGrowthActual;
                myStolons.tissue[0].DM += toStolon * myDGrowthActual;
				myDMRoot += toRoot * myDGrowthActual;
				myDGrowthShoot = (toLeaf + toStem + toStolon) * myDGrowthActual;
				myDGrowthRoot = toRoot * myDGrowthActual;

				// Partitioning N based on DM fractions and on max [N] in plant parts
				double Nsum = toLeaf * myLeafNmax 
					        + toStem * myNcStemMax
							+ toStolon * myNcStolonMax
							+ toRoot * myNcRootMax;
				double toLeafN = toLeaf * MathUtilities.Divide(myLeafNmax, Nsum, 0.0);
				double toStemN = toStem * MathUtilities.Divide(myNcStemMax, Nsum, 0.0);
				double toStolonN = toStolon * MathUtilities.Divide(myNcStolonMax, Nsum, 0.0);
				double toRootN = toRoot * MathUtilities.Divide(myNcRootMax, Nsum, 0.0);

				// Checking mass balance
				ToAll = toRootN + toLeafN + toStolonN + toStemN;
				if (Math.Abs(ToAll - 1.0) > 0.0001)
					throw new Exception("Mass balance lost on partition of new growth N");

                // Allocate N from new growth to the first tissue pools
                myLeaves.tissue[0].DM += toLeafN * myNewGrowthN;
                myStems.tissue[0].DM += toStemN * myNewGrowthN;
                myStolons.tissue[0].DM += toStolonN * myNewGrowthN;
				myNRoot += toRootN * myNewGrowthN;

				// Fraction of Nremob not used in new growth that is returned to dead tissue
				double leftoverNremob = myNRemobilised * myKappaNRemob4;
				if ((leftoverNremob > 0.0) && (myPrevState.leaves.tissue[3].Namount + myPrevState.stems.tissue[3].Namount > 0.0))
				{
					Nsum = myPrevState.leaves.tissue[3].Namount + myPrevState.stems.tissue[3].Namount;
                    myLeaves.tissue[3].Namount += leftoverNremob * MathUtilities.Divide(myPrevState.leaves.tissue[3].Namount, Nsum, 0.0);
                    myStems.tissue[3].Namount += leftoverNremob * MathUtilities.Divide(myPrevState.stems.tissue[3].Namount, Nsum, 0.0);
					// Note: this is only valid for leaf and stems, the remaining (1-kappaNRemob4) and the amounts in roots
					//  and stolon is disposed (added to soil FOM or Surface OM via litter)
				}

				// Check whether luxury N was remobilised during N balance
				if (myNFastRemob2 + myNFastRemob3 > 0.0)
				{
					// If N was remobilised, update the N content in tissues accordingly
					//  partition between parts is assumed proportional to N content
					if (myNFastRemob2 > 0.0)
					{
						Nsum = myPrevState.leaves.tissue[1].Namount + myPrevState.stems.tissue[1].Namount + myPrevState.stolons.tissue[1].Namount;
                        myLeaves.tissue[1].Namount += myNFastRemob2 * MathUtilities.Divide(myPrevState.leaves.tissue[1].Namount, Nsum, 0.0);
                        myStems.tissue[1].Namount += myNFastRemob2 * MathUtilities.Divide(myPrevState.stems.tissue[1].Namount, Nsum, 0.0);
                        myStolons.tissue[1].Namount += myNFastRemob2 * MathUtilities.Divide(myPrevState.stolons.tissue[1].Namount, Nsum, 0.0);
					}
					if (myNFastRemob3 > 0.0)
					{
						Nsum = myPrevState.leaves.tissue[2].Namount + myPrevState.stems.tissue[2].Namount + myPrevState.stolons.tissue[2].Namount;
                        myLeaves.tissue[2].Namount += myNFastRemob3 * MathUtilities.Divide(myPrevState.leaves.tissue[2].Namount, Nsum, 0.0);
                        myStems.tissue[2].Namount += myNFastRemob3 * MathUtilities.Divide(myPrevState.stems.tissue[2].Namount, Nsum, 0.0);
                        myStolons.tissue[2].Namount += myNFastRemob3 * MathUtilities.Divide(myPrevState.stolons.tissue[2].Namount, Nsum, 0.0);
					}
				}
			}
			else
			{
				// no actuall growth, just zero out some variables
				myDGrowthShoot = 0.0;
				myDGrowthRoot = 0.0;
			}
		}

		/// <summary>Computes the fraction of today's growth allocated to shoot</summary>
		/// <returns>The fraction of DM growth allocated to shoot (0-1)</returns>
		/// <remarks>
		/// Takes into consideration any seasonal variations and defoliation, this is done by
		/// targeting a given shoot:root ratio (that is the maxSRratio)
		/// </remarks>
		private double ToShootFraction()
		{
			double result = 1.0;

			if (myPrevState.dmRoot > 0.00001 || myDMShootGreen < myPrevState.dmRoot)
			{
				double fac = 1.0;
				int doyIncrease = myDOYIniHighShoot + myHigherShootAllocationPeriods[0];  //35;   //75
				int doyPlateau = doyIncrease + myHigherShootAllocationPeriods[1];   // 95;   // 110;
				int doyDecrease = doyPlateau + myHigherShootAllocationPeriods[2];  // 125;  // 140;
				int doy = myClock.Today.DayOfYear;

				if (doy > myDOYIniHighShoot)
				{
					if (doy < doyIncrease)
						fac = 1 + myShootSeasonalAllocationIncrease * MathUtilities.Divide(doy - myDOYIniHighShoot, myHigherShootAllocationPeriods[0], 0.0);
					else if (doy <= doyPlateau)
						fac = 1.0 + myShootSeasonalAllocationIncrease;
					else if (doy <= doyDecrease)
						fac = 1 + myShootSeasonalAllocationIncrease * (1 - MathUtilities.Divide(doy - doyPlateau, myHigherShootAllocationPeriods[2], 0.0));
					else
						fac = 1;
				}
				else
				{
					if (doyDecrease > 365 && doy <= doyDecrease - 365)
						fac = 1 + myShootSeasonalAllocationIncrease * (1 - MathUtilities.Divide(365 + doy - doyPlateau, myHigherShootAllocationPeriods[2], 0.0));
				}

				double presentSRratio = myDMShootGreen / myPrevState.dmRoot;
				double targetedSRratio = fac * myMaxSRratio;
				double newSRratio;

				if (presentSRratio > targetedSRratio)
					newSRratio = targetedSRratio;
				else
					newSRratio = targetedSRratio * (targetedSRratio / presentSRratio);

				newSRratio *= Math.Min(myGLFWater, myGLFN);

				result = newSRratio / (1.0 + newSRratio);

				if (result / (1 - result) < targetedSRratio)
					result = targetedSRratio / (1 + targetedSRratio);
			}

			return result;
		}

		/// <summary>Tentative - correction for the fraction of DM allocated to leaves</summary>
		/// <returns></returns>
		private double UpdatefLeaf()
		{
			double result;
			if (myIsLegume)
			{
				if (myDMShootGreen > 0.0 && (myStolons.DMGreen / myDMShootGreen) > myFracToStolon)
					result = 1.0;
				else if (myDMShootGreen + myStolons.DMGreen < 2000)
					result = myFracToLeaf + (1 - myFracToLeaf) * (myDMShootGreen + myStolons.DMGreen) / 2000;
				else
					result = myFracToLeaf;
			}
			else
			{
				if (myDMShootGreen < 2000)
					result = myFracToLeaf + (1 - myFracToLeaf) * myDMShootGreen / 2000;
				else
					result = myFracToLeaf;
			}
			return result;
		}

	    /// <summary>Computes the turnover rate and update each tissue pool of all plant parts</summary>
	    /// <exception cref="System.Exception">
	    /// Loss of mass balance on C remobilisation - leaf
	    /// or
	    /// Loss of mass balance on C remobilisation - stem
	    /// or
	    /// Loss of mass balance on C remobilisation - stolon
	    /// or
	    /// Loss of mass balance on C remobilisation - root
	    /// </exception>
	    /// <remarks>The C and N amounts for remobilisation are also computed in here</remarks>
	    private void TissueTurnoverAndRemobilisation()
	    {
	        // The turnover rates are affected by temperature and soil moisture
	        double TempFac = TempFactorForTissueTurnover(myTmean);
	        double WaterFac = WaterFactorForTissueTurnover();
	        double WaterFac2Litter = Math.Pow(myGLFWater, 3);
	        double WaterFac2Root = 2 - myGLFWater;
	        double SR = 0; //stocking rate affecting transfer of dead to litter (default as 0 for now - should be read in)
	        double StockFac2Litter = myStockParameter * SR;

	        // Turnover rate for leaf and stem
	        myGama = myTurnoverRateLive2Dead * TempFac * WaterFac;

	        // Turnover rate for stolon
	        myGamaS = myGama;

	        //double gamad = gftt * gfwt * rateDead2Litter;

	        // Turnover rate for dead to litter (TODO: check the use of digestibility here)
	        myGamaD = myTurnoverRateDead2Litter * WaterFac2Litter * myDigestibilityDead / 0.4;
	        myGamaD += StockFac2Litter;

	        // Turnover rate for roots
	        myGamaR = myTurnoverRateRootSenescence * TempFac * WaterFac2Root;


	        if (myGama > 0.0)
	        {
	            // there is some tissue turnover
	            if (myIsAnnual)
	            {
	                if (myPhenoStage == 1)
	                {
	                    //vegetative
	                    myGama *= MathUtilities.Divide(myDaysfromEmergence, myDaysEmgToAnth, 1.0);
	                    myGamaR *= MathUtilities.Divide(myDaysfromEmergence, myDaysEmgToAnth, 1.0);
	                }
	                else if (myPhenoStage == 2)
	                {
	                    //reproductive
	                    myGama = 1 -
	                           (1 - myGama) * (1 - Math.Pow(MathUtilities.Divide(myDaysfromAnthesis, myDaysToMature, 1.0), 2));
	                }
	            }

	            // Fraction of DM defoliated today
	            double FracDefoliated = MathUtilities.Divide(myDefoliatedDM,
	                myDefoliatedDM + myPrevState.leaves.DMTotal + myPrevState.stems.DMTotal + myPrevState.stolons.DMTotal, 0.0);

	            // Adjust stolon turnover due to defoliation (increase stolon senescence)
	            myGamaS += FracDefoliated * (1 - myGama);

	            // Check whether todays senescence will result in dmShootGreen < dmGreenmin
	            //   if that is the case then adjust (reduce) the turnover rates
	            // TODO: possibly should skip this for annuals to allow them to die - phenololgy-related?

	            // TODO: here it should be dGrowthShoot, not total (will fix after tests)
	            //double dmGreenToBe = dmShootGreen + dGrowthShoot - gama * (prevState.dmLeaf3 + prevState.dmStem3 + prevState.dmStolon3);
	            double dmGreenToBe = myDMShootGreen + myDGrowthActual -
	                                 myGama * (myPrevState.leaves.tissue[3].DM + myPrevState.stems.tissue[3].DM + myPrevState.stolons.tissue[3].DM);
	            if (dmGreenToBe < myMinimumGreenWt)
	            {
	                if (myDMShootGreen + myDGrowthShoot < myMinimumGreenWt)
	                {
	                    // this should not happen anyway
	                    myGama = 0.0;
	                    myGamaS = 0.0;
	                    myGamaR = 0.0;
	                }
	                else
	                {
	                    double gama_adj = MathUtilities.Divide(myDMShootGreen + myDGrowthShoot - myMinimumGreenWt,
                            myPrevState.leaves.tissue[3].DM + myPrevState.stems.tissue[3].DM + myPrevState.stolons.tissue[3].DM, myGama);
	                    myGamaR *= gama_adj / myGama;
	                    myGamaD *= gama_adj / myGama;
	                    myGama = gama_adj;
	                }
	            }
	            if (myDMRoot < 0.5 * myMinimumGreenWt) // set a minimum root too, probably not really needed
	                myGamaR = 0;

	            // Do the actual DM turnover for all tissues
	            double facGrowingTissue = 2.0; //TODO: move this to parameter list
	            // Stems
	            double DMfrom1to2 = facGrowingTissue * myGama * myPrevState.leaves.tissue[0].DM;
	            double DMfrom2to3 = myGama * myPrevState.leaves.tissue[1].DM;
	            double DMfrom3to4 = myGama * myPrevState.leaves.tissue[2].DM;
	            double DMfrom4toL = myGamaD * myPrevState.leaves.tissue[3].DM;

	            double ChRemobSugar = DMfrom3to4 * myKappaCRemob;
	            double ChRemobProtein = DMfrom3to4 * (myPrevState.leaves.tissue[2].Nconc - myLeafNmin) * CNratioProtein *
	                                    myFacCNRemob;
	            if (DMfrom3to4 - ChRemobSugar - ChRemobProtein < -myEpsilon)
	            {
	                ChRemobSugar = DMfrom3to4 * ChRemobSugar / (ChRemobSugar + ChRemobProtein);
	                ChRemobProtein = DMfrom3to4 * ChRemobProtein / (ChRemobSugar + ChRemobProtein);
	                DMfrom3to4 = 0.0;
	            }
	            else
	            {
	                DMfrom3to4 -= ChRemobSugar + ChRemobProtein;
	            }

                myLeaves.tissue[0].DM += 0.0 - DMfrom1to2; // growth has been accounted for in PartitionNewGrowth
                myLeaves.tissue[1].DM += DMfrom1to2 - DMfrom2to3;
                myLeaves.tissue[2].DM += DMfrom2to3 - DMfrom3to4;
                myLeaves.tissue[3].DM += DMfrom3to4 - DMfrom4toL;
	            myDGrowthShoot -= DMfrom4toL;
	            myDLitter = DMfrom4toL;
	            double ChRemobl = ChRemobSugar + ChRemobProtein;

	            double Nfrom1to2 = DMfrom1to2 * myPrevState.leaves.tissue[0].Nconc;
	            double Nfrom2to3 = DMfrom2to3 * myPrevState.leaves.tissue[1].Nconc;
	            double Nfrom3to4 = DMfrom3to4 * myLeafNmin;
	            double Nfrom4toL = DMfrom4toL * myPrevState.leaves.tissue[3].Nconc;

	            myNleaf3Remob = DMfrom3to4 * (myPrevState.leaves.tissue[2].Nconc - myLeafNmin);

                myLeaves.tissue[0].Namount += 0.0 - Nfrom1to2;
                myLeaves.tissue[1].Namount += Nfrom1to2 - Nfrom2to3;
                myLeaves.tissue[2].Namount += Nfrom2to3 - Nfrom3to4 - myNleaf3Remob;
                myLeaves.tissue[3].Namount += Nfrom3to4 - Nfrom4toL;
	            myDNlitter = Nfrom4toL;

	            // Stems
	            DMfrom1to2 = facGrowingTissue * myGama * myPrevState.stems.tissue[0].DM;
	            DMfrom2to3 = myGama * myPrevState.stems.tissue[1].DM;
	            DMfrom3to4 = myGama * myPrevState.stems.tissue[2].DM;
	            DMfrom4toL = myGamaD * myPrevState.stems.tissue[3].DM;
	            ChRemobSugar = DMfrom3to4 * myKappaCRemob;
	            ChRemobProtein = DMfrom3to4 * (myPrevState.stems.tissue[2].Nconc - myLeafNmin) * CNratioProtein * myFacCNRemob;
	            if (DMfrom3to4 - ChRemobSugar - ChRemobProtein < -myEpsilon)
	            {
	                ChRemobSugar = DMfrom3to4 * ChRemobSugar / (ChRemobSugar + ChRemobProtein);
	                ChRemobProtein = DMfrom3to4 * ChRemobProtein / (ChRemobSugar + ChRemobProtein);
	                DMfrom3to4 = 0.0;
	            }
	            else
	            {
	                DMfrom3to4 -= ChRemobSugar + ChRemobProtein;
	            }

	            myStems.tissue[0].DM += 0.0 - DMfrom1to2; // DM in was considered in PartitionDMGrown()
	            myStems.tissue[1].DM += DMfrom1to2 - DMfrom2to3;
	            myStems.tissue[2].DM += DMfrom2to3 - DMfrom3to4;
	            myStems.tissue[3].DM += DMfrom3to4 - DMfrom4toL;
	            myDGrowthShoot -= DMfrom4toL;
	            myDLitter += DMfrom4toL;
	            ChRemobl += ChRemobSugar + ChRemobProtein;

	            Nfrom1to2 = myPrevState.stems.tissue[0].Nconc * DMfrom1to2;
	            Nfrom2to3 = myPrevState.stems.tissue[1].Nconc * DMfrom2to3;
	            Nfrom3to4 = myNcStemMin * DMfrom3to4;
	            Nfrom4toL = myPrevState.stems.tissue[3].Nconc * DMfrom4toL;
	            myNstem3Remob = (myPrevState.stems.tissue[2].Nconc - myNcStemMin) * DMfrom3to4;

	            myStems.tissue[0].Namount += 0.0 - Nfrom1to2; // N in was considered in PartitionDMGrown()
	            myStems.tissue[1].Namount += Nfrom1to2 - Nfrom2to3;
	            myStems.tissue[2].Namount += Nfrom2to3 - Nfrom3to4 - myNstem3Remob;
	            myStems.tissue[3].Namount += Nfrom3to4 - Nfrom4toL;
	            myDNlitter += Nfrom4toL;

	            // Stolons
	            if (myIsLegume)
	            {
	                DMfrom1to2 = facGrowingTissue * myGamaS * myPrevState.stolons.tissue[0].DM;
	                DMfrom2to3 = myGamaS * myPrevState.stolons.tissue[1].DM;
	                double DMfrom3toL = myGamaS * myPrevState.stolons.tissue[2].DM;
	                ChRemobSugar = DMfrom3toL * myKappaCRemob;
	                ChRemobProtein = DMfrom3toL * (myPrevState.stolons.tissue[2].Nconc - myLeafNmin) * CNratioProtein *
	                                 myFacCNRemob;
	                if (DMfrom3toL - ChRemobSugar - ChRemobProtein < -myEpsilon)
	                {
	                    ChRemobSugar = DMfrom3toL * ChRemobSugar / (ChRemobSugar + ChRemobProtein);
	                    ChRemobProtein = DMfrom3toL * ChRemobProtein / (ChRemobSugar + ChRemobProtein);
	                    DMfrom3toL = 0.0;
	                }
	                else
	                {
	                    DMfrom3toL -= ChRemobSugar + ChRemobProtein;
	                }

	                myStolons.tissue[0].DM += 0.0 - DMfrom1to2; // DM in was considered in PartitionDMGrown()
	                myStolons.tissue[1].DM += DMfrom1to2 - DMfrom2to3;
	                myStolons.tissue[2].DM += DMfrom2to3 - DMfrom3toL;
	                myDGrowthShoot -= DMfrom3toL;
	                myDLitter += DMfrom3toL;
	                ChRemobl += ChRemobSugar + ChRemobProtein;

	                Nfrom1to2 = myPrevState.stolons.tissue[0].Nconc * DMfrom1to2;
	                Nfrom2to3 = myPrevState.stolons.tissue[1].Nconc * DMfrom2to3;
	                Nfrom3to4 = 0.5 * (myPrevState.stolons.tissue[2].Nconc + myNcStolonMin) * DMfrom3toL;
	                myNstol3Remob = 0.5 * (myPrevState.stolons.tissue[2].Nconc - myNcStolonMin) * DMfrom3toL;

	                myStolons.tissue[0].Namount += 0.0 - Nfrom1to2; // N in was considered in PartitionDMGrown()
	                myStolons.tissue[1].Namount += Nfrom1to2 - Nfrom2to3;
	                myStolons.tissue[2].Namount += Nfrom2to3 - Nfrom3to4 - myNstol3Remob;
	                myDNlitter += Nfrom3to4;

	                // Add stuff from dead material (should only have values if KillCrop was used)
	                if (myStolons.tissue[3].DM > 0.0)
	                {
	                    myDLitter += myStolons.tissue[3].DM;
	                    myDNlitter += myStolons.tissue[3].Namount;
	                    myStolons.tissue[3].DM = 0.0;
	                    myStolons.tissue[3].Namount = 0.0;
	                }
	            }

	            // Roots
	            myDRootSen = myGamaR * myPrevState.roots.tissue[0].DM;
	            myRoots.tissue[0].DM -= myDRootSen;
	            ChRemobSugar = myDRootSen * myKappaCRemob;
	            ChRemobProtein = myDRootSen * (myPrevState.roots.tissue[2].Nconc - myLeafNmin) * CNratioProtein * myFacCNRemob;
	            if (myDRootSen - ChRemobSugar - ChRemobProtein < -myEpsilon)
	            {
	                ChRemobSugar = myDRootSen * ChRemobSugar / (ChRemobSugar + ChRemobProtein);
	                ChRemobProtein = myDRootSen * ChRemobProtein / (ChRemobSugar + ChRemobProtein);
	                myDRootSen = 0.0;
	            }
	            else
	            {
	                myDRootSen -= ChRemobSugar + ChRemobProtein;
	            }

	            ChRemobl += ChRemobSugar + ChRemobProtein;

	            myNrootRemob = 0.5 * (myPrevState.roots.tissue[0].Nconc - myNcRootMin) * myDRootSen;
	            myDNrootSen = myPrevState.roots.tissue[0].Nconc * myDRootSen - myNrootRemob;
	            myRoots.tissue[0].Namount -= myPrevState.roots.tissue[0].Nconc * myDRootSen;

	            // Add stuff from dead material (should only have values if KillCrop was used)
	            if (myRoots.tissue[3].DM > 0.0)
	            {
	                myDRootSen += myRoots.tissue[3].DM;
	                myDNrootSen += myRoots.tissue[3].Namount;
	                myRoots.tissue[3].DM = 0.0;
	                myRoots.tissue[3].Namount = 0.0;
	            }

	            // Remobilised C to be used in tomorrow's growth (converted from carbohydrate to C)
	            myCRemobilised = ChRemobl * CarbonFractionInDM;

	            // Fraction of N remobilised yesterday that not used in new growth
	            //  it is added to today's litter
	            double leftoverNremob = myNRemobilised * (1 - myKappaNRemob4);
	            myDNlitter += leftoverNremob;

	            // N remobilised to be potentially used for growth tomorrow
	            myNRemobilised = myNleaf3Remob + myNstem3Remob + myNstol3Remob + myNrootRemob;
	            myPrevState.Nremob = myNRemobilised;
	            myNLuxury2 = Math.Max(0.0, myLeaves.tissue[1].Nconc - LeafNopt * myRelativeNStage2) * myLeaves.tissue[1].DM +
	                       Math.Max(0.0, myStems.tissue[1].Nconc - myNcStemOpt * myRelativeNStage2) * myStems.tissue[1].DM +
	                       Math.Max(0.0, myStolons.tissue[1].Nconc - myNcStolonOpt * myRelativeNStage2) * myStolons.tissue[1].DM;
	            myNLuxury3 = Math.Max(0.0, myLeaves.tissue[2].Nconc - LeafNopt * myRelativeNStage3) * myLeaves.tissue[2].DM +
	                       Math.Max(0.0, myStems.tissue[2].Nconc - myNcStemOpt * myRelativeNStage3) * myStems.tissue[2].DM +
	                       Math.Max(0.0, myStolons.tissue[2].Nconc - myNcStolonOpt * myRelativeNStage3) * myStolons.tissue[2].DM;
	            // only a fraction of luxury N is available for remobilisation:
	            myNLuxury2 *= myKappaNRemob2;
	            myNLuxury3 *= myKappaNRemob3;
	        }
	        else
	        {
	            // No turnover, just zero out some variables
	            myDLitter = 0.0;
	            myDNlitter = 0.0;
	            myDRootSen = 0.0;
	            myDNrootSen = 0.0;
	            myCRemobilised = 0.0;
	            myNRemobilised = 0.0;
	        }
	        // N remobilisable from luxury N to be potentially used for growth tomorrow
	        myNLuxury2 = Math.Max(0.0, (myLeaves.tissue[1].Nconc - myLeafNopt * myRelativeNStage3) * myLeaves.tissue[1].DM) +
                       Math.Max(0.0, (myStems.tissue[1].Nconc - myNcStemOpt * myRelativeNStage3) * myStems.tissue[1].DM) +
                       Math.Max(0.0, (myStolons.tissue[1].Nconc - myNcStolonOpt * myRelativeNStage3) * myStolons.tissue[1].DM);
	        myNLuxury3 = Math.Max(0.0, (myLeaves.tissue[2].Nconc - myLeafNopt * myRelativeNStage3) * myLeaves.tissue[2].DM) +
                       Math.Max(0.0, (myStems.tissue[2].Nconc - myNcStemOpt * myRelativeNStage3) * myStems.tissue[2].DM) +
                       Math.Max(0.0, (myStolons.tissue[2].Nconc - myNcStolonOpt * myRelativeNStage3) * myStolons.tissue[2].DM);
	        // only a fraction of luxury N is actually available for remobilisation:
	        myNLuxury2 *= myKappaNRemob2;
	        myNLuxury3 *= myKappaNRemob3;
	    }

	    #endregion

		#region - Water uptake processes  ----------------------------------------------------------------------------------

		/// <summary>Gets the water uptake for each layer as calculated by an external module (SWIM)</summary>
		/// <param name="SoilWater">The soil water.</param>
		/// <remarks>
		/// This method is only used when an external method is used to compute water uptake (this includes AgPasture)
		/// </remarks>
		[EventSubscribe("WaterUptakesCalculated")]
		private void OnWaterUptakesCalculated(PMF.WaterUptakesCalculatedType SoilWater)
		{
			for (int iCrop = 0; iCrop < SoilWater.Uptakes.Length; iCrop++)
			{
				if (SoilWater.Uptakes[iCrop].Name == Name)
				{
					for (int layer = 0; layer < SoilWater.Uptakes[iCrop].Amount.Length; layer++)
						mySoilWaterTakenUp[layer] = SoilWater.Uptakes[iCrop].Amount[layer];
				}
			}
		}
		
		/// <summary>
		/// Gets the amount of water uptake for this species as computed by the resource Arbitrator
		/// </summary>
		private void GetWaterUptake()
		{
			Array.Clear(mySoilWaterTakenUp, 0, mySoilWaterTakenUp.Length);
			for (int layer = 0; layer <= myRootFrontier; layer++)
				mySoilWaterTakenUp[layer] = uptakeWater[layer];
		}

		/// <summary>
		/// Consider water uptake calculations (plus GLFWater)
		/// </summary>
		internal void DoWaterCalculations()
		{
			if (myWaterUptakeSource == "species")
			{ // this module will compute water uptake
				MyWaterCalculations();

				// get the drought effects
				myGLFWater = WaterDeficitFactor();
				// get the water logging effects (only if there is no drought effect)
				if (myGLFWater > 0.999)
					myGLFWater = WaterLoggingFactor();
			}
			//else if myWaterUptakeSource == "AgPasture"
			//      myWaterDemand should have been supplied by MicroClimate (supplied as PotentialEP)
			//      water supply is hold by AgPasture only
			//      myWaterUptake should have been computed by AgPasture (set directly)
			//      glfWater is computed and set by AgPasture
			else if (myWaterUptakeSource == "arbitrator")
			{ // water uptake has been calcualted by the resource arbitrator

				// get the array with the amount of water taken up
				GetWaterUptake();

				// get the drought effects
				myGLFWater = WaterDeficitFactor();
				// get the water logging effects (only if there is no drought effect)
				if (myGLFWater > 0.999)
					myGLFWater = WaterLoggingFactor();
			}
			//else
			//      water uptake be calculated by other modules (e.g. SWIM) and supplied as
			//  Note: when AgPasture is doing the water uptake, it can do it using its own calculations or other module's...
		}

		/// <summary>
		/// Gather the amount of available eater and computes the water uptake for this species
		/// </summary>
		/// <remarks>
		/// Using this routine is discourage as it ignores the presence of other species and thus
		/// might result in loss of mass balance or unbalanced supply, i.e. over-supply for one
		/// while under-supply for other species (depending on the order that species are considered)
		/// </remarks>
		private void MyWaterCalculations()
		{
			mySoilAvailableWater = GetSoilAvailableWater();
			// myWaterDemand given by MicroClimate
			if (myWaterUptakeSource.ToLower() == "species")
				mySoilWaterTakenUp = DoSoilWaterUptake();
			//else
			//    uptake is controlled by the sward or by another apsim module
		}

		/// <summary>
		/// Finds out the amount soil water available for this plant (ignoring any other species)
		/// </summary>
		/// <returns>The amount of water available to plants in each layer</returns>
		internal double[] GetSoilAvailableWater()
		{
			double[] result = new double[myNLayers];
			SoilCrop soilCropData = (SoilCrop)mySoil.Crop(Name);
			if (useAltWUptake == "no")
			{
				for (int layer = 0; layer <= myRootFrontier; layer++)
				{
					result[layer] = Math.Max(0.0, mySoil.Water[layer] - soilCropData.LL[layer] * mySoil.Thickness[layer])
								  * LayerFractionWithRoots(layer);
					result[layer] *= soilCropData.KL[layer];
				}
			}
			else
			{ // Method implemented by RCichota
				// Available Water is function of root density, soil water content, and soil hydraulic conductivity
				// Assumptions: all factors are exponential functions and vary between 0 and 1;
				//   - If root density is equal to ReferenceRLD then plant can explore 90% of the water;
				//   - If soil Ksat is equal to ReferenceKSuptake then soil can supply 90% of its available water;
				//   - If soil water content is at DUL then 90% of its water is available;
				double[] myRLD = RLD;
				double facRLD = 0.0;
				double facCond = 0.0;
				double facWcontent = 0.0;
				for (int layer = 0; layer <= myRootFrontier; layer++)
				{
					facRLD = 1 - Math.Pow(10, -myRLD[layer] / ReferenceRLD);
					facCond = 1 - Math.Pow(10, -mySoil.KS[layer] / ReferenceKSuptake);
					facWcontent = 1 - Math.Pow(10,
								-(Math.Max(0.0, mySoil.Water[layer] - mySoil.SoilWater.LL15mm[layer]))
								/ (mySoil.SoilWater.DULmm[layer] - mySoil.SoilWater.LL15mm[layer]));

					// Theoretical total available water
					result[layer] = Math.Max(0.0, mySoil.Water[layer] - soilCropData.LL[layer] * mySoil.Thickness[layer])
								  * LayerFractionWithRoots(layer);
					// Actual available water
					result[layer] *= facRLD * facCond * facWcontent;
				}
			}

			return result;
		}

		/// <summary>Computes the actual water uptake and send the deltas to soil module</summary>
		/// <returns>The amount of water taken up for each soil layer</returns>
		/// <exception cref="System.Exception">Error on computing water uptake</exception>
		private double[] DoSoilWaterUptake()
		{
			PMF.WaterChangedType WaterTakenUp = new PMF.WaterChangedType();
			WaterTakenUp.DeltaWater = new double[myNLayers];

			double uptakeFraction = Math.Min(1.0, MathUtilities.Divide(myWaterDemand, mySoilAvailableWater.Sum(), 0.0));
			double[] result = new double[myNLayers];

			if (useAltWUptake == "no")
			{
				for (int layer = 0; layer <= myRootFrontier; layer++)
				{
					result[layer] = mySoilAvailableWater[layer] * uptakeFraction;
					WaterTakenUp.DeltaWater[layer] = -result[layer];
				}
			}
			else
			{ // Method implemented by RCichota
				// Uptake is distributed over the profile according to water availability,
				//  this means that water status and root distribution have been taken into account

				for (int layer = 0; layer <= myRootFrontier; layer++)
				{
					result[layer] = mySoilAvailableWater[layer] * uptakeFraction;
					WaterTakenUp.DeltaWater[layer] = -result[layer];
				}
				if (Math.Abs(WaterTakenUp.DeltaWater.Sum() + myWaterDemand) > 0.0001)
					throw new Exception("Error on computing water uptake");
			}

			// send the delta water taken up
			WaterChanged.Invoke(WaterTakenUp);

			return result;
		}

		#endregion

		#region - Nitrogen uptake processes  -------------------------------------------------------------------------------

		/// <summary>
		/// Gets the amount of nitrogen uptake for this species as computed by the resource Arbitrator
		/// </summary>
		private void GetNitrogenUptake()
		{
			// get N demand (optimum and luxury)
			CalcNDemand();

			// get N fixation
			myNfixation = CalcNFixation();

			// evaluate the use of N remobilised and get soil N demand
			CalcSoilNDemand();

			// get the amount of N taken up from soil
			Array.Clear(mySoilNitrogenTakenUp, 0, mySoilNitrogenTakenUp.Length);
			mySoilNuptake = 0.0;
			for (int layer = 0; layer <= myRootFrontier; layer++)
			{
				mySoilNitrogenTakenUp[layer] = uptakeNitrogen[layer];
				mySoilNuptake += mySoilNitrogenTakenUp[layer];
			}
			myNewGrowthN = myNfixation + myNremob2NewGrowth + mySoilNuptake;

			// evaluate whether further remobilisation (from luxury N) is needed
			CalcNLuxuryRemob();
			myNewGrowthN += myNFastRemob3 + myNFastRemob2;
		}

		/// <summary>
		/// Consider nitrogen uptake calculations (plus GLFN)
		/// </summary>
		internal void DoNitrogenCalculations()
		{
			if (myNitrogenUptakeSource == "species")
			{ // this module will compute the N uptake
				MyNitrogenCalculations();
				if (myNewGrowthN > 0.0)
					myGLFN = Math.Min(1.0, Math.Max(0.0, MathUtilities.Divide(myNewGrowthN, myNdemandOpt, 1.0)));
				else
					myGLFN = 1.0;
			}
			//else if (myNitrogenUptakeSource == "AgPasture")
			//{
			//    NdemandOpt is called by AgPasture
			//    NdemandLux is called by AgPasture
			//    Nfix is called by AgPasture
			//    myNitrogenSupply is hold by AgPasture
			//    soilNdemand is computed by AgPasture
			//    soilNuptake is computed by AgPasture
			//    remob2NewGrowth is computed by AgPasture
			//}
			else if (myNitrogenUptakeSource == "arbitrator")
			{ // Nitrogen uptake was computed by the resource arbitrator

				// get the amount of N taken up
				GetNitrogenUptake();
				if (myNewGrowthN > 0.0)
					myGLFN = Math.Min(1.0, Math.Max(0.0, MathUtilities.Divide(myNewGrowthN, myNdemandOpt, 1.0)));
				else
					myGLFN = 1.0;
			}
			//else
			//   N uptake is computed by another module (not implemented yet)
		}

		/// <summary>Performs the computations for N balance and uptake</summary>
		private void MyNitrogenCalculations()
		{
			// get soil available N
			if (myNitrogenUptakeSource.ToLower() == "species")
				GetSoilAvailableN();
			//else
			//    N available is computed in another module

			// get N demand (optimum and luxury)
			CalcNDemand();

			// get N fixation
			myNfixation = CalcNFixation();

			// evaluate the use of N remobilised and get soil N demand
			CalcSoilNDemand();

			// get the amount of N taken up from soil
			mySoilNuptake = CalcSoilNUptake();
			myNewGrowthN = myNfixation + myNremob2NewGrowth + mySoilNuptake;

			// evaluate whether further remobilisation (from luxury N) is needed
			CalcNLuxuryRemob();
			myNewGrowthN += myNFastRemob3 + myNFastRemob2;

			// send delta N to the soil model
			DoSoilNitrogenUptake();
		}

		/// <summary>Computes the N demanded for optimum N content as well as luxury uptake</summary>
		internal void CalcNDemand()
		{
			double toRoot = myDGrowthWstress * (1.0 - myFShoot);
			double toStol = myDGrowthWstress * myFShoot * myFracToStolon;
			double toLeaf = myDGrowthWstress * myFShoot * myFracToLeaf;
			double toStem = myDGrowthWstress * myFShoot * (1.0 - myFracToStolon - myFracToLeaf);

			// N demand for new growth, with optimum N (kg/ha)
			myNdemandOpt = toRoot * myNcRootOpt + toStol * myNcStolonOpt + toLeaf * myLeafNopt + toStem * myNcStemOpt;

			// get the factor to reduce the demand under elevated CO2
			double fN = NCO2Effects();
			myNdemandOpt *= fN;

			// N demand for new growth, with luxury uptake (maximum [N])
			myNdemandLux = toRoot * myNcRootMax + toStol * myNcStolonMax + toLeaf * myLeafNmax + toStem * myNcStemMax;
			// It is assumed that luxury uptake is not affected by CO2 variations
		}

		/// <summary>Computes the amount of N fixed from atmosphere</summary>
		/// <returns>The amount of N fixed (kgN/ha)</returns>
		internal double CalcNFixation()
		{
			double result = 0.0;

			if (myClock.Today.Date.Day == 31)
				result = 0.0;

			if (myIsLegume)
			{
				// Start with minimum fixation
				double iniFix = myMinimumNFixation * myNdemandLux;

				// evaluate N stress
				double Nstress = 1.0;
				if (myNdemandLux > 0.0 && (myNdemandLux > mySoilAvailableN.Sum() + iniFix))
					Nstress = MathUtilities.Divide(mySoilAvailableN.Sum(), myNdemandLux - iniFix, 1.0);

				// Update N fixation if under N stress
				if (Nstress < 0.99)
					result = myMaximumNFixation - (myMaximumNFixation - myMinimumNFixation) * Nstress;
				else
					result = myMinimumNFixation;
			}

			return Math.Max(0.0, result) * myNdemandLux;
		}

		/// <summary>Perform preliminary N budget and get soil N demand</summary>
		internal void CalcSoilNDemand()
		{
			if (myNfixation - myNdemandLux > -0.0001)
			{ // N demand is fulfilled by fixation alone
				myNfixation = myNdemandLux;  // should not be needed, but just in case...
				myNremob2NewGrowth = 0.0;
				mySoilNDemand = 0.0;
			}
			else if ((myNfixation + myNRemobilised) - myNdemandLux > -0.0001)
			{ // N demand is fulfilled by fixation plus N remobilised from senescent material
				myNremob2NewGrowth = Math.Max(0.0, myNdemandLux - myNfixation);
				myNRemobilised -= myNremob2NewGrowth;
				mySoilNDemand = 0.0;
			}
			else
			{ // N demand is greater than fixation and remobilisation of senescent, N uptake is needed
				myNremob2NewGrowth = myNRemobilised;
				myNRemobilised = 0.0;
				mySoilNDemand = myNdemandLux - (myNfixation + myNremob2NewGrowth);
			}

			// variable used by arbitrator
			demandNitrogen = mySoilNDemand;
		}

		/// <summary>
		/// Find out the amount of Nitrogen (NH4 and NO3) in the soil available to plants for each soil layer
		/// </summary>
		internal void GetSoilAvailableN()
		{
			mySoilNH4available = new double[myNLayers];
			mySoilNO3available = new double[myNLayers];
			mySoilAvailableN = new double[myNLayers];

			double facWtaken = 0.0;
			for (int layer = 0; layer <= myRootFrontier; layer++)   // TODO: this should be <=
			{
				if (useAltNUptake == "no")
				{
					// simple way, all N in the root zone is available
					mySoilNH4available[layer] = mySoil.NH4N[layer] * LayerFractionWithRoots(layer);
					mySoilNO3available[layer] = mySoil.NO3N[layer] * LayerFractionWithRoots(layer);
				}
				else
				{
					// Method implemented by RCichota,
					// N is available following water and a given 'availability' factor (for each N form) and the fraction of water taken up

					// fraction of available water taken up
					facWtaken = MathUtilities.Divide(mySoilWaterTakenUp[layer],
								Math.Max(0.0, mySoil.Water[layer] - mySoil.SoilWater.LL15mm[layer]), 0.0);

					// Theoretical amount available
					mySoilNH4available[layer] = mySoil.NH4N[layer] * kuNH4 * LayerFractionWithRoots(layer);
					mySoilNO3available[layer] = mySoil.NO3N[layer] * kuNO3 * LayerFractionWithRoots(layer);

					// actual amount available
					mySoilNH4available[layer] *= facWtaken;
					mySoilNO3available[layer] *= facWtaken;
				}
				mySoilAvailableN[layer] = mySoilNH4available[layer] + mySoilNO3available[layer];
			}
		}

		/// <summary>Computes the amount of N to be taken up from the soil</summary>
		/// <returns>The amount of N to be taken up from each soil layer</returns>
		private double CalcSoilNUptake()
		{
			double result;
			if (mySoilNDemand == 0.0)
			{ // No demand, no uptake
				result = 0.0;
			}
			else
			{
				if (mySoilAvailableN.Sum() >= mySoilNDemand)
				{ // soil can supply all remaining N needed
					result = mySoilNDemand;
				}
				else
				{ // soil cannot supply all N needed. Get the available N
					result = mySoilAvailableN.Sum();
				}
			}
			return result;
		}

		/// <summary>Computes the remobilisation of luxury N (from tissues 2 and 3)</summary>
		internal void CalcNLuxuryRemob()
		{
			// check whether N demand for optimum growth has been matched
			if (myNewGrowthN - myNdemandOpt > -0.0001)
			{
				// N demand has been matched, no further remobilisation is needed
				myNFastRemob3 = 0.0;
				myNFastRemob2 = 0.0;
			}
			else
			{
				// all N already considered is not enough for optimum growth, check remobilisation of luxury N
				//  check whether luxury N in plants can be used (luxury uptake is ignored)
				double Nmissing = myNdemandOpt - myNewGrowthN;
				if (Nmissing > myNLuxury2 + myNLuxury3)
				{
					// N luxury is still not enough for optimum growth, use up all there is
					if (myNLuxury2 + myNLuxury3 > 0)
					{
						myNFastRemob3 = myNLuxury3;
						myNFastRemob2 = myNLuxury2;
						Nmissing -= (myNLuxury3 + myNLuxury2);
					}
				}
				else
				{
					// There is luxury N that can be used for optimum growth, get first from tissue 3
					if (Nmissing <= myNLuxury3)
					{
						// tissue 3 is enough
						myNFastRemob3 = Nmissing;
						myNFastRemob2 = 0.0;
						Nmissing = 0.0;
					}
					else
					{
						// get first from tissue 3
						myNFastRemob3 = myNLuxury3;
						Nmissing -= myNLuxury3;

						// remaining from tissue 2
						myNFastRemob2 = Nmissing;
						Nmissing = 0.0;
					}
				}
			}
		}

		/// <summary>
		/// Computes the distribution of N uptake over the soil profile and send the delta to soil module
		/// </summary>
		/// <exception cref="System.Exception">
		/// Error on computing N uptake
		/// or
		/// N uptake source was not recognised. Please specify it as either \"sward\" or \"species\".
		/// </exception>
		private void DoSoilNitrogenUptake()
		{
			if (myNitrogenUptakeSource.ToLower() == "species")
			{
				// check whether there is any uptake
				if (mySoilAvailableN.Sum() > 0.0 && mySoilNuptake > 0.0)
				{

					Soils.NitrogenChangedType NUptake = new Soils.NitrogenChangedType();
					NUptake.Sender = Name;
					NUptake.SenderType = "Plant";
					NUptake.DeltaNO3 = new double[myNLayers];
					NUptake.DeltaNH4 = new double[myNLayers];

					mySoilNitrogenTakenUp = new double[myNLayers];
					double uptakeFraction = 0;

					if (useAltNUptake == "no")
					{
						if (mySoilAvailableN.Sum() > 0.0)
							uptakeFraction = Math.Min(1.0, MathUtilities.Divide(mySoilNuptake, mySoilAvailableN.Sum(), 0.0));

						for (int layer = 0; layer <= myRootFrontier; layer++)
						{
							NUptake.DeltaNH4[layer] = -mySoil.NH4N[layer] * uptakeFraction;
							NUptake.DeltaNO3[layer] = -mySoil.NO3N[layer] * uptakeFraction;

							mySoilNitrogenTakenUp[layer] = -(NUptake.DeltaNH4[layer] + NUptake.DeltaNO3[layer]);
						}
					}
					else
					{ // Method implemented by RCichota,
						// N uptake is distributed considering water uptake and N availability
						double[] fNH4Avail = new double[myNLayers];
						double[] fNO3Avail = new double[myNLayers];
						double[] fWUptake = new double[myNLayers];
						double totNH4Available = mySoilAvailableN.Sum();
						double totNO3Available = mySoilAvailableN.Sum();
						double totWuptake = mySoilWaterTakenUp.Sum();
						for (int layer = 0; layer < myNLayers; layer++)
						{
							fNH4Avail[layer] = Math.Min(1.0, MathUtilities.Divide(mySoilAvailableN[layer], totNH4Available, 0.0));
							fNO3Avail[layer] = Math.Min(1.0, MathUtilities.Divide(mySoilAvailableN[layer], totNO3Available, 0.0));
							fWUptake[layer] = Math.Min(1.0, MathUtilities.Divide(mySoilWaterTakenUp[layer], totWuptake, 0.0));
						}
						double totFacNH4 = fNH4Avail.Sum() + fWUptake.Sum();
						double totFacNO3 = fNO3Avail.Sum() + fWUptake.Sum();
						for (int layer = 0; layer < myNLayers; layer++)
						{
							uptakeFraction = Math.Min(1.0, MathUtilities.Divide(fNH4Avail[layer] + fWUptake[layer], totFacNH4, 0.0));
							NUptake.DeltaNH4[layer] = -mySoil.NH4N[layer] * uptakeFraction;

							uptakeFraction = Math.Min(1.0, MathUtilities.Divide(fNO3Avail[layer] + fWUptake[layer], totFacNO3, 0.0));
							NUptake.DeltaNO3[layer] = -mySoil.NO3N[layer] * uptakeFraction;

							mySoilNitrogenTakenUp[layer] = NUptake.DeltaNH4[layer] + NUptake.DeltaNO3[layer];
						}
					}

					//mySoilUptakeN.Sum()	2.2427998752781684	double

					if (Math.Abs(mySoilNuptake - mySoilNitrogenTakenUp.Sum()) > 0.0001)
						throw new Exception("Error on computing N uptake");

					// do the actual N changes
					NitrogenChanged.Invoke(NUptake);
				}
				else
				{
					// no uptake, just zero out the array
					mySoilNitrogenTakenUp = new double[myNLayers];
				}
			}
			else
			{
				// N uptake calculated by other modules (e.g., SWIM)
				string msg = "N uptake source was not recognised. Please specify it as either \"sward\" or \"species\".";
				throw new Exception(msg);
			}
		}

		#endregion

		#region - Organic matter processes  --------------------------------------------------------------------------------

		/// <summary>Return a given amount of DM (and N) to surface organic matter</summary>
		/// <param name="amountDM">DM amount to return</param>
		/// <param name="amountN">N amount to return</param>
		private void DoSurfaceOMReturn(double amountDM, double amountN)
		{
			if (BiomassRemoved != null)
			{
				Single dDM = (Single)amountDM;

				PMF.BiomassRemovedType BR = new PMF.BiomassRemovedType();
				String[] type = new String[] { "grass" };  // TODO:, shoud this be speciesFamily??
				Single[] dltdm = new Single[] { (Single)amountDM };
				Single[] dltn = new Single[] { (Single)amountN };
				Single[] dltp = new Single[] { 0 };         // P not considered here
				Single[] fraction = new Single[] { 1 };     // fraction is always 1.0 here

				BR.crop_type = "grass";   //TODO: this could be the Name, what is the diff between name and type??
				BR.dm_type = type;
				BR.dlt_crop_dm = dltdm;
				BR.dlt_dm_n = dltn;
				BR.dlt_dm_p = dltp;
				BR.fraction_to_residue = fraction;
				BiomassRemoved.Invoke(BR);
			}
		}

		/// <summary>Return scenescent roots to fresh organic matter pool in the soil</summary>
		/// <param name="amountDM">DM amount to return</param>
		/// <param name="amountN">N amount to return</param>
		private void DoIncorpFomEvent(double amountDM, double amountN)
		{
			Soils.FOMLayerLayerType[] FOMdataLayer = new Soils.FOMLayerLayerType[myNLayers];

			// ****  RCichota, Jun/2014
			// root senesced are returned to soil (as FOM) considering return is proportional to root mass

			double dAmtLayer = 0.0; //amount of root litter in a layer
			double dNLayer = 0.0;
			for (int layer = 0; layer < myNLayers; layer++)
			{
				dAmtLayer = amountDM * myRootFraction[layer];
				dNLayer = amountN * myRootFraction[layer];

				float amt = (float)dAmtLayer;

				Soils.FOMType fomData = new Soils.FOMType();
				fomData.amount = amountDM * myRootFraction[layer];
				fomData.N = amountN * myRootFraction[layer];
				fomData.C = amountDM * myRootFraction[layer] * CarbonFractionInDM;
				fomData.P = 0.0;			  // P not considered here
				fomData.AshAlk = 0.0;		  // Ash not considered here

				Soils.FOMLayerLayerType layerData = new Soils.FOMLayerLayerType();
				layerData.FOM = fomData;
				layerData.CNR = 0.0;	    // not used here
				layerData.LabileP = 0;      // not used here

				FOMdataLayer[layer] = layerData;
			}

			if (IncorpFOM != null)
			{
				Soils.FOMLayerType FOMData = new Soils.FOMLayerType();
				FOMData.Type = mySpeciesFamily;
				FOMData.Layer = FOMdataLayer;
				IncorpFOM.Invoke(FOMData);
			}
		}

		#endregion

		#endregion

		#region Other processes  -------------------------------------------------------------------------------------------

		/// <summary>Harvests the specified type.</summary>
		/// <param name="type">The type.</param>
		/// <param name="amount">The amount.</param>
		public void Harvest(string type, double amount)
		{
			GrazeType GrazeData = new GrazeType();
			GrazeData.amount = amount;
			GrazeData.type = type;
			OnGraze(GrazeData);
		}

		/// <summary>Called when [graze].</summary>
		/// <param name="GrazeData">The graze data.</param>
		[EventSubscribe("Graze")]
		private void OnGraze(GrazeType GrazeData)
		{
			if ((!myIsAlive) || StandingWt == 0)
				return;

			// get the amount required to remove
			double amountRequired = 0.0;
			if (GrazeData.type.ToLower() == "SetResidueAmount".ToLower())
			{ // Remove all DM above given residual amount
				amountRequired = Math.Max(0.0, StandingWt - GrazeData.amount);
			}
			else if (GrazeData.type.ToLower() == "SetRemoveAmount".ToLower())
			{ // Attempt to remove a given amount
				amountRequired = Math.Max(0.0, GrazeData.amount);
			}
			else
			{
				Console.WriteLine("  AgPasture - Method to set amount to remove not recognized, command will be ignored");
			}
			// get the actual amount to remove
			double amountToRemove = Math.Min(amountRequired, HarvestableWt);

			// Do the actual removal
			if (amountRequired > 0.0)
				RemoveDM(amountToRemove);
		}

		/// <summary>
		/// Remove a given amount of DM (and N) from this plant (consider preferences for green/dead material)
		/// </summary>
		/// <param name="AmountToRemove">Amount to remove (kg/ha)</param>
		/// <exception cref="System.Exception">   + Name +  - removal of DM resulted in loss of mass balance</exception>
		public void RemoveDM(double AmountToRemove)
		{
            // save current state
            SaveCurrentState();

            if (HarvestableWt > 0.0)
			{
				// get the DM weights for each pool, consider preference and available DM
				double tempPrefGreen = myPreferenceForGreenDM + (myPreferenceForDeadDM * (AmountToRemove / HarvestableWt));
				double tempPrefDead = myPreferenceForDeadDM + (myPreferenceForGreenDM * (AmountToRemove / HarvestableWt));
				double tempRemovableGreen = Math.Max(0.0, StandingLiveWt - myMinimumGreenWt);
				double tempRemovableDead = Math.Max(0.0, StandingDeadWt - MinimumDeadWt);

				// get partiton between dead and live materials
				double tempTotal = tempRemovableGreen * tempPrefGreen + tempRemovableDead * tempPrefDead;
				double fractionToHarvestGreen = 0.0;
				double fractionToHarvestDead = 0.0;
				if (tempTotal > 0.0)
				{
					fractionToHarvestGreen = tempRemovableGreen * tempPrefGreen / tempTotal;
					fractionToHarvestDead = tempRemovableDead * tempPrefDead / tempTotal;
				}

				// get amounts removed
				double RemovingGreenDM = AmountToRemove * fractionToHarvestGreen;
				double RemovingDeadDM = AmountToRemove * fractionToHarvestDead;

				// Fraction of DM remaining in the field
				double fractionRemainingGreen = 1.0;
				if (StandingLiveWt > 0.0)
					fractionRemainingGreen = Math.Max(0.0, Math.Min(1.0, 1.0 - RemovingGreenDM / StandingLiveWt));
				double fractionRemainingDead = 1.0;
				if (StandingDeadWt > 0.0)
					fractionRemainingDead = Math.Max(0.0, Math.Min(1.0, 1.0 - RemovingDeadDM / StandingDeadWt));

				// get digestibility of DM being harvested
				myDigestDefoliated = calcDigestibility();

                // update the various pools
                myLeaves.tissue[0].DM *= fractionRemainingGreen;
                myLeaves.tissue[1].DM *= fractionRemainingGreen;
                myLeaves.tissue[2].DM *= fractionRemainingGreen;
                myLeaves.tissue[3].DM *= fractionRemainingDead;
                myStems.tissue[0].DM *= fractionRemainingGreen;
                myStems.tissue[1].DM *= fractionRemainingGreen;
                myStems.tissue[2].DM *= fractionRemainingGreen;
                myStems.tissue[3].DM *= fractionRemainingDead;
                //No stolon remove

                // N remove
                myLeaves.tissue[0].Namount *= fractionRemainingGreen;
                myLeaves.tissue[1].Namount *= fractionRemainingGreen;
                myLeaves.tissue[2].Namount *= fractionRemainingGreen;
                myLeaves.tissue[3].Namount *= fractionRemainingDead;
                myStems.tissue[0].Namount *= fractionRemainingGreen;
                myStems.tissue[1].Namount *= fractionRemainingGreen;
                myStems.tissue[2].Namount *= fractionRemainingGreen;
                myStems.tissue[3].Namount *= fractionRemainingDead;

				//C and N remobilised are also removed proportionally
				myNRemobilised *= fractionRemainingGreen;
				myCRemobilised *= fractionRemainingGreen;

				// update Luxury N pools
				myNLuxury2 *= fractionRemainingGreen;
				myNLuxury3 *= fractionRemainingGreen;

				// update aggregate variables
				updateAggregated();

				// check mass balance and set outputs
                myDefoliatedDM= myPrevState.dmShoot - myDMShoot;
                myPrevState.dmdefoliated = myDefoliatedDM;
                myDefoliatedN = myPrevState.NShoot - myNShoot;
                myPrevState.Ndefoliated = myDefoliatedN;
               if (Math.Abs(myDefoliatedDM - AmountToRemove) > 0.00001)
					throw new Exception("  " + Name + " - removal of DM resulted in loss of mass balance");
			}
		}

		/// <summary>Remove biomass from plant</summary>
		/// <param name="RemovalData">Info about what and how much to remove</param>
		/// <remarks>Greater details on how much and which parts are removed is given</remarks>
		[EventSubscribe("RemoveCropBiomass")]
		private void Onremove_crop_biomass(RemoveCropBiomassType RemovalData)
		{
			// NOTE: It is responsability of the calling module to check that the amount of 
			//  herbage in each plant part is correct
			// No checking if the removing amount passed in are too much here

			// ATTENTION: The amounts passed should be in g/m^2

			double fractionToRemove = 0.0;


			// get digestibility of DM being removed
			myDigestDefoliated = calcDigestibility();

			for (int i = 0; i < RemovalData.dm.Length; i++)			  // for each pool (green or dead)
			{
				string plantPool = RemovalData.dm[i].pool;
				for (int j = 0; j < RemovalData.dm[i].dlt.Length; j++)   // for each part (leaf or stem)
				{
					string plantPart = RemovalData.dm[i].part[j];
					double amountToRemove = RemovalData.dm[i].dlt[j] * 10.0;    // convert to kgDM/ha
					if (plantPool.ToLower() == "green" && plantPart.ToLower() == "leaf")
					{
						if (LeafGreenWt - amountToRemove > 0.0)
						{
							fractionToRemove = MathUtilities.Divide(amountToRemove, LeafGreenWt, 0.0);
							RemoveFractionDM(fractionToRemove, plantPool, plantPart);
						}
					}
					else if (plantPool.ToLower() == "green" && plantPart.ToLower() == "stem")
					{
						if (StemGreenWt - amountToRemove > 0.0)
						{
							fractionToRemove = MathUtilities.Divide(amountToRemove, StemGreenWt, 0.0);
							RemoveFractionDM(fractionToRemove, plantPool, plantPart);
						}
					}
					else if (plantPool.ToLower() == "dead" && plantPart.ToLower() == "leaf")
					{
						if (LeafDeadWt - amountToRemove > 0.0)
						{
							fractionToRemove = MathUtilities.Divide(amountToRemove, LeafDeadWt, 0.0);
							RemoveFractionDM(fractionToRemove, plantPool, plantPart);
						}
					}
					else if (plantPool.ToLower() == "dead" && plantPart.ToLower() == "stem")
					{
						if (StemDeadWt - amountToRemove > 0.0)
						{
							fractionToRemove = MathUtilities.Divide(amountToRemove, StemDeadWt, 0.0);
							RemoveFractionDM(fractionToRemove, plantPool, plantPart);
						}
					}
				}
			}
			RefreshAfterRemove();
		}

		/// <summary>Remove a fraction of DM from a given plant part</summary>
		/// <param name="fractionR">The fraction of DM and N to remove</param>
		/// <param name="pool">The pool to remove from (green or dead)</param>
		/// <param name="part">The part to remove from (leaf or stem)</param>
		public void RemoveFractionDM(double fractionR, string pool, string part)
		{
			if (pool.ToLower() == "green")
			{
				if (part.ToLower() == "leaf")
				{
					// removing green leaves
					myDefoliatedDM += LeafGreenWt * fractionR;
					myDefoliatedN += LeafGreenN * fractionR;

                    myLeaves.tissue[0].DM *= fractionR;
                    myLeaves.tissue[1].DM *= fractionR;
                    myLeaves.tissue[2].DM *= fractionR;

                    myLeaves.tissue[0].Namount *= fractionR;
                    myLeaves.tissue[1].Namount *= fractionR;
                    myLeaves.tissue[2].Namount *= fractionR;
				}
				else if (part.ToLower() == "stem")
				{
					// removing green stems
					myDefoliatedDM += StemGreenWt * fractionR;
					myDefoliatedN += StemGreenN * fractionR;

                    myStems.tissue[0].DM *= fractionR;
                    myStems.tissue[1].DM *= fractionR;
                    myStems.tissue[2].DM *= fractionR;

                    myStems.tissue[0].Namount *= fractionR;
                    myStems.tissue[1].Namount *= fractionR;
                    myStems.tissue[2].Namount *= fractionR;
				}
			}
			else if (pool.ToLower() == "green")
			{
				if (part.ToLower() == "leaf")
				{
					// removing dead leaves
					myDefoliatedDM += LeafDeadWt * fractionR;
					myDefoliatedN += LeafDeadN * fractionR;

                    myLeaves.tissue[3].DM *= fractionR;
                    myLeaves.tissue[3].Namount *= fractionR;
				}
				else if (part.ToLower() == "stem")
				{
					// removing dead stems
					myDefoliatedDM += StemDeadWt * fractionR;
					myDefoliatedN += StemDeadN * fractionR;

                    myStems.tissue[3].DM *= fractionR;
                    myStems.tissue[3].Namount *= fractionR;
				}
			}
		}

		/// <summary>Performs few actions to update variables after RemoveFractionDM</summary>
		public void RefreshAfterRemove()
		{
			// set values for fractionHarvest (in fact fraction harvested)
			myFractionHarvested = MathUtilities.Divide(myDefoliatedDM, StandingWt + myDefoliatedDM, 0.0);

			// recalc the digestibility
			calcDigestibility();

			// update aggregated variables
			updateAggregated();
		}

		/// <summary>Reset this plant state to its initial values</summary>
		public void Reset()
		{
			SetInitialState();
			myPrevState = new SpeciesState();
		}

        /// <summary>Kills a fraction of this plant</summary>
        /// <remarks>
        /// This will move DM and N from live to dead pools, 
        /// if killFraction is 1.0 then the crop is ended
        /// </remarks>
        /// <param name="killFraction">Fraction of crop to kill (0-1)</param>
        [EventSubscribe("KillCrop")]
		public void OnKillCrop(double killFraction)
		{
            double fractionRemaining = 1.0 - killFraction;

            if (killFraction > myEpsilon)
            {
                if (fractionRemaining > myEpsilon)
                {
                    // move a fraction of live tissue to dead pool
                    myLeaves.tissue[3].DM += (myLeaves.tissue[0].DM + myLeaves.tissue[1].DM + myLeaves.tissue[2].DM) *
                                           killFraction;
                    myLeaves.tissue[0].DM *= fractionRemaining;
                    myLeaves.tissue[1].DM *= fractionRemaining;
                    myLeaves.tissue[2].DM *= fractionRemaining;
                    myStems.tissue[3].DM += (myStems.tissue[0].DM + myStems.tissue[1].DM + myStems.tissue[2].DM) * killFraction;
                    myStems.tissue[0].DM *= fractionRemaining;
                    myStems.tissue[1].DM *= fractionRemaining;
                    myStems.tissue[2].DM *= fractionRemaining;
                    myStolons.tissue[3].DM += (myStolons.tissue[0].DM + myStolons.tissue[1].DM + myStolons.tissue[2].DM) *
                                            killFraction;
                    myStolons.tissue[0].DM *= fractionRemaining;
                    myStolons.tissue[1].DM *= fractionRemaining;
                    myStolons.tissue[2].DM *= fractionRemaining;
                    myRoots.tissue[3].DM += myRoots.tissue[0].DM * killFraction;
                    myRoots.tissue[0].DM *= fractionRemaining;

                    myLeaves.tissue[3].Namount += (myLeaves.tissue[0].Namount + myLeaves.tissue[1].Namount +
                                                 myLeaves.tissue[2].Namount) * killFraction;
                    myLeaves.tissue[0].Namount *= fractionRemaining;
                    myLeaves.tissue[1].Namount *= fractionRemaining;
                    myLeaves.tissue[2].Namount *= fractionRemaining;
                    myStems.tissue[3].Namount += (myStems.tissue[0].Namount + myStems.tissue[1].Namount +
                                                myStems.tissue[2].Namount) * killFraction;
                    myStems.tissue[0].Namount *= fractionRemaining;
                    myStems.tissue[1].Namount *= fractionRemaining;
                    myStems.tissue[2].Namount *= fractionRemaining;
                    myStolons.tissue[3].Namount += (myStolons.tissue[0].Namount + myStolons.tissue[1].Namount +
                                                  myStolons.tissue[2].Namount) * killFraction;
                    myStolons.tissue[0].Namount *= fractionRemaining;
                    myStolons.tissue[1].Namount *= fractionRemaining;
                    myStolons.tissue[2].Namount *= fractionRemaining;
                    myRoots.tissue[3].Namount += myRoots.tissue[0].Namount * killFraction;
                    myRoots.tissue[0].Namount *= fractionRemaining;
                }
                else
                {
                    // End crop
                    //Above_ground part returns to surface OM comletey (frac = 1.0)
                    DoSurfaceOMReturn(myDMShoot, myNShoot);

                    //Incorporate root mass in soil fresh organic matter
                    DoIncorpFomEvent(myDMRoot, myNRoot);

                    //ZeroVars();

                    myIsAlive = false;
                }

                updateAggregated();
                SaveCurrentState();
            }
        }

        /// <summary>End the crop.</summary>
        public void EndCrop()
        {
            // Return all above ground parts to surface OM
            DoSurfaceOMReturn(myDMShoot, myNShoot);

            // Incorporate all root mass to soil fresh organic matter
            DoIncorpFomEvent(myDMRoot, myNRoot);

            ResetZero();

            myIsAlive = false;
        }

        /// <summary>Reset this plant to zero (kill crop)</summary>
        public void ResetZero()
		{

            // Zero out the DM pools
            myLeaves.tissue[0].DM = myLeaves.tissue[1].DM = myLeaves.tissue[2].DM = myLeaves.tissue[3].DM = 0.0;
            myStems.tissue[0].DM = myStems.tissue[1].DM = myStems.tissue[2].DM = myStems.tissue[3].DM = 0.0;
            myStolons.tissue[0].DM = myStolons.tissue[1].DM = myStolons.tissue[2].DM = myStolons.tissue[3].DM = 0.0;
            myRoots.tissue[0].DM = myRoots.tissue[1].DM = myRoots.tissue[2].DM = myRoots.tissue[3].DM = 0.0;
            myDefoliatedDM = 0.0;

            // Zero out the N pools
            myLeaves.tissue[0].Namount = myLeaves.tissue[1].Namount = myLeaves.tissue[2].Namount = myLeaves.tissue[3].Namount = 0.0;
            myStems.tissue[0].Namount = myStems.tissue[1].Namount = myStems.tissue[2].Namount = myStems.tissue[3].Namount = 0.0;
            myStolons.tissue[0].Namount = myStolons.tissue[1].Namount = myStolons.tissue[2].Namount = myStolons.tissue[3].Namount = 0.0;
            myRoots.tissue[0].Namount = myRoots.tissue[1].Namount = myRoots.tissue[2].Namount = myRoots.tissue[3].Namount = 0.0;
            myDefoliatedN = 0.0;

			myDigestDefoliated = 0.0;

			updateAggregated();

			myPhenoStage = 0;

			myPrevState = new SpeciesState();
		}

		#endregion

		#region Functions  -------------------------------------------------------------------------------------------------

		/// <summary>Placeholder for SoilArbitrator</summary>
        /// <param name="soilstate">soilstate</param>
		/// <returns></returns>
        public List<ZoneWaterAndN> GetSWUptakes(SoilState soilstate)
		{
            throw new NotImplementedException();
		}
        /// <summary>Placeholder for SoilArbitrator</summary>
        /// <param name="soilstate">soilstate</param>
        /// <returns></returns>
        public List<ZoneWaterAndN> GetNUptakes(SoilState soilstate)
        {
            throw new NotImplementedException();
        }

		/// <summary>
		/// Set the sw uptake for today
		/// </summary>
		public void SetSWUptake(List<ZoneWaterAndN> info)
		{ }
        /// <summary>
        /// Set the n uptake for today
        /// </summary>
        public void SetNUptake(List<ZoneWaterAndN> info)
        { }    

		/// <summary>Growth limiting factor due to temperature</summary>
		/// <param name="Temp">Temperature for which the limiting factor will be computed</param>
		/// <returns>The value for the limiting factor (0-1)</returns>
		/// <exception cref="System.Exception">Photosynthesis pathway is not valid</exception>
		private double TemperatureLimitingFactor(double Temp)
		{
			double result = 0.0;
			if (myPhotosynthesisPathway == "C3")
			{
				if (Temp > myGrowthTmin && Temp < myGrowthTmax)
				{
					double growthTmax1 = myGrowthTopt + (myGrowthTopt - myGrowthTmin) / myGrowthTq;
					double val1 = Math.Pow((Temp - myGrowthTmin), myGrowthTq) * (growthTmax1 - Temp);
					double val2 = Math.Pow((myGrowthTopt - myGrowthTmin), myGrowthTq) * (growthTmax1 - myGrowthTopt);  // TODO: replace Topt with Tref here
					result = val1 / val2;
				}
			}
			else if (myPhotosynthesisPathway == "C4")
			{
				if (Temp > myGrowthTmin)
				{
					if (Temp > myGrowthTopt)
						Temp = myGrowthTopt;

					double growthTmax1 = myGrowthTopt + (myGrowthTopt - myGrowthTmin) / myGrowthTq;
					double val1 = Math.Pow((Temp - myGrowthTmin), myGrowthTq) * (growthTmax1 - Temp);
					double val2 = Math.Pow((myGrowthTopt - myGrowthTmin), myGrowthTq) * (growthTmax1 - myGrowthTopt);  // TODO: replace Topt with Tref here
					result = val1 / val2;
				}
			}
			else
				throw new Exception("Photosynthesis pathway is not valid");
			return result;
		}

		/// <summary>Effect of temperature on tissue turnover</summary>
		/// <param name="Temp">The temporary.</param>
		/// <returns>Temperature factor (0-1)</returns>
		private double TempFactorForTissueTurnover(double Temp)
		{
			double result = 0.0;
			if (Temp > myTissueTurnoverTmin && Temp <= myTissueTurnoverTopt)
			{
				result = (Temp - myTissueTurnoverTmin) / (myTissueTurnoverTopt - myTissueTurnoverTmin);  // TODO: implement power function
			}
			else if (Temp > myTissueTurnoverTopt)
			{
				result = 1.0;
			}
			return result;
		}

		/// <summary>Photosynthesis reduction factor due to high temperatures (heat stress)</summary>
		/// <returns>The reduction in photosynthesis rate (0-1)</returns>
		private double HeatStress()
		{
			// evaluate recovery from the previous high temperature effects
			double recoverF = 1.0;

			if (myHighTempEffect < 1.0)
			{
				if (myReferenceT4Heat > myTmean)
					myAccumT4Heat += (myReferenceT4Heat - myTmean);

				if (myAccumT4Heat < myHeatSumT)
					recoverF = myHighTempEffect + (1 - myHighTempEffect) * myAccumT4Heat / myHeatSumT;
			}

			// Evaluate the high temperature factor for today
			double newHeatF = 1.0;
			if (myMetData.MaxT > myHeatFullT)
				newHeatF = 0;
			else if (myMetData.MaxT > myHeatOnsetT)
				newHeatF = (myMetData.MaxT - myHeatOnsetT) / (myHeatFullT - myHeatOnsetT);

			// If this new high temp. factor is smaller than 1.0, then it is compounded with the old one
			// also, the cumulative heat for recovery is re-started
			if (newHeatF < 1.0)
			{
				myHighTempEffect = recoverF * newHeatF;
				myAccumT4Heat = 0;
				recoverF = myHighTempEffect;
			}

			return recoverF;  // TODO: revise this function
		}

		/// <summary>Photosynthesis reduction factor due to low temperatures (cold stress)</summary>
		/// <returns>The reduction in potosynthesis rate (0-1)</returns>
		private double ColdStress()
		{
			//recover from the previous high temp. effect
			double recoverF = 1.0;
			if (myLowTempEffect < 1.0)
			{
				if (myTmean > myReferenceT4Cold)
					myAccumT4Cold += (myTmean - myReferenceT4Cold);

				if (myAccumT4Cold < myColdSumT)
					recoverF = myLowTempEffect + (1 - myLowTempEffect) * myAccumT4Cold / myColdSumT;
			}

			//possible new low temp. effect
			double newColdF = 1.0;
			if (myMetData.MinT < myColdFullT)
				newColdF = 0;
			else if (myMetData.MinT < myColdOnsetT)
				newColdF = (myMetData.MinT - myColdFullT) / (myColdOnsetT - myColdFullT);

			// If this new cold temp. effect happens when serious cold effect is still on,
			// compound & then re-start of the recovery from the new effect
			if (newColdF < 1.0)
			{
				myLowTempEffect = newColdF * recoverF;
				myAccumT4Cold = 0;
				recoverF = myLowTempEffect;
			}

			return recoverF; // TODO: revise this function
		}

		/// <summary>Photosynthesis factor (reduction or increase) to eleveated [CO2]</summary>
		/// <returns>A factor to adjust photosynthesis due to CO2</returns>
		private double PCO2Effects()
		{
			if (Math.Abs(myMetData.CO2 - myReferenceCO2) < 0.01)
				return 1.0;

			double Fp1 = myMetData.CO2 / (myCoefficientCO2EffectOnPhotosynthesis + myMetData.CO2);
			double Fp2 = (myReferenceCO2 + myCoefficientCO2EffectOnPhotosynthesis) / myReferenceCO2;

			return Fp1 * Fp2;
		}

		/// <summary>Effect on photosynthesis due to variations in optimum N concentration as affected by CO2</summary>
		/// <returns>A factor to adjust photosynthesis</returns>
		private double PmxNeffect()
		{
			if (myIsAnnual)
				return 0.0;
			else
			{
				double fN = NCO2Effects();

				double result = 1.0;
				if (myLeaves.NconcGreen < myLeafNopt * fN)
				{
					if (myLeaves.NconcGreen > myLeafNmin)
					{
						result = MathUtilities.Divide(myLeaves.NconcGreen - myLeafNmin, (myLeafNopt * fN) - myLeafNmin, 1.0);
						result = Math.Min(1.0, Math.Max(0.0, result));
					}
					else
					{
						result = 0.0;
					}
				}

				return result;
			}
		}

		/// <summary>Plant nitrogen [N] decline to elevated [CO2]</summary>
		/// <returns>A factor to adjust N demand</returns>
		private double NCO2Effects()
		{
			if (Math.Abs(myMetData.CO2 - myReferenceCO2) < 0.01)
				return 1.0;

			double termK = Math.Pow(myOffsetCO2EffectOnNuptake - myReferenceCO2, myExponentCO2EffectOnNuptake);
			double termC = Math.Pow(myMetData.CO2 - myReferenceCO2, myExponentCO2EffectOnNuptake);
			double result = (1 - myMinimumCO2EffectOnNuptake) * termK / (termK + termC);

			return myMinimumCO2EffectOnNuptake + result;
		}

		//Canopy conductance decline to elevated [CO2]
		/// <summary>Conductances the c o2 effects.</summary>
		/// <returns></returns>
		private double ConductanceCO2Effects()
		{
			if (Math.Abs(myMetData.CO2 - myReferenceCO2) < 0.5)
				return 1.0;
			//Hard coded here, not used, should go to Micromet!   - TODO
			double Gmin = 0.2;      //Fc = Gmin when CO2->unlimited
			double Gmax = 1.25;     //Fc = Gmax when CO2 = 0;
			double beta = 2.5;      //curvature factor,

			double aux1 = (1 - Gmin) * Math.Pow(myReferenceCO2, beta);
			double aux2 = (Gmax - 1) * Math.Pow(myMetData.CO2, beta);
			double Fc = (Gmax - Gmin) * aux1 / (aux2 + aux1);
			return Gmin + Fc;
		}

		/// <summary>Growth limiting factor due to soil moisture deficit</summary>
		/// <returns>The limiting factor due to soil water deficit (0-1)</returns>
		internal double WaterDeficitFactor()
		{
			double result = 0.0;

			if (myWaterDemand <= 0.0001)         // demand should never be really negative, but might be slightly because of precision of float numbers
				result = 1.0;
			else
				result = mySoilWaterTakenUp.Sum() / myWaterDemand;

			return Math.Max(0.0, Math.Min(1.0, result));
		}

		/// <summary>Growth limiting factor due to excess of water in soil (logging/saturation)</summary>
		/// <returns>The limiting factor due to excess of soil water</returns>
		/// <remarks>Assuming that water above field capacity is not good</remarks>
		internal double WaterLoggingFactor()
		{
			double result = 1.0;

			// calculate soil moisture thresholds in the root zone
			double mySWater = 0.0;
			double mySaturation = 0.0;
			double myDUL = 0.0;
			double fractionLayer = 0.0;
			for (int layer = 0; layer <= myRootFrontier; layer++)
			{
				// fraction of layer with roots 
				fractionLayer = LayerFractionWithRoots(layer);
				// actual soil water content
				mySWater += mySoil.Water[layer] * fractionLayer;
				// water content at saturation
				mySaturation += mySoil.SoilWater.SATmm[layer] * fractionLayer;
				// water content at field capacity
				myDUL += mySoil.SoilWater.DULmm[layer] * fractionLayer;
			}

			result = 1.0 - myWaterLoggingCoefficient * Math.Max(0.0, mySWater - myDUL) / (mySaturation - myDUL);

			return result;
		}

		/// <summary>Effect of water stress on tissue turnover</summary>
		/// <returns>Water stress factor (0-1)</returns>
		private double WaterFactorForTissueTurnover()
		{
			double result = 1.0;
			if (myGLFWater < myTissueTurnoverGLFWopt)
			{
				result = (myTissueTurnoverGLFWopt - myGLFWater) / myTissueTurnoverGLFWopt;
				result = (myTissueTurnoverWFactorMax - 1.0) * result;
				result = Math.Min(myTissueTurnoverWFactorMax, Math.Max(1.0, 1 + result));
			}

			return result;
		}

		/// <summary>Computes the ground cover for the plant, or plant part</summary>
		/// <param name="thisLAI">The LAI for this plant or part</param>
		/// <returns>Fraction of ground effectively covered (0-1)</returns>
		private double CalcPlantCover(double thisLAI)
		{
			return (1.0 - Math.Exp(-myLightExtentionCoeff * thisLAI));
		}

		/// <summary>Compute the distribution of roots in the soil profile (sum is equal to one)</summary>
		/// <returns>The proportion of root mass in each soil layer</returns>
		/// <exception cref="System.Exception">
		/// No valid method for computing root distribution was selected
		/// or
		/// Could not calculate root distribution
		/// </exception>
		private double[] RootProfileDistribution()
		{
			double[] result = new double[myNLayers];
			double sumProportion = 0;

			switch (myRootDistributionMethod.ToLower())
			{
				case "homogeneous":
					{
						// homogenous distribution over soil profile (same root density throughout the profile)
						double DepthTop = 0;
						for (int layer = 0; layer < myNLayers; layer++)
						{
							if (DepthTop >= myRootDepth)
								result[layer] = 0.0;
							else if (DepthTop + mySoil.Thickness[layer] <= myRootDepth)
								result[layer] = 1.0;
							else
								result[layer] = (myRootDepth - DepthTop) / mySoil.Thickness[layer];
							sumProportion += result[layer] * mySoil.Thickness[layer];
							DepthTop += mySoil.Thickness[layer];
						}
						break;
					}
				case "userdefined":
					{
						// distribution given by the user
						// Option no longer available
						break;
					}
				case "expolinear":
					{
						// distribution calculated using ExpoLinear method
						//  Considers homogeneous distribution from surface down to a fraction of root depth (p_ExpoLinearDepthParam)
						//   below this depth, the proportion of root decrease following a power function (exponent = p_ExpoLinearCurveParam)
						//   if exponent is one than the proportion decreases linearly.
						double DepthTop = 0;
						double DepthFirstStage = myRootDepth * myExpoLinearDepthParam;
						double DepthSecondStage = myRootDepth - DepthFirstStage;
						for (int layer = 0; layer < myNLayers; layer++)
						{
							if (DepthTop >= myRootDepth)
								result[layer] = 0.0;
							else if (DepthTop + mySoil.Thickness[layer] <= DepthFirstStage)
								result[layer] = 1.0;
							else
							{
								if (DepthTop < DepthFirstStage)
									result[layer] = (DepthFirstStage - DepthTop) / mySoil.Thickness[layer];
								if ((myExpoLinearDepthParam < 1.0) && (myExpoLinearCurveParam > 0.0))
								{
									double thisDepth = Math.Max(0.0, DepthTop - DepthFirstStage);
									double Ftop = (thisDepth - DepthSecondStage) * Math.Pow(1 - thisDepth / DepthSecondStage, myExpoLinearCurveParam) / (myExpoLinearCurveParam + 1);
									thisDepth = Math.Min(DepthTop + mySoil.Thickness[layer] - DepthFirstStage, DepthSecondStage);
									double Fbottom = (thisDepth - DepthSecondStage) * Math.Pow(1 - thisDepth / DepthSecondStage, myExpoLinearCurveParam) / (myExpoLinearCurveParam + 1);
									result[layer] += Math.Max(0.0, Fbottom - Ftop) / mySoil.Thickness[layer];
								}
								else if (DepthTop + mySoil.Thickness[layer] <= myRootDepth)
									result[layer] += Math.Min(DepthTop + mySoil.Thickness[layer], myRootDepth) - Math.Max(DepthTop, DepthFirstStage) / mySoil.Thickness[layer];
							}
							sumProportion += result[layer];
							DepthTop += mySoil.Thickness[layer];
						}
						break;
					}
				default:
					{
						throw new Exception("No valid method for computing root distribution was selected");
					}
			}
			if (sumProportion > 0)
				for (int layer = 0; layer < myNLayers; layer++)
					result[layer] = MathUtilities.Divide(result[layer], sumProportion, 0.0);
			else
				throw new Exception("Could not calculate root distribution");
			return result;
		}

		/// <summary>
		/// Compute how much of the layer is actually explored by roots (considering depth only)
		/// </summary>
		/// <param name="layer">The index for the layer being considered</param>
		/// <returns>Fraction of the layer in consideration that is explored by roots</returns>
		public double LayerFractionWithRoots(int layer)
		{
			if (layer > myRootFrontier)
				return 0.0;
			else
			{
				double depthAtTopThisLayer = 0;   // depth till the top of the layer being considered
				for (int z = 0; z < layer; z++)
					depthAtTopThisLayer += mySoil.Thickness[z];
				double result = (myRootDepth - depthAtTopThisLayer) / mySoil.Thickness[layer];
				return Math.Min(1.0, Math.Max(0.0, result));
			}
		}


		/// <summary>VPDs this instance.</summary>
		/// <returns></returns>
		/// The following helper functions [VDP and svp] are for calculating Fvdp
		private double VPD()
		{
			double VPDmint = svp(myMetData.MinT) - myMetData.VP;
			VPDmint = Math.Max(VPDmint, 0.0);

			double VPDmaxt = svp(myMetData.MaxT) - myMetData.VP;
			VPDmaxt = Math.Max(VPDmaxt, 0.0);

			double vdp = 0.66 * VPDmaxt + 0.34 * VPDmint;
			return vdp;
		}
		/// <summary>SVPs the specified temporary.</summary>
		/// <param name="temp">The temporary.</param>
		/// <returns></returns>
		private double svp(double temp)  // from Growth.for documented in MicroMet
		{
			return 6.1078 * Math.Exp(17.269 * temp / (237.3 + temp));
		}

        #endregion

        #region Plant parts  -----------------------------------------------------------------------------------------

        /// <summary>
        /// Defines a generic organ of a pasture species
        /// </summary>
        /// <remarks>
        /// Each organ (leaf, stem, etc) is defined as a collection of four tissues
        /// Three tissues are alive (growing, developing and mature), the fourth is dead material
        /// Each tissue has a record of DM and N amounts, from which Nconcentration is computed
        /// Methods to compute DM and N for total and 'green' tissues are given
        /// </remarks>
        internal class OrganPool
        {
            /// <summary>the collection of tissues for this organ</summary>
            internal Tissue[] tissue;

            /// <summary>Initialise tissues</summary>
            public OrganPool()
            {
                tissue = new Tissue[4];
                for (int t = 0; t < 4; t++)
                { tissue[t] = new Tissue(); }
            }

            /// <summary>Defines a generic plant tissue</summary>
            internal class Tissue
            {
                /// <summary>The dry matter amount (g/m^2)</summary>
                internal double DM = 0.0;

                /// <summary>The N content (g/m^2)</summary>
                internal double Namount = 0.0;

                /// <summary>The P content (g/m^2)</summary>
                internal double Pamount = 0.0;

                /// <summary>The nitrogen concentration (kg/kg)</summary>
                internal double Nconc
                {
                    get { return MathUtilities.Divide(Namount, DM, 0.0); }
                    set { Namount = value * DM; }
                }

                /// <summary>The phosphorus concentration (g/g)</summary>
                internal double Pconc
                {
                    get { return MathUtilities.Divide(Pamount, DM, 0.0); }
                    set { Pamount = value * DM; }
                }
            }

            /// <summary>The total dry matter in this tissue (g/m^2)</summary>
            internal double DMTotal
            {
                get
                {
                    double result = 0.0;
                    for (int t = 0; t < 4; t++)
                    {
                        result += tissue[t].DM;
                    }

                    return result;
                }
            }

            /// <summary>The dry matter in the green (alive) tissues (g/m^2)</summary>
            internal double DMGreen
            {
                get
                {
                    double result = 0.0;
                    for (int t = 0; t < 3; t++)
                    {
                        result += tissue[t].DM;
                    }

                    return result;
                }
            }

            /// <summary>The average N concentration in this tissue (g/g)</summary>
            internal double NconcTotal
            {
                get
                {
                    double result = 0.0;
                    double myDM = 0.0;
                    double myN = 0.0;
                    for (int t = 0; t < 4; t++)
                    {
                        myDM += tissue[t].DM;
                        myN += tissue[t].Namount;
                    }

                    if (myDM > 0.0)
                    { result = myN / myDM; }

                    return result;
                }
            }

            /// <summary>The dry matter in the green (alive) tissues (g/g)</summary>
            internal double NconcGreen
            {
                get
                {
                    double result = 0.0;
                    double myDM = 0.0;
                    double myN = 0.0;
                    for (int t = 0; t < 3; t++)
                    {
                        myDM += tissue[t].DM;
                        myN += tissue[t].Namount;
                    }

                    if (myDM > 0.0)
                    { result = myN / myDM; }

                    return result;
                }
            }

            /// <summary>The total N amount in this tissue (kg/ha)</summary>
            internal double NTotal
            {
                get
                {
                    double result = 0.0;
                    for (int t = 0; t < 4; t++)
                    { result += tissue[t].Namount; }

                    return result;
                }
            }

            /// <summary>The N amount in the green (alive) tissues (kg/ha)</summary>
            internal double NGreen
            {
                get
                {
                    double result = 0.0;
                    for (int t = 0; t < 3; t++)
                    { result += tissue[t].Namount; }

                    return result;
                }
            }
        }

        /// <summary>
        /// Stores the values of pool status of previous day
        /// </summary>
        private class SpeciesState
        {
            /// <summary>The state of leaves (DM and N)</summary>
            internal OrganPool leaves;

            /// <summary>The state of sheath/stems (DM and N)</summary>
            internal OrganPool stems;

            /// <summary>The state of stolons (DM and N)</summary>
            internal OrganPool stolons;

            /// <summary>The state of roots (DM and N)</summary>
            internal OrganPool roots;

            /// <summary>The constructor</summary>
            public SpeciesState()
            {
                leaves = new OrganPool();
                stems = new OrganPool();
                stolons = new OrganPool();
                roots = new OrganPool();
            }

            /// <summary>The DM of shoot (g/m^2)</summary>
            internal double dmShoot
            {
                get { return leaves.DMTotal + stems.DMTotal + stolons.DMGreen; }
            }

            /// <summary>The DM of roots (g/m^2)</summary>
            internal double dmRoot
            {
                get { return roots.DMGreen; }
            }

            /// <summary>The amount of N above ground (shoot)</summary>
            internal double NShoot
            {
                get { return leaves.NTotal + stems.NTotal + stolons.NGreen; }
            }

            /// <summary>The amount of N below ground (root)</summary>
            internal double NRoot
            {
                get { return roots.NGreen; }
            }

            /// <summary>DM weight of defoliated material (g/m^2)</summary>
            internal double dmdefoliated;

            /// <summary>N in defoliated material (g/m^2)</summary>
            internal double Ndefoliated;

            /// <summary>N remobilsed from senesced tissue (g/m^2)</summary>
            internal double Nremob;
        }

        #endregion
    }

	/// <summary>Defines a broken stick (piecewise) function</summary>
	[Serializable]
	public class BrokenStick
	{
		/// <summary>The x</summary>
		public double[] X;
		/// <summary>The y</summary>
		public double[] Y;

		/// <summary>Values the specified new x.</summary>
		/// <param name="newX">The new x.</param>
		/// <returns></returns>
		public double Value(double newX)
		{
			bool DidInterpolate = false;
			return MathUtilities.LinearInterpReal(newX, X, Y, out DidInterpolate);
		}
	}
}
