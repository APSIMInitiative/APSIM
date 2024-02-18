﻿using APSIM.Shared.Utilities;
using Models.Climate;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF.Interfaces;
using Models.PMF.Organs;
using Models.PMF.Phen;
using Models.Utilities;
using Newtonsoft.Json;
using PdfSharpCore.Pdf.Filters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Models.PMF.Struct
{
    /// <summary>
    /// This is a tillering method to control the number of tillers and leaf area
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(LeafCulms))]
    public class DynamicTillering : Model, ITilleringMethod
    {
        /// <summary>
        /// Link to clock (used for FTN calculations at time of sowing).
        /// </summary>
        [Link]
        private IClock clock = null;

        /// <summary>The parent Plant</summary>
        [Link]
        Plant plant = null;

        /// <summary> Culms on the leaf </summary>
        [Link]
        public LeafCulms culms = null;

        /// <summary>The parent tilering class</summary>
        [Link]
        Phenology phenology = null;

        /// <summary>The parent tilering class</summary>
        [Link]
        SorghumLeaf leaf = null;

        /// <summary>The met data</summary>
        [Link]
        private IWeather metData = null;

        /// <summary>
		/// Link to weather.
		/// </summary>
		[Link]
        private IWeather weather = null;

        /// <summary> Culms on the leaf </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction areaCalc = null;

        /// <summary> Propoensity to Tiller Intercept </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction tillerSdIntercept = null;

        /// <summary> Propsenity to Tiller Slope </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction tillerSdSlope = null;

        /// <summary> LAI Value where tillers are no longer added </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction maxLAIForTillerAddition = null;

        /// <summary> LAI Value where tillers are no longer added </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction MaxDailyTillerReduction = null;

        /// <summary> LAI Value where tillers are no longer added </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction tillerSlaBound = null;

        /// <summary> Culms on the leaf </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction slaMax = null;

        /// <summary>Number of potential Fertile Tillers at harvest</summary>
        [JsonIgnore]
        public double CalculatedTillerNumber { get; set; }

        /// <summary>Tillering calculation type</summary>
        [JsonIgnore]
        public int TilleringMethod { get; set; }

        /// <summary>Current Number of Tillers</summary>
        [JsonIgnore]
        public double CurrentTillerNumber { get; set; }

        /// <summary>Current Number of Tillers</summary>
        [JsonIgnore]
        public double DltTillerNumber { get; set; }

        /// <summary>Actual Number of Fertile Tillers</summary>
        [JsonIgnore]
        public double FertileTillerNumber
        {
            get => CurrentTillerNumber;
            set { }
        }

        /// <summary>Supply Demand Ratio used to calculate Tiller No</summary>
        [JsonIgnore]
        public double SupplyDemandRatio { get; private set; }

        private int flagStage;
        private int floweringStage;
        private int endJuvenilePhase;
        private int startOfGrainFillPhase;
        private const int startThermalQuotientLeafNo = 3;
        private const int endThermalQuotientLeafNo = 5;
        private double plantsPerMetre;
        private double population;
        private double linearLAI;
        private double radiationValues = 0.0;
        private double temperatureValues = 0.0;
        private readonly List<int> tillerOrder = new();
        private bool initialTillersAdded = false;

        private double GetDeltaDmGreen() { return leaf.potentialDMAllocation.Structural; }

        private bool BeforeFlagLeafStage()
        {
            return BeforePhase("FlagLeaf", ref flagStage);
        }

        private bool BeforeFloweringStage()
        {
            return BeforePhase("Flowering", ref floweringStage);
        }

        private bool BeforeEndJuvenileStage()
        {
            return BeforePhase("EndJuvenile", ref endJuvenilePhase);
        }

        private bool AfterEndJuvenileStage()
        {
            return AfterPhase("EndJuvenile", ref endJuvenilePhase);
        }

        private bool BeforeStartOfGrainFillStage()
        {
            return BeforePhase("StartGrainFill", ref startOfGrainFillPhase);
        }

        private bool BeforePhase(string phaseName, ref int phaseIndex)
        {
            if (phaseIndex < 1) phaseIndex = phenology.StartStagePhaseIndex(phaseName);
            return phenology.BeforePhase(phaseIndex);
        }

        private bool AfterPhase(string phaseName, ref int phaseIndex)
        {
            if (phaseIndex < 1) phaseIndex = phenology.StartStagePhaseIndex(phaseName);
            return phenology.BeyondPhase(phaseIndex);
        }

        /// <summary> Calculate number of leaves</summary>
        public double CalcLeafNumber()
        {
            if (culms.Culms?.Count == 0) return 0.0;
            if (!plant.IsEmerged) return 0.0;

            if (BeforeEndJuvenileStage())
            {
                // ThermalTime Targets to EndJuv are not known until the end of the Juvenile Phase
                // FinalLeafNo is not known until the TT Target is known - meaning the potential leaf sizes aren't known
                culms.Culms.ForEach(c => c.UpdatePotentialLeafSizes(areaCalc as ICulmLeafArea));
            }

            var mainCulm = culms.Culms[0];
            var existingLeafNo = (int)Math.Floor(mainCulm.CurrentLeafNo);
            var nLeaves = mainCulm.CurrentLeafNo;
            var dltLeafNoMainCulm = 0.0;
            culms.dltLeafNo = dltLeafNoMainCulm;

            if (BeforeStartOfGrainFillStage())
            {
                // Calculate the leaf apperance on the main culm.
                dltLeafNoMainCulm = CalcLeafAppearance(mainCulm);

                // Now calculate the leaf apperance on all of the other culms.
                for (int i = 1; i < culms.Culms.Count; i++)
                {
                    CalcLeafAppearance(culms.Culms[i]);
                }
            }

            // Calculate Potential Tiller Number.

            var newLeafNo = (int)Math.Floor(mainCulm.CurrentLeafNo);

            // Up to L5 FE store PTQ. At L5 FE calculate tiller number (endThermalQuotientLeafNo).
            // At L5 FE newLeaf = 6 and currentLeaf = 5
            if (newLeafNo >= startThermalQuotientLeafNo && existingLeafNo < endThermalQuotientLeafNo)
            {
                radiationValues += metData.Radn;
                temperatureValues += phenology.thermalTime.Value();
            }
            if (newLeafNo == endThermalQuotientLeafNo && !initialTillersAdded)   // L5 Fully Expanded
            {
                if (TilleringMethod == 1)        // Dynamic calculation
                {
                    double PTQ = radiationValues / temperatureValues;
                    CalcTillerNumber(PTQ);
                }

                AddInitialTillers();
                initialTillersAdded = true;
            }



            //CalcTillers(newLeafNo, existingLeafNo);
            if (newLeafNo >= 5)
            {
                CalcTillerAppearance(newLeafNo, existingLeafNo);
            }


            return dltLeafNoMainCulm;
        }

        private double CalcLeafAppearance(Culm culm)
        {
            var leavesRemaining = culms.FinalLeafNo - culm.CurrentLeafNo;
            var leafAppearanceRate = culms.getLeafAppearanceRate(leavesRemaining);
            // if leaves are still growing, the cumulative number of phyllochrons or fully expanded leaves is calculated from thermal time for the day.
            var dltLeafNo = MathUtilities.Bound(MathUtilities.Divide(phenology.thermalTime.Value(), leafAppearanceRate, 0), 0.0, leavesRemaining);

            culm.AddNewLeaf(dltLeafNo);

            return dltLeafNo;
        }

        private void CalcTillers(int newLeaf, int currentLeaf)
        {
            if (CalculatedTillerNumber > 0.0) return;

            // Up to L5 FE store PTQ. At L5 FE calculate tiller number (endThermalQuotientLeafNo).
            // At L5 FE newLeaf = 6 and currentLeaf = 5
            if (newLeaf >= startThermalQuotientLeafNo && currentLeaf < endThermalQuotientLeafNo)
            {
                radiationValues += metData.Radn;
                temperatureValues += phenology.thermalTime.Value();

                // L5 Fully Expanded
                if (newLeaf == endThermalQuotientLeafNo)
                {
                    double PTQ = radiationValues / temperatureValues;
                    CalcTillerNumber(PTQ);
                    AddInitialTillers();
                }
            }
        }

        private void CalcTillerNumber(double PTQ)
        {
            // The final tiller number (Ftn) is calculated after the full appearance of LeafNo 5 - when leaf 6 emerges.
            // Calc Supply = R/oCd * LA5 * Phy5
            var areaMethod = areaCalc as ICulmLeafArea;
            var mainCulm = culms.Culms[0];
            double L5Area = areaMethod.CalculateIndividualLeafArea(5, mainCulm);
            double L9Area = areaMethod.CalculateIndividualLeafArea(9, mainCulm);

            double Phy5 = culms.getLeafAppearanceRate(culms.FinalLeafNo - culms.Culms[0].CurrentLeafNo);

            // Calc Demand = LA9 - LA5
            var demand = L9Area - L5Area;
            var supply = PTQ * L5Area * Phy5;
            SupplyDemandRatio = MathUtilities.Divide(supply, demand, 0);

            CalculatedTillerNumber = Math.Max(
                tillerSdIntercept.Value() + tillerSdSlope.Value() * SupplyDemandRatio,
                0.0
            );
        }

        void AddInitialTillers()
        {
            tillerOrder.Clear();

            if (CalculatedTillerNumber <= 0) return;

            // Lafarge et al. (2002) reported a common hierarchy of tiller emergence of T3>T4>T2>T1>T5>T6 across diverse density treatments
            // 1 tiller  = T3 
            // 2 tillers = T3 + T4
            // 3 tillers = T2 + T3 + T4
            // 4 tillers = T1 + T2 + T3 + T4
            // 5 tillers = T1 + T2 + T3 + T4 + T5
            // 6 tillers = T1 + T2 + T3 + T4 + T5 + T6

            // At leaf 5 fully expanded only initialize T1 with 2 leaves if present.

            int nTillers = (int)Math.Ceiling(CalculatedTillerNumber);
            if (nTillers <= 0) return;

            if (nTillers < 3) tillerOrder.Add(3);
            if (nTillers == 2) tillerOrder.Add(4);
            if (nTillers == 3)
            {
                tillerOrder.Add(2);
                tillerOrder.Add(3);
                tillerOrder.Add(4);
            }
            if (nTillers > 3)
            {
                for (int i = 1; i <= nTillers; i++)
                {
                    tillerOrder.Add(i);
                }
            }

            if (nTillers > 3)
            {
                InitiateTiller(1, 1, 2);
                CurrentTillerNumber = 1;
            }
        }

        private void CalcTillerAppearance(int newLeaf, int currentLeaf)
        {
            // Each time a leaf becomes fully expanded starting at 5 see if a tiller should be initiated.
            // When a leaf is fully expanded a tiller can be initiated at the axil 3 leaves less
            // So at L5 FE (newLeaf = 6, currentLeaf = 5) a Tiller might be at axil 2. i.e. a T2 

            // Add any new tillers and then calc each tiller in turn. Add a tiller if:
            // 1. There are more tillers to add.
            // 2. linearLAI < maxLAIForTillerAddition
            // 3. A leaf has fully expanded.  (newLeaf >= 6, newLeaf > currentLeaf)
            // 4. there should be a tiller at that node. (Check tillerOrder)

            var tillersAdded = culms.Culms.Count - 1;
            if (TilleringMethod == 1)
            {
                double lLAI = CalcLinearLAI();
                if (lLAI > maxLAIForTillerAddition.Value()) return;
            }

            if (newLeaf >= 5 && newLeaf > currentLeaf && CalculatedTillerNumber > tillersAdded)
            {
                // Axil = currentLeaf - 3
                int newNodeNumber = newLeaf - 3;
                if (tillerOrder.Contains(newNodeNumber))
                {
                    var fractionToAdd = Math.Min(1.0, CalculatedTillerNumber - tillersAdded);

                    DltTillerNumber = fractionToAdd;
                    CurrentTillerNumber += fractionToAdd;

                    InitiateTiller(newNodeNumber, fractionToAdd, 1);
                }
            }
        }

        private double CalcLinearLAI()
        {
            var tpla = (leaf.LAI + leaf.SenescedLai) / population * 10000; // Leaf area of one plant.
            linearLAI = plantsPerMetre * tpla / 10000.0;
            return linearLAI;
        }

        /// <summary>
        /// Add a tiller.
        /// </summary>
        void InitiateTiller(int tillerNumber, double fractionToAdd, double initialLeaf)
        {
            double leafNoAtAppearance = 1.0;
            Culm newCulm = new(leafNoAtAppearance)
            {
                CulmNo = tillerNumber,
                CurrentLeafNo = initialLeaf,
                VertAdjValue = culms.MaxVerticalTillerAdjustment.Value() + (CurrentTillerNumber * culms.VerticalTillerAdjustment.Value()),
                Proportion = fractionToAdd,
                FinalLeafNo = culms.Culms[0].FinalLeafNo
            };
            newCulm.UpdatePotentialLeafSizes(areaCalc as ICulmLeafArea);
            culms.Culms.Add(newCulm);
        }

        /// <summary>Calculate the potential leaf area</summary>
        public double CalcPotentialLeafArea()
        {
            culms.Culms.ForEach(c => c.DltLAI = 0);
            if (BeforeFloweringStage())
            {
                return areaCalc.Value();
            }
            return 0.0;
        }

        /// <summary> calculate the actual leaf area</summary>
        public double CalcActualLeafArea(double dltStressedLAI)
        {
            var mainCulm = culms.Culms.FirstOrDefault();

            if (mainCulm != null &&
                AfterEndJuvenileStage() &&
                CalculatedTillerNumber > 0.0 &&
                mainCulm.CurrentLeafNo < mainCulm.PositionOfLargestLeaf &&
                TilleringMethod == 1
            )
            {
                CalculateTillerCessation(dltStressedLAI);
            }

            // recalculate todays LAI
            var dltStressedLAI2 = 0.0;
            foreach (var culm in culms.Culms)
                dltStressedLAI2 += culm.DltLAI;

            double laiSlaReductionFraction = CalcCarbonLimitation(dltStressedLAI2);
            double leaf = mainCulm.CurrentLeafNo;
            var dltLAI = Math.Max(dltStressedLAI2 * laiSlaReductionFraction, 0.0);

            // Apply to each culm
            if (laiSlaReductionFraction < 1.0)
            {
                ReduceAllTillersProportionately(laiSlaReductionFraction);
            }

            culms.Culms.ForEach(c => c.TotalLAI += c.DltStressedLAI);

            //var calculatedLeafArea = dltStressedLAI2 - laiSlaReductionFraction;
            //return calculatedLeafArea;
            return dltLAI;
        }

        //double CalcSLA()
        //{
        //    double dmGreen = leaf.Live.Wt;
        //    double dltDmGreen = GetDeltaDmGreen();

        //    if (dmGreen + dltDmGreen <= 0.0) return 0.0;

        //    // (cm^2/g)
        //    return (lai + dltLAI) / (dmGreen + dltDmGreen) * 10000;
        //}

        private void CalculateTillerCessation(double dltStressedLAI)
        {
            bool moreToAdd = (CurrentTillerNumber < CalculatedTillerNumber) && (linearLAI < maxLAIForTillerAddition.Value());
            var tillerLaiToReduce = CalcCeaseTillerSignal(dltStressedLAI);
            double nLeaves = culms.Culms[0].CurrentLeafNo;

            if (nLeaves < 8 || moreToAdd || tillerLaiToReduce < 0.00001) return;

            double maxTillerLoss = MaxDailyTillerReduction.Value();
            double accProportion = 0.0;
            double tillerLaiLeftToReduce = tillerLaiToReduce;

            for (var culmIndex = culms.Culms.Count - 1; culmIndex >= 1; culmIndex--)
            {
                if (accProportion < maxTillerLoss && tillerLaiLeftToReduce > 0)
                {
                    var culm = culms.Culms[culmIndex];

                    double tillerLAI = culm.TotalLAI;
                    double tillerProportion = culm.Proportion;

                    if (tillerProportion > 0.0 && tillerLAI > 0.0)
                    {
                        // Use the amount of LAI past the target as an indicator of how much of the tiller
                        // to remove which will affect tomorrow's growth - up to the maxTillerLoss
                        double propn = Math.Max(
                            0.0,
                            Math.Min(maxTillerLoss - accProportion, tillerLaiLeftToReduce / tillerLAI)
                        );

                        accProportion += propn;
                        tillerLaiLeftToReduce -= propn * tillerLAI;
                        double remainingProportion = Math.Max(0.0, culm.Proportion - propn);
                        // Can't increase the proportion
                        culm.Proportion = remainingProportion;

                        culm.DltLAI -= propn * tillerLAI;
                    }
                }

                if (!(tillerLaiLeftToReduce > 0) || accProportion >= maxTillerLoss) break;
            }
            CurrentTillerNumber = 0;
            culms.Culms.ForEach(c => CurrentTillerNumber += c.Proportion); CurrentTillerNumber -= 1;

        }

        private double CalcCeaseTillerSignal(double dltStressedLAI)
        {
            var mainCulm = culms.Culms.FirstOrDefault();

            // Calculate sla target that is below the actual SLA - so as the leaves gets thinner it signals to the tillers to cease growing further
            // max SLA (thinnest leaf) possible using Reeves (1960's Kansas) SLA = 429.72 - 18.158 * LeafNo
            double nLeaves = mainCulm.CurrentLeafNo;
            var maxSLA = 429.72 - 18.158 * (nLeaves);
            // sla bound vary 30 - 40%
            maxSLA *= ((100 - tillerSlaBound.Value()) / 100.0);
            maxSLA = Math.Min(400, maxSLA);
            maxSLA = Math.Max(150, maxSLA);

            double dmGreen = leaf.Live.Wt;
            double dltDmGreen = GetDeltaDmGreen();

            // Calc how much LAI we need to remove to get back to the SLA target line.
            // This is done by reducing the proportion of tiller area.
            var maxLaiTarget = maxSLA * (dmGreen + dltDmGreen) / 10000;
            return Math.Max(leaf.LAI + dltStressedLAI - maxLaiTarget, 0);
        }

        private double CalcCarbonLimitation(double dltStressedLAI)
        {
            double dltDmGreen = GetDeltaDmGreen();
            if (dltDmGreen <= 0.001) return 1.0;

            var mainCulm = culms.Culms[0];

            // Changing to Reeves + 10%
            double nLeaves = mainCulm.CurrentLeafNo;
            double maxSLA;
            maxSLA = 429.72 - 18.158 * (nLeaves);
            maxSLA = Math.Min(400, maxSLA);
            maxSLA = Math.Max(150, maxSLA);
            var dltLaiPossible = dltDmGreen * maxSLA / 10000.0;

            double fraction = Math.Min(dltStressedLAI > 0 ? (dltLaiPossible / dltStressedLAI) : 1.0, 1.0);
            return fraction;
        }

        // THIS IS THE OLD ONE.
        private double CalcLeafReductionForCarbonLimitation(double dltStressedLAI)
        {
            double dltDmGreen = leaf.potentialDMAllocation.Structural;
            if (dltDmGreen <= 0.0) return dltStressedLAI;
            //new growth should exceed the SLA limits - cannot grow too quickly so that new leaf is too thin
            return Math.Max(dltStressedLAI - (dltDmGreen * slaMax.Value().ConvertSqM2SqMM()), 0.0);
        }

        /// <summary>
        /// Calculate SLA for leafa rea including potential new growth - stressess effect
        /// </summary>
        /// <param name="stressedLAI"></param>
        /// <returns></returns>
        public double CalcCurrentSLA(double stressedLAI)
        {
            double dmGreen = leaf.Live.Wt;
            double dltDmGreen = GetDeltaDmGreen();

            if (dmGreen + dltDmGreen <= 0.0) return 0.0;

            return (leaf.LAI + stressedLAI) / (dmGreen + dltDmGreen) * 10000; // (cm^2/g)
        }

        //private double CalcCeaseTillerSignal(double dltStressedLAI)
        //{
        //    // calculate sla target that is below the actual SLA - so as the leaves gets thinner it signals to the tillers to cease growing further
        //    // max SLA (thinnest leaf) possible using Reeves (1960's Kansas) SLA = 429.72 - 18.158 * LeafNo
        //    var mainCulm = culms.Culms.First();
        //    double maxSLA = 429.72 - 18.158 * (mainCulm.CurrentLeafNo + mainCulm.dltLeafNo);
        //    maxSLA *= ((100 - tillerSlaBound.Value()) / 100.0);     // sla bound vary 30 - 40%
        //    maxSLA = Math.Min(400, maxSLA);
        //    maxSLA = Math.Max(150, maxSLA);

        //    //calc how much LAI we need to remove to get back to the SLA target line
        //    //this value will be limited by the proportion of tiller area in maxTillerLoss 
        //    //dltStressedLai can be greater than the actual SLA limitation would allow
        //    //provides a stronger signal
        //    double dmGreen = leaf.Live.Wt;
        //    double dltDmGreen = GetDeltaDmGreen();

        //    var maxLaiTarget = maxSLA * (dmGreen + dltDmGreen) / 10000;
        //    return Math.Max(leaf.LAI + dltStressedLAI - maxLaiTarget, 0);
        //}

        void ReduceAllTillersProportionately(double laiReduction)
        {
            if (laiReduction <= 0.0) return;

            double totalDltLeaf = culms.Culms.Sum(c => c.DltStressedLAI);
            if (totalDltLeaf <= 0.0) return;

            //reduce new leaf growth proportionally across all culms
            //not reducing the number of tillers at this stage
            culms.Culms.ForEach(
                c =>
                c.DltStressedLAI -= Math.Max(
                    c.DltStressedLAI / totalDltLeaf * laiReduction,
                    0.0
                )
            );
        }

        /// <summary> Reset Culms at start of the simulation </summary>
        [EventSubscribe("StartOfSimulation")]
        private void StartOfSim(object sender, EventArgs e)
        {
            CurrentTillerNumber = 0.0;
            CalculatedTillerNumber = 0.0;
            DltTillerNumber = 0.0;
            SupplyDemandRatio = 0.0;
        }

        /// <summary>Called when crop is sowed</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        protected void OnPlantSowing(object sender, SowingParameters data)
        {
            if (data.Plant == plant)
            {
                population = data.Population;
                plantsPerMetre = data.Population * data.RowSpacing / 1000.0 * data.SkipDensityScale;
                CurrentTillerNumber = 0.0;
                CalculatedTillerNumber = 0.0;

                radiationValues = 0.0;
                temperatureValues = 0.0;

                TilleringMethod = data.TilleringMethod;

                if (data.TilleringMethod == -1)     // Rule Of Thumb
                    CalculatedTillerNumber = CalculateFtn();
                else if (data.TilleringMethod == 0) // Fixed tiller number
                    CalculatedTillerNumber = data.FTN;
                //if (data.TilleringMethod != 1)      // Not dynamically calculated 
                //  CalculatedTillerNumber = FertileTillerNumber;
            }
        }
        private double CalculateFtn()
        {
            // Estimate tillering given latitude, density, time of planting and
            // row configuration. this will be replaced with dynamic
            // calculations in the near future. Above latitude -25 is CQ, -25
            // to -29 is SQ, below is NNSW.
            double intercept, slope;

            if (weather.Latitude > -12.5 || weather.Latitude < -38.0)
                // Unknown region.
                throw new Exception("Unable to estimate number of tillers at latitude {weather.Latitude}");

            if (weather.Latitude > -25.0)
            {
                // Central Queensland.
                if (clock.Today.DayOfYear < 319 && clock.Today.DayOfYear > 182)
                {
                    // Between 1 July and 15 November.
                    if (plant.SowingData.SkipRow > 1.9)
                    {
                        // Double (2.0).
                        intercept = 0.5786; slope = -0.0521;
                    }
                    else if (plant.SowingData.SkipRow > 1.4)
                    {
                        // Single (1.5).
                        intercept = 0.8786; slope = -0.0696;
                    }
                    else
                    {
                        // Solid (1.0).
                        intercept = 1.1786; slope = -0.0871;
                    }
                }
                else
                {
                    // After 15 November.
                    if (plant.SowingData.SkipRow > 1.9)
                    {
                        // Double (2.0).
                        intercept = 0.4786; slope = -0.0421;
                    }
                    else if (plant.SowingData.SkipRow > 1.4)
                    {
                        // Single (1.5)
                        intercept = 0.6393; slope = -0.0486;
                    }
                    else
                    {
                        // Solid (1.0).
                        intercept = 0.8000; slope = -0.0550;
                    }
                }
            }
            else if (weather.Latitude > -29.0)
            {
                // South Queensland.
                if (clock.Today.DayOfYear < 319 && clock.Today.DayOfYear > 182)
                {
                    // Between 1 July and 15 November.
                    if (plant.SowingData.SkipRow > 1.9)
                    {
                        // Double  (2.0).
                        intercept = 1.1571; slope = -0.1043;
                    }
                    else if (plant.SowingData.SkipRow > 1.4)
                    {
                        // Single (1.5).
                        intercept = 1.7571; slope = -0.1393;
                    }
                    else
                    {
                        // Solid (1.0).
                        intercept = 2.3571; slope = -0.1743;
                    }
                }
                else
                {
                    // After 15 November.
                    if (plant.SowingData.SkipRow > 1.9)
                    {
                        // Double (2.0).
                        intercept = 0.6786; slope = -0.0621;
                    }
                    else if (plant.SowingData.SkipRow > 1.4)
                    {
                        // Single (1.5).
                        intercept = 1.1679; slope = -0.0957;
                    }
                    else
                    {
                        // Solid (1.0).
                        intercept = 1.6571; slope = -0.1293;
                    }
                }
            }
            else
            {
                // Northern NSW.
                if (clock.Today.DayOfYear < 319 && clock.Today.DayOfYear > 182)
                {
                    //  Between 1 July and 15 November.
                    if (plant.SowingData.SkipRow > 1.9)
                    {
                        // Double (2.0).
                        intercept = 1.3571; slope = -0.1243;
                    }
                    else if (plant.SowingData.SkipRow > 1.4)
                    {
                        // Single (1.5).
                        intercept = 2.2357; slope = -0.1814;
                    }
                    else
                    {
                        // Solid (1.0).
                        intercept = 3.1143; slope = -0.2386;
                    }
                }
                else if (clock.Today.DayOfYear > 349 || clock.Today.DayOfYear < 182)
                {
                    // Between 15 December and 1 July.
                    if (plant.SowingData.SkipRow > 1.9)
                    {
                        // Double (2.0).
                        intercept = 0.4000; slope = -0.0400;
                    }
                    else if (plant.SowingData.SkipRow > 1.4)
                    {
                        // Single (1.5).
                        intercept = 1.0571; slope = -0.0943;
                    }
                    else
                    {
                        // Solid (1.0).
                        intercept = 1.7143; slope = -0.1486;
                    }
                }
                else
                {
                    // Between 15 November and 15 December.
                    if (plant.SowingData.SkipRow > 1.9)
                    {
                        // Double (2.0).
                        intercept = 0.8786; slope = -0.0821;
                    }
                    else if (plant.SowingData.SkipRow > 1.4)
                    {
                        // Single (1.5).
                        intercept = 1.6464; slope = -0.1379;
                    }
                    else
                    {
                        // Solid (1.0).
                        intercept = 2.4143; slope = -0.1936;
                    }
                }
            }

            return Math.Max(slope * plant.SowingData.Population + intercept, 0);
        }

    }
}
