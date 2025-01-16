using System;
using Models.Climate;
using Models.Core;
using Models.Interfaces;

namespace Models.Functions.SupplyFunctions
{
    /// <summary>
    /// This model calculates the CO~2~ impact on stomatal conductance using the approach of [Elli2020].
    /// 
    ///     _StomatalConductanceCO2Modifier = PhotosynthesisCO2Modifier x (350 - CP)/(CO~2~ - CP)_
    ///     
    /// where CP, is the compensation point calculated from daily average temperature (T) as
    /// 
    ///     _CP = (163.0 - T) / (5.0 - 0.1 * T)_
    ///     
    /// </summary>
    [Serializable]
    [Description("This model calculates CO2 Impact on stomatal conductance RUE using the approach of <br>Elli et al (2020) <br>Global sensitivity-based modelling approach to identify suitable Eucalyptus traits for adaptation to climate variability and change. <br> in silico Plants Vol. 2, No. 1, pp. 1–17")]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(IFunction))]
    public class StomatalConductanceCO2Modifier : Model, IFunction
    {
        /// <summary>The met data</summary>
        [Link]
        protected IWeather MetData = null;

        /// <summary>Photosynthesis CO2 Modifier</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction PhotosynthesisCO2Modifier = null;

        /// <summary>Gets the value.</summary>
        /// <value>The value.</value>
        public double Value(int arrayIndex = -1)
        {
            double temp = (MetData.MaxT + MetData.MinT) / 2.0; // Average temperature
            if (temp >= 50)
                throw new Exception("Average daily temperature too high for Stomatal Conductance CO2 Function");

            if (MetData.CO2 == WeatherConstants.DEFAULT_CO2)
                return 1.0;
            else
            {
                double CP = (163.0 - temp) / (5.0 - 0.1 * temp);  //co2 compensation point (ppm)
                double first = (MetData.CO2 - CP);
                double second = (WeatherConstants.DEFAULT_CO2 - CP);
                return PhotosynthesisCO2Modifier.Value() / (first / second);
            }
        }
    }
}