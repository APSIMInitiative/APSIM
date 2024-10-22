namespace UnitTests.Weather
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using Models;
    using Models.Climate;
    using Models.Core;
    using NUnit.Framework;

    /// <summary>Tests for weather randomiser</summary>
    class WeatherSamplerTests
    {
        /// <summary>Tests a 3 year weather selection.</summary>
        [Test]
        public void ThreeYearTest()
        {
            var binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            const double delta = 0.000001;

            var weatherFilePath = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples", "WeatherFiles", "AU_Dalby.met"));

            var baseSim = new Simulation()
            {
                Children = new List<IModel>()
                {
                    new Clock()
                    {
                        StartDate = new DateTime(3000, 6, 1)
                    },
                    new WeatherSampler()
                    {
                        FileName = weatherFilePath,
                        SampleYears = new int[] { 1995, 1952, 1993 }
                    },
                    new MockSummary()
                }
            };
            var clock = baseSim.Children[0] as Clock;
            var weatherSampler = baseSim.Children[1];

            Utilities.CallEvent(clock, "SimulationCommencing");
            Utilities.InjectLink(weatherSampler, "simulation", baseSim);
            Utilities.InjectLink(weatherSampler, "clock", clock);

            var weather = baseSim.Children[1] as WeatherSampler;
            Utilities.CallEvent(weather, "SimulationCommencing");

            // ################## YEAR 1 - year before a leap year
            // 1st Jun 1995 in weather file
            // year day  radn  maxt   mint  rain  pan    vp
            // 1995 152   14.0  22.5  10.0   4.3   2.2  15.0
            Utilities.CallEvent(weather, "DoWeather");
            Assert.That(weather.Radn, Is.EqualTo(14).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(22.5).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(10.0).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(4.3).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(15.0).Within(delta));

            // Advance 364 days
            for (int i = 0; i < 364; i++)
                AdvanceOneDay(weather, clock);

            // 31st May 1952 in weather file
            // year day  radn  maxt   mint  rain  pan    vp
            // 1952 152   16.0  21.7   6.1   0.0   2.8   9.4
            Assert.That(weather.Radn, Is.EqualTo(16).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(21.7).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(6.1).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(0.0).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(9.4).Within(delta));

            // ################## YEAR 2 - leap year
            // 1st June 1952 in weather file
            // year day  radn  maxt   mint  rain  pan    vp
            // 1952 153   16.0  20.6   6.1   0.0   2.8   9.7
            AdvanceOneDay(weather, clock);
            Assert.That(weather.Radn, Is.EqualTo(16).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(20.6).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(6.1).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(0.0).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(9.7).Within(delta));

            // Advance 364 days
            for (int i = 0; i < 364; i++)
                AdvanceOneDay(weather, clock);

            // 31st May 1993 in weather file
            // year day  radn  maxt   mint  rain  pan    vp
            // 1993 151   14.0  24.5   9.5   0.0   2.6  15.0
            Assert.That(weather.Radn, Is.EqualTo(14).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(24.5).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(9.5).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(0.0).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(15.0).Within(delta));

            // ################## YEAR 3 - year after leap year
            // 1st June 1993 in weather file
            // year day   radn  maxt   mint  rain  pan    vp
            // 1993 152    9.0  24.0  14.0   0.0   2.6  16.0
            AdvanceOneDay(weather, clock);
            Assert.That(weather.Radn, Is.EqualTo(9).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(24.0).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(14.0).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(0.0).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(16.0).Within(delta));

            // Advance 365 days
            for (int i = 0; i < 365; i++)
                AdvanceOneDay(weather, clock);

            // ***** BACK TO FIRST YEAR (1995)
            // 1st Jun 1995 in weather file
            // year day  radn  maxt   mint  rain  pan    vp
            // 1995 152   14.0  22.5  10.0   4.3   2.2  15.0
            Assert.That(weather.Radn, Is.EqualTo(14).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(22.5).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(10.0).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(4.3).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(15.0).Within(delta));
        }
        [Test]
        public void WaterYearTest()
        {
            var binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            const double delta = 0.000001;

            var weatherFilePath = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples", "WeatherFiles", "AU_Dalby.met"));

            var baseSim = new Simulation()
            {
                Children = new List<IModel>()
                {
                    new Clock()
                    {
                        StartDate = new DateTime(3000, 6, 1)
                    },
                    new WeatherSampler()
                    {
                        FileName = weatherFilePath,
                        SampleYears = new int[] { 1995, 1952, 1993 },
                        SplitDate = "2-jun"
                    },
                    new MockSummary()
                }
            };
            var clock = baseSim.Children[0] as Clock;
            var weatherSampler = baseSim.Children[1];

            Utilities.CallEvent(clock, "SimulationCommencing");
            Utilities.InjectLink(weatherSampler, "simulation", baseSim);
            Utilities.InjectLink(weatherSampler, "clock", clock);

            var weather = baseSim.Children[1] as WeatherSampler;
            Utilities.CallEvent(weather, "SimulationCommencing");

            // ################## YEAR 1
            // 1st Jun 1995 in weather file
            // year day  radn  maxt   mint  rain  pan    vp
            // 1995 152   14.0  22.5  10.0   4.3   2.2  15.0
            Utilities.CallEvent(weather, "DoWeather");
            Assert.That(weather.Radn, Is.EqualTo(14).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(22.5).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(10.0).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(4.3).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(15.0).Within(delta));

            // 2nd june will advance to next year
            AdvanceOneDay(weather, clock);
            // this is a leap year, and the day number will jump by 2 (to 154)
            // 1952 153   16.0  20.6   6.1   0.0   2.8   9.7 300070
            // 1952 154   16.0  20.6   7.2   0.0   2.8   7.5 300070
            Assert.That(weather.Radn, Is.EqualTo(16).Within(delta));
            Assert.That(weather.MaxT, Is.EqualTo(20.6).Within(delta));
            Assert.That(weather.MinT, Is.EqualTo(7.2).Within(delta));
            Assert.That(weather.Rain, Is.EqualTo(0.0).Within(delta));
            Assert.That(weather.VP, Is.EqualTo(7.5).Within(delta));
        }
        private void AdvanceOneDay(IModel weather, Clock clock)
        {
            DateTime d = clock.Today.AddDays(1);
            Utilities.SetProperty(clock, "Today", d);
            Utilities.CallEvent(weather, "DoWeather");
        }
    }
}
