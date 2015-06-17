using System;
using System.Collections.Generic;
using System.Text;
using Models.Core;

namespace Models.PMF.Functions.DemandFunctions
{
    /// <summary>
    /// Population based demand function
    /// </summary>
    [Serializable]
    [Description("Demand is calculated from the product of growth rate, thermal time and population.")]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class PopulationBasedDemandFunction : Model, IFunction
    {
        /// <summary>The thermal time</summary>
        [Link]
        IFunction ThermalTime = null;

        /// <summary>The number of growing organs</summary>
        [Link]
        IFunction OrganPopulation = null;

        /// <summary>The phenology</summary>
        [Link(IsOptional = true)]
        private Models.PMF.Phen.Phenology Phenology = null;

        /// <summary>The expansion stress</summary>
        [Link]
        IFunction ExpansionStress = null;

        /// <summary>The maximum organ wt</summary>
        [Description("Size individual organs will grow to when fully supplied with DM")]
        [Link]
        IFunction MaximumOrganWt = null;

        /// <summary>The start stage</summary>
        [Description("Stage when organ growth starts ")]
        [Link]
        IFunction StartStage = null;

        /// <summary>The growth duration</summary>
        [Description("ThermalTime duration of organ growth ")]
        [Link]
        IFunction GrowthDuration = null;

        /// <summary>The accumulated thermal time</summary>
        private double AccumulatedThermalTime = 0;
        /// <summary>The thermal time today</summary>
        private double ThermalTimeToday = 0;

        /// <summary>Called when DoDailyInitialisation invoked</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
        private void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            if ((Phenology.Stage >= StartStage.Value) && (AccumulatedThermalTime < GrowthDuration.Value))
            {
                ThermalTimeToday = Math.Min(ThermalTime.Value, GrowthDuration.Value - AccumulatedThermalTime);
                AccumulatedThermalTime += ThermalTimeToday;
            }
        }


        /// <summary>Gets the value.</summary>
        /// <value>The value.</value>
        public double Value
        {
            get
            {
                double Value = 0.0;
                if ((Phenology.Stage >= StartStage.Value) && (AccumulatedThermalTime < GrowthDuration.Value))
                {
                    double Rate = MaximumOrganWt.Value / GrowthDuration.Value;
                    Value = Rate * ThermalTimeToday * OrganPopulation.Value;
                }

                return Value * ExpansionStress.Value;
            }
        }

    }
}


