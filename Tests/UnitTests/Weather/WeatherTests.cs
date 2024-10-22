using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using APSIM.Shared.Utilities;
using Models;
using Models.Core;
using Models.Core.ApsimFile;
using Models.Core.Run;
using Models.Interfaces;
using Models.Storage;
using NUnit.Framework;

namespace UnitTests.Weather
{
    /// <summary>
    /// Tests for weather files.
    /// </summary>
    class WeatherFileTests
    {
        /// <summary>
        /// Tests a simple weather file in .xlsx (Excel) format.
        /// </summary>
        [Test]
        public void ExcelWeatherFileTest()
        {
            string weatherFilePath = Path.ChangeExtension(Path.GetTempFileName(), ".xlsx");
            using (FileStream file = new FileStream(weatherFilePath, FileMode.Create, FileAccess.Write))
            {
                Assembly.GetExecutingAssembly().GetManifestResourceStream("UnitTests.Weather.WeatherTestsExcelFile.xlsx").CopyTo(file);
            }

            Simulation baseSim = new Simulation()
            {
                Name = "Base",
                Children = new List<IModel>()
                {
                    new Models.Climate.Weather()
                    {
                        Name = "Weather",
                        FullFileName = weatherFilePath,
                        ExcelWorkSheetName = "Sheet1"
                    },
                    new Clock()
                    {
                        Name = "Clock",
                        StartDate = new DateTime(1998, 11, 9),
                        EndDate = new DateTime(1998, 11, 12)
                    },
                    new MockSummary()
                }
            };

            baseSim.Prepare();
            baseSim.Run();
            var summary = baseSim.FindDescendant<MockSummary>();
            Assert.That(summary.messages[0], Is.EqualTo("Simulation terminated normally"));
        }

        [Test]
        public void TestCustomMetData()
        {
            // Open an in-memory database.
            IDatabaseConnection database = new SQLite();
            database.OpenDatabase(":memory:", readOnly: false);

            string weatherData = ReflectionUtilities.GetResourceAsString("UnitTests.Weather.CustomMetData.met");
            string metFile = Path.GetTempFileName();
            File.WriteAllText(metFile, weatherData);
            try
            {

                Simulation sim = new Simulation()
                {
                    Children = new List<IModel>()
                    {
                        new Clock(),
                        new MockSummary(),
                        new Models.Climate.Weather()
                        {
                            FullFileName = metFile
                        },
                        new Models.Report()
                        {
                            VariableNames = new[]
                            {
                                "[Manager].Script.MyColumn as x"
                            },
                            EventNames = new[]
                            {
                                "[Clock].DoReport"
                            }
                        },
                        new Manager()
                        {
                            Code = "using System;\nusing Models.Core;\nusing Models.Climate;\n\nnamespace Models\n{\n    [Serializable]\n    public class Script : Model\n    {\n        [Link] private Weather weather;\n        \n        public double MyColumn\n        {\n        \tget\n        \t{\n        \t\treturn weather.GetValue(\"my_column_name\");\n        \t}\n        }\n    }\n}\n"
                        }
                    }
                };

                Simulations sims = new Simulations()
                {
                    Children = new List<IModel>()
                    {
                        new DataStore(database),
                        sim
                    }
                };

                // Run simulations.
                Runner runner = new Runner(sims);
                List<Exception> errors = runner.Run();
                Assert.That(errors, Is.Not.Null);
                if (errors.Count != 0)
                    throw new AggregateException(errors);

                int[] rawData = new int[] { 6, 7, 2, 3, 4 };
                List<object[]> rowData = rawData.Select(x => new object[] { x }).ToList();
                DataTable expected = Utilities.CreateTable(new string[] { "x" }, rowData);
                Assert.That(
                    expected
                .IsSame(database.ExecuteQuery("SELECT [x] FROM Report")), Is.True);
            }
            finally
            {
                database.CloseDatabase();
                File.Delete(metFile);
            }
        }

        [Test]
        public void TestGetAnyDayMetData()
        {

            var binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string weatherFilePath = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples", "WeatherFiles", "AU_Dalby.met"));

            Simulation baseSim = new Simulation()
            {
                Name = "Base",
                Children = new List<IModel>()
                    {
                        new Models.Climate.Weather()
                        {
                            Name = "Weather",
                            FullFileName = weatherFilePath,
                            ExcelWorkSheetName = "Sheet1"
                        },
                        new Clock()
                        {
                            Name = "Clock",
                        },
                        new MockSummary()
                    }
            };

            Models.Climate.Weather weather = baseSim.Children[0] as Models.Climate.Weather;
            Clock clock = baseSim.Children[1] as Clock;
            clock.StartDate = DateTime.ParseExact("1900-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            clock.EndDate = DateTime.ParseExact("1900-01-02", "yyyy-MM-dd", CultureInfo.InvariantCulture);

            baseSim.Prepare();
            baseSim.Run();

            DailyMetDataFromFile weather1900 = weather.GetMetData(DateTime.ParseExact("1900-01-03", "yyyy-MM-dd", CultureInfo.InvariantCulture));
            DailyMetDataFromFile weather2000 = weather.GetMetData(DateTime.ParseExact("2000-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture));

            Assert.That(weather1900.MaxT, Is.EqualTo(31.9).Within(0.01));
            Assert.That(weather1900.MinT, Is.EqualTo(16.6).Within(0.01));
            Assert.That(weather1900.Wind, Is.EqualTo(3.0).Within(0.01));
            Assert.That(weather1900.Radn, Is.EqualTo(25.0).Within(0.01));
            Assert.That(weather1900.Rain, Is.EqualTo(0.0).Within(0.01));

            Assert.That(weather2000.MaxT, Is.EqualTo(30.5).Within(0.01));
            Assert.That(weather2000.MinT, Is.EqualTo(13.5).Within(0.01));
            Assert.That(weather2000.Wind, Is.EqualTo(3.0).Within(0.01));
            Assert.That(weather2000.Radn, Is.EqualTo(28.0).Within(0.01));
            Assert.That(weather2000.Rain, Is.EqualTo(0.0).Within(0.01));

            //should get the 3/1/1900 weather data
            Assert.That(weather.TomorrowsMetData.MaxT, Is.EqualTo(weather1900.MaxT).Within(0.01));
            Assert.That(weather.TomorrowsMetData.MinT, Is.EqualTo(weather1900.MinT).Within(0.01));
            Assert.That(weather.TomorrowsMetData.Wind, Is.EqualTo(weather1900.Wind).Within(0.01));
            Assert.That(weather.TomorrowsMetData.Radn, Is.EqualTo(weather1900.Radn).Within(0.01));
            Assert.That(weather.TomorrowsMetData.Rain, Is.EqualTo(weather1900.Rain).Within(0.01));
        }

        /// <summary>
        /// Ensures all example .met files contain %root%
        /// </summary>
        [Test]
        public void TestAllExampleFilesWeatherModelsHaveCorrectRootReferenceInFileName()
        {
            bool allFilesHaveRootReference = true;
            string binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string exampleFileDirectory = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples"));
            IEnumerable<string> exampleFileNames = Directory.GetFiles(exampleFileDirectory, "*.apsimx", SearchOption.AllDirectories);
            foreach (string exampleFile in exampleFileNames)
            {
                Simulations sim = FileFormat.ReadFromFile<Simulations>(exampleFile, e => throw new Exception(), false).NewModel as Simulations;
                IEnumerable<Models.Climate.Weather> weatherModels = sim.FindAllDescendants<Models.Climate.Weather>();
                foreach (Models.Climate.Weather weatherModel in weatherModels)
                {
                    if (!weatherModel.FileName.Contains("%root%/Examples/WeatherFiles/") && weatherModel.FileName.Contains('\\'))
                    {
                        allFilesHaveRootReference = false;
                        Console.WriteLine($"{sim.FileName} has simulation {weatherModel.Parent.Name} with weather model {weatherModel.Name} that does not contain correct root reference.");
                    }
                }
            }
            Assert.That(allFilesHaveRootReference, Is.True);
        }


        /*
         * This doesn't make sense to use anymore since weather sensibility tests no longer throw exceptions
         * Useful to keep for later though if we need to check all the example weather.
        [Test]
        public void SanityCheckExampleWeather()
        {
            List<string> weatherFiles = new List<string>();
            weatherFiles.Add("75_34825.met");
            weatherFiles.Add("-225_36025.met");
            weatherFiles.Add("1000_39425.met");
            weatherFiles.Add("-1025_34875.met");
            weatherFiles.Add("-1375_37985.met");
            weatherFiles.Add("-2500_39425.met");
            weatherFiles.Add("4025_36675.met");
            weatherFiles.Add("Curvelo.met");
            weatherFiles.Add("Dalby.met");
            weatherFiles.Add("Gatton.met");
            weatherFiles.Add("Goond.met");
            weatherFiles.Add("Ingham.met");
            weatherFiles.Add("Kingaroy.met");
            weatherFiles.Add("lincoln.met");
            weatherFiles.Add("Makoka.met");
            weatherFiles.Add("Popondetta.met");
            weatherFiles.Add("Site1003_SEA.met");
            weatherFiles.Add("VCS_Ruakura.met");
            weatherFiles.Add("WaggaWagga.met");

            var binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            

            foreach (string wFile in weatherFiles)
            {
                string weatherFilePath = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples", "WeatherFiles", wFile));

                Simulation baseSim = new Simulation()
                {
                    Name = "Base",
                    Children = new List<IModel>()
                {
                    new Models.Climate.Weather()
                    {
                        Name = "Weather",
                        FullFileName = weatherFilePath,
                        ExcelWorkSheetName = "Sheet1"
                    },
                    new Clock()
                    {
                        Name = "Clock",
                    },
                    new MockSummary()
                }
                };

                (baseSim.Children[1] as Clock).StartDate = (baseSim.Children[0] as Models.Climate.Weather).StartDate;
                (baseSim.Children[1] as Clock).EndDate = (baseSim.Children[0] as Models.Climate.Weather).EndDate;

                baseSim.Prepare();
                baseSim.Run();
                Assert.AreEqual(MockSummary.messages[0], "Simulation terminated normally");
            }
        }
        */
    }
    /// <summary>
    /// Tests for weather files.
    /// </summary>
    class SimpleWeatherFileTests
    {
        [Test]
        public void TestGetAnyDayMetData()
        {

            var binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            // Start with dalby
            string weatherFilePath = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples", "WeatherFiles", "AU_Dalby.met"));
            // Switch to another
            string weatherFilePath2 = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples", "WeatherFiles", "AU_Gatton.met"));

            Simulation baseSim = new Simulation()
            {
                Name = "Base",
                Children = new List<IModel>()
                    {
                        new Models.Climate.SimpleWeather()
                        {
                            Name = "Weather",
                            FullFileName = weatherFilePath,
                            ExcelWorkSheetName = ""
                        },
                        new Clock()
                        {
                            Name = "Clock",
                        },
                        new MockSummary()
                    }
            };

            Models.Climate.SimpleWeather weather = baseSim.Children[0] as Models.Climate.SimpleWeather;
            Clock clock = baseSim.Children[1] as Clock;

            weather.FullFileName = weatherFilePath2;
            clock.StartDate = DateTime.ParseExact("1990-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            clock.EndDate = DateTime.ParseExact("1990-01-02", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            baseSim.Prepare();
            baseSim.Run();

            DailyMetDataFromFile weather1900 = weather.GetMetData(DateTime.ParseExact("1990-01-03", "yyyy-MM-dd", CultureInfo.InvariantCulture));
            DailyMetDataFromFile weather2000 = weather.GetMetData(DateTime.ParseExact("2000-01-01", "yyyy-MM-dd", CultureInfo.InvariantCulture));

            Assert.That(weather1900.MaxT, Is.EqualTo(33.7).Within(0.01));
            Assert.That(weather1900.MinT, Is.EqualTo(15.5).Within(0.01));
            Assert.That(weather1900.Radn, Is.EqualTo(30.0).Within(0.01));
            Assert.That(weather1900.Rain, Is.EqualTo(0.0).Within(0.01));

            Assert.That(weather2000.MaxT, Is.EqualTo(28.6).Within(0.01));
            Assert.That(weather2000.MinT, Is.EqualTo(19.4).Within(0.01));
            Assert.That(weather2000.Radn, Is.EqualTo(27.0).Within(0.01));
            Assert.That(weather2000.Rain, Is.EqualTo(0.0).Within(0.01));

            //should get the 3/1/1900 weather data
            Assert.That(weather.TomorrowsMetData.MaxT, Is.EqualTo(weather1900.MaxT).Within(0.01));
            Assert.That(weather.TomorrowsMetData.MinT, Is.EqualTo(weather1900.MinT).Within(0.01));
            Assert.That(weather.TomorrowsMetData.Wind, Is.EqualTo(weather1900.Wind).Within(0.01));
            Assert.That(weather.TomorrowsMetData.Radn, Is.EqualTo(weather1900.Radn).Within(0.01));
            Assert.That(weather.TomorrowsMetData.Rain, Is.EqualTo(weather1900.Rain).Within(0.01));
        }
    }
}
