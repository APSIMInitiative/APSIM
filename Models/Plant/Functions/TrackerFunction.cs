﻿// ----------------------------------------------------------------------
// <copyright file="TrackerFunction.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
//-----------------------------------------------------------------------
namespace Models.PMF.Functions
{
    using Models.Core;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    /// <summary>
    /// # [Name]
    /// A function that accumulates values from child functions
    /// </summary>
    [Serializable]
    [Description("Keeps track of a variable")]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class TrackerFunction : BaseFunction
    {
        /// <summary>Values we have kept</summary>
        private List<double> variableValues = new List<double>();

        /// <summary>Reference values we have kept</summary>
        private List<double> referenceValues = new List<double>();

        /// <summary>Should we be keeping track of the variable?</summary>
        private bool inTrackingWindow = false;

        /// <summary>Link to an event service.</summary>
        [Link]
        private IEvent events = null;

        /// <summary>The variable to track</summary>
        [ChildLinkByName]
        private IFunction variable = null;

        /// <summary>The variable to track</summary>
        [ChildLinkByName]
        private IFunction referenceVariable = null;

        /// <summary>The statistic to return e.g. value back 300</summary>
        [Description("The statistic to return e.g. value back 300")]
        public string Statistic { get; set; }

        /// <summary>Event name to start accumulation</summary>
        [Description("Event name to start accumulation")]
        public string StartEventName { get; set; }

        /// <summary>Event name to stop accumulation</summary>
        [Description("Event name to stop accumulation")]
        public string EndEventName { get; set; }

        /// <summary>Gets the value.</summary>
        public override double[] Values()
        {
            if (referenceValues.Count == 0)
                return new double[] { 0 };
            if (Statistic.StartsWith("value back "))
            {
                double accumulationTarget = Convert.ToDouble(Statistic.Replace("value back ", ""));

                // Go backwards through referenceValues until we reach our accumulation target.
                double accumulationValue = 0;
                for (int i = referenceValues.Count-1; i >= 0; i--)
                {
                    accumulationValue += referenceValues[i];
                    if (accumulationValue >= accumulationTarget)
                    {
                        Trace.WriteLine("Name: " + Name + " Type: " + GetType().Name + " Value:" + variableValues[i]);
                        return new double[] { variableValues[i] };
                    }
                }
            }
            else
                throw new Exception("Invalid statistic found in TrackerFunction: " + Statistic);

            Trace.WriteLine("Name: " + Name + " Type: " + GetType().Name + " Value:0");
            return new double[] { 0 };
        }

        /// <summary>Invoked when simulation commences</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            events.Subscribe(StartEventName, OnStartEvent);
            events.Subscribe(EndEventName, OnEndEvent);
        }

        /// <summary>
        /// Invoked when simulation has completed.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("Completed")]
        private void OnSimulationCompleted(object sender, EventArgs e)
        {
            events.Unsubscribe(StartEventName, OnStartEvent);
            events.Unsubscribe(StartEventName, OnEndEvent);
        }

        /// <summary>
        /// Invoked when simulation has completed.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("DoManagementCalculations")]
        private void OnDoDailyTracking(object sender, EventArgs e)
        {
            if (inTrackingWindow)
            {
                variableValues.Add(variable.Value());
                referenceValues.Add(referenceVariable.Value());
            }
        }
        
        /// <summary>
        /// Called to begin keeping track of variable
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void OnStartEvent(object sender, EventArgs e)
        {
            variableValues.Clear();
            inTrackingWindow = true;
        }

        /// <summary>
        /// Called to end keeping track of variable
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void OnEndEvent(object sender, EventArgs e)
        {
            inTrackingWindow = false;
        }


    }
}
