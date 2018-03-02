﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Models.Core;
using Models.PMF.Phen;
using System.Diagnostics;

namespace Models.PMF.Functions
{
    /// <summary>
    /// A function that accumulates values from child functions
    /// </summary>
    [Serializable]
    [Description("Adds the value of all children functions to the previous day's accumulation between start and end phases")]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class AccumulateByNumericPhase : BaseFunction 
    {
        /// <summary>The accumulated value</summary>
        private double AccumulatedValue = 0;
        
        /// <summary>The child functions</summary>
        private List<IModel> ChildFunctions;

        /// <summary>The phenology</summary>
        [Link]
        Phenology Phenology = null;

        /// <summary>The start stage name in numeric values</summary>
        [Description("Numeric Stage to start accumulation")]
        public Double StartStageName { get; set; }

        /// <summary>The end stage name</summary>
        [Description("Numeric Stage to stop accumulation")]
        public double EndStageName { get; set; }

        /// <summary>The reset stage name</summary>
        [Description("(optional) Stage name (string) to reset accumulation")]
        public string ResetStageName { get; set; }

        /// <summary>The fraction removed on Cut event</summary>
        [Description("(optional) Fraction to remove on Cut")]
        public double FractionRemovedOnCut { get; set; }

        /// <summary>The fraction removed on Harvest event</summary>
        [Description("(optional) Fraction to remove on Harvest")]
        public double FractionRemovedOnHarvest { get; set; }

        /// <summary>The fraction removed on Graze event</summary>
        [Description("(optional) Fraction to remove on Graze")]
        public double FractionRemovedOnGraze { get; set; }

        /// <summary>The fraction removed on Prun event</summary>
        [Description("(optional) Fraction to remove on Prun")]
        public double FractionRemovedOnPrune { get; set; }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            AccumulatedValue = 0;
        }

        /// <summary>Called by Plant.cs when phenology routines are complete.</summary>
        /// <param name="sender">Plant.cs</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("PostPhenology")]
        private void PostPhenology(object sender, EventArgs e)
        {
            if (ChildFunctions == null)
                ChildFunctions = Apsim.Children(this, typeof(IFunction));

            if (Phenology.Stage >= StartStageName && Phenology.Stage <= EndStageName)
            {
                double DailyIncrement = 0.0;
                foreach (IFunction function in ChildFunctions)
                {
                    DailyIncrement += function.Value();
                }

                AccumulatedValue += DailyIncrement;
            }
        }

        /// <summary>Called when [phase changed].</summary>
        /// <param name="phaseChange">The phase change.</param>
        /// <param name="sender">Sender plant.</param>
        [EventSubscribe("PhaseChanged")]
        private void OnPhaseChanged(object sender, PhaseChangedType phaseChange)
        {
            if (phaseChange.EventStageName == ResetStageName)
                AccumulatedValue = 0.0;
        }

        /// <summary>Gets the value.</summary>
        public override double[] Values()
        {
            Trace.WriteLine("Name: " + Name + " Type: " + GetType().Name + " Value:" + AccumulatedValue);
            return new double[] { AccumulatedValue };
        }

        /// <summary>Called when [cut].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Cutting")]
        private void OnCut(object sender, EventArgs e)
        {
            AccumulatedValue -= FractionRemovedOnCut * AccumulatedValue;
        }

        /// <summary>Called when [cut].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Harvesting")]
        private void OnHarvest(object sender, EventArgs e)
        {
            AccumulatedValue -= FractionRemovedOnHarvest * AccumulatedValue;
        }
        /// <summary>Called when [cut].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Grazing")]
        private void OnGraze(object sender, EventArgs e)
        {
            AccumulatedValue -= FractionRemovedOnGraze * AccumulatedValue;
        }

        /// <summary>Called when [cut].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Pruning")]
        private void OnPrune(object sender, EventArgs e)
        {
            AccumulatedValue -= FractionRemovedOnPrune * AccumulatedValue;
        }

        /// <summary>Called when [EndCrop].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantEnding")]
        private void OnPlantEnding(object sender, EventArgs e)
        {
            AccumulatedValue = 0;
        }
    }
}
