﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Models.Core;

namespace Models.PMF.Functions
{
    /// <summary>
    /// Return the value of a nominated internal \ref Models.PMF.Plant "Plant" numerical variable
    /// </summary>
    /// \warning You have to specify the full path of numerical variable, which starts from the child of \ref Models.PMF.Plant "Plant".
    /// For example,  <b>[Phenology].ThermalTime.Value</b> refers to value of ThermalTime under phenology function.
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [Description("Returns the value of a nominated internal Plant numerical variable")]
    public class VariableReference : Model, IFunction
    {
        /// <summary>The variable name</summary>
        [Description("Specify an internal Plant variable")]
        public string VariableName { get; set; }


        /// <summary>Gets the value.</summary>
        /// <value>The value.</value>
        public double Value
        {
            get
            {
                return Convert.ToDouble(ExpressionFunction.Evaluate(VariableName.Trim(), this));
            }
        }

    }
}