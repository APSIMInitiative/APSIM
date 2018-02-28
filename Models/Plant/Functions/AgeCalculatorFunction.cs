﻿// -----------------------------------------------------------------------
// <copyright file="AgeCalculatorFunction.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
//-----------------------------------------------------------------------
namespace Models.PMF.Functions
{
    using System;
    using Models.Core;

    /// <summary>
    /// # [Name]
    /// An age calculator function
    /// </summary>
    [Serializable]
    [Description("Returns the age (in years) of the crop")]
    public class AgeCalculatorFunction : BaseFunction
    {
        /// <summary>The value being returned</summary>
        private double[] returnValue = new double[1];

        private int _Age = 0;

        /// <summary>Called when [do daily initialisation].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoDailyInitialisation")]
        private void OnDoDailyInitialisation(object sender, EventArgs e)
        {
            _Age = _Age + 1;
        }

        /// <summary>Gets the value.</summary>
        public override double[] Values()
        {
            returnValue[0] = _Age / 365.25;
            return returnValue;
        }
    }
}