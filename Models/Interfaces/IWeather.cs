using System;
using Models.Core;

namespace Models.Interfaces
{

    /// <summary>A weather data interface</summary>
    public interface IWeather
    {
        /// <summary>Gets the start date of the weather file</summary>
        DateTime StartDate { get; }

        /// <summary>Gets the end date of the weather file</summary>
        DateTime EndDate { get; }

        /// <summary>Gets or sets the minimum temperature (oC)</summary>
        double MinT { get; set; }

        /// <summary>Gets or sets the maximum temperature (oC)</summary>
        double MaxT { get; set; }

        /// <summary>Gets or sets the mean temperature (oC)</summary>
        double MeanT { get; }

        /// <summary>Gets or sets the solar radiation (MJ/m2)</summary>
        double Radn { get; set; }

        /// <summary> Gets or sets the diffuse radiation fraction (0-1)</summary>
        double DiffuseFraction { get; set; }

        /// <summary>Gets or sets the rainfall (mm)</summary>
        double Rain { get; set; }

        /// <summary>Class A pan evaporation (mm)</summary>
        public double PanEvap { get; set; }

        /// <summary>Gets or sets the vapour pressure (hPa)</summary>
        double VP { get; set; }

        /// <summary>Daily mean vapour pressure deficit (hPa)</summary>
        double VPD { get; }

        /// <summary> Gets or sets the mean wind speed (m/s)</summary>
        double Wind { get; set; }

        /// <summary> Gets or sets the atmospheric CO2 level (ppm)</summary>
        double CO2 { get; set; }

        /// <summary>Gets or sets the atmospheric air pressure (hPa)</summary>
        double AirPressure { get; set; }

        /// <summary>Gets the latitude (degrees)</summary>
        double Latitude { get; }

        /// <summary>Gets the longitude (degrees)</summary>
        double Longitude { get; }

        /// <summary>Gets the long-term average temperature (oC)</summary>
        double Tav { get; }

        /// <summary>Gets the long-term temperature amplitude (oC)</summary>
        double Amp { get; }

        /// <summary>Gets the weather file name</summary>
        string FileName { get; }

        /// <summary>Gets the duration of the daylight (h)</summary>
        double CalculateDayLength(double Twilight);

        /// <summary> Gets the time the sun raises (h)</summary>
        double CalculateSunRise();

        /// <summary> Gets the time the sun sets (h)</summary>
        double CalculateSunSet();

        /// <summary> MetData for tomorrow </summary>
        DailyMetDataFromFile TomorrowsMetData { get; }

        /// <summary> MetData for tomorrow </summary>
        DailyMetDataFromFile YesterdaysMetData { get; }
    }

    /// <summary>Structure containing daily weather data variables</summary>
    [Serializable]
    public class DailyMetDataFromFile : Model
    {
        /// <summary>Gets or sets the minimum temperature (oC)</summary>
        public double MinT { get; set; }

        /// <summary>Gets or sets the maximum temperature (oC)</summary>
        public double MaxT { get; set; }

        /// <summary>Gets or sets the mean temperature (oC)</summary>
        public double MeanT { get; set; }

        /// <summary>Gets or sets the solar radiation (MJ/m2)</summary>
        public double Radn { get; set; }

        /// <summary> Gets or sets the daylight length (h)</summary>
        public double DayLength { get; set; }

        /// <summary> Gets or sets the diffuse radiation fraction(0-1)</summary>
        public double DiffuseFraction { get; set; }

        /// <summary>Gets or sets the rainfall (mm)</summary>
        public double Rain { get; set; }

        /// <summary>Daily class A pan evaporation (mm)</summary>
        public double PanEvap { get; set; }

        /// <summary> Gets or sets the duration of rain within a day (h)</summary>
        public double RainfallHours { get; set; }

        /// <summary>Gets or sets the vapour pressure (hPa)</summary>
        public double VP { get; set; }

        /// <summary> Gets or sets the mean wind speed (m/s)</summary>
        public double Wind { get; set; }

        /// <summary> Gets or sets the atmospheric CO2 level (ppm)</summary>
        public double CO2 { get; set; }

        /// <summary> Gets or sets the atmospheric air pressure (hPa)</summary>
        public double AirPressure { get; set; }

        /// <summary> Gets or sets the potential evapotranspiration (mm)</summary>
        public double PotentialEvapotranspiration { get; set; }

        /// <summary> Gets or sets the potential evaporation (mm)</summary>
        public double PotentialSoilEvaporation { get; set; }

        /// <summary> Gets or sets the actual soil evaporation (mm)</summary>
        public double ActualSoilEvaporation { get; set; }

        /// <summary>
        /// Raw data straight from the met file. This can be used to access
        /// non-standard variables which aren't auto-mapped to properties.
        /// </summary>
        public object[] Raw { get; set; }
    }

    ///<summary>
    /// Stores a weather data file with the date-time it was read from
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
