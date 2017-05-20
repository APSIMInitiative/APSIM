﻿using System;
using System.Reflection;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.Xml;
using System.Linq;
using Models.Core;
using APSIM.Shared.Utilities;
using Models.SurfaceOM;
using Models.Interfaces;

namespace Models.Soils
{

    /// <summary>
    /// Computes the soil C and N processes
    /// </summary>
    /// <remarks>
    /// Implements internal 'patches', which are replicates of state variables and processes used for simulating soil variability
    /// 
    /// Based on a more-or-less direct port of the Fortran SoilN model  -  Ported by Eric Zurcher Sept/Oct 2010
    /// Code tidied up by RCichota initially on Aug/Sep-2012 (updates in Feb-Mar/2014)
    /// </remarks>
    // RJM public partial class SoilNitrogen
    [Serializable]
    [ValidParent(ParentType = typeof(Soil))]
    public partial class SoilNitrogen : Model, ISolute
    {
        /// <summary>Initializes a new instance of the <see cref="SoilNitrogen"/> class.</summary>
        public SoilNitrogen()
        {
            Patch = new List<soilCNPatch>();
            soilCNPatch newPatch = new soilCNPatch(this);
            Patch.Add(newPatch);
            Patch[0].RelativeArea = 1.0;
            Patch[0].PatchName = "base";

        }

        #region >>  Events which we publish

        /// <summary>
        /// Event to communicate other modules of C and/or N changes to/from outside the simulation
        /// </summary>
        /// <param name="Data">The data.</param>
        // RJM [Event]        
        public delegate void ExternalMassFlowDelegate(ExternalMassFlowType Data);
        /// <summary>Occurs when [external mass flow].</summary>
        public event ExternalMassFlowDelegate ExternalMassFlow;


        /* RJM
        
        /// <summary>
        /// Event to communicate other modules that solutes have been added to the simulation (owned by SoilNitrogen)
        /// </summary>
        /// <param name="Data">The data.</param>        
        public delegate void NewSoluteDelegate(NewSoluteType Data);
        /// <summary>Occurs when [new solute].</summary>
        public event NewSoluteDelegate NewSolute;

            */

        /// <summary>
        /// Event to comunicate other modules (SurfaceOM) that residues have been decomposed
        /// </summary>
        // RJM [Event]
        public delegate void SurfaceOrganicMatterDecompDelegate(SurfaceOrganicMatterDecompType Data);

        // <summary>Occurs when [actualresiduedecompositioncalculated].</summary>
        // RJM public event SurfaceOrganicMatterDecompDelegate actualresiduedecompositioncalculated;

        #endregion events published

        #region >>  Setup events handlers and methods

        /// <summary>Performs the initial checks and setup</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {

            Patch = new List<soilCNPatch>();  // Section RJM Copied from X
            soilCNPatch newPatch = new soilCNPatch(this);
            Patch.Add(newPatch);
            Patch[0].RelativeArea = 1.0;
            Patch[0].PatchName = "base";

            // Variable handling when using APSIMX
            initDone = false;
            dlayer = Soil.Thickness;
            SoilDensity = Soil.BD;
            sat_dep = MathUtilities.Multiply(Soil.SAT, Soil.Thickness);
            dul_dep = MathUtilities.Multiply(Soil.DUL, Soil.Thickness);
            ll15_dep = MathUtilities.Multiply(Soil.LL15, Soil.Thickness);
            sw_dep = MathUtilities.Multiply(Soil.InitialWaterVolumetric, Soil.Thickness);
            oc = Soil.OC;
            ph = Soil.PH;
            salb = Soil.SoilWater.Salb;
            NO3ppm = Soil.InitialNO3N;
            NH4ppm = Soil.InitialNH4N;
            ureappm = new double[Soil.Thickness.Length];
            
            // Tsoil = null;  RJM
            // simpleST = null;  //RJM removed - deprecated

            fbiom = Soil.FBiom;
            finert = Soil.FInert;

            soil_cn = SoilOrganicMatter.SoilCN;
            root_wt = SoilOrganicMatter.RootWt;
            root_cn = SoilOrganicMatter.RootCN;
            enr_a_coeff = SoilOrganicMatter.EnrACoeff;
            enr_b_coeff = SoilOrganicMatter.EnrBCoeff;

            stf_DecompFOM_Topt = new double[] { 32, 32 };
            stf_DecompFOM_FctrZero = new double[] { 0, 0 };
            stf_DecompFOM_CvExp = new double[] { 2, 2 };
            swf_DecompFOM_swx = new double[] { 0, 1, 1.5, 2, 3};
            swf_DecompFOM_y = new double[] { 0, 0, 1, 1, 0.5};

            stf_MinerSOM_Topt = new double[] { 32, 32 };
            stf_MinerSOM_FctrZero = new double[] { 0, 0 };
            stf_MinerSOM_CvExp = new double[] { 2, 2 };
            swf_MinerSOM_swx = new double[] { 0, 1, 1.5, 2, 3 };
            swf_MinerSOM_y = new double[] { 0, 0, 1, 1, 0.5 };

            stf_Hydrol_Topt = new double[] { 32, 32 };
            stf_Hydrol_FctrZero = new double[] { 0.2, 0.2 };
            stf_Hydrol_CvExp = new double[] { 1, 1 };
            swf_Hydrol_swx = new double[] { 0, 1, 1.4, 2.4, 3 };
            swf_Hydrol_y = new double[] { 0.2, 0.2, 1.0, 1.0, 0.7 };

            stf_Nitrif_Topt = new double[] { 32, 32 };
            stf_Nitrif_FctrZero = new double[] { 0, 0 };
            stf_Nitrif_CvExp = new double[] { 2, 2 };
            swf_Nitrif_swx = new double[] { 0, 1, 1.25, 2, 3 };
            swf_Nitrif_y = new double[] { 0, 0, 1, 1, 0 };
            phf_Nitrif_phx = new double[] { 0, 4.5, 6, 8, 9, 14 };
            phf_Nitrif_y = new double[] { 0, 0, 1, 1, 0, 0 };

            TOptmimunNitrififaction = new double[] { 32, 32 };
            FactorZeroNitrification = new double[] { 0, 0 };
            ExponentNitrification = new double[] { 2, 2 };
            Nitrification_swx = new double[] { 0, 1, 1.25, 2, 3 };
            Nitrification_swy = new double[] { 0, 0, 1, 1, 0 };
            Nitritation_phx = new double[] { 0, 4.5, 6, 8, 9, 14 };
            Nitritation_phy = new double[] { 0, 0, 1, 1, 0, 0 };
            //Nitratation_NIFx = new double[] { 0, 5, 20, 25 };
            //Nitratation_NIFy = new double[] { 0.5, 0.5, 1, 1 };

            TOptmimunCodenitrififaction = new double[] { 50.056, 50.056 };
            FactorZeroCodenitrification = new double[] { 0.1, 0.1 };
            ExponentCodenitrification = new double[] { 68350, 68350 };
            Codenitrification_swx = new double[] { 0, 2, 3 };
            Codenitrification_swy = new double[] { 0, 0, 1 };
            Codenitrification_phx = new double[] { 0, 4.5, 6, 8, 9, 14 };
            Codenitrification_phy = new double[] { 0, 0, 1, 1, 0, 0 };
            Codenitrification_NHNOx = new double[] { 0, 4.5, 6, 8, 9, 14 };
            Codenitrification_NHNOy = new double[] { 0, 0, 1, 1, 0, 0 };

            stf_dnit_Topt = new double[] { 50.056, 50.0056 };
            stf_dnit_FctrZero = new double[] { 0.1, 0.1 };
            stf_dnit_CvExp = new double[] { 68350, 68350 };
            swf_dnit_swx = new double[] { 0, 2, 3 };
            swf_dnit_y = new double[] { 0, 0, 1 };
            swpsf_dnit_swpx = new double[] { 0, 28, 88, 100 };
            swpsf_dnit_y = new double[] { 0.1, 0.1, 1, 1.18 };

            // set the creation date
            Patch[0].CreationDate = Clock.Today;

            // check few initialisation parameters
            CheckParameters();

            // set the size of arrays
            ResizeLayeredVariables(dlayer.Length);

            // check the initial values of some basic variables
            CheckInitialVariables();

            // set the variables up with their the initial values
            SetInitialValues();

            /* RJM removed - deprecated
            // initialise soil temperature
            if (usingSimpleSoilTemp)
            {
                simpleST = new simpleSoilTemp(MetFile.Latitude, MetFile.Tav, MetFile.Amp, MetFile.MinT, MetFile.MaxT);
                Tsoil = simpleST.SoilTemperature(Clock.Today, MetFile.MinT, MetFile.MaxT, MetFile.Radn, salb, dlayer, SoilDensity, ll15_dep, sw_dep);
            }*/

            // notify apsim about solutes
            // RJM AdvertiseMySolutes();

            // print SoilN report
            WriteSummaryReport();
            
        }

        /* /// <summary>
        /// Sets the commands for resetting the module to the initial setup
        /// </summary>
        // RJM [EventHandler(EventName = "reset")] */

        /// <summary>Reset the state values to those set during the initialisation</summary>
        public void Reset()
        {
            isResetting = true;

            // Save the present C and N status
            StoreStatus();

            // reset the size of arrays
            ResizeLayeredVariables(dlayer.Length);

            // reset C and N variables, i.e. redo initialisation and setup
            SetInitialValues();

            // RJM // Save the present C and N status
            // RJM StoreStatus();  // RJM

            // reset soil temperature
            /* RJM removed - deprecated
            if (usingSimpleSoilTemp)
            {
                simpleST = new simpleSoilTemp(MetFile.Latitude, MetFile.Tav, MetFile.Amp, MetFile.MinT, MetFile.MaxT);
                Tsoil = simpleST.SoilTemperature(Clock.Today, MetFile.MinT, MetFile.MaxT, MetFile.Radn, salb, dlayer, SoilDensity, ll15_dep, sw_dep);
            } */

            // get the changes of state and publish (let other component to know)
            SendDeltaState();

            Console.WriteLine();
            Console.WriteLine("        - Re-setting SoilNitrogen state variables");

            // print SoilN report
            WriteSummaryReport();

            isResetting = false;
        }

        /// <summary>
        /// Checks general initialisation parameters, and let user know of some settings
        /// </summary>
        private void CheckParameters()
        {
            string myMessage = "";
            int nLayers = dlayer.Length;

            Console.WriteLine();
            myMessage = "        - Reading/checking parameters";
            Console.WriteLine(myMessage);
            Console.WriteLine();

            SoilNParameterSet = SoilNParameterSet.Trim();
            myMessage = "          - Using " + SoilNParameterSet + " SoilN parameter set specification";
            Console.WriteLine(myMessage);

            // check whether soil temperature is present. If not, check whether the basic params for simpleSoilTemp have been supplied
            if (SimpleSoilTempAllowed)
                usingSimpleSoilTemp = (ave_soil_temp == null);
            if (usingSimpleSoilTemp)
                myMessage = "           + Soil temperature calculated internally";
            else
                myMessage = "           + Soil temperature supplied by apsim";
            Console.WriteLine(myMessage);

            // check whether ph is supplied, use a default if not - might be better to throw an exception?
            usingSimpleSoilpH = (ph == null);
            if (usingSimpleSoilpH)
            {
                ph = new double[nLayers];
                for (int layer = 0; layer < nLayers; ++layer)
                    ph[layer] = defaultInitialpH;
                myMessage = "          + Soil pH was not supplied, the value " + defaultInitialpH.ToString("0.00") + " will be used for all layers";
            }
            else
                myMessage = "           + Soil pH supplied by apsim";
            Console.WriteLine(myMessage);

            // Check if all fom values have been supplied
            if (fract_carb.Length != fom_types.Length)
                throw new Exception("Number of \"fract_carb\" different to \"fom_type\"");
            if (fract_cell.Length != fom_types.Length)
                throw new Exception("Number of \"fract_cell\" different to \"fom_type\"");
            if (fract_lign.Length != fom_types.Length)
                throw new Exception("Number of \"fract_lign\" different to \"fom_type\"");

            // Check if all C:N values have been supplied. If not use average C:N ratio in all pools
            if (fomPoolsCNratio == null || fomPoolsCNratio.Length < 3)
            {
                fomPoolsCNratio = new double[3];
                for (int i = 0; i < 3; i++)
                    fomPoolsCNratio[i] = iniFomCNratio;
            }

            // Check if initial fom depth has been supplied, if not assume that initial fom is distributed over the whole profile
            if (iniFomDepth <= epsilon)
                iniFomDepth = SumDoubleArray(dlayer);

            // Check if initial root depth has been supplied, if not use whole profile
            if (rootDepth <= epsilon)
                rootDepth = SumDoubleArray(dlayer);

            // Calculate conversion factor from kg/ha to ppm (mg/kg)
            convFactor = new double[nLayers];
            for (int layer = 0; layer < nLayers; ++layer)
                convFactor[layer] = MathUtilities.Divide(100.0, SoilDensity[layer] * dlayer[layer], 0.0);

            // Check parameters for patches
            if (DepthToTestByLayer <= epsilon)
                LayerDepthToTestDiffs = dlayer.Length - 1;
            else
                LayerDepthToTestDiffs = getCumulativeIndex(DepthToTestByLayer, dlayer);

        }

        /// <summary>
        /// Checks whether initial values for OM and mineral N were given and make sure all layers have valid values
        /// </summary>
        /// <remarks>
        /// Initial OC values are mandatory, but not for all layers. Zero is assumed for layers not set.
        /// Initial values for mineral N are optional, assume zero if not given
        /// The inital FOM values are given as a total amount which is distributed using an exponential function.
        /// In this procedure the fraction of total FOM that goes in each layer is also computed
        /// </remarks>
        private void CheckInitialVariables()
        {
            int nLayers = dlayer.Length;

            // ensure that array for initial OC have a value for each layer
            if (reset_oc.Length < nLayers)
                Console.WriteLine("           + Values supplied for the initial OC content do not cover all layers - zero will be assumed");
            else if (reset_oc.Length > nLayers)
                Console.WriteLine("           + More values were supplied for the initial OC content than the number of layers - excess will ignored");
            Array.Resize(ref reset_oc, nLayers);

            // ensure that array for initial urea content have a value for each layer
            if (reset_ureappm == null)
                Console.WriteLine("           + No values were supplied for the initial content of urea - zero will be assumed");
            else if (reset_ureappm.Length < nLayers)
                Console.WriteLine("           + Values supplied for the initial content of urea do not cover all layers - zero will be assumed");
            else if (reset_ureappm.Length > nLayers)
                Console.WriteLine("           + More values were supplied for the initial content of urea than the number of layers - excess will ignored");
            Array.Resize(ref reset_ureappm, nLayers);

            // ensure that array for initial content of NH4 have a value for each layer
            if (reset_nh4ppm == null)
                Console.WriteLine("           + No values were supplied for the initial content of nh4 - zero will be assumed");
            else if (reset_nh4ppm.Length < nLayers)
                Console.WriteLine("           + Values supplied for the initial content of nh4 do not cover all layers - zero will be assumed");
            else if (reset_nh4ppm.Length > nLayers)
                Console.WriteLine("           + More values were supplied for the initial content of nh4 than the number of layers - excess will ignored");
            Array.Resize(ref reset_nh4ppm, nLayers);

            // ensure that array for initial content of NO3 have a value for each layer
            if (reset_no3ppm == null)
                Console.WriteLine("           + No values were supplied for the initial content of no3 - zero will be assumed");
            else if (reset_no3ppm.Length < nLayers)
                Console.WriteLine("           + Values supplied for the initial content of no3 do not cover all layers - zero will be assumed");
            else if (reset_no3ppm.Length > nLayers)
                Console.WriteLine("           + More values were supplied for the initial content of no3 than the number of layers - excess will ignored");
            Array.Resize(ref reset_no3ppm, nLayers);

            // compute initial FOM distribution in the soil (FOM fraction)
            FOMiniFraction = new double[nLayers];
            double totFOMfraction = 0.0;
            int deepestLayer = getCumulativeIndex(iniFomDepth, dlayer);
            double cumDepth = 0.0;
            double FracLayer = 0.0;
            for (int layer = 0; layer <= deepestLayer; layer++)
            {
                FracLayer = Math.Min(1.0, MathUtilities.Divide(iniFomDepth - cumDepth, dlayer[layer], 0.0));
                cumDepth += dlayer[layer];
                FOMiniFraction[layer] = FracLayer * Math.Exp(-FOMDistributionCoefficient * Math.Min(1.0, MathUtilities.Divide(cumDepth, iniFomDepth, 0.0)));
            }

            // distribute FOM through layers
            totFOMfraction = SumDoubleArray(FOMiniFraction);
            for (int layer = 0; layer <= deepestLayer; layer++)
            {
                FOMiniFraction[layer] /= totFOMfraction;
            }

            // initialise some residue decomposition variables
            residueName = new string[1] { "none" };
            pot_c_decomp = new double[1] { 0.0 };
        }

        /// <summary>
        /// Performs the initial setup and calculations
        /// </summary>
        /// <remarks>
        /// This procedure is also used onReset
        /// </remarks>
        private void SetInitialValues()
        {
            // general variables
            int nLayers = dlayer.Length;

            // convert and set C an N values over the profile
            for (int layer = 0; layer < nLayers; layer++)
            {
                // convert the amounts of mineral N
                double iniUrea = MathUtilities.Divide(reset_ureappm[layer], convFactor[layer], 0.0);       // convert from ppm to kg/ha
                double iniNH4 = MathUtilities.Divide(reset_nh4ppm[layer], convFactor[layer], 0.0);
                double iniNO3 = MathUtilities.Divide(reset_no3ppm[layer], convFactor[layer], 0.0);

                // calculate total soil C
                double Soil_OC = reset_oc[layer] * 10000;                       // = (oc/100)*1000000 - convert from % to ppm
                Soil_OC = MathUtilities.Divide(Soil_OC, convFactor[layer], 0.0);  //Convert from ppm to kg/ha

                // calculate inert soil C
                double InertC = finert[layer] * Soil_OC;
                double InertN = MathUtilities.Divide(InertC, hum_cn, 0.0);

                // calculate microbial biomass C and N
                double BiomassC = MathUtilities.Divide((Soil_OC - InertC) * fbiom[layer], 1.0 + fbiom[layer], 0.0);
                double BiomassN = MathUtilities.Divide(BiomassC, MBiomassCNr, 0.0);

                // calculate C and N values for active humus
                double HumusC = Soil_OC - BiomassC;
                double HumusN = MathUtilities.Divide(HumusC, hum_cn, 0.0);

                // distribute C over fom pools
                double[] fomPool = new double[3];
                fomPool[0] = iniFomWt * FOMiniFraction[layer] * fract_carb[FOMtypeID_reset] * defaultCarbonInFOM;
                fomPool[1] = iniFomWt * FOMiniFraction[layer] * fract_cell[FOMtypeID_reset] * defaultCarbonInFOM;
                fomPool[2] = iniFomWt * FOMiniFraction[layer] * fract_lign[FOMtypeID_reset] * defaultCarbonInFOM;

                // set the initial values across patches - assume there is only one patch (as it is initialisation)
                int k = 0;
                Patch[k].urea[layer] = iniUrea;
                Patch[k].nh4[layer] = iniNH4;
                Patch[k].no3[layer] = iniNO3;
                Patch[k].inert_c[layer] = InertC;
                Patch[k].inert_n[layer] = InertN;
                Patch[k].biom_c[layer] = BiomassC;
                Patch[k].biom_n[layer] = BiomassN;
                Patch[k].hum_c[layer] = HumusC;
                Patch[k].hum_n[layer] = HumusN;
                Patch[k].fom_c[0][layer] = fomPool[0];
                Patch[k].fom_c[1][layer] = fomPool[1];
                Patch[k].fom_c[2][layer] = fomPool[2];
                Patch[k].fom_n[0][layer] = MathUtilities.Divide(fomPool[0], fomPoolsCNratio[0], 0.0);
                Patch[k].fom_n[1][layer] = MathUtilities.Divide(fomPool[1], fomPoolsCNratio[1], 0.0);
                Patch[k].fom_n[2][layer] = MathUtilities.Divide(fomPool[2], fomPoolsCNratio[2], 0.0);

                // set maximum uptake rates for N forms (only really used for AgPasture when patches exist)
                MaximumNH4UptakeRate[layer] = reset_MaximumNH4Uptake / convFactor[layer];
                MaximumNO3UptakeRate[layer] = reset_MaximumNO3Uptake / convFactor[layer];
            }

            Patch[0].CalcTotalMineralNInRootZone();

            initDone = true;

            StoreStatus();

        }

        /// <summary>
        /// Sets the size of arrays (with nLayers)
        /// </summary>
        /// <remarks>
        /// This is used during initialisation and whenever the soil profile changes (thus not often at all)
        /// </remarks>
        /// <param name="nLayers">The number of layers</param>
        private void ResizeLayeredVariables(int nLayers)
        {
            // Amounts - N
            //Array.Resize(ref _nh4, nLayers);
            //Array.Resize(ref _no3, nLayers);
            //Array.Resize(ref _urea, nLayers);
            //Array.Resize(ref TodaysInitialNO3, nLayers);
            //Array.Resize(ref TodaysInitialNH4, nLayers);
            //Array.Resize(ref fom_n_pool[0], nLayers);
            //Array.Resize(ref fom_n_pool[1], nLayers);
            //Array.Resize(ref fom_n_pool[2], nLayers);
            //Array.Resize(ref biom_n, nLayers);
            //Array.Resize(ref hum_n, nLayers);

            //// Amounts - C
            //Array.Resize(ref fom_c_pool[0], nLayers);
            //Array.Resize(ref fom_c_pool[1], nLayers);
            //Array.Resize(ref fom_c_pool[2], nLayers);
            //Array.Resize(ref inert_c, nLayers);
            //Array.Resize(ref biom_c, nLayers);
            //Array.Resize(ref hum_c, nLayers);

            //// deltas
            //Array.Resize(ref dlt_c_res_to_biom, nLayers);
            //Array.Resize(ref dlt_c_res_to_hum, nLayers);
            //Array.Resize(ref dlt_c_res_to_atm, nLayers);
            //Array.Resize(ref dlt_res_nh4_min, nLayers);
            //Array.Resize(ref dlt_res_no3_min, nLayers);

            //Array.Resize(ref dlt_urea_hydrolysis, nLayers);
            //Array.Resize(ref dlt_nitrification, nLayers);
            //Array.Resize(ref dlt_n2o_nitrif, nLayers);
            //Array.Resize(ref dlt_no3_dnit, nLayers);
            //Array.Resize(ref dlt_n2o_dnit, nLayers);
            //Array.Resize(ref dlt_fom_n_min, nLayers);
            //Array.Resize(ref dlt_biom_n_min, nLayers);
            //Array.Resize(ref dlt_hum_n_min, nLayers);
            //Array.Resize(ref nh4_deficit_immob, nLayers);
            //for (int i = 0; i < 3; i++)
            //{
            //    Array.Resize(ref dlt_c_fom_to_biom[i], nLayers);
            //    Array.Resize(ref dlt_c_fom_to_hum[i], nLayers);
            //    Array.Resize(ref dlt_c_fom_to_atm[i], nLayers);
            //    Array.Resize(ref dlt_n_fom[i], nLayers);
            //}
            //Array.Resize(ref dlt_biom_c_hum, nLayers);
            //Array.Resize(ref dlt_biom_c_atm, nLayers);
            //Array.Resize(ref dlt_hum_c_biom, nLayers);
            //Array.Resize(ref dlt_hum_c_atm, nLayers);

            Array.Resize(ref InhibitionFactor_Nitrification, nLayers);

            Array.Resize(ref MaximumNH4UptakeRate, nLayers);
            Array.Resize(ref MaximumNO3UptakeRate, nLayers);

            for (int k = 0; k < Patch.Count; k++)
                Patch[k].ResizeLayeredVariables(nLayers);
        }

        /// <summary>
        /// Clear (zero out) the values of variables storing deltas
        /// </summary>
        /// <remarks>
        /// This is used to zero out the variables that need resetting every day, those that are not necessarily computed everyday
        /// </remarks>
        private void ClearDeltaVariables()
        {
            // residue decomposition
            Array.Clear(pot_c_decomp, 0, pot_c_decomp.Length);
            // this is also cleared onPotentialResidueDecompositionCalculated, but it is here to ensure it will be reset every timestep

            for (int k = 0; k < Patch.Count; k++)
                Patch[k].ClearDeltaVariables();
        }


        /* RJM 
        /// <summary>
        /// Notifies any interested module about this module's ownership of solute information.
        /// </summary>
        private void AdvertiseMySolutes()
        {

            if (NewSolute != null)
            {
                string[] solute_names;
                if (useOrganicSolutes)
                {
                    // RJM solute_names = new string[7] { "urea", "nh4", "no3", "org_c_pool1", "org_c_pool2", "org_c_pool3", "org_n" };
                    solute_names = new string[7] { "urea", "NH4", "NO3", "org_c_pool1", "org_c_pool2", "org_c_pool3", "org_n" };
                }
                else
                { // don't publish the organic solutes
                    // RJM solute_names = new string[3] { "urea", "nh4", "no3" };
                    solute_names = new string[3] { "urea", "NH4", "NO3" };
                }

                NewSoluteType SoluteData = new NewSoluteType();
                SoluteData.OwnerFullPath = Apsim.FullPath(this);
                SoluteData.solutes = solute_names;

                NewSolute.Invoke(SoluteData);
            }
        }

        */

        /// <summary>
        /// Store today's initial N amounts
        /// </summary>
        private void StoreStatus()
        {
            TodaysInitialN = SumDoubleArray(TotalN);
            TodaysInitialC = SumDoubleArray(TotalC);

            for (int k = 0; k < Patch.Count; k++)
                Patch[k].StoreStatus();
        }

        /// <summary>
        /// Calculates variations in C an N, and publishes MassFlows to APSIM
        /// </summary>
        private void SendDeltaState()
        {
            double dltN = SumDoubleArray(TotalN) - TodaysInitialN;
            double dltC = SumDoubleArray(TotalC) - TodaysInitialC;

            SendExternalMassFlowN(dltN);
            SendExternalMassFlowC(dltC);
        }

        /// <summary>
        /// Writes in the summaryfile a report about setup and status of SoilNitrogen
        /// </summary>
        private void WriteSummaryReport()
        {
            Console.WriteLine();

            Console.Write(@"
                      Soil Profile Properties
          ------------------------------------------------
           Layer    pH    OC     NO3     NH4    Urea
                         (%) (kg/ha) (kg/ha) (kg/ha)
          ------------------------------------------------
");
            for (int layer = 0; layer < dlayer.Length; ++layer)
            {
                Console.WriteLine("          {0,4:d1}     {1,4:F2}  {2,4:F2}  {3,6:F2}  {4,6:F2}  {5,6:F2}",
                layer + 1, ph[layer], oc[layer], NO3[layer], NH4[layer], urea[layer]);
            }
            Console.WriteLine("          ------------------------------------------------");
            Console.WriteLine("           Totals              {0,6:F2}  {1,6:F2}  {2,6:F2}",
                      SumDoubleArray(NO3), SumDoubleArray(NH4), SumDoubleArray(urea));
            Console.WriteLine("          ------------------------------------------------");
            Console.WriteLine();
            Console.Write(@"
                  Initial Soil Organic Matter Status
          ---------------------------------------------------------
           Layer      Hum-C   Hum-N  Biom-C  Biom-N   FOM-C   FOM-N
                    (kg/ha) (kg/ha) (kg/ha) (kg/ha) (kg/ha) (kg/ha)
          ---------------------------------------------------------
");

            double TotalFomC = 0.0;
            for (int layer = 0; layer < dlayer.Length; ++layer)
            {
                TotalFomC += FOMC[layer];
                Console.WriteLine("          {0,4:d1}   {1,10:F1}{2,8:F1}{3,8:F1}{4,8:F1}{5,8:F1}{6,8:F1}",
                layer + 1, HumicC[layer], HumicN[layer], MicrobialC[layer], MicrobialN[layer], FOMC[layer], FOMN[layer]);
            }
            Console.WriteLine("          ---------------------------------------------------------");
            Console.WriteLine("           Totals{0,10:F1}{1,8:F1}{2,8:F1}{3,8:F1}{4,8:F1}{5,8:F1}",
                SumDoubleArray(HumicC), SumDoubleArray(HumicN), SumDoubleArray(MicrobialC),
                SumDoubleArray(MicrobialN), TotalFomC, SumDoubleArray(FOMN));
            Console.WriteLine("          ---------------------------------------------------------");
            Console.WriteLine();
        }

        #endregion setup events

        #region >>  Process events handlers and methods

        #region »   Recurrent processes (each timestep)

        /// <summary>
        /// Sets the commands for each timestep - at very beginning of of it
        /// </summary>
        // RJM [EventHandler(EventName = "tick")]
        // RJM public void OnTick(TimeType time)
        [EventSubscribe("tick")]  // RJM TODO Check
        
        private void OnTick(object sender, EventArgs e)        
        {
            if (initDone)
            {
                // store some initial values, so they may be for mass balance
                StoreStatus();
                // clear variables holding deltas
                ClearDeltaVariables();
            }

            // +  Purpose:
            //      Reset potential decomposition variables
            num_residues = 0;
            Array.Resize(ref pot_c_decomp, 0);
            Array.Resize(ref pot_n_decomp, 0);
            Array.Resize(ref pot_p_decomp, 0);

        }

        /// <summary>
        /// Sets the commands for each timestep - at the main process phase
        /// </summary>
        // RJM TODO Check
        // RJM [EventHandler(EventName = "process")]
        [EventSubscribe("DoSoilOrganicMatter")]  // RJM TODO Check
        private void OnDoSoilOrganicMatter(object sender, EventArgs e)
        {

            // Get potential residue decomposition from surfaceom.
            SurfaceOrganicMatterDecompType SurfaceOrganicMatterDecomp = SurfaceOrganicMatter.PotentialDecomposition();

            // RJM
            //foreach (soilCNPatch aPatch in Patch)
            //    aPatch.OnPotentialResidueDecompositionCalculated(SurfaceOrganicMatterDecomp);
            OnPotentialResidueDecompositionCalculated(SurfaceOrganicMatterDecomp);

            num_residues = SurfaceOrganicMatterDecomp.Pool.Length;

            sw_dep = Soil.Water;

            /* RJM removed - deprecated
            // update soil temperature
            if (usingSimpleSoilTemp)
                Tsoil = simpleST.SoilTemperature(Clock.Today, MetFile.MinT, MetFile.MaxT, MetFile.Radn, salb, dlayer, SoilDensity, ll15_dep, sw_dep);
            else
            */
            // RJM not used? Tsoil = ave_soil_temp;

            // calculate C and N processes
            EvaluateProcesses();

            // send actual decomposition back to surface OM
            //if (!isPondActive || SumDoubleArray(pot_c_decomp) > epsilon)   RJM
            if (!isPondActive) 
                SendActualResidueDecompositionCalculated();
        }

        // RJM /// <summary>
        // RJM /// Sets the commands for each timestep - end of day processes
        // RJM /// </summary>
        // RJM [EventHandler(EventName = "post")]
        // RJM public void OnPost()
        /// <summary>Performs every-day calculations - end of day processes</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoUpdate")]
        private void OnDoUpdate(object sender, EventArgs e)
        {
            // Check whether patch auto amalgamation is allowed
            if ((Patch.Count > 1) && (PatchAutoAmalgamationAllowed))
            {
                if ((PatchAmalgamationApproach.ToLower() == "CompareAll".ToLower()) ||
                    (PatchAmalgamationApproach.ToLower() == "CompareBase".ToLower()) ||
                    (PatchAmalgamationApproach.ToLower() == "CompareAge".ToLower()) ||
                    (PatchAmalgamationApproach.ToLower() == "CompareMerge".ToLower()))
                {
                    CheckPatchAutoAmalgamation();
                }
            }
        }


        /// <summary></summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("EndOfMonth")]
        private void OnEndOfMonth (object sender, EventArgs e)
        {
            // Check whether patch amalgamation by age is allowed (done on a monthly basis)
            if (AllowPatchAmalgamationByAge.ToLower() == "yes") CheckPatchAgeAmalgamation();
        }

        /// <summary>
        /// Performs the soil C and N balance processes, at APSIM timestep.
        /// </summary>
        /// <remarks>
        /// The processes considered, in order, are:
        ///  - Decomposition of surface residues
        ///  - Urea hydrolysis
        ///  - Denitrification + N2O production
        ///  - SOM mineralisation (humus then m. biomass) + decomposition of FOM
        ///  - Nitrification + N2O production
        /// Note: potential surface organic matter decomposition is given by SurfaceOM module, only N balance is considered here
        ///  If there is a pond then surfaceOM is inactive, the decomposition of OM is done wholly by the pond module
        ///  Also, different parameters are used for some processes when pond is active
        /// </remarks>
        private void EvaluateProcesses()
        {
            for (int k = 0; k < Patch.Count; k++)
            {
                // 1. Check surface residues decomposition
                Patch[k].DecomposeResidues();

                // 2. Check urea hydrolysis
                Patch[k].ConvertUrea();

                // 3. Check denitrification
                Patch[k].ConvertNitrate();

                // 4. Check transformations of soil organic matter pools
                Patch[k].ConvertSoilOM();

                // 5. Check nitrification
                Patch[k].ConvertAmmonium();

                // 6. check whether values are ok
                Patch[k].CheckVariables();
            }
        }

        #endregion recurrent processes

        #region »   Sporadic processes (not necessarily every timestep)

        
        /* // RJM 
        
        /// <summary>
        /// Set the commands for writing a summary report to the summaryfile
        /// </summary>
        [EventHandler(EventName = "sum_report")]
        public void OnSum_report()
        {
            WriteSummaryReport();
        } 
        */

            /// <summary>
            /// Passes the information about the potential decomposition of surface residues
            /// </summary>
            /// <remarks>
            /// This information is passed by a residue/SurfaceOM module
            /// </remarks>
            /// <param name="SurfaceOrganicMatterDecomp">Data about the potential decomposition of each residue type on soil surface</param>
            //RJM [EventHandler(EventName = "PotentialResidueDecompositionCalculated")]
        [EventSubscribe("PotentialResidueDecompositionCalculated")]
        public void OnPotentialResidueDecompositionCalculated(SurfaceOrganicMatterDecompType SurfaceOrganicMatterDecomp)
        {

            // number of residues being considered
            int nResidues = SurfaceOrganicMatterDecomp.Pool.Length;

            // zero variables by assigning new array
            residueName = new string[nResidues];
            residueType = new string[nResidues];
            pot_c_decomp = new double[nResidues];
            pot_n_decomp = new double[nResidues];
            pot_p_decomp = new double[nResidues];

            // store potential decomposition into appropriate variables
            for (int residue = 0; residue < nResidues; residue++)
            {
                residueName[residue] = SurfaceOrganicMatterDecomp.Pool[residue].Name;
                residueType[residue] = SurfaceOrganicMatterDecomp.Pool[residue].OrganicMatterType;
                pot_c_decomp[residue] = SurfaceOrganicMatterDecomp.Pool[residue].FOM.C;
                pot_n_decomp[residue] = SurfaceOrganicMatterDecomp.Pool[residue].FOM.N;
                // this P decomposition is needed to formulate data required by SOILP - struth, this is very ugly
                pot_p_decomp[residue] = SurfaceOrganicMatterDecomp.Pool[residue].FOM.P;
            }
        }


        /// <summary>
        /// Sends back to SurfaceOM the information about residue decomposition
        /// </summary>
        private void SendActualResidueDecompositionCalculated()
        {
            // Note:
            //    - Potential decomposition was given to this module by a residue/surfaceOM module. This module evaluated
            //        whether the conditions (C-N balance) allowed the decomposition to happen.
            //      - Now we explicitly tell the sender module the actual decomposition rate for each of its residues.
            //    - If there wasn't enough mineral N to decompose, the rate will be reduced to zero !!  - MUST CHECK THE VALIDITY OF THIS

            //if (actualResidueDecompositionCalculated != null && SumDoubleArray(pot_c_decomp) >= epsilon) RJM
            // if (SumDoubleArray(pot_c_decomp) >= epsilon)  // RJM remoevd
            {
                int nLayers = dlayer.Length;
                num_residues = residueName.Length;
                SurfaceOrganicMatterDecompType ActualSOMDecomp = new SurfaceOrganicMatterDecompType();
                //SurfOMDecomposed.Pool = new SurfaceOrganicMatterDecompPoolType[num_residues];
                Array.Resize(ref ActualSOMDecomp.Pool, num_residues);

                //SurfaceOrganicMatterDecompType ActualSOMDecomp = new SurfaceOrganicMatterDecompType();
                //Array.Resize(ref ActualSOMDecomp.Pool, num_residues);



                for (int residue = 0; residue < num_residues; residue++)
                {
                    // get the total amount decomposed over all existing patches
                    double c_summed = 0.0;
                    double n_summed = 0.0;
                    for (int k = 0; k < Patch.Count; k++)
                    {
                        c_summed += Patch[k].SurfOMActualDecomposition.Pool[residue].FOM.C * Patch[k].RelativeArea;
                        n_summed += Patch[k].SurfOMActualDecomposition.Pool[residue].FOM.N * Patch[k].RelativeArea;
                    }

                    // pack up the structure to return decompositions to SurfaceOrganicMatter
                    ActualSOMDecomp.Pool[residue] = new SurfaceOrganicMatterDecompPoolType();
                    ActualSOMDecomp.Pool[residue].FOM = new FOMType();
                    ActualSOMDecomp.Pool[residue].Name = Patch[0].SurfOMActualDecomposition.Pool[residue].Name;
                    ActualSOMDecomp.Pool[residue].OrganicMatterType = Patch[0].SurfOMActualDecomposition.Pool[residue].OrganicMatterType;
                    ActualSOMDecomp.Pool[residue].FOM.amount = 0.0F;
                    ActualSOMDecomp.Pool[residue].FOM.C = (float)c_summed;
                    ActualSOMDecomp.Pool[residue].FOM.N = (float)n_summed;
                    ActualSOMDecomp.Pool[residue].FOM.P = 0.0F;
                    ActualSOMDecomp.Pool[residue].FOM.AshAlk = 0.0F;
                    // Note: The values for 'amount', 'P', and 'AshAlk' will not be collected by SurfaceOrganicMatter, so send zero as default.

                }
                // raise the event
                // RJM actualResidueDecompositionCalculated.Invoke(SurfOMDecomposed);
                SurfaceOrganicMatter.ActualSOMDecomp = ActualSOMDecomp;
            }
        }

        /// <summary>
        /// Passes the instructions to incorporate FOM to the soil - simple FOM
        /// </summary>
        /// <remarks>
        /// In this event, the FOM is given as a single amount, so it will be assumed that the CN ratios of all fractions are equal
        /// Notes:
        ///  - if N (or CNR) values are not given, no FOM will not be added to soil
        ///  - if both CNR and N values are given, CNR is used and N is overwritten
        /// </remarks>
        /// <param name="inFOMdata">Data about the FOM to be added to the soil</param>
        
        // RJM [EventHandler(EventName = "IncorpFOM")]
        [EventSubscribe("IncorpFOM")]
        private void OnIncorpFOM(FOMLayerType inFOMdata)
        {
            int nLayers = dlayer.Length;

            // get the total amount to be added
            double totalCAmount = 0.0;
            double totalNAmount = 0.0;
            double amountCnotAdded = 0.0;
            double amountNnotAdded = 0.0;
            for (int layer = 0; layer < inFOMdata.Layer.Length; layer++)
            {
                if (layer < nLayers)
                {
                    if (inFOMdata.Layer[layer].FOM.amount >= epsilon)
                    {
                        inFOMdata.Layer[layer].FOM.C = inFOMdata.Layer[layer].FOM.amount * (float)defaultCarbonInFOM;
                        if (inFOMdata.Layer[layer].CNR > epsilon)
                        {   // we have C:N info - note that this has precedence over N amount
                            totalCAmount += inFOMdata.Layer[layer].FOM.C;
                            inFOMdata.Layer[layer].FOM.N = inFOMdata.Layer[layer].FOM.C / inFOMdata.Layer[layer].CNR;
                            totalNAmount += inFOMdata.Layer[layer].FOM.N;
                        }
                        else if (inFOMdata.Layer[layer].FOM.N > epsilon)
                        {   // we have N info
                            totalCAmount += inFOMdata.Layer[layer].FOM.C;
                            totalNAmount += inFOMdata.Layer[layer].FOM.N;
                        }
                        else
                        {   // no info for N
                            amountCnotAdded += inFOMdata.Layer[layer].FOM.C;
                        }
                    }
                    else if (inFOMdata.Layer[layer].FOM.N >= epsilon)
                    {   // no info for C
                        amountNnotAdded += inFOMdata.Layer[layer].FOM.N;
                    }
                }
                else
                    Console.WriteLine(" IncorpFOM: information passed contained more layers than the soil, these will be ignored");
            }

            // If any FOM was passed, make the partition into FOM pools
            if (totalCAmount >= epsilon)
            {
                // check whether a valid FOM type was given
                fom_type = 0;   // use the default if fom_type was not given
                for (int i = 0; i < fom_types.Length; i++)
                {
                    if (inFOMdata.Type == fom_types[i])
                    {
                        fom_type = i;
                        break;
                    }
                }

                // Pack the handled values of fom, so they can be added to the soil
                FOMPoolType myFOMPoolData = new FOMPoolType();
                myFOMPoolData.Layer = new FOMPoolLayerType[inFOMdata.Layer.Length];

                // Now partition the .C and .N amounts into FOM pools
                for (int layer = 0; layer < inFOMdata.Layer.Length; layer++)
                {
                    if (layer < nLayers)
                    {
                        myFOMPoolData.Layer[layer] = new FOMPoolLayerType();
                        myFOMPoolData.Layer[layer].Pool = new FOMType[3];
                        myFOMPoolData.Layer[layer].Pool[0] = new FOMType();
                        myFOMPoolData.Layer[layer].Pool[1] = new FOMType();
                        myFOMPoolData.Layer[layer].Pool[2] = new FOMType();

                        if (inFOMdata.Layer[layer].FOM.C > epsilon)
                        {
                            myFOMPoolData.Layer[layer].nh4 = 0.0F;
                            myFOMPoolData.Layer[layer].no3 = 0.0F;
                            myFOMPoolData.Layer[layer].Pool[0].C = (float)(inFOMdata.Layer[layer].FOM.C * fract_carb[fom_type]);
                            myFOMPoolData.Layer[layer].Pool[1].C = (float)(inFOMdata.Layer[layer].FOM.C * fract_cell[fom_type]);
                            myFOMPoolData.Layer[layer].Pool[2].C = (float)(inFOMdata.Layer[layer].FOM.C * fract_lign[fom_type]);

                            myFOMPoolData.Layer[layer].Pool[0].N = (float)(inFOMdata.Layer[layer].FOM.N * fract_carb[fom_type]);
                            myFOMPoolData.Layer[layer].Pool[1].N = (float)(inFOMdata.Layer[layer].FOM.N * fract_cell[fom_type]);
                            myFOMPoolData.Layer[layer].Pool[2].N = (float)(inFOMdata.Layer[layer].FOM.N * fract_lign[fom_type]);
                        }
                    }
                }

                // actually add the FOM to soil
                IncorporateFOM(myFOMPoolData);
            }
            else
                writeMessage("IncorpFOM: action was not carried out because no amount was given");

            // let the user know of any issues
            if ((amountCnotAdded >= epsilon) | (amountNnotAdded >= epsilon))
            {
                writeMessage("IncorpFOM - Warning Error: The amounts of " + amountCnotAdded.ToString("#0.00") +
                "kgC/ha and " + amountCnotAdded.ToString("#0.00") + "were not added because some information was missing");
            }
        }

        /// <summary>
        /// Passes the instructions to incorporate FOM to the soil - FOM pools
        /// </summary>
        /// <remarks>
        /// In this event, the FOM amount is given already partitioned by pool
        /// </remarks>
        /// <param name="inFOMPoolData">Data about the FOM to be added to the soil</param>
        // RJM [EventHandler(EventName = "IncorpFOMPool")]
        [EventSubscribe("IncorpFOMPool")]
        public void OnIncorpFOMPool(FOMPoolType inFOMPoolData)
        {
            // get the total amount to be added
            double totalCAmount = 0.0;
            double totalNAmount = 0.0;
            double amountCnotAdded = 0.0;
            double amountNnotAdded = 0.0;
            for (int layer = 0; layer < inFOMPoolData.Layer.Length; layer++)
            {
                if (layer < dlayer.Length)
                {
                    for (int pool = 0; pool < 3; pool++)
                    {
                        if (inFOMPoolData.Layer[layer].Pool[pool].C >= epsilon)
                        {   // we have both C and N, can add
                            totalCAmount += inFOMPoolData.Layer[layer].Pool[pool].C;
                            totalNAmount += inFOMPoolData.Layer[layer].Pool[pool].N;
                        }
                        else
                        {   // some data is mising, cannot add
                            amountCnotAdded += inFOMPoolData.Layer[layer].Pool[pool].C;
                            amountNnotAdded += inFOMPoolData.Layer[layer].Pool[pool].N;
                        }
                    }
                }
                else
                    Console.WriteLine(" IncorpFOMPool: information passed contained more layers than the soil, these will be ignored");
            }

            // add FOM to soil layers, if given
            if (totalCAmount >= epsilon)
                IncorporateFOM(inFOMPoolData);
            else
                writeMessage("IncorpFOMPool: action was not carried out because no amount was given");

            // let the user know of any issues
            if ((amountCnotAdded >= epsilon) | (amountNnotAdded >= epsilon))
            {
                writeMessage("IncorpFOMPool - Warning Error: The amounts of " + amountCnotAdded.ToString("#0.00") +
                "kgC/ha and " + amountCnotAdded.ToString("#0.00") + "were not added because some information was missing");
            }
        }

        /// <summary>
        /// Gets the data about incoming FOM, add to the patch's FOM pools
        /// </summary>
        /// <remarks>
        /// The FOM amount is given already partitioned by pool
        /// </remarks>
        /// <param name="FOMPoolData"></param>
        public void IncorporateFOM(FOMPoolType FOMPoolData)
        {
            for (int k = 0; k < Patch.Count; k++)
            {
                for (int layer = 0; layer < FOMPoolData.Layer.Length; layer++)
                {
                    // update FOM amounts and check values
                    for (int pool = 0; pool < 3; pool++)
                    {
                        Patch[k].fom_c[pool][layer] += FOMPoolData.Layer[layer].Pool[pool].C;
                        Patch[k].fom_n[pool][layer] += FOMPoolData.Layer[layer].Pool[pool].N;
                        CheckNegativeValues(ref Patch[k].fom_c[pool][layer], layer, "fom_c[" + (pool + 1).ToString() + "]", "Patch[" + Patch[k].PatchName + "].IncorporateFOM");
                        CheckNegativeValues(ref Patch[k].fom_n[pool][layer], layer, "fom_n[" + (pool + 1).ToString() + "]", "Patch[" + Patch[k].PatchName + "].IncorporateFOM");
                    }
                    // update mineral N forms and check values
                    Patch[k].nh4[layer] += FOMPoolData.Layer[layer].nh4;
                    Patch[k].no3[layer] += FOMPoolData.Layer[layer].no3;
                    CheckNegativeValues(ref Patch[k].nh4[layer], layer, "nh4", "Patch[" + Patch[k].PatchName + "].IncorporateFOM");
                    CheckNegativeValues(ref Patch[k].no3[layer], layer, "no3", "Patch[" + Patch[k].PatchName + "].IncorporateFOM");
                }
            }
        }

        /// <summary>
        /// Passes the information about setting/changes in the soil profile
        /// </summary>
        /// <remarks>
        /// We get the basic soil physics data in here;
        /// The event is also used to account for getting changes due to soil erosion
        /// It is assumed that if there are any changes in the soil profile the module doing it will let us know.
        ///  this will be done by setting both 'soil_loss' and 'n_reduction' (ProfileReductionAllowed) to a non-default value
        /// </remarks>
        /// <param name="NewProfile">Data about the new soil profile</param>
        // RJM [EventHandler(EventName = "new_profile")]
        [EventSubscribe("NewProfile")]
        public void OnNew_profile(NewProfileType NewProfile)
        {
            // get the basic soil data info
            int nLayers = NewProfile.dlayer.Length;
            Array.Resize(ref SoilDensity, nLayers);
            Array.Resize(ref ll15_dep, nLayers);
            Array.Resize(ref dul_dep, nLayers);
            Array.Resize(ref sat_dep, nLayers);
            for (int layer = 0; layer < nLayers; layer++)
            {
                SoilDensity[layer] = (double)NewProfile.bd[layer];
                ll15_dep[layer] = (double)NewProfile.ll15_dep[layer];
                dul_dep[layer] = (double)NewProfile.dul_dep[layer];
                sat_dep[layer] = (double)NewProfile.sat_dep[layer];
            }

            // check the layer structure
            if (dlayer == null)
            {
                // we are at initialisation, set the layer structure
                Array.Resize(ref dlayer, nLayers);
                Array.Resize(ref reset_dlayer, nLayers);
                for (int layer = 0; layer < nLayers; layer++)
                {
                    dlayer[layer] = (double)NewProfile.dlayer[layer];
                    reset_dlayer[layer] = dlayer[layer];
                }
            }
            else if (soil_loss > epsilon && ProfileReductionAllowed)
            {
                // check for variations in the soil profile. and update the C and N amounts appropriately
                // Are these changes mainly (only) due to by erosion? what else??
                double[] new_dlayer = new double[NewProfile.dlayer.Length];
                for (int layer = 0; layer < new_dlayer.Length; layer++)
                    new_dlayer[layer] = (double)NewProfile.dlayer[layer];

                // RCichota: this routine will need carefull review, what happens if soil profile is changed and then we have a reset?
                //  I have set up a 'reset_dlayer' which holds the original dlayer and may be used check the changes in the soil
                //   profile. But what about the other variables??

                // There have been some soil loss, launch the UpdateProfile
                UpdateProfile(new_dlayer);

                // don't we need to update the other variables (at least their size)???
            }
        }

        /// <summary>
        /// Check whether profile has changed and move values between layers
        /// </summary>
        /// <param name="new_dlayer">New values for dlayer</param>
        private void UpdateProfile(double[] new_dlayer)
        {
            // move the values of variables
            for (int k = 0; k < Patch.Count; k++)
                Patch[k].UpdateProfile(new_dlayer);

            // now update the layer structure
            for (int layer = 0; layer < new_dlayer.Length; layer++)
                dlayer[layer] = new_dlayer[layer];
        }

        /// <summary>Gets the changes in mineral N made by other modules</summary>
        /// <param name="NitrogenChanges">The nitrogen changes.</param>
        public void SetNitrogenChanged(NitrogenChangedType NitrogenChanges)
        {
            OnNitrogenChanged(NitrogenChanges);
        }

        /// <summary>
        /// Passes the information about changes in mineral N made by other modules
        /// </summary>
        /// <remarks>
        /// These values will be passed to each existing patch. Generally the values are passed as they come,
        ///  however, if the deltas come from a soil (i.e. leaching) or plant (i.e. uptake) then the values should
        ///  be handled (partioned).  This will be done based on soil N concentration
        /// </remarks>
        /// <param name="NitrogenChanges">The variation (delta) for each mineral N form</param>
        /// 
        // RJM [EventHandler(EventName = "NitrogenChanged")]
        [EventSubscribe("NitrogenChanged")]
        public void OnNitrogenChanged(NitrogenChangedType NitrogenChanges)
        {
            // get the type of module sending this change
            senderModule = NitrogenChanges.SenderType.ToLower();

            // get the total amount of N in root zone (for partition of plant uptake)
            for (int k = 0; k < Patch.Count; k++)
                Patch[k].CalcTotalMineralNInRootZone();

            // check whether there are significant values, if so pass them to appropriate dlt
            if (hasSignificantValues(NitrogenChanges.DeltaUrea, epsilon))
            {
                if ((Patch.Count > 1) && ((senderModule == "WaterModule".ToLower()) || (senderModule == "Plant".ToLower())))
                {
                    // the values come from a module that requires partition
                    double[][] newDelta = partitionDelta(NitrogenChanges.DeltaUrea, "Urea", PatchNPartitionApproach.ToLower());
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_urea = newDelta[k];
                }
                else
                {
                    // the values come from a module that do not require partition or there is only one patch
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_urea = NitrogenChanges.DeltaUrea;
                }
            }
            // else{}  No values, no action needed

            if (hasSignificantValues(NitrogenChanges.DeltaNH4, epsilon))
            {
                if ((Patch.Count > 1) && ((senderModule == "WaterModule".ToLower()) || (senderModule == "Plant".ToLower())))
                {
                    // the values come from a module that requires partition
                    double[][] newDelta = partitionDelta(NitrogenChanges.DeltaNH4, "NH4", PatchNPartitionApproach.ToLower());
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_nh4 = newDelta[k];
                }
                else
                {
                    // the values come from a module that do not require partition or there is only one patch
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_nh4 = NitrogenChanges.DeltaNH4;
                }
            }
            // else{}  No values, no action needed

            if (hasSignificantValues(NitrogenChanges.DeltaNO3, epsilon))
            {
                if ((Patch.Count > 1) && ((senderModule == "WaterModule".ToLower()) || (senderModule == "Plant".ToLower())))
                {
                    // the values come from a module that requires partition
                    double[][] newDelta = partitionDelta(NitrogenChanges.DeltaNO3, "NO3", PatchNPartitionApproach.ToLower());
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_no3 = newDelta[k];
                }
                else
                {
                    // the values come from a module that do not require partition or there is only one patch
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_no3 = NitrogenChanges.DeltaNO3;
                }
            }
            // else{}  No values, no action needed
        }

        /* /// <summary>
        /// Get the information about urine being added
        /// </summary>
        /// <param name="UrineAdded">Urine deposition data (includes urea N amount, volume, area affected, etc)</param>         */
        // RJM [EventHandler(EventName = "AddUrine")]
        /*[EventSubscribe("AddUrine")]
        public void OnAddUrine(AddUrineType UrineAdded)*/


        /// <summary>
        /// Get the information about urine being added
        /// </summary>
        /// <param name="UrineAdded">Urine deposition data (includes urea N amount, volume, area affected, etc)</param>         
        public void AddUrine(AddUrineType UrineAdded)
        {
            // Starting with the minimalist version. To be updated by Val's group to include a urine patch algorithm
            //urea[0] += UrineAdded.Urea;
        }

        /// <summary>
        /// Passes and handles the information about new patch and add it to patch list
        /// </summary>
        /// <param name="PatchtoAdd">Patch data</param>
        // RJM [EventHandler]
        [EventSubscribe("AddSoilCNPatc")]
        public void OnAddSoilCNPatch_old(AddSoilCNPatchType PatchtoAdd)
        {
            // data passed with this event:
            //.Sender: the name of the module that raised this event
            //.DepositionType: the type of deposition:
            //  - ToAllPaddock: No patch is created, add stuff as given to all patches. It is the default;
            //  - ToSpecificPatch: No patch is created, add stuff to given patches;
            //      (recipient patch is given using its index or name; if not supplied, defaults to homogeneous)
            //  - ToNewPatch: create new patch based on an existing patch, add stuff to created patch;
            //      - recipient or base patch is given using index or name; if not supplied, new patch will be based on the base/Patch[0];
            //      - patches are only created is area is larger than a minimum (minPatchArea);
            //      - new areas are proportional to existing patches;
            //  - NewOverlappingPatches: create new patch(es), these overlap with all existing patches, add stuff to created patches;
            //      (new patches are created only if their area is larger than a minimum (minPatchArea))
            //.AffectedPatches_id (AffectedPatchesByIndex): the index of the existing patches affected by new patch
            //.AffectedPatches_nm (AffectedPatchesByName): the name of the existing patches affected by new patch
            //.AreaFraction: the relative area (fraction) of new patches (0-1)
            //.PatchName: the name(s) of the patch(es) being created
            //.Water: amount of water to add per layer (mm), not handled here
            //.Urea: amount of urea to add per layer (kgN/ha)
            //.NH4: amount of ammonium to add per layer (kgN/ha)
            //.NO3: amount of nitrate to add per layer (kgN/ha)
            //.POX: amount of POx to add per layer (kgP/ha), not handled here
            //.SO4: amount of SO4 to add per layer (kgS/ha), not handled here
            //.Ashalk: ash amount to add per layer (mol/ha), not handled here
            //.FOM_C: amount of carbon in fom (all pools) to add per layer (kgC/ha)  - if present, the entry for pools will be ignored
            //.FOM_C_pool1: amount of carbon in fom_pool1 to add per layer (kgC/ha)
            //.FOM_C_pool2: amount of carbon in fom_pool2 to add per layer (kgC/ha)
            //.FOM_C_pool3: amount of carbon in fom_pool3 to add per layer (kgC/ha)
            //.FOM_N.: amount of nitrogen in fom to add per layer (kgN/ha)


            //// check that required data is supplied
            //bool isDataOK = true;

            //if (PatchtoAdd.DepositionType.ToLower() == "ToNewPatch".ToLower())
            //{
            //    if (PatchtoAdd.AffectedPatches_id.Length == 0 && PatchtoAdd.AffectedPatches_nm.Length == 0)
            //    {
            //        writeMessage(" Command to add patch did not supply a valid patch to be used as base for the new one. Command will be ignored.");
            //        isDataOK = false;
            //    }
            //    else if (PatchtoAdd.AreaFraction <= 0.0)
            //    {
            //        writeMessage(" Command to add patch did not supply a valid area fraction for the new patch. Command will be ignored.");
            //        isDataOK = false;
            //    }
            //}
            //else if (PatchtoAdd.DepositionType.ToLower() == "ToSpecificPatch".ToLower())
            //{
            //    if (PatchtoAdd.AffectedPatches_id.Length == 0 && PatchtoAdd.AffectedPatches_nm.Length == 0)
            //    {
            //        writeMessage(" Command to add patch did not supply a valid patch to be used as base for the new one. Command will be ignored.");
            //        isDataOK = false;
            //    }
            //}
            //else if (PatchtoAdd.DepositionType.ToLower() == "NewOverlappingPatches".ToLower())
            //{
            //    if (PatchtoAdd.AreaFraction <= 0.0)
            //    {
            //        writeMessage(" Command to add patch did not supply a valid area fraction for the new patch. Command will be ignored.");
            //        isDataOK = false;
            //    }
            //}
            //else if ((PatchtoAdd.DepositionType.ToLower() == "ToAllPaddock".ToLower()) || (PatchtoAdd.DepositionType == ""))
            //{
            //    // assume stuff is added homogeneously and with no patch creation, thus no factors are actually required
            //}
            //else
            //{
            //    writeMessage(" Command to add patch did not supply a valid DepositionType. Command will be ignored.");
            //    isDataOK = false;
            //}

            //if (isDataOK)
            //{
            //    List<int> PatchesToAddStuff;

            //    if ((PatchtoAdd.DepositionType.ToLower() == "ToNewPatch".ToLower()) ||
            //        (PatchtoAdd.DepositionType.ToLower() == "NewOverlappingPatches".ToLower()))
            //    { // New patch(es) will be added
            //        AddNewCNPatch(PatchtoAdd);
            //    }
            //    else if (PatchtoAdd.DepositionType.ToLower() == "ToSpecificPatch".ToLower())
            //    {  // add stuff to selected patches, no new patch will be created

            //        // 1. get the list of patch id's to which stuff will be added
            //        PatchesToAddStuff = CheckPatchIDs(PatchtoAdd.AffectedPatches_id, PatchtoAdd.AffectedPatches_nm);
            //        // 2. add the stuff to patches listed
            //        AddStuffToPatches(PatchesToAddStuff, PatchtoAdd);
            //    }
            //    else
            //    {  // add stuff to all existing patches, no new patch will be created
            //        // 1. create the list of patches receiving stuff (all)
            //        PatchesToAddStuff = new List<int>();
            //        for (int k = 0; k < Patch.Count; k++)
            //            PatchesToAddStuff.Add(k);
            //        // 2. add the stuff to patches listed
            //        AddStuffToPatches(PatchesToAddStuff, PatchtoAdd);
            //    }
            //}
        }

        /// <summary>
        /// Passes and handles the information about new patch and add it to patch list
        /// </summary>
        /// <param name="PatchtoAdd">Patch data</param>
        // RJM [EventHandler]
        [EventSubscribe("AddSoilCNPatch")]  // RJM TODO check
        public void OnAddSoilCNPatch(AddSoilCNPatchType PatchtoAdd)
        {
            // data passed with this event:
            //.Sender: the name of the module that raised this event
            //.SuppressMessages: flags wheter massages are suppressed or not (default is not)
            //.DepositionType: the type of deposition:
            //  - ToAllPaddock: No patch is created, add stuff as given to all patches. It is the default;
            //  - ToSpecificPatch: No patch is created, add stuff to given patches;
            //      (recipient patch is given using its index or name; if not supplied, defaults to homogeneous)
            //  - ToNewPatch: create new patch based on an existing patch, add stuff to created patch;
            //      - recipient or base patch is given using index or name; if not supplied, new patch will be based on the base/Patch[0];
            //      - patches are only created is area is larger than a minimum (minPatchArea);
            //      - new areas are proportional to existing patches;
            //  - NewOverlappingPatches: create new patch(es), these overlap with all existing patches, add stuff to created patches;
            //      (new patches are created only if their area is larger than a minimum (minPatchArea))
            //.AffectedPatches_id (AffectedPatchesByIndex): the index of the existing patches affected by new patch
            //.AffectedPatches_nm (AffectedPatchesByName): the name of the existing patches affected by new patch
            //.AreaFraction: the relative area (fraction) of new patches (0-1)
            //.PatchName: the name(s) of the patch(es) being created
            //.Water: amount of water to add per layer (mm), not handled here
            //.Urea: amount of urea to add per layer (kgN/ha)
            //.NH4: amount of ammonium to add per layer (kgN/ha)
            //.NO3: amount of nitrate to add per layer (kgN/ha)
            //.POX: amount of POx to add per layer (kgP/ha), not handled here
            //.SO4: amount of SO4 to add per layer (kgS/ha), not handled here
            //.Ashalk: ash amount to add per layer (mol/ha), not handled here
            //.FOM_C: amount of carbon in fom (all pools) to add per layer (kgC/ha)  - if present, the entry for pools will be ignored
            //.FOM_C_pool1: amount of carbon in fom_pool1 to add per layer (kgC/ha)
            //.FOM_C_pool2: amount of carbon in fom_pool2 to add per layer (kgC/ha)
            //.FOM_C_pool3: amount of carbon in fom_pool3 to add per layer (kgC/ha)
            //.FOM_N.: amount of nitrogen in fom to add per layer (kgN/ha)

            // - here we'll just convert to AddSoilCNPatchwithFOM and raise that event  - This will be deleted in the future

            AddSoilCNPatchwithFOMType PatchData = new AddSoilCNPatchwithFOMType();
            PatchData.Sender = PatchtoAdd.Sender;
            PatchData.SuppressMessages = PatchtoAdd.SuppressMessages;
            PatchData.DepositionType = PatchtoAdd.DepositionType;
            PatchData.AreaNewPatch = PatchtoAdd.AreaFraction;
            PatchData.AffectedPatches_id = PatchtoAdd.AffectedPatches_id;
            PatchData.AffectedPatches_nm = PatchtoAdd.AffectedPatches_nm;
            PatchData.Urea = PatchtoAdd.Urea;
            PatchData.NH4 = PatchtoAdd.NH4;
            PatchData.NO3 = PatchtoAdd.NO3;
            // need to also initialise FOM, even if it is empty
            PatchData.FOM = new AddSoilCNPatchwithFOMFOMType();
            PatchData.FOM.Type = "none";
            PatchData.FOM.Pool = new SoilOrganicMaterialType[3];
            for (int pool = 0; pool < 3; pool++)
                PatchData.FOM.Pool[pool] = new SoilOrganicMaterialType();

            OnAddSoilCNPatchwithFOM(PatchData);

        }

        /// <summary>
        /// Passes and handles the information about new patch and add it to patch list
        /// </summary>
        /// <param name="PatchtoAdd">Patch data</param>
        // RJM [EventHandler]
        [EventSubscribe("AddSoilCNPatchwithFOM")]  // RJM TODO check
        public void OnAddSoilCNPatchwithFOM(AddSoilCNPatchwithFOMType PatchtoAdd)
        {
            // data passed with this event:
            //.Sender: the name of the module that raised this event
            //.SuppressMessages: flags wheter massages are suppressed or not (default is not)
            //.DepositionType: the type of deposition:
            //  - ToAllPaddock: No patch is created, add stuff as given to all patches. It is the default;
            //  - ToSpecificPatch: No patch is created, add stuff to given patches;
            //      (recipient patch is given using its index or name; if not supplied, defaults to homogeneous)
            //  - ToNewPatch: create new patch based on an existing patch, add stuff to created patch;
            //      - recipient or base patch is given using index or name; if not supplied, new patch will be based on base/Patch[0];
            //      - patches are only created is area is larger than a minimum (minPatchArea);
            //      - new areas are proportional to existing patches;
            //  - NewOverlappingPatches: create new patch(es), these overlap with all existing patches, add stuff to created patches;
            //      (new patches are created only if their area is larger than a minimum (minPatchArea))
            //.AffectedPatches_id (AffectedPatchesByIndex): the index of the existing patches affected by new patch
            //.AffectedPatches_nm (AffectedPatchesByName): the name of the existing patches affected by new patch
            //.AreaNewPatch: the relative area (fraction) of new patches (0-1)
            //.PatchName: the name(s) of the patch(es) being created
            //.Water: amount of water to add per layer (mm), not handled here
            //.Urea: amount of urea to add per layer (kgN/ha)
            //.NH4: amount of ammonium to add per layer (kgN/ha)
            //.NO3: amount of nitrate to add per layer (kgN/ha)
            //.POX: amount of POx to add per layer (kgP/ha), not handled here
            //.SO4: amount of SO4 to add per layer (kgS/ha), not handled here
            //.AshAlk: ash amount to add per layer (mol/ha), not handled here
            //.FOM: fresh organic matter to add, per fom pool
            //   .name: name of given pool being altered
            //   .type: type of the given pool being altered (not used here)
            //   .Pool[]: info about FOM pools being added
            //      .type: type of the given pool being altered (not used here)
            //      .type: type of the given pool being altered (not used here)
            //      .C: amount of carbon in given pool to add per layer (kgC/ha)
            //      .N: amount of nitrogen in given pool to add per layer (kgN/ha)
            //      .P: amount of phosphorus (kgC/ha), not handled here
            //      .S: amount of sulphur (kgC/ha), not handled here
            //      .AshAlk: amount of alkaline ash (kg/ha), not handled here


            // check that required data is supplied
            bool isDataOK = true;

            if (PatchtoAdd.DepositionType.ToLower() == "ToNewPatch".ToLower())
            {
                if (PatchtoAdd.AffectedPatches_id.Length == 0 && PatchtoAdd.AffectedPatches_nm.Length == 0)
                {
                    writeMessage(" Command to add patch did not supply a valid patch to be used as base for the new one. Command will be ignored.");
                    isDataOK = false;
                }
                else if (PatchtoAdd.AreaNewPatch <= 0.0)
                {
                    writeMessage(" Command to add patch did not supply a valid area fraction for the new patch. Command will be ignored.");
                    isDataOK = false;
                }
            }
            else if (PatchtoAdd.DepositionType.ToLower() == "ToSpecificPatch".ToLower())
            {
                if (PatchtoAdd.AffectedPatches_id.Length == 0 && PatchtoAdd.AffectedPatches_nm.Length == 0)
                {
                    writeMessage(" Command to add patch did not supply a valid patch to be used as base for the new one. Command will be ignored.");
                    isDataOK = false;
                }
            }
            else if (PatchtoAdd.DepositionType.ToLower() == "NewOverlappingPatches".ToLower())
            {
                if (PatchtoAdd.AreaNewPatch <= 0.0)
                {
                    writeMessage(" Command to add patch did not supply a valid area fraction for the new patch. Command will be ignored.");
                    isDataOK = false;
                }
            }
            else if ((PatchtoAdd.DepositionType.ToLower() == "ToAllPaddock".ToLower()) || (PatchtoAdd.DepositionType == ""))
            {
                // assume stuff is added homogeneously and with no patch creation, thus no factors are actually required
            }
            else
            {
                writeMessage(" Command to add patch did not supply a valid DepositionType. Command will be ignored.");
                isDataOK = false;
            }

            if (isDataOK)
            {
                List<int> PatchesToAddStuff;

                if ((PatchtoAdd.DepositionType.ToLower() == "ToNewPatch".ToLower()) ||
                    (PatchtoAdd.DepositionType.ToLower() == "NewOverlappingPatches".ToLower()))
                { // New patch(es) will be added
                    AddNewCNPatch(PatchtoAdd);
                }
                else if (PatchtoAdd.DepositionType.ToLower() == "ToSpecificPatch".ToLower())
                {  // add stuff to selected patches, no new patch will be created

                    // 1. get the list of patch id's to which stuff will be added
                    PatchesToAddStuff = CheckPatchIDs(PatchtoAdd.AffectedPatches_id, PatchtoAdd.AffectedPatches_nm);
                    // 2. add the stuff to patches listed
                    AddStuffToPatches(PatchesToAddStuff, PatchtoAdd);
                }
                else
                {  // add stuff to all existing patches, no new patch will be created
                   // 1. create the list of patches receiving stuff (all)
                    PatchesToAddStuff = new List<int>();
                    for (int k = 0; k < Patch.Count; k++)
                        PatchesToAddStuff.Add(k);
                    // 2. add the stuff to patches listed
                    AddStuffToPatches(PatchesToAddStuff, PatchtoAdd);
                }
            }
        }

        /// <summary>
        /// Passes the list of patches that will be merged into one, as defined by user
        /// </summary>
        /// <param name="MergeCNPatch">The list of CNPatches to merge</param>
        // RJM [EventHandler]
        [EventSubscribe("MergeSoilCNPatch")]
        public void OnMergeSoilCNPatch(MergeSoilCNPatchType MergeCNPatch)
        {
            List<int> PatchesToMerge = new List<int>();
            if (MergeCNPatch.MergeAll)
            {
                // all patches will be merged
                for (int k = 0; k < Patch.Count; k++)
                    PatchesToMerge.Add(k);
            }
            else if ((MergeCNPatch.AffectedPatches_id.Length > 1) | (MergeCNPatch.AffectedPatches_nm.Length > 1))
            {
                // get the list of patch id's to which stuff will be added
                PatchesToMerge = CheckPatchIDs(MergeCNPatch.AffectedPatches_id, MergeCNPatch.AffectedPatches_nm);
            }

            // send the list to merger - all values are copied to first patch in the list, remaining will be deleted
            if (PatchesToMerge.Count > 0)
                AmalgamatePatches(PatchesToMerge, MergeCNPatch.SuppressMessages);
        }
        /// <summary>
        /// Comunicate other components that C amount in the soil has changed
        /// </summary>
        /// <param name="dltC">C changes</param>
        private void SendExternalMassFlowC(double dltC)
        {

            if (ExternalMassFlow != null)
            {
                ExternalMassFlowType massBalanceChange = new ExternalMassFlowType();
                if (Math.Abs(dltC) <= EPSILON)
                    dltC = 0.0;
                massBalanceChange.FlowType = dltC >= 0 ? "gain" : "loss";
                massBalanceChange.PoolClass = "soil";
                massBalanceChange.N = (float)Math.Abs(dltC);
                ExternalMassFlow.Invoke(massBalanceChange);
            }

        }

        /// <summary>
        /// Comunicate other components that N amount in the soil has changed
        /// </summary>
        /// <param name="dltN">N changes</param>
        private void SendExternalMassFlowN(double dltN)
        {
            ExternalMassFlowType massBalanceChange = new ExternalMassFlowType();
            if (Math.Abs(dltN) < epsilon)
                dltN = 0.0;
            massBalanceChange.FlowType = dltN >= epsilon ? "gain" : "loss";
            massBalanceChange.PoolClass = "soil";
            massBalanceChange.N = (float)Math.Abs(dltN);
            if (ExternalMassFlow != null)
            {
                ExternalMassFlow.Invoke(massBalanceChange);
            }
        }

        #endregion sporadic processes

        #endregion processes events

        #region >>  Auxiliary functions



        /// <summary>
        /// Print a message to the summaryfile
        /// </summary>
        /// <param name="myMessage">The message to be printed below the date and module info</param>
        private void writeMessage(string myMessage)
        {
            Console.WriteLine(Clock.Today.ToString("dd MMMM yyyy") + " (Day of year=" + Clock.Today.DayOfYear.ToString() + "), SoilNitrogen:");
            Console.WriteLine("  " + myMessage);
        }

        /// <summary>
        /// Checks whether the variable is significantly negative, considering thresholds
        /// </summary>
        /// <remarks>
        /// Three levels are considered when analying a negative value, these are defined by the warning and the fatal threshold value:
        ///  (1) If the variable is negative, but the value is really small (in absolute terms) than the deviation is considered irrelevant;
        ///  (2) If the value of the variable is negative and greater than the warning threshold, then a warning message is given;
        ///  (3) If the variable value is negative and greater than the fatal threshold, then a fatal error is raised and the calculation stops.
        /// In any case the value any negative value is reset to zero;
        /// </remarks>
        /// <param name="TheValue">Reference to the variable being tested</param>
        /// <param name="layer">The layer to which the variable belongs to</param>
        /// <param name="VariableName">The name of the variable</param>
        /// <param name="MethodName">The name of the method calling the test</param>
        private void CheckNegativeValues(ref double TheValue, int layer, string VariableName, string MethodName)
        {
            // Note: the layer number and the variable name are passed only so that they can be added to the error message

            if (TheValue < FatalNegativeThreshold)
            {
                // Deviation is too large, stop the calculations
                string myMessage = " - " + MethodName + ", attempt to change " + VariableName + "["
                                 + (layer + 1).ToString() + "] to a value(" + TheValue.ToString()
                                 + ") below the fatal threshold (" + FatalNegativeThreshold.ToString() + ")\n";
                TheValue = 0.0;
                throw new Exception(myMessage);
            }
            else if (TheValue < WarningNegativeThreshold)
            {
                // Deviation is small, but warrants a notice to user
                string myMessage = " - " + MethodName + ", attempt to change " + VariableName + "["
                                 + (layer + 1).ToString() + "] to a value(" + TheValue.ToString()
                                 + ") below the warning threshold (" + WarningNegativeThreshold.ToString()
                                 + ". Value will be reset to zero.";
                TheValue = 0.0;
                writeMessage(myMessage);
            }
            else if (TheValue < 0.0)
            {
                // Realy small value, likely a minor numeric issue, don't bother to report
                TheValue = 0.0;
            }
            //else { } // Value is positive
        }

        /// <summary>
        /// Computes the fraction of each layer that is between the surface and a given depth
        /// </summary>
        /// <param name="maxDepth">The depth down to which the fractions are computed</param>
        /// <returns>An array with the fraction (0-1) of each layer that is between the surface and maxDepth</returns>
        private double[] FractionLayer(double maxDepth)
        {
            double cumDepth = 0.0;
            double[] result = new double[dlayer.Length];
            int maxLayer = getCumulativeIndex(maxDepth, dlayer);
            for (int layer = 0; layer <= maxLayer; layer++)
            {
                result[layer] = Math.Min(1.0, MathUtilities.Divide(maxDepth - cumDepth, dlayer[layer], 0.0));
            }
            return result;
        }

        /// <summary>
        /// Find the index at which the cumulative amount is equal or greater than a given value
        /// </summary>
        /// <param name="sumTarget">The target value being sought</param>
        /// <param name="anArray">The array to analyse</param>
        /// <returns>The index of the array item at which the sum is equal or greater than the target</returns>
        private int getCumulativeIndex(double sumTarget, double[] anArray)
        {
            double cum = 0.0f;
            for (int i = 0; i < anArray.Length; i++)
            {
                cum += anArray[i];
                if (cum >= sumTarget)
                    return i;
            }
            return anArray.Length - 1;
        }

        /// <summary>
        /// Check whether there is at least one considerable/significant value in the array
        /// </summary>
        /// <param name="anArray">The array to analyse</param>
        /// <param name="MinValue">The minimum considerable value</param>
        /// <returns>True if there is any value greater than the minimum, false otherwise</returns>
        private bool hasSignificantValues(double[] anArray, double MinValue)
        {
            bool result = false;
            if (anArray != null)
            {
                for (int i = 0; i < anArray.Length; i++)
                {
                    if (Math.Abs(anArray[i]) >= MinValue)
                    {
                        result = true;
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Calculate the sum of all values of an array of doubles
        /// </summary>
        /// <param name="anArray">The array of values</param>
        /// <returns>The sum</returns>
        private double SumDoubleArray(double[] anArray)
        {
            double result = 0.0;
            if (anArray != null)
            {
                for (int i = 0; i < anArray.Length; i++)
                    result += anArray[i];
            }
            return result;
        }

        /// <summary>Check whether there is any considerable values in the array</summary>
        /// <param name="anArray">The array to analyse</param>
        /// <param name="Lowerue">The minimum considerable value</param>
        /// <returns>True if there is any value greater than the minimum, false otherwise</returns>
        private bool hasValues(double[] anArray, double Lowerue)
        {
            bool result = false;
            if (anArray != null)
            {
                foreach (double Value in anArray)
                {
                    if (Math.Abs(Value) > Lowerue)
                    {
                        result = true;
                        break;
                    }
                }
            }
            return result;
        }

        #endregion Aux functions
    }


    /* // RJM
    public class SoilTypeDefinition
    {
        [Param]
        protected XmlNode SoilTypeDefinitionXML;
    }
    */

    }