using APSIM.Shared.Utilities;
using Models.Core;
using Models.Interfaces;
using Newtonsoft.Json;
using System;

namespace Models.Climate
{
    ///<summary>
    /// Reads in controlled environment weather data and makes it available to models
    ///</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    public class ControlledEnvironment : Model, IWeather
    {
        /// <summary>
        /// A link to the clock model
        /// </summary>
        [Link]
        private IClock clock = null;

        /// <summary>
        /// Event that will be invoked immediately before the daily weather data is updated
        /// </summary>
        /// <remarks>
        /// This provides models and scripts an opportunity to change the weather data before
        /// other models access them
        /// </remarks>
        public event EventHandler PreparingNewWeatherData;

        /// <summary>
        /// Gets the weather file name. Should be relative file path where possible
        /// </summary>
        public string FileName { get { return "Controlled Environment does not read from a file"; } }

        /// <summary>Gets the start date of the weather file</summary>
        public DateTime StartDate { get { return clock.StartDate; } }

        /// <summary>Gets the end date of the weather file</summary>
        public DateTime EndDate { get { return clock.EndDate; } }

        /// <summary>Gets or sets the daily minimum air temperature (oC)</summary>
        [Description("Minimum air temperature")]
        [Units("oC")]
        public double MinT { get; set; }

        /// <summary>Gets or sets the daily maximum air temperature (oC)</summary>
        [Description("Maximum air temperature")]
        [Units("oC")]
        public double MaxT { get; set; }

        /// <summary>Gets the daily mean air temperature (oC)</summary>
        [Units("oC")]
        [JsonIgnore]
        public double MeanT { get { return (MaxT + MinT) / 2.0; } }

        /// <summary>Gets or sets the solar radiation (MJ/m2)</summary>
        [Description("Solar radiation")]
        [Units("MJ/m2")]
        public double Radn { get; set; }

        /// <summary>Gets or sets the day length, period with light (h)</summary>
        [Description("Day length")]
        [Units("h")]
        public double DayLength { get; set; }

        /// <summary>Gets or sets the diffuse radiation fraction (0-1)</summary>
        [Description("Diffuse radiation fraction")]
        [Units("0-1")]
        public double DiffuseFraction { get; set; }

        /// <summary>Gets or sets the rainfall amount (mm)</summary>
        [Description("Rainfall")]
        [Units("mm")]
        public double Rain { get; set; }

        /// <summary>Gets or sets the class A pan evaporation (mm)</summary>
        [Description("Class A pan evaporation")]
        [Units("mm")]
        public double PanEvap { get; set; }

        /// <summary>Gets or sets the air vapour pressure (hPa)</summary>
        [Description("Air vapour pressure")]
        [Units("hPa")]
        public double VP { get; set; }

        /// <summary>Gets the daily mean vapour pressure deficit (hPa)</summary>
        [JsonIgnore]
        [Units("hPa")]
        public double VPD { get { return calculateVapourPressureDefict(MinT, MaxT, VP); } }

        /// <summary>Gets or sets the average wind speed (m/s)</summary>
        [Description("Wind speed")]
        [Units("m/s")]
        public double Wind { get; set; }

        /// <summary>Gets or sets the CO2 level in the atmosphere (ppm)</summary>
        [Description("Atmospheric CO2 concentration")]
        [Units("ppm")]
        public double CO2 { get; set; }

        /// <summary>Gets or sets the mean atmospheric air pressure</summary>
        [Description("Atmospheric air pressure")]
        [Units("hPa")]
        public double AirPressure { get; set; } = 1010;

        /// <summary>Gets or sets the latitude (decimal degrees)</summary>
        [Description("Latitude")]
        [Units("degrees")]
        public double Latitude { get; set; }

        /// <summary>Gets or sets the longitude (decimal degrees)</summary>
        [Description("Longitude")]
        [Units("degrees")]
        public double Longitude { get; set; }

        /// <summary>Gets the long-term average air temperature (oC)</summary>
        [Units("oC")]
        public double Tav { get { return (MinT + MaxT) / 2.0; } }

        /// <summary>Gets the long-term average temperature amplitude (oC)</summary>
        [Units("oC")]
        public double Amp { get { return 0; } }

        /// <summary>Gets or sets time of the day for sunrise (h)</summary>
        [Description("The hour of the day for sunrise")]
        [Units("h")]
        public double SunRise { get; set; }

        /// <summary>Gets or sets time of the day for sunset (h)</summary>
        [Description("The hour of the day for sunset")]
        [Units("h")]
        public double SunSet { get; set; }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile YesterdaysMetData { get; set; }

        /// <summary>Met Data for tomorrow</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TomorrowsMetData { get; set; }

        /// <summary>Performs the tasks to update the weather data</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("DoWeather")]
        private void OnDoWeather(object sender, EventArgs e)
        {
            if (PreparingNewWeatherData != null)
                PreparingNewWeatherData.Invoke(this, new EventArgs());
            YesterdaysMetData = new DailyMetDataFromFile();
            YesterdaysMetData.Radn = Radn;
            YesterdaysMetData.Rain = Rain;
            YesterdaysMetData.MaxT = MaxT;
            YesterdaysMetData.MinT = MinT;
            YesterdaysMetData.VP = VP;
            YesterdaysMetData.PanEvap = PanEvap;
            TomorrowsMetData = YesterdaysMetData;
        }

        /// <summary>Computes the duration of the day, with light (hours)</summary>
        /// <param name="Twilight">The angle to measure time for twilight (degrees)</param>
        /// <returns>The number of hours of daylight</returns>
        public double CalculateDayLength(double Twilight)
        {
            return DayLength;
        }

        /// <summary>Computes the time of sun rise (h)</summary>
        /// <returns>Sun set time</returns>
        public double CalculateSunRise()
        {
            return SunRise;
        }

        /// <summary>Computes the time of sun set (h)</summary>
        /// <returns>Sun set time</returns>
        public double CalculateSunSet()
        {
            return SunSet;
        }

        /// <summary>Computes today's atmospheric vapour pressure deficit (hPa)</summary>
        /// <param name="minTemp">Today's minimum temperature (oC)</param>
        /// <param name="maxTemp">Today's maximum temperature (oC)</param>
        /// <param name="vapourPressure">Today's vapour pressure (hPa)</param>
        /// <returns>The vapour pressure deficit (hPa)</returns>
        private double calculateVapourPressureDefict(double minTemp, double maxTemp, double vapourPressure)
        {
            const double SVPfrac = 0.66;

            double result;
            double VPDmint = MetUtilities.svp(minTemp) - vapourPressure;
            VPDmint = Math.Max(VPDmint, 0.0);

            double VPDmaxt = MetUtilities.svp(MaxT) - vapourPressure;
            VPDmaxt = Math.Max(VPDmaxt, 0.0);

            result = SVPfrac * VPDmaxt + (1 - SVPfrac) * VPDmint;
            return result;
        }
    }
}
