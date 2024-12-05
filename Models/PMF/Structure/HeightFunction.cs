using System;
using System.Collections.Generic;
using Models.Core;
using Models.Functions;
using Newtonsoft.Json;
using Models.Interfaces;

namespace Models.PMF.Struct
{
    /// <summary>
    /// Height is used by the MicroClimate model to calculate the aerodynamic resistance used for calculation of potential transpiration.
    /// Calculates the potential height increment and then multiplies it by the smallest of any childern functions (Child functions represent stress).
    /// </summary>
    [Serializable]
    [ValidParent(ParentType = typeof(Structure))]
    [ValidParent(ParentType = typeof(ICanopy))]
    public class HeightFunction : Model, IFunction
    {
        /// <summary>The potential height</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction PotentialHeight = null;

        /// <summary>The potential height yesterday</summary>
        private double PotentialHeightYesterday = 0;

        /// <summary>The height</summary>
        private double Height = 0;

        /// <summary>The child functions</summary>
        private IEnumerable<IFunction> ChildFunctions;

        /// <summary>Gets or sets the height of the delta.</summary>
        [JsonIgnore]
        public double DeltaHeight { get; set; }

        /// <summary>Gets the value.</summary>
        public double Value(int arrayIndex = -1)
        {
            if (ChildFunctions == null)
                ChildFunctions = FindAllChildren<IFunction>();

            double PotentialHeightIncrement = PotentialHeight.Value(arrayIndex) - PotentialHeightYesterday;
            double StressValue = 1.0;
            //This function is counting potential height as a stress.
            foreach (IFunction F in ChildFunctions)
            {
                StressValue = Math.Min(StressValue, F.Value(arrayIndex));
            }
            DeltaHeight = PotentialHeightIncrement * StressValue;
            PotentialHeightYesterday = PotentialHeight.Value(arrayIndex);
            Height += DeltaHeight;
            return Height;
        }

        /// <summary>Clear all variables</summary>
        private void Clear()
        {
            PotentialHeightYesterday = 0;
            Height = 0;
            DeltaHeight = 0;
        }

        /// <summary>Called when crop is sowing</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        private void OnPlantSowing(object sender, SowingParameters data)
        {
            Clear();
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantEnding")]
        private void OnPlantEnding(object sender, EventArgs e)
        {
            Clear();
        }
    }
}
