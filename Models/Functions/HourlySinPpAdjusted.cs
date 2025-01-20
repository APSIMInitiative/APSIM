using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Models.Climate;
using Models.Core;
using Models.Interfaces;
using Newtonsoft.Json;

namespace Models.Functions
{
    /// <summary>
    /// Firstly hourly estimates of air temperature (Ta) are interpolated from Tmax, Tmin and daylength (d) 
    /// using the method of [Goudriaan1994].  
    /// During sunlight hours Ta is calculated each hour using a 
    /// sinusoidal curve fitted to Tmin and Tmax . 
    /// After sunset Ta is calculated as an exponential decline from Ta at sunset 
    /// to the Tmin at sunrise the next day.
    /// The hour (Th) of sunrise is calculated as Th = 12 − d/2 and Ta is assumed 
    /// to equal Tmin at this time.  Tmax is reached when Th equals 13.5. 
    /// If Controled Environment module is used for Weather it takes hourly data from that instead of the above calculation.
    /// </summary>
    [Serializable]
    [Description("calculating the hourly temperature based on Tmax, Tmin and daylength")]
    [ValidParent(ParentType = typeof(SubDailyInterpolation))]
    public class HourlySinPpAdjusted : Model, IInterpolationMethod
    {

        /// <summary>The met data</summary>
        [Link]
        protected IWeather MetData = null;

        /// <summary>Link to controled environment if it exisits.  Thus uses CE hourly data instead of the extrapolation used here</summary>
        [Link(IsOptional = true)]
        protected ControlledEnvironment CE = null;

        private const double P = 1.5;

        private const double TC = 4.0;

        /// <summary>The type of variable for sub-daily values</summary>
        [JsonIgnore]
        public string OutputValueType { get; set; } = "air temperature";

        /// <summary>
        /// Temperature at the most recent sunset
        /// </summary>
        [JsonIgnore]
        public double Tsset { get; set; }

        /// <summary> Set the sub daily temperature range factor values at sowing</summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {

        }

        /// <summary>Creates a list of temperature range factors used to estimate daily temperature from Min and Max temp</summary>
        /// <returns></returns>
        public List<double> SubDailyValues()
        {
            if (CE != null)
            {
                return CE.SubDailyTemperature.ToList();
            }
            else
            {
                double d = MetData.CalculateDayLength(-6);
                double Tmin = MetData.MinT;
                double Tmax = MetData.MaxT;
                double TmaxB = (MetData.YesterdaysMetData == null) ? MetData.MaxT : MetData.YesterdaysMetData.MaxT;
                double TminA = (MetData.TomorrowsMetData == null) ? MetData.MinT : MetData.TomorrowsMetData.MinT;
                double Hsrise = MetData.CalculateSunRise();
                double Hsset = MetData.CalculateSunSet();

                List<double> sdts = new List<double>();

                for (int Th = 0; Th <= 23; Th++)
                {
                    double Ta = 1.0;
                    if (Th < Hsrise)
                    {
                        //  Hour between midnight and sunrise
                        //  PERIOD A MaxTB is max. temperature, before day considered

                        //this is the sunset temperature of based on the previous day
                        double n = 24 - d;
                        Tsset = Tmin + (TmaxB - Tmin) *
                                        Math.Sin(Math.PI * (d / (d + 2 * P)));

                        Ta = (Tmin - Tsset * Math.Exp(-n / TC) +
                                (Tsset - Tmin) * Math.Exp(-(Th + 24 - Hsset) / TC)) /
                                (1 - Math.Exp(-n / TC));
                    }
                    else if (Th >= Hsrise & Th < 12 + P)
                    {
                        // PERIOD B Hour between sunrise and normal time of MaxT
                        Ta = Tmin + (Tmax - Tmin) *
                                Math.Sin(Math.PI * (Th - Hsrise) / (d + 2 * P));
                    }
                    else if (Th >= 12 + P & Th < Hsset)
                    {
                        // PERIOD C Hour between normal time of MaxT and sunset
                        //  MinTA is min. temperature, after day considered

                        Ta = TminA + (Tmax - TminA) *
                            Math.Sin(Math.PI * (Th - Hsrise) / (d + 2 * P));
                    }
                    else
                    {
                        // PERIOD D Hour between sunset and midnight
                        Tsset = TminA + (Tmax - TminA) * Math.Sin(Math.PI * (d / (d + 2 * P)));
                        double n = 24 - d;
                        Ta = (TminA - Tsset * Math.Exp(-n / TC) +
                                (Tsset - TminA) * Math.Exp(-(Th - Hsset) / TC)) /
                                (1 - Math.Exp(-n / TC));
                    }
                    sdts.Add(Ta);
                }
                return sdts;
            }
        }
    }

}
