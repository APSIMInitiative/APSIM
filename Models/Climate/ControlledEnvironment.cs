using APSIM.Shared.Utilities;
using Models.Core;
using Models.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Models.Climate
{

    ///<summary>
    /// Reads in controlled environment weather data and makes it available to models.
    ///</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    public class ControlledEnvironment : Model, IWeather
    {
        /// <summary>
        /// A link to the clock model.
        /// </summary>
        [Link]
        private IClock clock = null;
        /// <summary>
        /// Gets the start date of the weather file
        /// </summary>
        public DateTime StartDate { get { return clock.StartDate; } }

        /// <summary>
        /// Gets the end date of the weather file
        /// </summary>
        public DateTime EndDate { get { return clock.EndDate; } }

        /// <summary>
        /// This event will be invoked immediately before models get their weather data.
        /// models and scripts an opportunity to change the weather data before other models
        /// reads it.
        /// </summary>
        public event EventHandler PreparingNewWeatherData;

        /// <summary>
        /// Gets or sets the maximum temperature (oC)
        /// </summary>
        [Description("Maximum Air Temperature")]
        [Units("°C")]
        public double MaxT { get; set; }

        /// <summary>
        /// Gets or sets the minimum temperature (oC)
        /// </summary>
        [Description("Minimum Air Temperature")]
        [Units("°C")]
        public double MinT { get; set; }

        /// <summary>
        /// Daily Mean temperature (oC)
        /// </summary>
        [Units("°C")]
        [JsonIgnore]
        public double MeanT { get { return MathUtilities.Average(SubDailyTemperature); } }

        /// <summary>
        /// Hourly temperature values assuming t = Tmin when dark and t=Tmax when light (oC)
        /// </summary>
        [JsonIgnore]
        public double[] SubDailyTemperature = null;

        /// <summary>
        /// Daily mean VPD (hPa)
        /// </summary>
        [Units("hPa")]
        [JsonIgnore]
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

        /// <summary>
        /// Gets or sets the rainfall (mm)
        /// </summary>
        [Description("Rainfall")]
        [Units("mm")]
        public double Rain { get; set; }

        /// <summary>
        /// Gets or sets the solar radiation. MJ/m2/day
        /// </summary>
        [Description("Solar Radiation")]
        [Units("MJ/m2/d")]
        public double Radn { get; set; }

        /// <summary>
        /// Gets or sets the Pan Evaporation (mm) (Class A pan)
        /// </summary>
        [Description("Pan Evaporation")]
        [Units("mm")]
        public double PanEvap { get; set; }

        /// <summary>
        /// Gets or sets the vapor pressure (hPa)
        /// </summary>
        [Description("Vapour Pressure")]
        [Units("hPa")]
        public double VP { get; set; }

        /// <summary>
        /// Gets or sets the wind value found in weather file or zero if not specified. (code says 3.0 not zero)
        /// </summary>
        [Description("Wind Speed")]
        [Units("m/s")]
        public double Wind { get; set; }

        /// <summary>
        /// Gets or sets the CO2 level. If not specified in the weather file the default is 350.
        /// </summary>
        [Description("CO2 concentration of the air")]
        [Units("ppm")]
        public double CO2 { get; set; }

        /// <summary>
        /// Gets or sets the atmospheric air pressure. If not specified in the weather file the default is 1010 hPa.
        /// </summary>
        [Description("Air Pressure")]
        [Units("hPa")]
        public double AirPressure { get; set; }

        /// <summary>
        /// Gets or sets the diffuse radiation fraction. If not specified in the weather file the default is 1.
        /// </summary>
        [Description("Diffuse Fraction")]
        [Units("0-1")]
        public double DiffuseFraction { get; set; }

        /// <summary>
        /// Gets the latitude
        /// </summary>
        [Description("Latitude")]
        [Units("°")]
        public double Latitude { get; set; }

        /// <summary>Gets the longitude</summary>
        [Description("Longitude")]
        [Units("°")]
        public double Longitude { get; set; }

        /// <summary>
        /// Gets the average temperature
        /// </summary>
        public double Tav { get { return (this.MinT + this.MaxT) / 2; } }

        /// <summary>
        /// Gets the temperature amplitude.
        /// </summary>
        public double Amp { get { return 0; } }

        /// <summary>
        /// Gets the temperature amplitude.
        /// </summary>
        public string FileName { get { return "Controlled Environment does not read from a file"; } }

        /// <summary>
        /// Gets the duration of the day in hours.
        /// </summary>
        [Description("Day Length")]
        [Units("h")]
        public double DayLength { get; set; }

        /// <summary>
        /// Calculate daylength using a given twilight angle
        /// </summary>
        /// <param name="twilight"></param>
        /// <returns></returns>
        public double CalculateDayLength(double twilight)
        {
            return DayLength;
        }


        /// <summary>
        /// Gets the duration of the day in hours.
        /// </summary>
        [Description("The hour of the day for sunrise")]
        public double SunRise { get; set; }

        /// <summary>
        /// Number of hours after sun up we switch from Min to Max temp.
        /// </summary>
        [Description("Hours after sunrise to switch from Min to Max temp")]

        public double TempLag { get; set; } = 0;

        /// <summary>
        /// Calculate daylength using a given twilight angle
        /// </summary>
        public double CalculateSunRise()
        {
            return SunRise;
        }

        /// <summary>
        /// Gets the duration of the day in hours.
        /// </summary>
        [JsonIgnore]
        public double SunSet { get { return SunRise + DayLength; }}

        /// <summary>
        /// Calculate daylength using a given twilight angle
        /// </summary>
        public double CalculateSunSet()
        {
            return SunSet;
        }
        /// <summary>
        /// Constructor
        /// </summary>
        public ControlledEnvironment()
        {
            AirPressure = 1010;
        }

        /// <summary>
        /// An event handler for the daily DoWeather event.
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("DoWeather")]
        private void OnDoWeather(object sender, EventArgs e)
        {
            calculateHourlyTemperature();
            if (this.PreparingNewWeatherData != null)
                this.PreparingNewWeatherData.Invoke(this, new EventArgs());
            YesterdaysMetData = new DailyMetDataFromFile();
            YesterdaysMetData.Radn = Radn;
            YesterdaysMetData.Rain = Rain;
            YesterdaysMetData.MaxT = MaxT;
            YesterdaysMetData.MinT = MinT;
            YesterdaysMetData.VP = VP;
            YesterdaysMetData.PanEvap = PanEvap;
            TomorrowsMetData = YesterdaysMetData;
        }

        /// <summary>Called when a simulation commences.</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            SubDailyTemperature = new double[24];
        }

        private void calculateHourlyTemperature()
        {
            for (int i = 0; i < SubDailyTemperature.Length; i++)
            {
                if ((i <= SunRise+TempLag) || (i > SunSet))
                    SubDailyTemperature[i] = MinT;
                else
                    SubDailyTemperature[i] = MaxT;
            }
        }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile YesterdaysMetData { get; set; }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TomorrowsMetData { get; set; }
    }
}