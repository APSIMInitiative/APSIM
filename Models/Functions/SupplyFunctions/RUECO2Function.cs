using System;
using Models.Climate;
using Models.Core;
using Models.Interfaces;

namespace Models.Functions.SupplyFunctions
{
    /// <summary>
    /// This model calculates the CO~2~ impact on RUE using the approach of [Reyenga1999].
    /// 
    /// For C3 plants,
    /// 
    ///     _F~CO2~ = (CO~2~ - CP) x (350 + 2 x CP)/(CO~2~ + 2 x CP) x (350 - CP)_
    ///     
    /// where CP, is the compensation point calculated from daily average temperature (T) as
    /// 
    ///     _CP = (163.0 - T) / (5.0 - 0.1 * T)_
    /// 
    /// For C4 plants,
    /// 
    ///     _F~CO2~ = 0.000143 * CO~2~ + 0.95_
    /// 
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(IFunction))]
    public class RUECO2Function : Model, IFunction
    {
        /// <summary>The photosynthetic pathway</summary>
        [Description("PhotosyntheticPathway")]
        public String PhotosyntheticPathway { get; set; }


        /// <summary>The met data</summary>
        [Link]
        protected IWeather MetData = null;


        /// <summary>Gets the value.</summary>
        /// <value>The value.</value>
        /// <exception cref="System.Exception">
        /// Average daily temperature too high for RUE CO2 Function
        /// or
        /// CO2 concentration too low for RUE CO2 Function
        /// or
        /// Unknown photosynthetic pathway in RUECO2Function
        /// </exception>
        public double Value(int arrayIndex = -1)
        {
            if (PhotosyntheticPathway == "C3")
            {

                double temp = (MetData.MaxT + MetData.MinT) / 2.0; // Average temperature
                double CP = (163.0 - temp) / (5.0 - 0.1 * temp);

                if (temp >= 46.5)
                    throw new Exception("Average daily temperature too high for RUE CO2 Function");
                if (MetData.CO2 <= CP)
                    throw new Exception("Daily C02 concentrations are below compensation point");
                else if (MetData.CO2 == WeatherConstants.DEFAULT_CO2)
                    return 1.0;
                else
                {
                    double first;
                    double second;
                    first = (MetData.CO2 - CP) * (WeatherConstants.DEFAULT_CO2 + 2.0 * CP);
                    second = (MetData.CO2 + 2.0 * CP) * (WeatherConstants.DEFAULT_CO2 - CP);
                    return first / second;
                }
            }
            else if (PhotosyntheticPathway == "C4")
            {
                return 0.000143 * MetData.CO2 + 0.95; //Mark Howden, personal communication
            }
            else
                throw new Exception("Unknown photosynthetic pathway in RUECO2Function");
        }
    }
}