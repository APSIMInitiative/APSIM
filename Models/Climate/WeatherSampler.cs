using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Interfaces;
using Newtonsoft.Json;

namespace Models.Climate
{
    /// <summary>
    /// Generates random sampling for whole years of weather data from a weather file
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    [Description("This model samples from a weather file taking slices of data of one or more years at a time in order to preserve seasonal patterns. 'Year' means 12 months rather than a calendar year\r\n \r\n" +
                 "The start of a year is taken from Clock.StartDate using dd-mmm. The duration of the simulation is set using the difference between StartDate and EndDate. \r\n  \r\n" +
                 "Options with random sampling can be with a random seed or with a specified seed. Provide a seed for repeatable simulation results. \r\n \r\n" +
                 "There are three sampling methods.\r\n" +
                 "1. RandomSample - whole years of weather data will be sampled randomly and independently until the duration specified in Clock has been met.\r\n" +
                 "2. SpecificYears - specific years can be specified. Weather data will be taken from these years in the order specified. Once all years have been sampled, the model will cycle back to the first year until the duration specified in Clock has been met.\r\n" +
                 "3. RandomChooseFirstYear - allows multi-year slices of weather data to be sampled. It will randomly choose a start year in the weather record and continue from that date until the duration specified in Clock has been met.")]

    public class WeatherSampler : Model, IWeather
    {
        /// <summary>
        /// A link to the clock model
        /// </summary>
        [Link]
        private IClock clock = null;

        /// <summary>
        /// A link to access the simulation level models
        /// </summary>
        [Link]
        private Simulation simulation = null;

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

        /// <summary>A data table of all weather data</summary>
        private DataTable data;

        /// <summary>The current row into the weather data table</summary>
        private int currentRowIndex;

        /// <summary>The current index into the years array</summary>
        private int currentYearIndex;

        /// <summary>The first date in the weather file</summary>
        private DateTime firstDateInFile;

        /// <summary>The last date in the weather file</summary>
        private DateTime lastDateInFile;

        /// <summary>Options defining year sampling type</summary>
        public enum RandomiserTypeEnum
        {
            /// <summary>Specify years manually</summary>
            SpecificYears,

            /// <summary>Fully random sampler</summary>
            RandomSample,

            /// <summary>Random choose the first year to draw weather data from</summary>
            RandomChooseFirstYear
        }

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
        /// Maximum value expected for mean long-term temperature amplitude (oC)
        /// </summary>
        private const double maximumAMP = 25.0;

        /// <summary>
        /// Gets or sets the weather file name. Should be relative file path where possible
        /// </summary>
        [Summary]
        [Description("Weather file to sample from")]
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the full file name (with path)
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

        /// <summary>Choice of year sampling type</summary>
        [Description("Type of sampling")]
        public RandomiserTypeEnum TypeOfSampling { get; set; }

        /// <summary>The seed for sampling years</summary>
        [Summary]
        [Description("Seed to pass to random number generator. Leave blank for fully random")]
        [Display(VisibleCallback = "IsRandomEnabled")]
        public string Seed { get; set; }

        /// <summary>The sample years.</summary>
        [Summary]
        [Description("Years from which to sample the weather file")]
        [Display(VisibleCallback = "IsSpecifyYearsEnabled")]
        public int[] SampleYears { get; set; }

        /// <summary>Flag whether random sampling is enabled</summary>
        public bool IsRandomEnabled { get { return TypeOfSampling == RandomiserTypeEnum.RandomSample || TypeOfSampling == RandomiserTypeEnum.RandomChooseFirstYear; } }

        /// <summary>Flag whether 'specify years' is enabled</summary>
        public bool IsSpecifyYearsEnabled { get { return TypeOfSampling == RandomiserTypeEnum.SpecificYears; } }

        /// <summary>The date marking when years tick over</summary>
        [Summary]
        [Description("Date marking the start of a sampling year (d-mmm). Leave blank for 1-Jan")]
        public string SplitDate { get; set; }

        /// <summary>Gets the start date of the weather file</summary>
        public DateTime StartDate => clock.StartDate;

        /// <summary>Gets the end date of the weather file</summary>
        public DateTime EndDate => clock.EndDate;

        /// <summary>Gets or sets the daily minimum air temperature (oC)</summary>
        [JsonIgnore]
        [Units("oC")]
        public double MinT { get; set; }

        /// <summary>Gets or sets the daily maximum air temperature (oC)</summary>
        [JsonIgnore]
        [Units("oC")]
        public double MaxT { get; set; }

        /// <summary>Gets the daily mean air temperature (oC)</summary>
        [JsonIgnore]
        [Units("oC")]
        public double MeanT { get { return (MaxT + MinT) / 2; } }

        /// <summary>Gets or sets the solar radiation (MJ/m2)</summary>
        [JsonIgnore]
        [Units("MJ/m2")]
        public double Radn { get; set; }

        /// <summary>Gets or sets the maximum clear sky radiation (MJ/m2)</summary>
        [JsonIgnore]
        [Units("MJ/m2")]
        public double Qmax { get; set; }

        /// <summary>Gets or sets the day length, period with light (h)</summary>
        [JsonIgnore]
        [Units("h")]
        public double DayLength { get; set; }

        /// <summary>Gets or sets the diffuse radiation fraction (0-1)</summary>
        [JsonIgnore]
        [Units("0-1")]
        public double DiffuseFraction { get; set; }

        /// <summary>Gets or sets the rainfall amount (mm)</summary>
        [JsonIgnore]
        [Units("mm")]
        public double Rain { get; set; }

        /// <summary>Gets or sets the number duration of rainfall within a day (h)</summary>
        [JsonIgnore]
        [Units("h")]
        public double RainfallHours { get; set; }

        /// <summary>Gets or sets the class A pan evaporation (mm)</summary>
        [JsonIgnore]
        [Units("mm")]
        public double PanEvap { get; set; }

        /// <summary>Gets or sets the air vapour pressure (hPa)</summary>
        [JsonIgnore]
        [Units("hPa")]
        public double VP { get; set; }

        /// <summary>Gets or sets the daily mean vapour pressure deficit (hPa)</summary>
        [JsonIgnore]
        [Units("hPa")]
        public double VPD { get; set; }

        /// <summary>Gets or sets the average wind speed (m/s)</summary>
        [JsonIgnore]
        [Units("m/s")]
        public double Wind { get; set; }

        /// <summary>Gets or sets the CO2 level in the atmosphere (ppm)</summary>
        [JsonIgnore]
        [Units("ppm")]
        public double CO2 { get; set; }

        /// <summary>Gets or sets the mean atmospheric air pressure</summary>
        [JsonIgnore]
        [Units("hPa")]
        public double AirPressure { get; set; }

        /// <summary>Gets or sets the latitude (decimal degrees)</summary>
        [JsonIgnore]
        [Units("degrees")]
        public double Latitude { get; set; }

        /// <summary>Gets or sets the longitude (decimal degrees)</summary>
        [JsonIgnore]
        [Units("degrees")]
        public double Longitude { get; set; }

        /// <summary>Gets the long-term average air temperature (oC)</summary>
        [JsonIgnore]
        [Units("oC")]
        public double Tav { get; set; }

        /// <summary>Gets the long-term average temperature amplitude (oC)</summary>
        [JsonIgnore]
        [Units("oC")]
        public double Amp { get; set; }

        /// <summary>Gets the day for the winter solstice (day)</summary>
        [JsonIgnore]
        [Units("day")]
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
        [JsonIgnore]
        [Units("d")]
        public int DaysSinceWinterSolstice { get; set; }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile YesterdaysMetData { get; set; }

        /// <summary>Met Data for tomorrow</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TomorrowsMetData { get; set; }

        /// <summary>
        /// Check values in weather and return a collection of warnings
        /// </summary>
        public IEnumerable<string> Validate()
        {
            if (Amp > maximumAMP)
            {
                yield return $"The value of Weather.AMP ({Amp}) is > {maximumAMP} oC. Please check the value.";
            }
        }

        /// <summary>Overrides the base class method to allow for initialization of this model </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            // open the weather file and read its contents.
            var file = new ApsimTextFile();
            try
            {
                file.Open(PathUtilities.GetAbsolutePath(FileName, simulation.FileName));
                data = file.ToTable();
                firstDateInFile = file.FirstDate;
                lastDateInFile = file.LastDate;
                Latitude = Convert.ToDouble(file.Constant("Latitude").Value);
                if (file.Constant("TAV") == null)
                    throw new Exception("TAV not specified in weather file.");
                Tav = Convert.ToDouble(file.Constant("TAV").Value);
                if (file.Constant("AMP") == null)
                    throw new Exception("AMP not specified in weather file.");
                Amp = Convert.ToDouble(file.Constant("Amp").Value);
                if (file.Constant("CO2") != null)
                    CO2 = Convert.ToDouble(file.Constant("CO2").Value);
                else
                    CO2 = defaultCO2;
            }
            finally
            {
                file.Close();
            }

            // check that some data was read
            if (data.Rows.Count == 0)
                throw new Exception($"No weather data found in file {FileName}");

            // if sampling method is random then create an array of random years
            if (IsRandomEnabled)
            {
                // number of years to extract out of the weather file
                int numYears = clock.EndDate.Year - clock.StartDate.Year + 1;

                // year range to sample from. Only sample from years that have a full annual record
                var firstYearToSampleFrom = firstDateInFile.Year;
                var lastYearToSampleFrom = lastDateInFile.Year;
                if (firstDateInFile.DayOfYear > 1)
                    firstYearToSampleFrom++;
                if (lastDateInFile.Month != 12 && lastDateInFile.Day != 31)
                    lastYearToSampleFrom--;

                Random random;
                if (string.IsNullOrEmpty(Seed))
                    random = new();
                else
                    random = new Random(Convert.ToInt32(Seed));

                if (TypeOfSampling == RandomiserTypeEnum.RandomSample)
                {
                    // randomly sample from the weather record for the required number of years
                    SampleYears = new int[numYears];

                    for (int i = 0; i < numYears; i++)
                        SampleYears[i] = random.Next(firstYearToSampleFrom, lastYearToSampleFrom);
                }
                else if (TypeOfSampling == RandomiserTypeEnum.RandomChooseFirstYear)
                {
                    // randomly choose a year from which to start drawing weather data
                    // The year chosen can't be to close to end of the weather record. As this is the year
                    // to start the sampling, there needs to at least as many year after this as the length
                    // of the simulation. Here the random number generator is limited to account for this
                    var lastYearForRandomNumberGenerator = lastYearToSampleFrom - numYears + 1;
                    if (lastYearForRandomNumberGenerator < firstYearToSampleFrom)
                        throw new Exception("There is insufficient weather data for the length of simulation (clock enddate-startdate). Cannot randomly sample the start date.");
                    firstYearToSampleFrom = random.Next(firstYearToSampleFrom, lastYearForRandomNumberGenerator);
                    SampleYears = Enumerable.Range(firstYearToSampleFrom, numYears).ToArray();
                }
            }            

            if (SampleYears == null || SampleYears.Length == 0)
                throw new Exception("No years specified in WeatherRandomiser");

            if (string.IsNullOrEmpty(SplitDate))
                SplitDate = "1-Jan";

            foreach (var message in Validate())
                summary.WriteMessage(this, message, MessageType.Warning);

            currentYearIndex = 0;
            currentRowIndex = FindRowForDate(new DateTime(SampleYears[currentYearIndex], clock.StartDate.Month, clock.StartDate.Day));
        }

        /// <summary>Performs the tasks to update the weather data</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("DoWeather")]
        private void OnDoWeather(object sender, EventArgs e)
        {
            if (clock.Today == DateUtilities.GetDate(SplitDate, clock.Today.Year))
            {
                // need to change years to next one in sequence
                currentYearIndex++;
                if (currentYearIndex == SampleYears.Length)
                    currentYearIndex = 0;
                
                var dateToFind = DateUtilities.GetDate(SplitDate, SampleYears[currentYearIndex]);
                currentRowIndex = FindRowForDate(dateToFind);
            }

            var dateInFile = DataTableUtilities.GetDateFromRow(data.Rows[currentRowIndex]);
            if (dateInFile.Day == 29 && dateInFile.Month == 2 && clock.Today.Day == 1 && clock.Today.Month == 3)
            {
                // leap day in weather data but not clock - skip the leap day
                currentRowIndex++;
            }
            else if (dateInFile.Day == 1 && dateInFile.Month == 3 && clock.Today.Day == 29 && clock.Today.Month == 2)
            {
                // leap day in clock but not weather data - repeat last day
                currentRowIndex--;
            }
            else
            {
                // Make sure date in file matches the clock
                if (clock.Today.Day != dateInFile.Day || clock.Today.Month != dateInFile.Month)
                    throw new Exception($"Non contiguous weather data found at date {dateInFile}");
            }

            MaxT = Convert.ToDouble(data.Rows[currentRowIndex]["MaxT"]);
            MinT = Convert.ToDouble(data.Rows[currentRowIndex]["MinT"]);
            Radn = Convert.ToDouble(data.Rows[currentRowIndex]["Radn"]);
            Rain = Convert.ToDouble(data.Rows[currentRowIndex]["Rain"]);
            if (data.Columns.Contains("VP"))
                VP = Convert.ToDouble(data.Rows[currentRowIndex]["VP"]);
            if (data.Columns.Contains("Wind"))
                Wind = Convert.ToDouble(data.Rows[currentRowIndex]["Wind"]);
            if (AirPressure == 0)
                AirPressure = 1010;

            currentRowIndex++;

            PreparingNewWeatherData?.Invoke(this, new EventArgs());
        }

        /// <summary>Finds a row in the data table that matches a given date</summary>
        /// <remarks>Will throw an exception if date not found</remarks>
        /// <param name="date">The date to find</param>
        /// <returns>The index of the row found</returns>
        private int FindRowForDate(DateTime date)
        {
            var firstDateInFile = DataTableUtilities.GetDateFromRow(data.Rows[0]);
            var rowIndex = (date - firstDateInFile).Days;

            // check to make sure dates are ok
            if (rowIndex < 0)
                throw new Exception($"Cannot find year in weather file. Year = {SampleYears[currentYearIndex]}");
            if (DataTableUtilities.GetDateFromRow(data.Rows[rowIndex]) != date)
                throw new Exception($"Non consecutive dates found in file {FileName}");
            return rowIndex;
        }

        /// <summary>Computes the duration of the day, with light (hours)</summary>
        /// <param name="Twilight">The angle to measure time for twilight (degrees)</param>
        /// <returns>The number of hours of daylight</returns>
        public double CalculateDayLength(double Twilight)
        {
            if (DayLength <= -1)
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
    }
}
