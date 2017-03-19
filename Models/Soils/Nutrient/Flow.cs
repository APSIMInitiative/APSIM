﻿
namespace Models.Soils.Nutrient
{
    using Core;
    using Models.PMF.Functions;
    using System;

    /// <summary>
    /// Encapsulates a flow between pools.
    /// </summary>
    [Serializable]
    [ValidParent(ParentType = typeof(Nutrient))]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ViewName("UserInterface.Views.GridView")]
    public class Flow : Model
    {
        private NutrientPool source = null;
        private NutrientPool destination = null;

        [Link]
        private IFunction rate = null;

        [Link]
        private IFunction CO2Efficiency = null;

        /// <summary>
        /// Name of source pool
        /// </summary>
        [Description("Name of source pool")]
        public string sourceName { get; set; }

        /// <summary>
        /// Name of destination pool
        /// </summary>
        [Description("Name of destination pool")]
        public string destinationName { get; set; }

        /// <summary>Performs the initial checks and setup</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            source = Apsim.Find(this, sourceName) as NutrientPool;
            if (source == null)
                throw new Exception("Cannot find source pool with name: " + sourceName);

            destination = Apsim.Find(this, destinationName) as NutrientPool;
            if (destination == null)
                throw new Exception("Cannot find destination pool with name: " + destinationName);
        }

        /// <summary>
        /// Get the information on potential residue decomposition - perform daily calculations as part of this.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoSoilOrganicMatter")]
        private void OnDoSoilOrganicMatter(object sender, EventArgs e)
        {
            for (int i= 0; i < source.C.Length; i++)
            {
                double carbonFlow = rate.Value(i) * source.C[i];
                double nitrogenFlow = carbonFlow * source.CNRatio;
                source.C[i] -= carbonFlow;
                destination.C[i] += carbonFlow*CO2Efficiency.Value(i);
                source.N[i] -= nitrogenFlow;
                destination.N[i] += nitrogenFlow;
            }
        }


    }
}
