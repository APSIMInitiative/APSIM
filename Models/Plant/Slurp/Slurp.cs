﻿using System;
using System.Collections.Generic;
using System.Text;
using Models.Core;

using System.Reflection;
using System.Collections;
using Models.PMF.Functions;
using Models.Soils;
using System.Xml.Serialization;
using Models.PMF;
using Models.Interfaces;
using APSIM.Shared.Utilities;


namespace Models.PMF.Slurp
{
    /// <summary>
    /// Slurp is a 'dummy' static crop model.  The user sets very basic input information such as ....  These states will
    /// not change during the simulation (no growth or death) unless the states are reset by the user.
    /// Need to check canopy height and depth units.  Micromet documentation says m but looks like is in mm in the module
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Zone))]
    public class Slurp : Model, ICrop2
    {
        /// <summary>
        /// MicroClimate will get 'CropType' and use it to look up
        /// canopy properties for this crop.
        /// </summary>
        public string CropType { get; set; }
        /// <summary>Gets a list of cultivar names</summary>
        public string[] CultivarNames
        {
            get
            {
                string[] items = new string[1];
                items[1] = "NotACultivar";

                return items;
            }
        }
        /// <summary>Link to the soil module</summary>
        [Link] Soils.Soil Soil = null;

        // The variables that are in CanopyProperties
        /// <summary>
        /// Holds the set of crop canopy properties that is used by Arbitrator for light and engergy calculations
        /// </summary>
        public CanopyProperties CanopyProperties { get { return LocalCanopyData; } }
        /// <summary>The local canopy data</summary>
        CanopyProperties LocalCanopyData = new CanopyProperties();

        /// <summary>Is the plant in the ground?</summary>
        [XmlIgnore]
        public bool PlantInGround { get { return true; } }

        /// <summary>Test if the plant has emerged</summary>
        [XmlIgnore]
        public bool PlantEmerged { get { return true; } }

        /// <summary>The initial value of leaf area index (m2/m2)</summary>
        /// <value>The local lai green.</value>
        [Description("Green LAI (m2/m2)")] public double localLAIGreen { get; set; }

        /// <summary>The initial value of total (green and dead) leaf area index (m2/m2)</summary>
        /// <value>The local la itot.</value>
        [Description("Total LAI (m2/m2)")] public double localLAItot { get; set; }

        /// <summary>
        /// The initial value of the light extinction coefficient (-)
        /// THe simple Beer's law is used to calculate CoverGreen and CoverTot from the LAI values
        /// </summary>
        /// <value>The local light extinction.</value>
        [Description("Light extinction coefficient (-)")]        public double localLightExtinction { get; set; }

        /// <summary>The initial value of canopy height (mm)</summary>
        /// <value>The height of the local canopy.</value>
        [Description("Height of the canopy (mm)")] public double localCanopyHeight { get; set; }

        /// <summary>The intial value of canopy depth (mm)</summary>
        /// <value>The local canopy depth.</value>
        [Description("Depth of the canopy (mm)")] public double localCanopyDepth { get; set; }

        /// <summary>The initial value of maximum stomatal conductance (m/s)</summary>
        /// <value>The local maximum stomatal conductance.</value>
        [Description("Maximum stomatal conductance (m/s)")] public double localMaximumStomatalConductance { get; set; }

        /// <summary>The initial value of the relative growth rate factor (-)</summary>
        /// <value>The local FRGR.</value>
        [Description("Frgr - effect on stomatal conductance (-)")] public double localFrgr { get; set; }

        /// <summary>
        /// The initial value of water demand (mm/day) - will eventually be replaced by a calculation by MicroClimate
        /// </summary>
        /// <value>The local demand water.</value>
        [Description("Water demand (mm /day)")] public double localDemandWater { get; set; }

        /// <summary>The initial value of nitrogen demand (kgN/ha/day)</summary>
        /// <value>The local demand nitrogen.</value>
        [Description("Nitrogen demand (kgN /ha /day)")]
        public double localDemandNitrogen { get; set; }

        /// <summary>The initial value for nitrate uptake coefficient</summary>
        /// <value>The local kn o3.</value>
        [Description("Nitrate uptake coefficient")]
        public double localKNO3 { get; set; }

        /// <summary>The initial value for ammonium uptake coefficient</summary>
        /// <value>The local kn h4.</value>
        [Description("Ammonium uptake coefficient")]
        public double localKNH4 { get; set; }

        /// <summary>The initial value of green cover (-)</summary>
        /// <value>The local cover green.</value>
        [XmlIgnore]        public double localCoverGreen { get; set; }

        /// <summary>The initial value of total cover (-)</summary>
        /// <value>The local cover tot.</value>
        [XmlIgnore]        public double localCoverTot { get; set; }

        // The variables that are in RootProperties
        /// <summary>
        /// Holds the set of crop root properties that is used by Arbitrator for water and nutrient calculations
        /// </summary>
        public RootProperties RootProperties { get { return LocalRootData; } }
        /// <summary>The local root data</summary>
        RootProperties LocalRootData = new RootProperties();

        /// <summary>
        /// The initial value of rooting depth (mm)
        /// This is used to calculate RootExplorationByLayer and RootLengthDensityByVolume
        /// </summary>
        /// <value>The local root depth.</value>
        [Description("Rooting Depth (mm)")] public double localRootDepth { get; set; }

        /// <summary>
        /// The initial value of the root length density at the soil surface (mm/mm3)
        /// This is used to calculate RootExplorationByLayer and RootLengthDensityByVolume
        /// </summary>
        /// <value>The local surface root length density.</value>
        [Description("Root length density at the soil surface (mm/mm3)")] public double localSurfaceRootLengthDensity { get; set; }

        /// <summary>
        /// The initial value of the extent to which the roots have penetrated the soil layer (0-1)
        /// </summary>
        /// <value>The local root exploration by layer.</value>
        [XmlIgnore] public double[] localRootExplorationByLayer { get; set; }

        /// <summary>The initial value of the root length densities for each soil layer (mm/mm3)</summary>
        /// <value>The local root length density by volume.</value>
        [XmlIgnore] public double[] localRootLengthDensityByVolume { get; set; }

        /// <summary>The temporary depth upper</summary>
        double tempDepthUpper;
        /// <summary>The temporary depth middle</summary>
        double tempDepthMiddle;
        /// <summary>The temporary depth lower</summary>
        double tempDepthLower;

        /// <summary>
        /// Water demand (mm/day) - at the moment is set from the UI but eventually will be supplied by the Arbitrator (or MicroClimate)
        /// </summary>
        [XmlIgnore]        public double demandWater { get; set; }

        /// <summary>
        /// The is the actual supply of water to the plant as an array (mm) of values for each soil layer - calculated  by the Arbitrator
        /// Note that Arbitrator does the uptake so the plant does not do the removal from the soil
        /// </summary>
        [XmlIgnore]        public double[] uptakeWater { get; set; }

        /// <summary>Nitrogen demand (kg N /ha /day) - directly set from the UI</summary>
        [XmlIgnore]        public double demandNitrogen { get; set; }

        /// <summary>
        /// The is the actual supply of nitrogen (nitrate plus ammonium) to the plant as an array (kgN/ha/day) of values for each soil layer - calculated  by the Arbitrator
        /// Note that Arbitrator does the uptake so the plant does not do the removal from the soil
        /// </summary>
        [XmlIgnore]        public double[] uptakeNitrogen { get; set; }

        /// <summary>This is the proportion of the nitrogen uptake from any layer that is nitrate (-)</summary>
        [XmlIgnore]        public double[] uptakeNitrogenPropNO3 { get; set; }

        /// <summary>MicroClimate supplies LightProfile</summary>
        [XmlIgnore]       public CanopyEnergyBalanceInterceptionlayerType[] LightProfile { get; set; }


        // The following event handler will be called once at the beginning of the simulation
        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            CropType = "Slurp";
            uptakeWater = new double[Soil.Thickness.Length];
            uptakeNitrogen = new double[Soil.Thickness.Length];
            uptakeNitrogenPropNO3 = new double[Soil.Thickness.Length];
            
            // set the canopy and root properties here - no need to capture the sets from any Managers as they directly set the properties
            CanopyProperties.Name = "Slurp";
            CanopyProperties.CoverGreen = 1.0 - Math.Exp(-1*localLightExtinction*localLAIGreen);
            CanopyProperties.CoverTot = 1.0 - Math.Exp(-1 * localLightExtinction * localLAItot);
            CanopyProperties.CanopyDepth = localCanopyDepth;
            CanopyProperties.CanopyHeight = localCanopyHeight;
            CanopyProperties.LAIGreen = localLAIGreen;
            CanopyProperties.LAItot = localLAItot;
            CanopyProperties.MaximumStomatalConductance = localMaximumStomatalConductance;
            CanopyProperties.HalfSatStomatalConductance = 200.0;  // should this be on the UI?
            CanopyProperties.CanopyEmissivity = 0.96;
            CanopyProperties.Frgr = localFrgr;

            SoilCrop soilCrop = this.Soil.Crop(Name) as SoilCrop;

            RootProperties.KL = soilCrop.KL;

            RootProperties.MinNO3ConcForUptake = new double[Soil.Thickness.Length];
            RootProperties.MinNH4ConcForUptake = new double[Soil.Thickness.Length];

            RootProperties.LowerLimitDep = new double[Soil.Thickness.Length];

            for (int j = 0; j < Soil.Thickness.Length; j++)
            {
                RootProperties.LowerLimitDep[j] = soilCrop.LL[j] * Soil.Thickness[j];
                RootProperties.MinNO3ConcForUptake[j] = 0.0;
                RootProperties.MinNH4ConcForUptake[j] = 0.0;
            }
            RootProperties.RootDepth = localRootDepth;
            RootProperties.KNO3 = localKNO3;
            RootProperties.KNH4 = localKNH4;

            RootProperties.UptakePreferenceByLayer = new double[Soil.Thickness.Length];
            for (int j = 0; j < Soil.Thickness.Length; j++)
            {
                RootProperties.UptakePreferenceByLayer[j] = 1.0;
            }

            localRootExplorationByLayer = new double[Soil.Thickness.Length];
            localRootLengthDensityByVolume = new double[Soil.Thickness.Length];

            uptakeWater = new double[Soil.Thickness.Length];

            tempDepthUpper = 0.0;
            tempDepthMiddle = 0.0;
            tempDepthLower = 0.0;

            demandWater = localDemandWater;

            // calculate root exploration (proprotion of the layer occupied by the roots) for each layer
            for (int j = 0; j < Soil.Thickness.Length; j++)
            {

                tempDepthLower += Soil.Thickness[j];  // increment soil depth thorugh the layers
                tempDepthMiddle = tempDepthLower - Soil.Thickness[j]*0.5;
                tempDepthUpper = tempDepthLower - Soil.Thickness[j];
                if (tempDepthUpper < localRootDepth)        // set the root exploration
                {
                    localRootExplorationByLayer[j] = 1.0;
                }
                else if (tempDepthLower <= localRootDepth)
                {
                    localRootExplorationByLayer[j] = MathUtilities.Divide(localRootDepth - tempDepthUpper, Soil.Thickness[j], 0.0);
                }
                else
                {
                    localRootExplorationByLayer[j] = 0.0;
                }
                // set a triangular root length density by scaling layer depth against maximum rooting depth, constrain the multiplier between 0 and 1
                localRootLengthDensityByVolume[j] = localSurfaceRootLengthDensity * localRootExplorationByLayer[j] * (1.0 - MathUtilities.Constrain(MathUtilities.Divide(tempDepthMiddle, localRootDepth, 0.0), 0.0, 1.0));
            }
            RootProperties.RootExplorationByLayer = localRootExplorationByLayer;
            RootProperties.RootLengthDensityByVolume = localRootLengthDensityByVolume;
        }


        /// <summary>Called when [do plant potential growth].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        private void OnDoPlantPotentialGrowth(object sender, EventArgs e)
        {
            // nothing for Slurp to do in here but a full/proper crop model would use the LightProfile, PotenialEP and ActualEP to calculate a 
            // PotentialNDemand - the N that the plant wants in order to satisfy growth after accounting for the water supply

            demandNitrogen = localDemandNitrogen;

            //Summary.WriteMessage(FullPath, "Slurp " + CanopyProperties.Name + " has a value of " + uptakeWater[3].ToString() + " for the last element in the soil water supply");


        }

        /// <summary>Called when [do plant actual growth].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPlantActualGrowth")]
        private void OnDoPlantActualGrowth(object sender, EventArgs e)
        {
            // At this stage a full/proper crop model would be supplied the N uptake from Arbitrator and it would then complete its calculations for the day
        }

    }   
}