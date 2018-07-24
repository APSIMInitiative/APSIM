﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Models.Core;
using Models.PMF.Development;
using APSIM.Shared.Utilities;

namespace Models.Functions
{
    /// <summary>
    /// A function that accumulates values from child functions
    /// </summary>
    [Serializable]
    [Description("Maintains a moving average of a given value for a user-specified number of simulation days")]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class MovingAverageFunction : Model, IFunction, ICustomDocumentation
    {
        /// <summary>The number of days over which to calculate the moving average</summary>
        [Description("Number of Days")]
        public int NumberOfDays { get; set; }

        /// <summary>The stage to start calculating moving average</summary>
        [Description("The stage to start calculating moving average")]
        public string StageToStartMovingAverage { get; set; }


        /// <summary>The accumulated value</summary>
        private List<double> AccumulatedValues = new List<double>();

        /// <summary>Was the moving average array initialised today</summary>
        private bool InitialisedToday { get; set; }

        /// <summary>Was the moving average array initialised today</summary>
        private bool Calculate { get; set; }

        /// <summary>The child functions</summary>
        private IFunction ChildFunction
        {
            get
            {
                IFunction value;
                List<IModel> ChildFunctions = Apsim.Children(this, typeof(IFunction));
                if (ChildFunctions.Count == 1)
                    value = ChildFunctions[0] as IFunction;
                else
                    throw new ApsimXException(this, "Moving average function " + this.Name + " must only have one child node.");
                return value;
            }
        }


        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        private void OnSowing(object sender, EventArgs e)
        {
            AccumulatedValues.Clear();
            Calculate = false;
            
        }

        /// <summary>Called when [phase changed].</summary>
        /// <param name="phaseChange">The phase change.</param>
        /// <param name="sender">Sender plant.</param>
        [EventSubscribe("PhaseChanged")]
        private void OnPhaseChanged(object sender, PhaseChangedType phaseChange)
        {
            //Put the first data member into the list on the day that moving average is to start being calculated
            if (phaseChange.EventStageName == StageToStartMovingAverage)
            {
                AccumulatedValues.Add(ChildFunction.Value());
                InitialisedToday = true;
                Calculate = true;
            }
        }

        /// <summary>Called by Plant.cs at the end of the day.</summary>
        /// <param name="sender">Plant.cs</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("EndOfDay")]
        private void EndOfDay(object sender, EventArgs e)
        {
            if (Calculate)
            {
                if (InitialisedToday == false)
                    AccumulatedValues.Add(ChildFunction.Value());
                if (AccumulatedValues.Count > NumberOfDays)
                    AccumulatedValues.RemoveAt(0);
                InitialisedToday = false;
            }
        }

        /// <summary>Gets the value.</summary>
        /// <value>The value.</value>
        public double Value(int arrayIndex = -1)
        {
            if (NumberOfDays == 0)
                throw new ApsimXException(this, "Number of days for moving average cannot be zero in function " + this.Name);
            return MathUtilities.Average(AccumulatedValues);
        }

        /// <summary>Writes documentation for this function by adding to the list of documentation tags.</summary>
        /// <param name="tags">The list of tags to add to.</param>
        /// <param name="headingLevel">The level (e.g. H2) of the headings.</param>
        /// <param name="indent">The level of indentation 1, 2, 3 etc.</param>
        public void Document(List<AutoDocumentation.ITag> tags, int headingLevel, int indent)
        {
            if (IncludeInDocumentation)
            {
                // add a heading.
                Name = this.Name;
                tags.Add(new AutoDocumentation.Heading(Name, headingLevel));
                tags.Add(new AutoDocumentation.Paragraph(Name + " is calculated from a moving average of " + (ChildFunction as IModel).Name + " over a series of " + NumberOfDays.ToString() + " days.", indent));

                // write children.
                foreach (IModel child in Apsim.Children(this, typeof(IModel)))
                    AutoDocumentation.DocumentModel(child, tags, headingLevel + 1, indent + 1);
            }
        }
    }
}
