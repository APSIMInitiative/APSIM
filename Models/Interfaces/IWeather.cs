using Models.Core;
using System;

namespace Models.Interfaces
{
    /// <summary>A weather interface.</summary>
    public interface IWeather
    {

        /// <summary>Start date of the weather file.</summary>
        DateTime StartDate { get; }

        /// <summary>End date of the weather file.</summary>
        DateTime EndDate { get; }

        /// <summary>Maximum temperature</summary>
        [Units("oC")]
        double MaxT { get; set; }

        /// <summary>Minimum temperature</summary>
        [Units("oC")]
        double MinT { get; set; }

        /// <summary>Mean temperature</summary>
        [Units("oC")]
        double MeanT { get; }

        /// <summary>Daily mean VPD</summary>
        [Units("hPa")]
        double VPD { get; }

        /// <summary>Rainfall</summary>
        [Units("mm")]
        double Rain { get; set; }

        /// <summary>Pan evaporation</summary>
        [Units("mm")]
        public double PanEvap { get; set; }

        /// <summary>Solar radiation</summary>
        [Units("MJ/m2/day")]
        double Radn { get; set; }

        /// <summary>Vapor pressure</summary>
        [Units("hPa")]
        double VP { get; set; }

        /// <summary>Wind speed</summary>
        [Units("m/s")]
        double Wind { get; set; }

        /// <summary>CO2</summary>
        [Units("ppm")]
        double CO2 { get; set; }

        /// <summary>Atmospheric air pressure</summary>
        [Units("hPa")]
        double AirPressure { get; set; }

        /// <summary>Diffuse radiation fraction</summary>
        [Units("0-1")]
        double DiffuseFraction { get; set; }

        /// <summary>Latitude</summary>
        [Units("degrees")]
        double Latitude { get; }

        /// <summary>Longitude</summary>
        [Units("degrees")]
        double Longitude { get; }

        /// <summary>Average annual temperature</summary>
        [Units("oC")]
        double Tav { get; }

        /// <summary>Average monthly temperature amplitude</summary>
        [Units("oC")]
        double Amp { get; }

        /// <summary>Filename</summary>
        string FileName { get; }

        /// <summary>Gets the duration of the day in hours.</summary>
        double CalculateDayLength(double Twilight);

        /// <summary> Gets the time the sun came up.</summary>
        double CalculateSunRise();

        /// <summary> Gets the time the sun went down. </summary>
        double CalculateSunSet();



        /// <summary> MetData for tomorrow </summary>
        DailyMetDataFromFile TomorrowsMetData { get; }

        /// <summary> MetData for tomorrow </summary>
        DailyMetDataFromFile YesterdaysMetData { get; }
    }

    /// <summary>
    /// Structure containing daily met data variables
    /// </summary>
    [Serializable]
    public class DailyMetDataFromFile : Model
    {
        /// <summary>Gets or sets the maximum temperature (oc)</summary>
        public double MaxT { get; set; }

        /// <summary>Gets or sets the minimum temperature (oc)</summary>
        public double MinT { get; set; }

        /// <summary>Daily evap  /// </summary>
        public double PanEvap { get; set; }

        /// <summary>Gets or sets the rainfall (mm)</summary>
        public double Rain { get; set; }

        /// <summary>Gets or sets the solar radiation. MJ/m2/day</summary>
        public double Radn { get; set; }

        /// <summary>Gets or sets the vapor pressure</summary>
        public double VP { get; set; }

        /// <summary> Gets or sets the wind value found in weather file or zero if not specified. /// </summary>
        public double Wind { get; set; }

        /// <summary> Gets or sets the CO2 level. If not specified in the weather file the default is WeatherConstants.DEFAULT_CO2. </summary>
        public double RainfallHours { get; set; }

        /// <summary> Gets or sets the atmospheric air pressure. If not specified in the weather file the default is 1010 hPa. </summary>
        public double AirPressure { get; set; }

        /// <summary> Gets or sets the diffuse radiation fraction. If not specified in the weather file the default is 1010 hPa. </summary>
        public double DiffuseFraction { get; set; }

        /// <summary> Gets or sets the diffuse radiation fraction. If not specified in the weather file the default is 1010 hPa. </summary>
        public double DayLength { get; set; }

        /// <summary>Daily co2 level.</summary>
        public double CO2 { get; set; }

        /// <summary>
        /// Raw data straight from the met file. This can be used to access
        /// non-standard variables which aren't auto-mapped to properties.
        /// </summary>
        public object[] Raw { get; set; }
    }

    ///<summary>
    /// Stores a weather data file with a datetime it was read from
    ///</summary>
    [Serializable]
    public class WeatherRecordEntry
    {
        /// <summary>Date this weather was recorded on</summary>
        public DateTime Date { get; set; }

        /// <summary>The weather data</summary>
        public DailyMetDataFromFile MetData { get; set; }

    }
}
