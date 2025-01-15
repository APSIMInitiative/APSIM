using APSIM.Shared.Utilities;
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
    /// Reads in weather data and makes it available to other models.
    ///</summary>
    [Serializable]
    [ViewName("UserInterface.Views.TabbedMetDataView")]
    [PresenterName("UserInterface.Presenters.MetDataPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    [ValidParent(ParentType = typeof(Zone))]
    public class Weather : Model, IWeather, IReferenceExternalFiles
    {
        /// <summary>
        /// A link to the clock model.
        /// </summary>
        [Link]
        private IClock clock = null;

        /// <summary>
        /// A link to the the summary
        /// </summary>
        [Link]
        private ISummary summary = null;

        /// <summary>
        /// A reference to the text file reader object
        /// </summary>
        [NonSerialized]
        private ApsimTextFile reader = null;

        /// <summary>
        /// The index of the maximum temperature column in the weather file
        /// </summary>
        private int maximumTemperatureIndex;

        /// <summary>
        /// The index of the minimum temperature column in the weather file
        /// </summary>
        private int minimumTemperatureIndex;

        /// <summary>
        /// The index of the solar radiation column in the weather file
        /// </summary>
        private int radiationIndex;

        /// <summary>
        /// The index of the rainfall column in the weather file
        /// </summary>
        private int rainIndex;

        /// <summary>
        /// The index of the evaporation column in the weather file
        /// </summary>
        private int evaporationIndex;

        /// <summary>
        /// The index of the evaporation column in the weather file
        /// </summary>
        private int rainfallHoursIndex;

        /// <summary>
        /// The index of the vapor pressure column in the weather file
        /// </summary>
        private int vapourPressureIndex;

        /// <summary>
        /// The index of the wind column in the weather file
        /// </summary>
        private int windIndex;

        /// <summary>
        /// The index of the co2 column in the weather file, or -1
        /// if the weather file doesn't contain co2.
        /// </summary>
        private int co2Index;

        /// <summary>
        /// The index of the DiffuseFraction column in the weather file
        /// </summary>
        private int DiffuseFractionIndex;

        /// <summary>
        /// The index of the DayLength column in the weather file
        /// </summary>
        private int dayLengthIndex;

        /// <summary>
        /// This event will be invoked immediately before models get their weather data.
        /// models and scripts an opportunity to change the weather data before other models
        /// reads it.
        /// </summary>
        public event EventHandler PreparingNewWeatherData;

        /// <summary>
        /// Optional constants file name. This should only be accessed via
        /// <see cref="ConstantsFile" />, which handles conversion between
        /// relative/absolute paths.
        /// </summary>
        private string constantsFile;

        /// <summary>
        /// A LinkedList of weather that has previously been read.
        /// Stored in order of date from newest to oldest as most recent weather is most common to search for
        /// </summary>
        private LinkedList<WeatherRecordEntry> weatherCache = new LinkedList<WeatherRecordEntry>();

        /// <summary>
        /// Stores the CO2 value from either the default WeatherConstants.DEFAULT_CO2 or from a column in met file. Public property can then also check
        /// if this value was supplied by a constant
        /// </summary>
        [JsonIgnore]
        private double co2Value { get; set; }

        /// <summary>
        /// Allows to specify a second file which contains constants such as lat, long,
        /// tav, amp, etc. Really only used when the actual met data is in a .csv file.
        /// </summary>
        [Description("Constants file")]
        public string ConstantsFile
        {
            get
            {
                Simulation simulation = FindAncestor<Simulation>();
                if (simulation != null)
                    return PathUtilities.GetAbsolutePath(this.constantsFile, simulation.FileName);
                else
                {
                    Simulations simulations = FindAncestor<Simulations>();
                    if (simulations != null)
                        return PathUtilities.GetAbsolutePath(this.constantsFile, simulations.FileName);
                    else
                        return this.constantsFile;
                }
            }
            set
            {
                Simulations simulations = FindAncestor<Simulations>();
                if (simulations != null)
                    this.constantsFile = PathUtilities.GetRelativePath(value, simulations.FileName);
                else
                    this.constantsFile = value;
            }
        }

        /// <summary>
        /// Gets or sets the file name. Should be relative filename where possible.
        /// </summary>
        [Summary]
        [Description("Weather file name")]
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the full file name (with path). The user interface uses this.
        /// </summary>
        [JsonIgnore]
        public string FullFileName
        {
            get
            {
                Simulation simulation = FindAncestor<Simulation>();
                if (simulation != null && simulation.FileName != null)
                    return PathUtilities.GetAbsolutePath(this.FileName, simulation.FileName);
                else
                {
                    Simulations simulations = FindAncestor<Simulations>();
                    if (simulations != null)
                        return PathUtilities.GetAbsolutePath(this.FileName, simulations.FileName);
                    else
                        return PathUtilities.GetAbsolutePath(this.FileName, "");
                }
            }
            set
            {
                Simulations simulations = FindAncestor<Simulations>();
                if (simulations != null)
                    this.FileName = PathUtilities.GetRelativePath(value, simulations.FileName);
                else
                    this.FileName = value;
            }
        }

        /// <summary>
        /// Used to hold the WorkSheet Name if data retrieved from an Excel file
        /// </summary>
        public string ExcelWorkSheetName { get; set; }

        /// <summary>
        /// Gets the start date of the weather file
        /// </summary>
        public DateTime StartDate
        {
            get
            {
                if (this.reader == null && !this.OpenDataFile())
                    return new DateTime(0);

                return this.reader.FirstDate;
            }
        }

        /// <summary>
        /// Gets the end date of the weather file
        /// </summary>
        public DateTime EndDate
        {
            get
            {
                if (this.reader == null && !this.OpenDataFile())
                    return new DateTime(0);

                return this.reader.LastDate;
            }
        }

        /// <summary>
        /// Gets or sets the maximum temperature (oC)
        /// </summary>
        [Units("°C")]
        [JsonIgnore]
        public double MaxT { get; set; }

        /// <summary>
        /// Gets or sets the minimum temperature (oC)
        /// </summary>
        [JsonIgnore]
        [Units("°C")]
        public double MinT { get; set; }

        /// <summary>
        /// Daily Mean temperature (oC)
        /// </summary>
        [Units("°C")]
        [JsonIgnore]
        public double MeanT { get { return (MaxT + MinT) / 2; } }

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
        /// days since winter solstice (day)
        /// </summary>
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

        private bool First = true;
        /// <summary>
        /// Number of days lapsed since the winter solstice
        /// </summary>
        [Units("d")]
        [JsonIgnore]
        public int DaysSinceWinterSolstice { get; set; }

        /// <summary>
        /// Maximum clear sky radiation (MJ/m2)
        /// </summary>
        [Units("MJ/M2")]
        [JsonIgnore]
        public double Qmax { get; set; }

        /// <summary>
        /// Gets or sets the rainfall (mm)
        /// </summary>
        [Units("mm")]
        [JsonIgnore]
        public double Rain { get; set; }

        /// <summary>
        /// Gets or sets the solar radiation. MJ/m2/day
        /// </summary>
        [Units("MJ/m^2/d")]
        [JsonIgnore]
        public double Radn { get; set; }

        /// <summary>
        /// Gets or sets the Pan Evaporation (mm) (Class A pan)
        /// </summary>
        [Units("mm")]
        [JsonIgnore]
        public double PanEvap { get; set; }

        /// <summary>
        /// Gets or sets the number of hours rainfall occured in
        /// </summary>
        [JsonIgnore]
        public double RainfallHours { get; set; }

        /// <summary>
        /// Gets or sets the vapor pressure (hPa)
        /// </summary>
        [Units("hPa")]
        [JsonIgnore]
        public double VP { get; set; }

        /// <summary>
        /// Gets or sets the wind value found in weather file or zero if not specified. (code says 3.0 not zero)
        /// </summary>
        [JsonIgnore]
        public double Wind { get; set; }

        /// <summary>
        /// Gets or sets the DF value found in weather file or zero if not specified
        /// </summary>
        [Units("0-1")]
        [JsonIgnore]
        public double DiffuseFraction { get; set; }

        /// <summary>
        /// Gets or sets the Daylength value found in weather file or zero if not specified
        /// </summary>
        [JsonIgnore]
        public double DayLength { get; set; }

        /// <summary>
        /// Gets or sets the CO2 level. If not specified in the weather file the default is WeatherConstants.DEFAULT_CO2.
        /// </summary>
        [JsonIgnore]
        public double CO2 {
            get
            {
                if (this.reader == null || this.reader.Constant("co2") == null)
                    return co2Value;
                else
                    return this.reader.ConstantAsDouble("co2");
            }
            set
            {
                co2Value = value;
            }
        }

        /// <summary>
        /// Gets or sets the atmospheric air pressure. If not specified in the weather file the default is 1010 hPa.
        /// </summary>
        [Units("hPa")]
        [JsonIgnore]
        public double AirPressure { get; set; }

        /// <summary>
        /// Gets the latitude
        /// </summary>
        public double Latitude
        {
            get
            {
                if (this.reader == null && !this.OpenDataFile())
                    return 0;

                return this.reader.ConstantAsDouble("Latitude");
            }
            set
            {
                if (this.reader != null)
                    reader.Constant("Latitude").Value = value.ToString();
            }
        }

        /// <summary>
        /// Gets the longitude
        /// </summary>
        public double Longitude
        {
            get
            {
                if (this.reader == null || this.reader.Constant("Longitude") == null)
                    return 0;
                else
                    return this.reader.ConstantAsDouble("Longitude");
            }
        }

        /// <summary>
        /// Gets the average temperature
        /// </summary>
        [Units("°C")]
        public double Tav
        {
            get
            {
                if (this.reader == null)
                    return 0;
                else if (this.reader.Constant("tav") == null)
                    this.CalcTAVAMP();

                return this.reader.ConstantAsDouble("tav");
            }
            set
            {
                if (this.reader != null)
                    reader.Constant("tav").Value = value.ToString();
            }
        }

        /// <summary>
        /// Gets the temperature amplitude.
        /// </summary>
        public double Amp
        {
            get
            {
                if (this.reader == null)
                    return 0;
                else if (this.reader.Constant("amp") == null)
                    this.CalcTAVAMP();

                return this.reader.ConstantAsDouble("amp");
            }
            set
            {
                if (this.reader != null)
                    reader.Constant("amp").Value = value.ToString();
            }
        }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile YesterdaysMetData { get { return GetAdjacentMetData(-1); } }

        /// <summary>Met Data for Today</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TodaysMetData { get; set; }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TomorrowsMetData { get { return GetAdjacentMetData(1); } }

        /// <summary>First date of summer.</summary>
        [JsonIgnore]
        public string FirstDateOfSummer { get; set; } = "1-dec";

        /// <summary>First date of autumn / fall.</summary>
        [JsonIgnore]
        public string FirstDateOfAutumn { get; set; } = "1-mar";

        /// <summary>First date of winter.</summary>
        [JsonIgnore]
        public string FirstDateOfWinter { get; set; } = "1-jun";

        /// <summary>First date of spring.</summary>
        [JsonIgnore]
        public string FirstDateOfSpring { get; set; } = "1-sep";

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

        /// <summary>Start of spring event.</summary>
        public event EventHandler StartOfSpring;

        /// <summary>Start of summer event.</summary>
        public event EventHandler StartOfSummer;

        /// <summary>Start of autumn/fall event.</summary>
        public event EventHandler StartOfAutumn;

        /// <summary>Start of winter event.</summary>
        public event EventHandler StartOfWinter;

        /// <summary>End of spring event.</summary>
        public event EventHandler EndOfSpring;

        /// <summary>End of summer event.</summary>
        public event EventHandler EndOfSummer;

        /// <summary>End of autumn/fall event.</summary>
        public event EventHandler EndOfAutumn;

        /// <summary>End of winter event.</summary>
        public event EventHandler EndOfWinter;

        /// <summary>Name of current season.</summary>
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

        /// <summary>Return our input filenames</summary>
        public IEnumerable<string> GetReferencedFileNames()
        {
            return new string[] { FullFileName };
        }

        /// <summary>Remove all paths from referenced filenames.</summary>
        public void RemovePathsFromReferencedFileNames()
        {
            FileName = Path.GetFileName(FileName);
        }

        /// <summary>
        /// Gets the duration of the day in hours.
        /// </summary>
        public double CalculateDayLength(double Twilight)
        {
            if (dayLengthIndex == -1 && DayLength == -1)  // daylength is not set as column or constant
                return MathUtilities.DayLength(this.clock.Today.DayOfYear, Twilight, this.Latitude);
            else
                return this.DayLength;
        }

        /// <summary> calculate the time of sun rise</summary>
        /// <returns>Sun rise time</returns>
        public double CalculateSunRise()
        {
            return 12 - CalculateDayLength(-6) / 2;
        }

        /// <summary> calculate the time of sun set</summary>
        /// <returns>Sun set time</returns>
        public double CalculateSunSet()
        {
            return 12 + CalculateDayLength(-6) / 2;
        }

        /// <summary>
        /// Check values in weather and return a collection of warnings.
        /// </summary>
        public IEnumerable<string> Validate()
        {
            if (Amp > 20)
            {
                yield return $"The value of Weather.AMP ({Amp}) is > 20 oC. Please check the value.";
            }
        }

        /// <summary>
        /// Overrides the base class method to allow for initialization.
        /// </summary>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            this.maximumTemperatureIndex = 0;
            this.minimumTemperatureIndex = 0;
            this.radiationIndex = 0;
            this.rainIndex = 0;
            this.evaporationIndex = 0;
            this.rainfallHoursIndex = 0;
            this.vapourPressureIndex = 0;
            this.windIndex = 0;
            this.co2Index = -1;
            this.DiffuseFractionIndex = 0;
            this.dayLengthIndex = 0;
            if (AirPressure == 0)
                this.AirPressure = 1010;
            if (DiffuseFraction == 0)
                this.DiffuseFraction = -1;
            if (reader != null)
            {
                reader.Close();
                reader = null;
            }

            if (Latitude > 0)
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

            foreach (var message in Validate())
                summary.WriteMessage(this, message, MessageType.Warning);
        }

        /// <summary>
        /// Perform the necessary initialisation at the start of simulation.
        /// </summary>
        [EventSubscribe("StartOfSimulation")]
        private void OnStartOfSimulation(object sender, EventArgs e)
        {
            First = true;
        }

        /// <summary>
        /// Overrides the base class method to allow for clean up
        /// </summary>
        [EventSubscribe("Completed")]
        private void OnSimulationCompleted(object sender, EventArgs e)
        {
            if (this.reader != null)
                this.reader.Close();
            this.reader = null;
        }

        /// <summary>
        /// Get the DataTable view of this data
        /// </summary>
        /// <returns>The DataTable</returns>
        public DataTable GetAllData()
        {
            this.reader = null;

            if (this.OpenDataFile())
            {
                List<string> metProps = new List<string>();
                metProps.Add("mint");
                metProps.Add("maxt");
                metProps.Add("radn");
                metProps.Add("rain");
                metProps.Add("wind");
                metProps.Add("diffr");

                return this.reader.ToTable(metProps);
            }
            else
                return null;
        }

        /// <summary>
        /// An event handler for the daily DoWeather event.
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("DoWeather")]
        private void OnDoWeather(object sender, EventArgs e)
        {
            TodaysMetData = GetMetData(this.clock.Today); //Read first date to get todays data

            this.Radn = TodaysMetData.Radn;
            this.MaxT = TodaysMetData.MaxT;
            this.MinT = TodaysMetData.MinT;
            this.Rain = TodaysMetData.Rain;
            this.PanEvap = TodaysMetData.PanEvap;
            this.RainfallHours = TodaysMetData.RainfallHours;
            this.VP = TodaysMetData.VP;
            this.Wind = TodaysMetData.Wind;
            this.DiffuseFraction = TodaysMetData.DiffuseFraction;
            this.DayLength = TodaysMetData.DayLength;
            this.CO2 = TodaysMetData.CO2;

            if (this.PreparingNewWeatherData != null)
                this.PreparingNewWeatherData.Invoke(this, new EventArgs());

            if (First)
            {
                //StartDAWS = met.DaysSinceWinterSolstice;
                if (clock.Today.DayOfYear < WinterSolsticeDOY)
                {
                    if (DateTime.IsLeapYear(clock.Today.Year - 1))
                        DaysSinceWinterSolstice = 366 - WinterSolsticeDOY + clock.Today.DayOfYear;
                    else
                        DaysSinceWinterSolstice = 365 - WinterSolsticeDOY + clock.Today.DayOfYear;
                }
                else
                    DaysSinceWinterSolstice = clock.Today.DayOfYear - WinterSolsticeDOY;

                First = false;
            }

            if (clock.Today.DayOfYear == WinterSolsticeDOY & First == false)
                DaysSinceWinterSolstice = 0;
            else DaysSinceWinterSolstice += 1;

            Qmax = MetUtilities.QMax(clock.Today.DayOfYear + 1, Latitude, MetUtilities.Taz, MetUtilities.Alpha, VP);

            //do sanity check on weather
            SensibilityCheck(clock as Clock, this);
        }

        /// <summary>
        /// Method to read one days met data in from file
        /// </summary>
        /// <param name="date">the date to read met data</param>
        public DailyMetDataFromFile GetMetData(DateTime date)
        {
            //check if we've looked at this date before
            WeatherRecordEntry previousEntry = null;
            foreach (WeatherRecordEntry entry in this.weatherCache)
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

            if (this.reader == null)
                if (!this.OpenDataFile())
                    throw new ApsimXException(this, "Cannot find weather file '" + this.FileName + "'");

            //get weather for that date
            DailyMetDataFromFile readMetData = new DailyMetDataFromFile();
            try
            {
                this.reader.SeekToDate(date);
                readMetData.Raw = reader.GetNextLineOfData();
            }
            catch (IndexOutOfRangeException err)
            {
                throw new Exception($"Unable to retrieve weather data on {date.ToString("yyy-MM-dd")} in file {FileName}", err);
            }

            if (date != this.reader.GetDateFromValues(readMetData.Raw))
                throw new Exception("Non consecutive dates found in file: " + this.FileName + ".");

            //since this data was valid, store in our cache for next time

            WeatherRecordEntry record = new WeatherRecordEntry();
            record.Date = date;
            record.MetData = readMetData;
            if (previousEntry != null)
                this.weatherCache.AddBefore(this.weatherCache.Find(previousEntry), record);
            else
                this.weatherCache.AddFirst(record);


            return CheckDailyMetData(readMetData);
        }

        private DailyMetDataFromFile CheckDailyMetData(DailyMetDataFromFile readMetData)
        {

            if (this.radiationIndex != -1)
                readMetData.Radn = Convert.ToSingle(readMetData.Raw[this.radiationIndex], CultureInfo.InvariantCulture);
            else
                readMetData.Radn = this.reader.ConstantAsDouble("radn");

            if (this.maximumTemperatureIndex != -1)
                readMetData.MaxT = Convert.ToSingle(readMetData.Raw[this.maximumTemperatureIndex], CultureInfo.InvariantCulture);
            else
                readMetData.MaxT = this.reader.ConstantAsDouble("maxt");

            if (this.minimumTemperatureIndex != -1)
                readMetData.MinT = Convert.ToSingle(readMetData.Raw[this.minimumTemperatureIndex], CultureInfo.InvariantCulture);
            else
                readMetData.MinT = this.reader.ConstantAsDouble("mint");

            if (this.rainIndex != -1)
                readMetData.Rain = Convert.ToSingle(readMetData.Raw[this.rainIndex], CultureInfo.InvariantCulture);
            else
                readMetData.Rain = this.reader.ConstantAsDouble("rain");

            if (this.evaporationIndex == -1)
                readMetData.PanEvap = double.NaN;
            else
                readMetData.PanEvap = Convert.ToSingle(readMetData.Raw[this.evaporationIndex], CultureInfo.InvariantCulture);

            if (this.rainfallHoursIndex == -1)
                readMetData.RainfallHours = double.NaN;
            else
                readMetData.RainfallHours = Convert.ToSingle(readMetData.Raw[this.rainfallHoursIndex], CultureInfo.InvariantCulture);

            if (this.vapourPressureIndex == -1)
                readMetData.VP = Math.Max(0, MetUtilities.svp(readMetData.MinT));
            else
                readMetData.VP = Convert.ToSingle(readMetData.Raw[this.vapourPressureIndex], CultureInfo.InvariantCulture);

            if (this.windIndex == -1)
                readMetData.Wind = 3.0;
            else
                readMetData.Wind = Convert.ToSingle(readMetData.Raw[this.windIndex], CultureInfo.InvariantCulture);

            if (co2Index == -1)
                readMetData.CO2 = WeatherConstants.DEFAULT_CO2;
            else
                readMetData.CO2 = Convert.ToDouble(readMetData.Raw[co2Index], CultureInfo.InvariantCulture);

            if (this.DiffuseFractionIndex == -1)
            {
                // Estimate Diffuse Fraction using the Approach of Bristow and Campbell
                double Qmax = MetUtilities.QMax(clock.Today.DayOfYear + 1, Latitude, MetUtilities.Taz, MetUtilities.Alpha, 0.0); // Radiation for clear and dry sky (ie low humidity)
                double Q0 = MetUtilities.Q0(clock.Today.DayOfYear + 1, Latitude);
                double B = Qmax / Q0;
                double Tt = MathUtilities.Bound(readMetData.Radn / Q0, 0, 1);
                if (Tt > B) Tt = B;
                readMetData.DiffuseFraction = (1 - Math.Exp(0.6 * (1 - B / Tt) / (B - 0.4)));
                if (Tt > 0.5 && readMetData.DiffuseFraction < 0.1) readMetData.DiffuseFraction = 0.1;
            }
            else
                readMetData.DiffuseFraction = Convert.ToSingle(readMetData.Raw[this.DiffuseFractionIndex], CultureInfo.InvariantCulture);

            if (this.dayLengthIndex == -1)  // Daylength is not a column - check for a constant
            {
                if (this.reader.Constant("daylength") != null)
                    readMetData.DayLength = this.reader.ConstantAsDouble("daylength");
                else
                    readMetData.DayLength = -1;
            }
            else
                readMetData.DayLength = Convert.ToSingle(readMetData.Raw[this.dayLengthIndex], CultureInfo.InvariantCulture);

            return readMetData;
        }

        /// <summary>
        /// An event handler for the start of day event.
        /// </summary>
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

        /// <summary>
        /// An event handler for the end of day event.
        /// </summary>
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

        /// <summary>
        /// Open the weather data file.
        /// </summary>
        /// <returns>True if the file was successfully opened</returns>
        public bool OpenDataFile()
        {
            if (!System.IO.File.Exists(this.FullFileName) &&
                System.IO.Path.GetExtension(FullFileName) == string.Empty)
                FileName += ".met";

            if (System.IO.File.Exists(this.FullFileName))
            {
                if (this.reader == null)
                {
                    if (ExcelUtilities.IsExcelFile(FullFileName) && string.IsNullOrEmpty(ExcelWorkSheetName))
                        throw new Exception($"Unable to open excel file {FullFileName}: no sheet name is specified");

                    this.reader = new ApsimTextFile();
                    this.reader.Open(this.FullFileName, this.ExcelWorkSheetName);

                    if (this.reader.Headings == null)
                    {
                        string message = "Cannot find the expected header in ";
                        if (ExcelUtilities.IsExcelFile(FullFileName))
                            message += $"sheet '{ExcelWorkSheetName}' of ";
                        message += $"weather file: {FullFileName}";
                        throw new Exception(message);
                    }

                    this.maximumTemperatureIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "Maxt");
                    this.minimumTemperatureIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "Mint");
                    this.radiationIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "Radn");
                    this.rainIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "Rain");
                    this.evaporationIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "Evap");
                    this.rainfallHoursIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "RainHours");
                    this.vapourPressureIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "VP");
                    this.windIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "Wind");
                    this.DiffuseFractionIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "DifFr");
                    this.dayLengthIndex = StringUtilities.IndexOfCaseInsensitive(this.reader.Headings, "DayLength");
                    this.co2Index = StringUtilities.IndexOfCaseInsensitive(reader.Headings, "CO2");

                    if (!string.IsNullOrEmpty(ConstantsFile))
                    {
                        ApsimTextFile constantsReader = new ApsimTextFile();
                        constantsReader.Open(ConstantsFile);
                        if (constantsReader.Constants != null)
                            foreach (ApsimConstant constant in constantsReader.Constants)
                                this.reader.AddConstant(constant.Name, constant.Value, constant.Units, constant.Comment);
                    }

                    if (this.maximumTemperatureIndex == -1)
                        if (this.reader == null || this.reader.Constant("maxt") == null)
                            throw new Exception("Cannot find MaxT in weather file: " + this.FullFileName);

                    if (this.minimumTemperatureIndex == -1)
                        if (this.reader == null || this.reader.Constant("mint") == null)
                            throw new Exception("Cannot find MinT in weather file: " + this.FullFileName);

                    if (this.radiationIndex == -1)
                        if (this.reader == null || this.reader.Constant("radn") == null)
                            throw new Exception("Cannot find Radn in weather file: " + this.FullFileName);

                    if (this.rainIndex == -1)
                        if (this.reader == null || this.reader.Constant("rain") == null)
                            throw new Exception("Cannot find Rain in weather file: " + this.FullFileName);
                }
                else
                {
                    if (this.reader.IsExcelFile != true)
                        this.reader.SeekToDate(this.reader.FirstDate);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>Close the datafile.</summary>
        public void CloseDataFile()
        {
            if (reader != null)
                reader.Close();
            reader = null;
        }

        /// <summary>
        /// Read a user-defined value from today's weather data.
        /// </summary>
        /// <param name="columnName">Name of the column.</param>
        public double GetValue(string columnName)
        {
            int columnIndex = StringUtilities.IndexOfCaseInsensitive(reader.Headings, columnName);
            if (columnIndex == -1)
                throw new InvalidOperationException($"Column {columnName} does not exist in {FileName}");
            return Convert.ToDouble(TodaysMetData.Raw[columnIndex], CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Calculate the amp and tav 'constant' values for this weather file
        /// and store the values into the File constants.
        /// </summary>
        private void CalcTAVAMP()
        {
            double tav = 0;
            double amp = 0;

            // do the calculations
            this.ProcessMonthlyTAVAMP(out tav, out amp);

            if (this.reader.Constant("tav") == null)
                this.reader.AddConstant("tav", tav.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty); // add a new constant
            else
                this.reader.SetConstant("tav", tav.ToString(CultureInfo.InvariantCulture));

            if (this.reader.Constant("amp") == null)
                this.reader.AddConstant("amp", amp.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty); // add a new constant
            else
                this.reader.SetConstant("amp", amp.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Calculate the amp and tav 'constant' values for this weather file.
        /// </summary>
        /// <param name="tav">The calculated tav value</param>
        /// <param name="amp">The calculated amp value</param>
        private void ProcessMonthlyTAVAMP(out double tav, out double amp)
        {
            long savedPosition = reader.GetCurrentPosition();

            // init return values
            tav = 0;
            amp = 0;
            double maxt, mint;

            // get dataset size
            DateTime start = this.reader.FirstDate;
            DateTime last = this.reader.LastDate;
            int nyears = last.Year - start.Year + 1;

            // temp storage arrays
            double[,] monthlyMeans = new double[12, nyears];
            double[,] monthlySums = new double[12, nyears];
            int[,] monthlyDays = new int[12, nyears];

            this.reader.SeekToDate(start); // goto start of data set

            // read the daily data from the met file
            object[] values;
            DateTime curDate;
            int curMonth = 0;
            bool moreData = true;
            while (moreData)
            {
                values = this.reader.GetNextLineOfData();
                curDate = this.reader.GetDateFromValues(values);
                int yearIndex = curDate.Year - start.Year;
                maxt = Convert.ToDouble(values[this.maximumTemperatureIndex], System.Globalization.CultureInfo.InvariantCulture);
                mint = Convert.ToDouble(values[this.minimumTemperatureIndex], System.Globalization.CultureInfo.InvariantCulture);

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

        /// <summary>
        /// Does a santiy check on this weather data to check that temperatures,
        /// VP, radition and rain are potentially valid numbers.
        /// Also checks that every day has weather.
        /// </summary>
        /// <param name="clock">The clock</param>
        /// <param name="weatherToday">The weather</param>
        private void SensibilityCheck(Clock clock, Weather weatherToday)
        {
            //things to check:
            //Mint > MaxtT
            //VP(if present) <= 0
            //Radn < 0 or Radn > 40
            //Rain < 0
            if (weatherToday.MinT > weatherToday.MaxT)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has higher minimum temperature (" + weatherToday.MinT + ") than maximum (" + weatherToday.MaxT + ")", MessageType.Warning);
            }
            if (weatherToday.VP <= 0)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has Vapor Pressure (" + weatherToday.VP + ") which is below 0", MessageType.Warning);
            }
            if (weatherToday.Radn < 0)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has negative solar raditation (" + weatherToday.Radn + ")", MessageType.Warning);
            }
            if (weatherToday.Radn > 40)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has solar raditation (" + weatherToday.Radn + ") which is above 40", MessageType.Warning);
            }
            if (weatherToday.Rain < 0)
            {
                summary.WriteMessage(weatherToday, "Error: Weather on " + clock.Today.ToString() + " has negative ranfaill (" + weatherToday.Radn + ")", MessageType.Warning);
            }
            return;
        }

        /// <summary>
        /// Returns an adjacent day's weather data as defined by the offset.
        /// Will return today's data if yesterday is not a valid entry in the weather file.
        /// </summary>
        /// <param name="offset">The number of days away from today</param>
        private DailyMetDataFromFile GetAdjacentMetData(int offset)
        {
            if ((this.clock.Today.Equals(this.reader.FirstDate) && offset < 0) ||
                this.clock.Today.Equals(this.reader.LastDate) && offset > 0)
            {
                //in the case that we try to get yesterdays/tomorrows weather and today is the same as the start or end of the weather file
                //we should instead return today's weather
                summary.WriteMessage(this, "Warning: Weather on " + this.clock.Today.AddDays(offset).ToString("d") + " does not exist. Today's weather on " + this.clock.Today.ToString("d") + " was used instead.", MessageType.Warning);
                return GetMetData(this.clock.Today);
            }
            else
                return GetMetData(this.clock.Today.AddDays(offset));
        }
    }
}