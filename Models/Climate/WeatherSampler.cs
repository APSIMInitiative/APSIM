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
    /// Allow random sampling of whole years of weather data from a weather file.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    [Description("This model samples from a weather file taking slices of data of one or more years at a time in order to preserve seasonal patterns. 'Year' means 12 months rather than a calendar year\r\n \r\n" +
                 "The start of a year is taken from the user input below using the specified start to the sampling year. The duration of the simulation is set using the difference between Clock's StartDate and EndDate. \r\n  \r\n" +
                 "Options with random sampling can be with a random seed or with a specified seed. Provide a seed for repeatable simulation results. \r\n \r\n" +
                 "There are four sampling methods.\r\n \r\n" +
                 "0. SpecificYears - specific years can be specified. Weather data will be taken from these years in the order specified. Once all years have been sampled, the model will cycle back to the first year until the duration specified in Clock has been met. The years do have have to be contiguous.\r\n" +
                 "1. SpecificYearRange - As above but specify the first and last year only.\r\n" +
                 "2. RandomSample - whole years of weather data will be sampled randomly and independently until the duration specified in Clock has been met.\r\n" +
                 "3. RandomChooseFirstYear - allows multi-year slices of weather data to be sampled. It will randomly choose a start year in the weather record and continue from that date until the duration specified in Clock has been met.")]

    public class WeatherSampler : Model, IWeather
    {
        /// <summary>A data table of all weather data.</summary>
        private DataTable data;

        /// <summary>The current row into the weather data table.</summary>
        private int currentRowIndex;

        /// <summary>The current index into the Years array.</summary>
        private int currentYearIndex;

        /// <summary>The first date in the weather file.</summary>
        private DateTime firstDateInFile;

        /// <summary>The last date in the weather file.</summary>
        private DateTime lastDateInFile;

        [Link]
        private IClock clock = null;

        [Link]
        private Simulation simulation = null;

        /// <summary>Type of randomiser enum for drop down.</summary>
        public enum RandomiserTypeEnum
        {
            /// <summary>Specify years manually.</summary>
            SpecificYears,

            /// <summary>Specify the first and last year manually.</summary>
            SpecificYearRange,

            /// <summary>Random sampler.</summary>
            RandomSample,

            /// <summary>Random choose the first year to draw weather data from.</summary>
            RandomChooseFirstYear
        }


        /// <summary>The weather file name.</summary>
        [Summary]
        [Description("Weather file to sample from")]
        public string FileName { get; set; }

        /// <summary>Type of year sampling.</summary>
        [Description("Type of sampling")]
        public RandomiserTypeEnum TypeOfSampling { get; set; }

        /// <summary>The sample years.</summary>
        [Summary]
        [Description("Seed to pass to random number generator. Leave blank for fully random")]
        [Display(VisibleCallback = "IsRandomEnabled")]
        public string Seed { get; set; }

        /// <summary>The sample years.</summary>
        [Summary]
        [Description("Years to sample from the weather file.")]
        [Display(VisibleCallback = "IsSpecifyYearsEnabled")]
        public int[] Years { get; set; }

        /// <summary>The sample years.</summary>
        [Summary]
        [Description("First and last years of the range to sample from the weather file.")]
        [Display(VisibleCallback = "IsSpecifyYearRangeEnabled")]
        public int[] YearRange { get; set; }  // apparently I can't dimension this here


        /// <summary>Is random enabled?</summary>
        public bool IsRandomEnabled { get { return TypeOfSampling == RandomiserTypeEnum.RandomSample || TypeOfSampling == RandomiserTypeEnum.RandomChooseFirstYear; } }

        /// <summary>Is 'specify years' enabled?</summary>
        public bool IsSpecifyYearsEnabled { get { return TypeOfSampling == RandomiserTypeEnum.SpecificYears; } }

        /// <summary>Is 'specify years' enabled?</summary>
        public bool IsSpecifyYearRangeEnabled { get { return TypeOfSampling == RandomiserTypeEnum.SpecificYearRange; } }

        /// <summary>The date when years tick over.</summary>
        [Summary]
        [Description("The date marking the start of sampling years (d-mmm). Leave blank for 1-Jan")]
        public string SplitDate { get; set; }


        /// <summary>Date that is being sampled from the actual weather file</summary>
        [JsonIgnore]
        public DateTime DateToFind { get; set; }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile YesterdaysMetData { get; set; }

        /// <summary>Met Data from yesterday</summary>
        [JsonIgnore]
        public DailyMetDataFromFile TomorrowsMetData { get; set; }

        /// <summary>The start date of the weather file.</summary>
        public DateTime StartDate => clock.StartDate;

        /// <summary>The end date of the weather file.</summary>
        public DateTime EndDate => clock.EndDate;

        /// <summary>The maximum temperature (oc).</summary>
        [Units("°C")]
        [JsonIgnore]
        public double MaxT { get; set; }

        /// <summary>Gets or sets the minimum temperature (oc).</summary>
        [Units("°C")]
        [JsonIgnore]
        public double MinT { get; set; }

        /// <summary>Mean temperature. </summary>
        [Units("°C")]
        [JsonIgnore]
        public double MeanT { get { return (MaxT + MinT) / 2; } }

        /// <summary>Daily mean VPD.</summary>
        [Units("hPa")]
        [JsonIgnore]
        public double VPD { get; set; }

        /// <summary>Daily Pan evaporation.</summary>
        [Units("mm")]
        [JsonIgnore]
        public double PanEvap { get; set; }

        /// <summary>Rainfall (mm).</summary>
        [Units("mm")]
        [JsonIgnore]
        public double Rain { get; set; }

        /// <summary>Solar radiation (MJ/m2/day).</summary>
        [Units("MJ/m^2/d")]
        [JsonIgnore]
        public double Radn { get; set; }

        /// <summary>Vapor pressure.</summary>
        [Units("hPa")]
        [JsonIgnore]
        public double VP { get; set; }

        /// <summary>Wind.</summary>
        [JsonIgnore]
        public double Wind { get; set; }

        /// <summary>CO2 level. If not specified in the weather file the default is WeatherConstants.DEFAULT_CO2.</summary>
        [Units("ppm")]
        [JsonIgnore]
        public double CO2 { get; set; }

        /// <summary>Atmospheric air pressure. If not specified in the weather file the default is 1010 hPa.</summary>
        [Units("hPa")]
        [JsonIgnore]
        public double AirPressure { get; set; }

        /// <summary>Diffuse radiation fraction. If not specified in the weather file the default is 1.</summary>
        [Units("0-1")]
        [JsonIgnore]
        public double DiffuseFraction { get; set; }

        /// <summary>Latitude.</summary>
        [JsonIgnore]
        public double Latitude { get; set; }

        /// <summary>Gets the longitude</summary>
        [JsonIgnore]
        public double Longitude { get; set; }

        /// <summary>Average temperature.</summary>
        [Units("°C")]
        [JsonIgnore]
        public double Tav { get; set; }

        /// <summary>Temperature amplitude.</summary>
        [Units("°C")]
        [JsonIgnore]
        public double Amp { get; set; }

        /// <summary>
        /// This event will be invoked immediately before models get their weather data.
        /// models and scripts an opportunity to change the weather data before other models
        /// reads it.
        /// </summary>
        public event EventHandler PreparingNewWeatherData;

        /// <summary>Duration of the day in hours.</summary>
        /// <param name="Twilight">The twilight angle.</param>
        public double CalculateDayLength(double Twilight)
        {
            return MathUtilities.DayLength(clock.Today.DayOfYear, Twilight, this.Latitude);
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

        /// <summary>Called at the beginning of a simulation.</summary>
        [EventSubscribe("Commencing")]
        private void OnStartOfSimulation(object sender, EventArgs e)
        {
            // Open the weather file and read its contents.
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
                    CO2 = WeatherConstants.DEFAULT_CO2;
            }
            finally
            {
                file.Close();
            }

            // Make sure some data was read.
            if (data.Rows.Count == 0)
                throw new Exception($"No weather data found in file {FileName}");

            // If sampling method is random then create an array of random years.
            if (IsRandomEnabled)
            {
                // Determine the number of years to extract out of the weather file.
                int numYears = clock.EndDate.Year - clock.StartDate.Year + 1;    // VOS - only when the notional year start is 1-Jan, otherwise not the "+1"

                // Determine the year range to sample from. Only sample from years that have a full record i.e. 1-jan to 31-dec
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
                    // Randomly sample from the weather record for the required number of years.
                    Years = new int[numYears];

                    for (int i = 0; i < numYears; i++)
                        Years[i] = random.Next(firstYearToSampleFrom, lastYearToSampleFrom);
                }
                else if (TypeOfSampling == RandomiserTypeEnum.RandomChooseFirstYear)
                {
                    // Randomly choose a year to draw weather data from.
                    // The year chosen can't be at the end of the weather record because there needs to be sufficient years of 
                    // consecutive weather data after the chosen year for the length of simulation. Limit the random number generator to 
                    // allow for this.
                    var lastYearForRandomNumberGenerator = lastYearToSampleFrom - numYears + 1;
                    if (lastYearForRandomNumberGenerator < firstYearToSampleFrom)
                        throw new Exception("There is insufficient weather data for the length of simulation (clock enddate-startdate). Cannot randomly sample the start date.");
                    firstYearToSampleFrom = random.Next(firstYearToSampleFrom, lastYearForRandomNumberGenerator);
                    Years = Enumerable.Range(firstYearToSampleFrom, numYears).ToArray();
                }
            }
            else if (IsSpecifyYearRangeEnabled)
            {
                Years = new int[YearRange[1] - YearRange[0] + 1];
                for (int i = YearRange[0]; i <= YearRange[1]; i++)
                    Years[i- YearRange[0]] = i;
            }

            if (Years == null || Years.Length == 0)
                throw new Exception("No years specified in WeatherRandomiser");

            if (string.IsNullOrEmpty(SplitDate)) {
                SplitDate = "1-jan";
            }

            currentYearIndex = 0;
            currentRowIndex = FindRowForDate(new DateTime(Years[currentYearIndex], clock.StartDate.Month, clock.StartDate.Day));
        }

        /// <summary>An event handler for the daily DoWeather event.</summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The arguments of the event</param>
        [EventSubscribe("DoWeather")]
        private void OnDoWeather(object sender, EventArgs e)
        {
            if (clock.Today == DateUtilities.GetDate(SplitDate, clock.Today.Year))  
            {
                // Need to change years to next one in sequence. VOS - but  not if this is the first day of the simulation
                if (clock.StartDate != clock.Today)
                    currentYearIndex++;

                if (currentYearIndex == Years.Length)
                    currentYearIndex = 0;

                DateToFind = DateUtilities.GetDate(SplitDate, Years[currentYearIndex]);
                currentRowIndex = FindRowForDate(DateToFind);
            }

            var dateInFile = DataTableUtilities.GetDateFromRow(data.Rows[currentRowIndex]);
            if (dateInFile.Day == 29 && dateInFile.Month == 2 && clock.Today.Day == 1 && clock.Today.Month == 3)
            {
                // Leap day in weather data but not clock - skip the leap day.
                currentRowIndex++;
            }
            else if (dateInFile.Day == 1 && dateInFile.Month == 3 && clock.Today.Day == 29 && clock.Today.Month == 2)
            {
                // Leap day in clock but not weather data.
                currentRowIndex--;
            }
            else
            {
                // Make sure date in file matches the clock.
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
                this.AirPressure = 1010;

            currentRowIndex++;

            PreparingNewWeatherData?.Invoke(this, new EventArgs());
        }

        /// <summary>
        /// Find a row in the data table that matches a date. Will throw if not found.
        /// </summary>
        /// <param name="dateToFind">The date to find.</param>
        /// <returns>The index of the found row.</returns>
        private int FindRowForDate(DateTime dateToFind)
        {
            var firstDateInFile = DataTableUtilities.GetDateFromRow(data.Rows[0]);
            var rowIndex = (dateToFind - firstDateInFile).Days;

            // check to make sure dates are ok.
            if (rowIndex < 0)
                throw new Exception($"Cannot find year in weather file. Year = {Years[currentYearIndex]}");
            if (DataTableUtilities.GetDateFromRow(data.Rows[rowIndex]) != dateToFind)
                throw new Exception($"Non consecutive dates found in file {FileName}");
            return rowIndex;
        }
    }
}
