using System;
using Models.Core;
using Models.Climate;
using Models.Interfaces;
using Newtonsoft.Json;
using APSIM.Shared.Utilities;


namespace Models.Agroforestry
{
    /// <summary>
    /// Class to calculate and communicate local microclimate in agroforestry systems
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    [ValidParent(ParentType = typeof(Zone))]
    public class LocalMicroClimate : Model, IWeather
    {

        [Link]
        Weather weather = null; // parent weather.
        [Link]
        AgroforestrySystem ParentSystem = null;

        /// <summary>Gets the start date of the weather file</summary>
        [JsonIgnore]
        public DateTime StartDate { get { return weather.StartDate; } }

        /// <summary>Gets the end date of the weather file</summary>
        [JsonIgnore]
        public DateTime EndDate { get { return weather.EndDate; } }

        /// <summary>Gets or sets the maximum temperature (oc)</summary>
        [JsonIgnore]
        public double MaxT { get { return weather.MaxT; } set { weather.MaxT = value; } }

        /// <summary>Gets or sets the minimum temperature (oc)</summary>
        [JsonIgnore]
        public double MinT { get { return weather.MinT; } set { weather.MinT = value; } }

        /// <summary>Pan evaporation</summary>
        [JsonIgnore]
        public double PanEvap { get { return weather.PanEvap; } set{ weather.PanEvap = value; } }

        /// <summary>
        /// Daily Mean temperature (oC)
        /// </summary>
        public double MeanT { get { return (MaxT + MinT) / 2; } }

        /// <summary>
        /// Daily mean VPD (hPa)
        /// </summary>
        [Units("hPa")]
        public double VPD
        {
            get
            {
                const double SVPfrac = 0.66;
                double VPDmint = MetUtilities.svp((float)MinT) - VP;
                VPDmint = Math.Max(VPDmint, 0.0);

                double VPDmaxt = MetUtilities.svp((float)MaxT) - VP;
                VPDmaxt = Math.Max(VPDmaxt, 0.0);

                return SVPfrac * VPDmaxt + (1 - SVPfrac) * VPDmint;
            }
        }

        /// <summary>Gets or sets the rainfall (mm)</summary>
        [JsonIgnore]
        public double Rain { get { return weather.Rain; } set { weather.Rain = value; } }

        /// <summary>Gets or sets the solar radiation. MJ/m2/day</summary>
        [JsonIgnore]
        public double Radn { get { return weather.Radn * ParentSystem.GetRadiationReduction(Parent as Zone) ; } set { weather.Radn = value; } }

        /// <summary>Gets or sets the vapor pressure</summary>
        [JsonIgnore]
        public double VP { get { return weather.VP; } set { weather.VP = value; } }

        /// <summary>
        /// Gets or sets the wind value found in weather file or zero if not specified.
        /// </summary>
        [JsonIgnore]
        public double Wind { get { return weather.Wind * ParentSystem.GetWindReduction(Parent as Zone); } set { weather.Wind = value; } }

        /// <summary>
        /// Gets or sets the CO2 level. If not specified in the weather file the default is WeatherConstants.DEFAULT_CO2.
        /// </summary>
        [JsonIgnore]
        public double CO2 { get { return weather.CO2; } set { weather.CO2 = value; } }

        /// <summary>
        /// Gets or sets the atmospheric air pressure. If not specified in the weather file the default is 1010 hPa.
        /// </summary>
        [JsonIgnore]
        public double AirPressure { get { return weather.AirPressure; } set { weather.AirPressure = value; } }

        /// <summary>
        /// Gets or sets the diffuse radiation fraction. If not specified in the weather file the default is 1.
        /// </summary>
        [JsonIgnore]
        public double DiffuseFraction { get { return weather.DiffuseFraction; } set { weather.DiffuseFraction = value; } }

        /// <summary>Gets the latitude</summary>
        [JsonIgnore]
        public double Latitude { get { return weather.Latitude; } }

        /// <summary>Gets the longitude</summary>
        [JsonIgnore]
        public double Longitude { get { return weather.Longitude; } }

        /// <summary>Gets the average temperature</summary>
        [JsonIgnore]
        public double Tav { get { return weather.Tav; } }

        /// <summary>Gets the temperature amplitude.</summary>
        [JsonIgnore]
        public double Amp { get { return weather.Amp; } }

        /// <summary>Gets the temperature amplitude.</summary>
        [JsonIgnore]
        public string FileName { get { return weather.FileName; } }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile YesterdaysMetData { get { return weather.YesterdaysMetData; } }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TomorrowsMetData { get { return weather.TomorrowsMetData; } }

        /// <summary>Gets the duration of the day in hours.</summary>
        public double CalculateDayLength(double Twilight) { return weather.CalculateDayLength(Twilight); }

        /// <summary> calculate the time of sun rise </summary>
        /// <returns>the time of sun rise</returns>
        public double CalculateSunRise(){return 12 - CalculateDayLength(-6) / 2;}

        /// <summary> calculate the time of sun set</summary>
        /// <returns>Sun set time</returns>
        public double CalculateSunSet(){return 12 + CalculateDayLength(-6) / 2;}
    }
}

