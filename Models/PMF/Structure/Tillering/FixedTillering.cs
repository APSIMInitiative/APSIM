﻿using APSIM.Shared.Utilities;
using DocumentFormat.OpenXml.Spreadsheet;
using Models.Core;
using Models.Interfaces;
using Models.PMF.Interfaces;
using Models.PMF.Phen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models.PMF.Struct
{
    /// <summary>
    /// This is a tillering method to control the number of tillers and leaf area
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Tillering))]
    public class FixedTillering : Model, ITilleringMethod
    {
		/// <summary>The parent plant</summary>
		[Link]
		private Plant parentPlant = null;

		/// <summary>The parent tilering class</summary>
		[Link]
		public Tillering tillering = null;
		/// <summary>The parent tilering class</summary>
		[Link]
		public Phenology phenology = null;
		/// <summary>The parent tilering class</summary>
		[Link]
		public Plant plant = null;

		/// <summary>Number of Fertile Tillers at Harvest</summary>
		public double FertileTillerNumber { get; private set; }

		private int _floweringStage;
		private int _endJuvenileStage;
		private double _tillersAdded;

		private bool beforeFlowering()
        {
			if (_floweringStage < 1) _floweringStage = phenology.StartStagePhaseIndex("Flowering");
			return phenology.Stage < _floweringStage;
		}
		private bool beforeEndJuvenileStage()
		{
			if (_endJuvenileStage < 1) _endJuvenileStage = phenology.StartStagePhaseIndex("EndJuvenile");
			return phenology.Stage < _endJuvenileStage;
		}

		/// <summary> Update number of leaves for all culms </summary>
		public void UpdateLeafNumber() 
        {
			if (tillering.Culms?.Count == 0) return;
			if (!plant.IsEmerged) return;

			var currentLeafNo = tillering.Culms[0].CurrentLeafNo;
			if (beforeFlowering())
            {
                if (beforeEndJuvenileStage())
                {
                    //ThermalTime Targets to EndJuv are not known until the end of the Juvenile Phase
                    //FinalLeafNo is not known until the TT Target is known - meaning the potential leaf sizes aren't known
                    tillering.Culms.ForEach(c => c.UpdatePotentialLeafSizes(tillering.AreaCalc));
                }
                calcLeafAppearance(tillering.Culms[0]);
            }
            //should there be any growth after flowering?
            calcTillerAppearance((int)Math.Floor(tillering.Culms[0].CurrentLeafNo), (int)Math.Floor(currentLeafNo));

			for(int i = 1; i < tillering.Culms.Count; i++)
            {
				calcLeafAppearance(tillering.Culms[i]);
			}
		}

        private void calcLeafAppearance(Culm culm)
        {
            var leavesRemainingOnMainStem = tillering.FinalLeafNo - culm.CurrentLeafNo;
            var leafAppearanceRate = tillering.LeafAppearanceRate.ValueForX(leavesRemainingOnMainStem);

            // if leaves are still growing, the cumulative number of phyllochrons or fully expanded leaves is calculated from thermal time for the day.
            var dltLeafNo = MathUtilities.Bound(MathUtilities.Divide(phenology.thermalTime.Value(), leafAppearanceRate, 0), 0.0, leavesRemainingOnMainStem);
            culm.CurrentLeafNo += dltLeafNo;
        }

        void calcTillerAppearance(int newLeafNo, int currentLeafNo)
		{
			if (newLeafNo <= currentLeafNo) return;
			if (newLeafNo < 3) return; //don't add before leaf 3

			//if there are still more tillers to add and the newleaf is greater than 3
			if (_tillersAdded >= FertileTillerNumber) return;


			{
				//tiller emergence is more closely aligned with tip apearance, but we don't track tip, so will use ligule appearance
				//could also use Thermal Time calcs if needed
				//Environmental & Genotypic Control of Tillering in Sorghum ppt - Hae Koo Kim
				//T2=L3, T3=L4, T4=L5, T5=L6

				//logic to add new tillers depends on which tiller, which is defined by FTN (fertileTillerNo)
				//this should be provided at sowing  //what if fertileTillers == 1?
				//2 tillers = T3 + T4
				//3 tillers = T2 + T3 + T4
				//4 tillers = T2 + T3 + T4 + T5
				//more than that is too many tillers - but will assume existing pattern for 3 and 4
				//5 tillers = T2 + T3 + T4 + T5 + T6

				//tiller 2 emergences with leaf 3, and then adds 1 each time
				//not sure what I'm supposed to do with tiller 1
				//if there are only 2 tillers, then t2 is not present - T3 & T4 are
				//if there is a fraction - between 2 and 3, 
				//this can be interpreted as a proportion of plants that have 2 and a proportion that have 3. 
				//to keep it simple, the fraction will be applied to the 2nd tiller
				double leafAppearance = tillering.Culms.Count + 2; //first culm added will equal 3
				double fraction = 1.0;

				if (FertileTillerNumber > 2 && FertileTillerNumber < 3 && leafAppearance < 4)
				{
					fraction = FertileTillerNumber % 1;
				}
				else
				{
					if (FertileTillerNumber - _tillersAdded < 1)
						fraction = FertileTillerNumber - _tillersAdded;
				}
				AddTiller(leafAppearance, currentLeafNo, fraction);
			}
		}
		/// <summary>
		/// Add a tiller.
		/// </summary>
		/// <param name="leafAtAppearance"></param>
		/// <param name="Leaves"></param>
		/// <param name="fractionToAdd"></param>
		private void AddTiller(double leafAtAppearance, double Leaves, double fractionToAdd)
		{
			double fraction = 1;
			if (FertileTillerNumber - _tillersAdded < 1)
				fraction = FertileTillerNumber - _tillersAdded;

			// get number of tillers 
			// add fractionToAdd 
			// if new tiller is neded add one
			// fraction goes to proportions
			double tillerFraction = tillering.Culms.Last().Proportion;
			//tillerFraction +=fractionToAdd;
			fraction = tillerFraction + fractionToAdd - Math.Floor(tillerFraction);
			//a new tiller is created with each new leaf, up the number of fertileTillers
			if (tillerFraction + fractionToAdd > 1)
			{
				Culm newCulm = new Culm(leafAtAppearance, new CulmParams { });

				//bell curve distribution is adjusted horizontally by moving the curve to the left.
				//This will cause the first leaf to have the same value as the nth leaf on the main culm.
				//T3&T4 were defined during dicussion at initial tillering meeting 27/06/12
				//all others are an assumption
				//T2 = 3 Leaves
				//T3 = 4 Leaves
				//T4 = 5 leaves
				//T5 = 6 leaves
				//T6 = 7 leaves
				newCulm.CulmNo = tillering.Culms.Count;
				newCulm.CurrentLeafNo = 0;//currentLeaf);
				newCulm.VertAdjValue = tillering.MaxVerticalTillerAdjustment.Value() + (_tillersAdded * tillering.VerticalTillerAdjustment.Value());
				newCulm.Proportion = fraction;
				newCulm.FinalLeafNo = tillering.FinalLeafNo;
				//newCulm.calcLeafAppearance();
				//newCulm.calculateLeafSizes();
				tillering.Culms.Add(newCulm);
			}
			else
			{
				tillering.Culms.Last().Proportion = fraction;
			}
			_tillersAdded += fractionToAdd;
		}
		/// <summary>
		/// 
		/// </summary>
		public void UpdatePotentialLeafSizes()
        {
			
			//tillering.AreaCalc.CalculateIndividualLeafArea(currentLeafNo, tillering.FinalLeafNo);

		}

		/// <summary> 
		/// Update potential number of tillers for all culms as well as the current number of active tillers.
		/// </summary>
		public void UpdateTillerNumber() { }

        /// <summary> Calculate the potential leaf area before inputs are updated</summary>
        public double CalculatePotentialLeafArea() { return 0.0; }

        /// <summary> Calculate the actual leaf area once inputs are known</summary>
        public double CalculateActualLeafArea() { return 0.0; }

		/// <summary>Called when crop is sowed</summary>
		/// <param name="sender">The sender.</param>
		/// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
		[EventSubscribe("PlantSowing")]
		protected void OnPlantSowing(object sender, SowingParameters data)
		{
			if (data.Plant == parentPlant)
			{
				FertileTillerNumber = data.BudNumber;
				_tillersAdded = 0.0;
			}
		}
	}
}
