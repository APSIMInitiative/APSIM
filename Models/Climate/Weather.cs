﻿using APSIM.Shared.Utilities;
using Models.Core;
using Models.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Models.Climate
{
    ///<summary>
    /// Reads in weather data from a met file and makes it available to other models
    ///</summary>
    [Serializable]
    [ViewName("UserInterface.Views.TabbedMetDataView")]
    [PresenterName("UserInterface.Presenters.MetDataPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    [ValidParent(ParentType = typeof(Zone))]
    public class Weather : Model, IWeather, IReferenceExternalFiles
    {
        /// <summary>
        /// A link to the clock model
        /// </summary>
        [Link]
        private IClock clock = null;

        /// <summary>
        /// A link to the zone model
        /// </summary>
        [Link]
        private Zone zone = null;

        /// <summary>
        /// A link to the summary (log file)
        /// </summary>
        [Link]
        private ISummary summary = null;

        /// <summary>
        /// Event that will be invoked immediately before the daily weather data is updated
        /// </summary>
        /// <remarks>
        /// This provides models and scripts an opportunity to change the weather data before
        /// other models access them
        /// </remarks>
        public event EventHandler PreparingNewWeatherData;

        /// <summary>Event that will be invoked at the start of spring </summary>
        public event EventHandler StartOfSpring;

        /// <summary>Event that will be invoked at the start of summer</summary>
        public event EventHandler StartOfSummer;

        /// <summary>Event that will be invoked at the start of autumn</summary>
        public event EventHandler StartOfAutumn;

        /// <summary>Event that will be invoked at the start of winter</summary>
        public event EventHandler StartOfWinter;

        /// <summary>Event that will be invoked at the end of spring</summary>
        public event EventHandler EndOfSpring;

        /// <summary>Event that will be invoked at the end of summer</summary>
        public event EventHandler EndOfSummer;

        /// <summary>Event that will be invoked at the end of autumn</summary>
        public event EventHandler EndOfAutumn;

        /// <summary>Event that will be invoked at the end of winter</summary>
        public event EventHandler EndOfWinter;

        /// <summary>A reference to the text file reader object</summary>
        [NonSerialized]
        private ApsimTextFile reader = null;

        /// <summary>
        /// A LinkedList of weather that has previously been read. The data is stored in order of
        /// date, from newest to oldest, as most recent weather is most likely to be searched for
        /// </summary>
        private LinkedList<WeatherRecordEntry> weatherCache = new LinkedList<WeatherRecordEntry>();

        /// <summary>
        /// The index of the minimum temperature column in the weather file
        /// </summary>
        private int minimumTemperatureIndex;

        /// <summary>
        /// The index of the maximum temperature column in the weather file
        /// </summary>
        private int maximumTemperatureIndex;

        /// <summary>
        /// The index of the mean temperature column in the weather file
        /// </summary>
        private int meanTemperatureIndex;

        /// <summary>
        /// The index of the solar radiation column in the weather file
        /// </summary>
        private int radiationIndex;

        /// <summary>
        /// The index of the day length column in the weather file
        /// </summary>
        private int dayLengthIndex;

        /// <summary>
        /// The index of the diffuse radiation fraction column in the weather file
        /// </summary>
        private int diffuseFractionIndex;

        /// <summary>
        /// The index of the rainfall column in the weather file
        /// </summary>
        private int rainIndex;

        /// <summary>
        /// The index of the pan evaporation column in the weather file
        /// </summary>
        private int panEvaporationIndex;

        /// <summary>
        /// The index of the rainfall duration column in the weather file
        /// </summary>
        private int rainfallHoursIndex;

        /// <summary>
        /// The index of the vapour pressure column in the weather file
        /// </summary>
        private int vapourPressureIndex;

        /// <summary>
        /// The index of the wind speed column in the weather file
        /// </summary>
        private int windIndex;

        /// <summary>
        /// The index of the co2 column in the weather file
        /// </summary>
        private int co2Index;

        /// <summary>
        /// The index of the air pressure column in the weather file
        /// </summary>
        private int airPressureIndex;

        /// <summary>
        /// The index of the potential ET column in the weather file
        /// </summary>
        private int potentialEvapotranspirationIndex;

        /// <summary>
        /// The index of the potential evaporation column in the weather file
        /// </summary>
        private int potentialEvaporationIndex;

        /// <summary>
        /// The index of the actual evaporation column in the weather file
        /// </summary>
        private int actualEvaporationIndex;

        /// <summary>
        /// Default value for wind speed (m/s)
        /// </summary>
        private const double defaultWind = 3.0;

        /// <summary>
        /// Default value for atmospheric CO2 concentration (ppm)
        /// </summary>
        private const double defaultCO2 = 350.0;

        /// <summary>
        /// Default value for solar angle for computing twilight (degrees)
        /// </summary>
        private const double defaultTwilight = 6.0;

        /// <summary>
        /// Stores the optional constants file name. This should only be accessed via
        /// <see cref="ConstantsFile"/>, which handles conversion between
        /// relative/absolute paths
        /// </summary>
        private string constantsFile;

        /// <summary>
        /// Gets or sets the optional constants file name. Allows to specify a second file which
        /// contains constants such as latitude, longitude, tav, amp, etc.; really only used when
        /// the actual met data is in a .csv file
        /// </summary>
        [Description("Constants file")]
        public string ConstantsFile
        {
            get
            {
                Simulation simulation = FindAncestor<Simulation>();
                if (simulation != null)
                    return PathUtilities.GetAbsolutePath(constantsFile, simulation.FileName);
                else
                {
                    Simulations simulations = FindAncestor<Simulations>();
                    if (simulations != null)
                        return PathUtilities.GetAbsolutePath(constantsFile, simulations.FileName);
                    else
                        return constantsFile;
                }
            }
            set
            {
                Simulations simulations = FindAncestor<Simulations>();
                if (simulations != null)
                    constantsFile = PathUtilities.GetRelativePath(value, simulations.FileName);
                else
                    constantsFile = value;
            }
        }

        /// <summary>
        /// Gets or sets the weather file name. Should be relative file path where possible
        /// </summary>
        [Summary]
        [Description("Weather file name")]
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the full file name (with path). Needed for the user interface
        /// </summary>
        [JsonIgnore]
        public string FullFileName
        {
            get
            {
                Simulation simulation = FindAncestor<Simulation>();
                if (simulation != null && simulation.FileName != null)
                    return PathUtilities.GetAbsolutePath(FileName, simulation.FileName);
                else
                {
                    Simulations simulations = FindAncestor<Simulations>();
                    if (simulations != null)
                        return PathUtilities.GetAbsolutePath(FileName, simulations.FileName);
                    else
                        return PathUtilities.GetAbsolutePath(FileName, "");
                }
            }
            set
            {
                Simulations simulations = FindAncestor<Simulations>();
                if (simulations != null)
                    FileName = PathUtilities.GetRelativePath(value, simulations.FileName);
                else
                    FileName = value;
            }
        }

        /// <summary>
        /// Gets or sets the WorkSheet name with weather data, if data is supplied as an Excel file
        /// </summary>
        public string ExcelWorkSheetName { get; set; }

        /// <summary>Gets the start date of the weather file</summary>
        public DateTime StartDate
        {
            get
            {
                if (reader == null && !OpenDataFile())
                    return new DateTime(0);

                return reader.FirstDate;
            }
        }

        /// <summary>Gets the end date of the weather file</summary>
        public DateTime EndDate
        {
            get
            {
                if (reader == null && !OpenDataFile())
                    return new DateTime(0);

                return reader.LastDate;
            }
        }

        /// <summary>Gets or sets the daily minimum air temperature (oC)</summary>
        [JsonIgnore]
        [Units("oC")]
        public double MinT { get; set; }

        /// <summary>Gets or sets the daily maximum air temperature (oC)</summary>
        [Units("oC")]
        [JsonIgnore]
        public double MaxT { get; set; }

        /// <summary>Gets or sets the daily mean air temperature (oC)</summary>
        [Units("oC")]
        [JsonIgnore]
        public double MeanT { get; set; }

        /// <summary>Gets or sets the solar radiation (MJ/m2)</summary>
        [Units("MJ/m2")]
        [JsonIgnore]
        public double Radn { get; set; }

        /// <summary>Gets or sets the maximum clear sky radiation (MJ/m2)</summary>
        [Units("MJ/m2")]
        [JsonIgnore]
        public double Qmax { get; set; }

        /// <summary>Gets or sets the day length, period with light (h)</summary>
        [Units("h")]
        [JsonIgnore]
        public double DayLength { get; set; }

        /// <summary>Gets or sets the diffuse radiation fraction (0-1)</summary>
        [Units("0-1")]
        [JsonIgnore]
        public double DiffuseFraction { get; set; }

        /// <summary>Gets or sets the rainfall amount (mm)</summary>
        [Units("mm")]
        [JsonIgnore]
        public double Rain { get; set; }

        /// <summary>Gets or sets the class A pan evaporation (mm)</summary>
        [Units("mm")]
        [JsonIgnore]
        public double PanEvap { get; set; }

        /// <summary>Gets or sets the number duration of rainfall within a day (h)</summary>
        [Units("h")]
        [JsonIgnore]
        public double RainfallHours { get; set; }

        /// <summary>Gets or sets the air vapour pressure (hPa)/// </summary>
        [Units("hPa")]
        [JsonIgnore]
        public double VP { get; set; }

        /// <summary>Gets or sets the daily mean vapour pressure deficit (hPa)</summary>
        [Units("hPa")]
        [JsonIgnore]
        public double VPD { get; set; }

        /// <summary>Gets or sets the average wind speed (m/s)</summary>
        [Units("m/s")]
        [JsonIgnore]
        public double Wind { get; set; }

        /// <summary>Gets or sets the CO2 level in the atmosphere (ppm)</summary>
        [Units("ppm")]
        [JsonIgnore]
        public double CO2 { get; set; }

        /// <summary>Gets or sets the mean atmospheric air pressure</summary>
        [Units("hPa")]
        [JsonIgnore]
        public double AirPressure { get; set; }

        /// <summary>Gets or sets the potential evapotranspiration</summary>
        [Units("mm")]
        [JsonIgnore]
        public double GivenPotentialEvapotranspiration { get; set; }

        /// <summary>Gets or sets the potential soil evaporation</summary>
        [Units("mm")]
        [JsonIgnore]
        public double GivenPotentialSoilEvaporation { get; set; }

        /// <summary>Gets or sets the actual soil evaporation</summary>
        [Units("mm")]
        [JsonIgnore]
        public double GivenActualSoilEvaporation { get; set; }

        /// <summary>Gets or sets the latitude (decimal degrees)</summary>
        [Units("degrees")]
        public double Latitude
        {
            get
            {
                if (reader == null && !OpenDataFile())
                    return 0;

                return reader.ConstantAsDouble("Latitude");
            }
            set
            {
                if (reader != null)
                {
                    reader.Constant("Latitude").Value = value.ToString();
                    checkSeasonDates();
                }
            }
        }

        /// <summary>Gets or sets the longitude (decimal degrees)</summary>
        [Units("degrees")]
        public double Longitude
        {
            get
            {
                if (reader == null || reader.Constant("Longitude") == null)
                    return 0;
                else
                    return reader.ConstantAsDouble("Longitude");
            }
            set
            {
                if (reader != null)
                    reader.Constant("Longitude").Value = value.ToString();
            }
        }

        /// <summary>Gets the long-term average air temperature (oC)</summary>
        [Units("oC")]
        public double Tav
        {
            get
            {
                if (reader == null)
                    return 0;
                else if (reader.Constant("tav") == null)
                    calculateTAVAMP();

                return reader.ConstantAsDouble("tav");
            }
        }

        /// <summary>Gets the long-term average temperature amplitude (oC)</summary>
        [Units("oC")]
        public double Amp
        {
            get
            {
                if (reader == null)
                    return 0;
                else if (reader.Constant("amp") == null)
                    calculateTAVAMP();

                return reader.ConstantAsDouble("amp");
            }
        }

        /// <summary>Gets the day for the winter solstice (day)</summary>
        [Units("day")]
        [JsonIgnore]
        public int WinterSolsticeDOY
        {
            get
            {
                if (Latitude <= 0)
                {
                    if (DateTime.IsLeapYear(clock.Today.Year))
                        return 173;
                    else
                        return 172;
                }
                else
                {
                    if (DateTime.IsLeapYear(clock.Today.Year))
                        return 356;
                    else
                        return 355;
                }
            }
        }

        /// <summary>Gets or sets the number of days since the winter solstice</summary>
        [Units("d")]
        [JsonIgnore]
        public int DaysSinceWinterSolstice { get; set; }

        /// <summary>Gets or sets the first date of summer (dd-mmm)</summary>
        [JsonIgnore]
        public string FirstDateOfSummer { get; set; } = "1-Dec";

        /// <summary>Gets or sets the first date of autumn (dd-mmm)</summary>
        [JsonIgnore]
        public string FirstDateOfAutumn { get; set; } = "1-Mar";

        /// <summary>Gets or sets the first date of winter (dd-mmm)</summary>
        [JsonIgnore]
        public string FirstDateOfWinter { get; set; } = "1-Jun";

        /// <summary>Gets or sets the first date of spring (dd-mmm)</summary>
        [JsonIgnore]
        public string FirstDateOfSpring { get; set; } = "1-Sep";

        /// <summary>Met data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile YesterdaysMetData { get { return GetAdjacentMetData(-1); } }

        /// <summary>Met data for today</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TodaysMetData { get; set; }

        /// <summary>Met data for tomorrow</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TomorrowsMetData { get { return GetAdjacentMetData(1); } }

        /// <summary>
        /// Temporarily stores which tab is currently displayed.
        /// Meaningful only within the GUI
        /// </summary>
        [JsonIgnore] public int ActiveTabIndex = 0;

        /// <summary>
        /// Temporarily stores the starting date for charts.
        /// Meaningful only within the GUI
        /// </summary>
        [JsonIgnore] public int StartYear = -1;

        /// <summary>
        /// Temporarily stores the years to show in charts.
        /// Meaningful only within the GUI
        /// </summary>
        [JsonIgnore] public int ShowYears = 1;

        /// <summary>Name of current season</summary>
        public string Season
        {
            get
            {
                if (DateUtilities.WithinDates(FirstDateOfSummer, clock.Today, FirstDateOfAutumn))
                    return "Summer";
                else if (DateUtilities.WithinDates(FirstDateOfAutumn, clock.Today, FirstDateOfWinter))
                    return "Autumn";
                else if (DateUtilities.WithinDates(FirstDateOfWinter, clock.Today, FirstDateOfSpring))
                    return "Winter";
                else
                    return "Spring";
            }
        }

        /// <summary>Returns our input file names</summary>
        public IEnumerable<string> GetReferencedFileNames()
        {
            return new string[] { FullFileName };
        }

        /// <summary>Remove all paths from referenced file names</summary>
        public void RemovePathsFromReferencedFileNames()
        {
            FileName = Path.GetFileName(FileName);
        }

        /// <summary>
        /// Check values in weather and return a collection of warnings
        /// </summary>
        public IEnumerable<string> Validate()
        {
            if (Amp > 20)
            {
                yield return $"The value of Weather.AMP ({Amp}) is > 20 oC. Please check the value.";
            }
        }

        /// <summary>Overrides the base class method to allow for initialization of this model </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            minimumTemperatureIndex = 0;
            maximumTemperatureIndex = 0;
            meanTemperatureIndex = 0;
            radiationIndex = 0;
            dayLengthIndex = 0;
            diffuseFractionIndex = 0;
            rainIndex = 0;
            panEvaporationIndex = 0;
            rainfallHoursIndex = 0;
            vapourPressureIndex = 0;
            windIndex = 0;
            co2Index = 0;
            airPressureIndex = 0;
            potentialEvapotranspirationIndex = 0;
            potentialEvaporationIndex = 0;
            actualEvaporationIndex = 0;

            if (reader != null)
            {
                reader.Close();
                reader = null;
            }

            checkSeasonDates();

            foreach (var message in Validate())
                summary.WriteMessage(this, message, MessageType.Warning);
        }

        /// <summary>Overrides the base class method to allow for clean up task</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("Completed")]
        private void OnSimulationCompleted(object sender, EventArgs e)
        {
            if (reader != null)
                reader.Close();
            reader = null;
        }

        /// <summary> Performs the tasks to update the weather data</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("DoWeather")]
        private void OnDoWeather(object sender, EventArgs e)
        {
            // read today's weather data
            TodaysMetData = GetMetData(clock.Today);

            // assign values to output variables
            MinT = TodaysMetData.MinT;
            MaxT = TodaysMetData.MaxT;
            MeanT = TodaysMetData.MeanT;
            Radn = TodaysMetData.Radn;
            DayLength = TodaysMetData.DayLength;
            DiffuseFraction = TodaysMetData.DiffuseFraction;
            Rain = TodaysMetData.Rain;
            PanEvap = TodaysMetData.PanEvap;
            RainfallHours = TodaysMetData.RainfallHours;
            VP = TodaysMetData.VP;
            Wind = TodaysMetData.Wind;
            CO2 = TodaysMetData.CO2;
            AirPressure = TodaysMetData.AirPressure;

            // invoke event that allows other models to modify weather data
            if (PreparingNewWeatherData != null)
                PreparingNewWeatherData.Invoke(this, new EventArgs());

            // do basic sanity checks on weather data
            sensibilityCheck(clock as Clock, this);

            // check whether some variables need to be set with 'default' values (functions)
            if (double.IsNaN(MeanT))
                MeanT = (MinT + MaxT) / 2.0;
            if (double.IsNaN(DiffuseFraction))
                DiffuseFraction = calculateDiffuseRadiationFraction(Radn);
            if (double.IsNaN(VP))
                VP = Math.Max(0.0, MetUtilities.svp(MinT));
            if (double.IsNaN(AirPressure))
                AirPressure = calculateAirPressure(zone.Altitude);

            // compute additional outputs derived from weather data
            Qmax = MetUtilities.QMax(clock.Today.DayOfYear + 1, Latitude, MetUtilities.Taz, MetUtilities.Alpha, VP);
            VPD = calculateVapourPressureDefict(MinT, MaxT, VP);
            DaysSinceWinterSolstice = calculateDaysSinceSolstice(DaysSinceWinterSolstice);
        }

        /// <summary>Get the DataTable view of the weather data</summary>
        /// <returns>The DataTable</returns>
        public DataTable GetAllData()
        {
            reader = null;

            if (OpenDataFile())
            {
                List<string> metProps = new List<string>();
                metProps.Add("mint");
                metProps.Add("maxt");
                metProps.Add("radn");
                metProps.Add("rain");
                metProps.Add("diffr");
                metProps.Add("wind");

                return reader.ToTable(metProps);
            }
            else
                return null;
        }

        /// <summary>Reads the weather data for one day from file</summary>
        /// <param name="date">The date to read met data</param>
        public DailyMetDataFromFile GetMetData(DateTime date)
        {
            // check if we've looked at this date before
            WeatherRecordEntry previousEntry = null;
            foreach (WeatherRecordEntry entry in weatherCache)
            {
                if (entry.Date.Equals(date))
                {
                    return entry.MetData;
                }
                else if (entry.Date < date)
                {
                    previousEntry = entry;
                    break;
                }
            }

            if (reader == null)
                if (!OpenDataFile())
                    throw new ApsimXException(this, "Cannot find weather file '" + FileName + "'");

            // get the weather data for that date
            DailyMetDataFromFile readMetData = new DailyMetDataFromFile();
            try
            {
                reader.SeekToDate(date);
                readMetData.Raw = reader.GetNextLineOfData();
            }
            catch (IndexOutOfRangeException err)
            {
                throw new Exception($"Unable to retrieve weather data on {date.ToString("yyy-MM-dd")} in file {FileName}", err);
            }

            if (date != reader.GetDateFromValues(readMetData.Raw))
                throw new Exception("Non consecutive dates found in file: " + FileName + ".");

            // since this data was valid, store in our cache for next time
            WeatherRecordEntry record = new WeatherRecordEntry();
            record.Date = date;
            record.MetData = readMetData;
            if (previousEntry != null)
                weatherCache.AddBefore(weatherCache.Find(previousEntry), record);
            else
                weatherCache.AddFirst(record);

            return checkDailyMetData(readMetData);
        }

        /// <summary>Checks the values for weather data, uses either daily values or a constant</summary>
        /// <remarks>
        /// For each variable handled by Weather, this method will firstly check whether there is a column 
        /// with daily data in the met file (i.e. there is an index equal or greater than zero), if not, it
        /// will check whether a constant value was given (a single value in the met file, like latitude or
        /// TAV). If that fails, either a default value is supplied or 'null' is returned (which results in
        /// an exception being thrown later on)
        /// </remarks>
        /// <param name="readMetData">The weather data structure with values for one line</param>
        /// <returns>The weather data structure with values checked</returns>
        private DailyMetDataFromFile checkDailyMetData(DailyMetDataFromFile readMetData)
        {
            if (minimumTemperatureIndex >= 0)
            {
                readMetData.MinT = Convert.ToDouble(readMetData.Raw[minimumTemperatureIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                readMetData.MinT = reader.ConstantAsDouble("mint");
            }

            if (maximumTemperatureIndex >= 0)
            {
                readMetData.MaxT = Convert.ToDouble(readMetData.Raw[maximumTemperatureIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                readMetData.MaxT = reader.ConstantAsDouble("maxt");
            }

            if (meanTemperatureIndex >= 0)
            {
                readMetData.MeanT = Convert.ToDouble(readMetData.Raw[meanTemperatureIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("meant") != null)
                    readMetData.MeanT = reader.ConstantAsDouble("meant");
                else
                    readMetData.MeanT = double.NaN;
            }

            if (radiationIndex >= 0)
            {
                readMetData.Radn = Convert.ToDouble(readMetData.Raw[radiationIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                readMetData.Radn = reader.ConstantAsDouble("radn");
            }

            if (dayLengthIndex >= 0)
            {
                readMetData.DayLength = Convert.ToDouble(readMetData.Raw[dayLengthIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("daylength") != null)
                    readMetData.DayLength = reader.ConstantAsDouble("daylength");
                else
                    readMetData.DayLength = -1;
            }

            if (diffuseFractionIndex >= 0)
            {
                readMetData.DiffuseFraction = Convert.ToDouble(readMetData.Raw[diffuseFractionIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("diffr") != null)
                    readMetData.DiffuseFraction = reader.ConstantAsDouble("diffr");
                else
                    readMetData.DiffuseFraction = double.NaN;
            }

            if (rainIndex >= 0)
            {
                readMetData.Rain = Convert.ToDouble(readMetData.Raw[rainIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                readMetData.Rain = reader.ConstantAsDouble("rain");
            }

            if (panEvaporationIndex >= 0)
            {
                readMetData.PanEvap = Convert.ToDouble(readMetData.Raw[panEvaporationIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("evap") != null)
                    readMetData.PanEvap = reader.ConstantAsDouble("evap");
                else
                    readMetData.PanEvap = double.NaN;
            }

            if (rainfallHoursIndex >= 0)
            {
                readMetData.RainfallHours = Convert.ToDouble(readMetData.Raw[rainfallHoursIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("rainhours") != null)
                    readMetData.RainfallHours = reader.ConstantAsDouble("rainhours");
                else
                    readMetData.RainfallHours = double.NaN;
            }

            if (vapourPressureIndex >= 0)
            {
                readMetData.VP = Convert.ToDouble(readMetData.Raw[vapourPressureIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("vp") != null)
                    readMetData.VP = reader.ConstantAsDouble("vp");
                else
                    readMetData.VP = double.NaN;
            }

            if (windIndex >= 0)
            {
                readMetData.Wind = Convert.ToDouble(readMetData.Raw[windIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("wind") != null)
                    readMetData.Wind = reader.ConstantAsDouble("wind");
                else
                    readMetData.Wind = defaultWind;
            }

            if (co2Index >= 0)
            {
                readMetData.CO2 = Convert.ToDouble(readMetData.Raw[co2Index], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("co2") != null)
                    readMetData.CO2 = reader.ConstantAsDouble("co2");
                else
                    readMetData.CO2 = defaultCO2;
            }

            if (airPressureIndex >= 0)
            {
                readMetData.AirPressure = Convert.ToDouble(readMetData.Raw[airPressureIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("airpressure") != null)
                    readMetData.AirPressure = reader.ConstantAsDouble("airpressure");
                else
                    readMetData.AirPressure = double.NaN;
            }

            if (potentialEvapotranspirationIndex >= 0)
            {
                readMetData.PotentialEvapotranspiration = Convert.ToDouble(readMetData.Raw[potentialEvapotranspirationIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("pet") != null)
                    readMetData.PotentialEvapotranspiration = reader.ConstantAsDouble("pet");
                else
                    readMetData.PotentialEvapotranspiration = double.NaN;
            }

            if (potentialEvaporationIndex >= 0)
            {
                readMetData.PotentialSoilEvaporation = Convert.ToDouble(readMetData.Raw[potentialEvaporationIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("potevap") != null)
                    readMetData.PotentialSoilEvaporation = reader.ConstantAsDouble("potevap");
                else
                    readMetData.PotentialSoilEvaporation = double.NaN;
            }

            if (actualEvaporationIndex >= 0)
            {
                readMetData.ActualSoilEvaporation = Convert.ToDouble(readMetData.Raw[actualEvaporationIndex], CultureInfo.InvariantCulture);
            }
            else
            {
                if (reader.Constant("actualevap") != null)
                    readMetData.ActualSoilEvaporation = reader.ConstantAsDouble("actualevap");
                else
                    readMetData.ActualSoilEvaporation = double.NaN;
            }

            return readMetData;
        }

        /// <summary>Performs tasks at the start of the day</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("StartOfDay")]
        private void OnStartOfDay(object sender, EventArgs e)
        {
            if (StartOfSummer != null && DateUtilities.DayMonthIsEqual(FirstDateOfSummer, clock.Today))
                StartOfSummer.Invoke(this, e);

            if (StartOfAutumn != null && DateUtilities.DayMonthIsEqual(FirstDateOfAutumn, clock.Today))
                StartOfAutumn.Invoke(this, e);

            if (StartOfWinter != null && DateUtilities.DayMonthIsEqual(FirstDateOfWinter, clock.Today))
                StartOfWinter.Invoke(this, e);

            if (StartOfSpring != null && DateUtilities.DayMonthIsEqual(FirstDateOfSpring, clock.Today))
                StartOfSpring.Invoke(this, e);
        }

        /// <summary>Performs the tasks for the end of the day</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("EndOfDay")]
        private void OnEndOfDay(object sender, EventArgs e)
        {
            if (EndOfSummer != null && DateUtilities.DayMonthIsEqual(FirstDateOfAutumn, clock.Today.AddDays(1)))
                EndOfSummer.Invoke(this, e);

            if (EndOfAutumn != null && DateUtilities.DayMonthIsEqual(FirstDateOfWinter, clock.Today.AddDays(1)))
                EndOfAutumn.Invoke(this, e);

            if (EndOfWinter != null && DateUtilities.DayMonthIsEqual(FirstDateOfSpring, clock.Today.AddDays(1)))
                EndOfWinter.Invoke(this, e);

            if (EndOfSpring != null && DateUtilities.DayMonthIsEqual(FirstDateOfSummer, clock.Today.AddDays(1)))
                EndOfSpring.Invoke(this, e);
        }

        /// <summary>Opens the weather data file</summary>
        /// <returns>True if the file was successfully opened</returns>
        public bool OpenDataFile()
        {
            if (!System.IO.File.Exists(FullFileName) &&
                System.IO.Path.GetExtension(FullFileName) == string.Empty)
                FileName += ".met";

            if (System.IO.File.Exists(FullFileName))
            {
                if (reader == null)
                {
                    if (ExcelUtilities.IsExcelFile(FullFileName) && string.IsNullOrEmpty(ExcelWorkSheetName))
                        throw new Exception($"Unable to open excel file {FullFileName}: no sheet name is specified");

                    reader = new ApsimTextFile();
                    reader.Open(FullFileName, ExcelWorkSheetName);

                    if (reader.Headings == null)
                    {
                        string message = "Cannot find the expected header in ";
                        if (ExcelUtilities.IsExcelFile(FullFileName))
                            message += $"sheet '{ExcelWorkSheetName}' of ";
                        message += $"weather file: {FullFileName}";
                        throw new Exception(message);
                    }

                    minimumTemperatureIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "Mint");
                    maximumTemperatureIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "Maxt");
                    meanTemperatureIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "Meant");
                    radiationIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "Radn");
                    dayLengthIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "DayLength");
                    diffuseFractionIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "DifFr");
                    rainIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "Rain");
                    panEvaporationIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "Evap");
                    rainfallHoursIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "RainHours");
                    vapourPressureIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "VP");
                    windIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "Wind");
                    co2Index = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "CO2");
                    airPressureIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "AirPressure");
                    potentialEvapotranspirationIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "PET");
                    potentialEvaporationIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "PotEvap");
                    actualEvaporationIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "ActaulEvap");

                    if (!string.IsNullOrEmpty(ConstantsFile))
                    {
                        ApsimTextFile constantsReader = new ApsimTextFile();
                        constantsReader.Open(ConstantsFile);
                        if (constantsReader.Constants != null)
                            foreach (ApsimConstant constant in constantsReader.Constants)
                                reader.AddConstant(constant.Name, constant.Value, constant.Units, constant.Comment);
                    }

                    if (minimumTemperatureIndex == -1)
                        if (reader == null || reader.Constant("mint") == null)
                            throw new Exception("Cannot find MinT in weather file: " + FullFileName);

                    if (maximumTemperatureIndex == -1)
                        if (reader == null || reader.Constant("maxt") == null)
                            throw new Exception("Cannot find MaxT in weather file: " + FullFileName);

                    if (radiationIndex == -1)
                        if (reader == null || reader.Constant("radn") == null)
                            throw new Exception("Cannot find Radn in weather file: " + FullFileName);

                    if (rainIndex == -1)
                        if (reader == null || reader.Constant("rain") == null)
                            throw new Exception("Cannot find Rain in weather file: " + FullFileName);
                }
                else
                {
                    if (reader.IsExcelFile != true)
                        reader.SeekToDate(reader.FirstDate);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>Closes the data file</summary>
        public void CloseDataFile()
        {
            if (reader != null)
                reader.Close();
            reader = null;
        }

        /// <summary>
        /// Checks whether the dates defining the seasons should be swapped if in the northern hemisphere
        /// </summary>
        private void checkSeasonDates()
        {
            if (Latitude > 0.0)
            {
                // Swap summer and winter dates.
                var temp = FirstDateOfSummer;
                FirstDateOfSummer = FirstDateOfWinter;
                FirstDateOfWinter = temp;

                // Swap spring and autumn dates.
                temp = FirstDateOfSpring;
                FirstDateOfSpring = FirstDateOfAutumn;
                FirstDateOfAutumn = temp;
            }
        }

        /// <summary>Computes the duration of the day, with light (hours)</summary>
        /// <param name="Twilight">The angle to measure time for twilight (degrees)</param>
        /// <returns>The number of hours of daylight</returns>
        public double CalculateDayLength(double Twilight)
        {
            if (dayLengthIndex == -1 && DayLength == -1)
            { // day length was not given as column or set as a constant
                return MathUtilities.DayLength(clock.Today.DayOfYear, Twilight, Latitude);
            }
            else
            {
                return DayLength;
            }
        }

        /// <summary>Computes the time of sun rise (h)</summary>
        /// <returns>Sun rise time</returns>
        public double CalculateSunRise()
        {
            return 12 - CalculateDayLength(-6) / 2;
        }

        /// <summary>Computes the time of sun set (h)</summary>
        /// <returns>Sun set time</returns>
        public double CalculateSunSet()
        {
            return 12 + CalculateDayLength(-6) / 2;
        }

        /// <summary>Estimate diffuse radiation fraction (0-1)</summary>
        /// <remarks>
        /// Uses the approach of Bristow and Campbell (1984). On the relationship between incoming solar
        /// radiation and daily maximum and minimum temperature. Agricultural and Forest Meteorology
        /// </remarks>
        /// <returns>The diffuse radiation fraction</returns>
        private double calculateDiffuseRadiationFraction(double todaysRadiation)
        {
            double Qmax = MetUtilities.QMax(clock.Today.DayOfYear + 1, Latitude, MetUtilities.Taz, MetUtilities.Alpha, 0.0); // Radiation for clear and dry sky (ie low humidity)
            double Q0 = MetUtilities.Q0(clock.Today.DayOfYear + 1, Latitude);
            double B = Qmax / Q0;
            double Tt = MathUtilities.Bound(todaysRadiation / Q0, 0, 1);
            if (Tt > B) Tt = B;
            double result = (1 - Math.Exp(0.6 * (1 - B / Tt) / (B - 0.4)));
            if (Tt > 0.5 && result < 0.1)
                result = 0.1;
            return result;
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

        /// <summary>Computes the air pressure for a given location</summary>
        /// <remarks>From Jacobson (2005). Fundamentals of atmospheric modeling</remarks>
        /// <param name="localAltitude">The altitude (m)</param>
        /// <returns>The air pressure (hPa)</returns>
        private double calculateAirPressure(double localAltitude)
        {
            const double standardAtmosphericTemperature = 288.15;      // (K)
            const double standardAtmosphericPressure = 101325.0;       // (Pa)
            const double StandardTemperatureLapseRate = 0.0065;        // (K/m)
            const double standardGravitationalAcceleration = 9.80665;  // (m/s)
            const double standardAtmosphericMolarMass = 0.0289644;     // (kg/mol)
            const double universalGasConstant = 8.3144598;             // (J/mol/K)
            double result;
            result = standardAtmosphericPressure * Math.Pow(1.0 - localAltitude * StandardTemperatureLapseRate / standardAtmosphericTemperature,
                     standardGravitationalAcceleration * standardAtmosphericMolarMass / (universalGasConstant * StandardTemperatureLapseRate));
            return result / 100.0;  // to hPa
        }

        /// <summary>Computes the number of days since winter solstice</summary>
        /// <param name="currentDaysSinceSolstice">The current number of days since solstice</param>
        /// <returns>Updated number of days since winter solstice</returns>
        private int calculateDaysSinceSolstice(int currentDaysSinceSolstice)
        {
            int daysSinceSolstice = 0;
            if (clock.Today.Date == clock.StartDate.Date)
            {
                if (clock.Today.DayOfYear < WinterSolsticeDOY)
                {
                    if (DateTime.IsLeapYear(clock.Today.Year - 1))
                        daysSinceSolstice = 366 - WinterSolsticeDOY + clock.Today.DayOfYear;
                    else
                        daysSinceSolstice = 365 - WinterSolsticeDOY + clock.Today.DayOfYear;
                }
                else
                    DaysSinceWinterSolstice = clock.Today.DayOfYear - WinterSolsticeDOY;
            }
            else
            {
                if (clock.Today.DayOfYear == WinterSolsticeDOY)
                    daysSinceSolstice = 0;
                else
                    daysSinceSolstice = currentDaysSinceSolstice + 1;
            }

            return daysSinceSolstice;
        }

        /// <summary>Read a user-defined variable from today's weather data</summary>
        /// <param name="columnName">Name of the column/variable to retrieve</param>
        public double GetValue(string columnName)
        {
            int columnIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, columnName);
            if (columnIndex == -1)
                throw new InvalidOperationException($"Column {columnName} does not exist in {FileName}");
            return Convert.ToDouble(TodaysMetData.Raw[columnIndex], CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Calculates the values for the constants Tav and Amp for this weather file
        /// and store the values into the reader constants list.
        /// </summary>
        private void calculateTAVAMP()
        {
            double tav = 0;
            double amp = 0;

            // do the calculations
            processMonthlyTAVAMP(out tav, out amp);

            if (reader.Constant("tav") == null)
                reader.AddConstant("tav", tav.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty); // add a new constant
            else
                reader.SetConstant("tav", tav.ToString(CultureInfo.InvariantCulture));

            if (reader.Constant("amp") == null)
                reader.AddConstant("amp", amp.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty); // add a new constant
            else
                reader.SetConstant("amp", amp.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Calculates the values for the constants Tav and Amp for this weather file
        /// </summary>
        /// <param name="tav">The calculated tav value</param>
        /// <param name="amp">The calculated amp value</param>
        private void processMonthlyTAVAMP(out double tav, out double amp)
        {
            long savedPosition = reader.GetCurrentPosition();

            // init return values
            tav = 0;
            amp = 0;
            double maxt, mint;

            // get dataset size
            DateTime start = reader.FirstDate;
            DateTime last = reader.LastDate;
            int nyears = last.Year - start.Year + 1;

            // temp storage arrays
            double[,] monthlyMeans = new double[12, nyears];
            double[,] monthlySums = new double[12, nyears];
            int[,] monthlyDays = new int[12, nyears];

            reader.SeekToDate(start); // goto start of data set

            // read the daily data from the met file
            object[] values;
            DateTime curDate;
            int curMonth = 0;
            bool moreData = true;
            while (moreData)
            {
                values = reader.GetNextLineOfData();
                curDate = reader.GetDateFromValues(values);
                int yearIndex = curDate.Year - start.Year;
                maxt = Convert.ToDouble(values[maximumTemperatureIndex], System.Globalization.CultureInfo.InvariantCulture);
                mint = Convert.ToDouble(values[minimumTemperatureIndex], System.Globalization.CultureInfo.InvariantCulture);

                // accumulate the daily mean for each month
                if (curMonth != curDate.Month)
                {
                    // if next month then
                    curMonth = curDate.Month;
                    monthlySums[curMonth - 1, yearIndex] = 0;    // initialise the total
                }

                monthlySums[curMonth - 1, yearIndex] = monthlySums[curMonth - 1, yearIndex] + ((maxt + mint) * 0.5);
                monthlyDays[curMonth - 1, yearIndex]++;

                if (curDate >= last)
                    moreData = false;
            }

            // do more summary calculations
            double sumOfMeans;
            double maxMean, minMean;
            double yearlySumMeans = 0;
            double yearlySumAmp = 0;
            for (int y = 0; y < nyears; y++)
            {
                maxMean = double.MinValue;
                minMean = double.MaxValue;
                sumOfMeans = 0;
                for (int m = 0; m < 12; m++)
                {
                    monthlyMeans[m, y] = monthlySums[m, y] / monthlyDays[m, y];  // calc monthly mean
                    if (monthlyDays[m, y] != 0)
                    {
                        sumOfMeans += monthlyMeans[m, y];
                        maxMean = Math.Max(monthlyMeans[m, y], maxMean);
                        minMean = Math.Min(monthlyMeans[m, y], minMean);
                    }
                }

                if (maxMean != double.MinValue && minMean != double.MaxValue)
                {
                    yearlySumMeans += sumOfMeans / 12.0;        // accum the ave of monthly means
                    yearlySumAmp += maxMean - minMean;          // accum the amp of means
                }
            }

            tav = yearlySumMeans / nyears;  // calc the ave of the yearly ave means
            amp = yearlySumAmp / nyears;    // calc the ave of the yearly amps

            reader.SeekToPosition(savedPosition);
        }

        /// <summary>Checks the weather data to ensure values are valid/sensible</summary>
        /// <remarks>
        /// This will send an error message if:
        ///  - MinT is less than MaxT
        ///  - Radn is greater than 0.0 or greater than 40.0
        ///  - Rain is less than 0.0
        ///  - VP is less or equal to 0.0
        /// Also checks that every day has weather
        /// </remarks>
        /// <param name="clock">The clock</param>
        /// <param name="weatherToday">The weather</param>
        private void sensibilityCheck(Clock clock, Weather weatherToday)
        {
            if (weatherToday.MinT > weatherToday.MaxT)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has higher minimum temperature (" + weatherToday.MinT + ") than maximum (" + weatherToday.MaxT + ")", MessageType.Warning);
            }
            if (weatherToday.Radn < 0)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has negative solar radiation (" + weatherToday.Radn + ")", MessageType.Warning);
            }
            if (weatherToday.Radn > 40)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has solar radiation (" + weatherToday.Radn + ") which is above 40", MessageType.Warning);
            }
            if (weatherToday.Rain < 0)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has negative rainfall (" + weatherToday.Radn + ")", MessageType.Warning);
            }
            if (!double.IsNaN(weatherToday.VP) && weatherToday.VP <= 0)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has vapour pressure (" + weatherToday.VP + ") which is below 0", MessageType.Warning);
            }

            return;
        }

        /// <summary>Returns the weather data for a date defined by an offset from today</summary>
        /// <remarks>
        /// Will return today's data if the offset given would make the date sit outside those
        /// available in the weather file
        /// </remarks>
        /// <param name="offset">The number of days away from today</param>
        private DailyMetDataFromFile GetAdjacentMetData(int offset)
        {
            if ((clock.Today.Equals(reader.FirstDate) && offset < 0) ||
                clock.Today.Equals(reader.LastDate) && offset > 0)
            {
                summary.WriteMessage(this, "Warning: Weather on " + clock.Today.AddDays(offset).ToString("d") + " does not exist. Today's weather on " + clock.Today.ToString("d") + " was used instead.", MessageType.Warning);
                return GetMetData(clock.Today);
            }
            else
                return GetMetData(clock.Today.AddDays(offset));
        }
    }
}
