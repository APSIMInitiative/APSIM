using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using APSIM.Shared.Utilities;
using Models.Climate;
using Models.Core;
using Models.Interfaces;
using Models.PMF;
using Models.PMF.Interfaces;
using Models.PMF.Organs;

namespace Models.Functions.SupplyFunctions
{
    /// <summary>
    /// This module simulates photosynthesis using the Soil-Plant-Atmosphere System Simulation(SPASS) developed by Wang(1997), with a few modifications, such as a downscaling to hourly time step with the temperature diurnal pattern from Resource et al. (1981). Hence, the model simulates hourly potential biomass assimilation.VPD is calculated as the difference between hourly saturated vapor pressure(SVP) and SVP at minimum temperature(Messina et al., 2015) and is used to calculate hourly transpiration as follows:
    ///
    /// TR<sub>pot,t</sub> = (ΔDM<sub>pot, t</sub>× VPD<sub>t</sub>) / TEC
    ///
    /// where TR<sub>pot</sub> is the potential transpiration driven by radiation interception (mm), ΔDM<sub>pot</sub>is the hourly potential increase in dry matter (i.e.amount of CO<sub>2</sub> fixed by photosynthesis; g.m<sup>-2</sup>), TEC is the transpiration efficiency coefficient (kPa.gC<sup>-1</sup>.m<sup>-2</sup>.mm water<sup>-1</sup>), and t is time.
    ///
    /// The potential transpiration is then adjusted based on the level of evaporative demand and soil water stress when applicable.First, VPD-limited hourly transpiration(TR<sub>VPD-limited,t</sub>) is calculated as follows for VPD<sub>t</sub> > VPD<sub>ref</sub>:
    ///
    /// TR<sub>VPDref</sub> = (ΔDM<sub>pot, VPDref</sub> × VPD<sub>ref</sub>)/TEC
    ///
    /// Reduction<sub>t</sub> = Max(0, TR<sub>pot,t </sub> - TR<sub>VPDref</sub>) × α
    ///
    /// TR<sub>VPD-limited,t</sub> = TR<sub>pot,t</sub> - Reduction<sub>t</sub>
    ///
    /// where VPD<sub>ref</sub> is the threshold VPD above which transpiration rate is reduced (kPa), TR<sub>VPDref</sub> is the transpiration rate at VPD<sub>ref</sub> (mm.hr<sup>-1</sup>), ΔDM<sub>pot,VPDref</sub> is the interpolated hourly growth at VPD<sub>ref</sub> (g.m<sup>-2</sup>). Reduction is the reduction in transpiration rate above VPD<sub>ref</sub>(mm.hr<sup>-1</sup>), and α is the reduction factor for VPD<sub>t</sub> > VPD<sub>ref</sub>(decimal; zero for no reduction, 1 for complete capping of transpiration rate at TR<sub>VPDref</sub>)
    ///
    /// Then, soil-water-limited hourly transpiration (TR<sub>water-limited</sub>) is calculated.Hourly transpiration is capped starting from the maximum TR<sub>VPD-limited</sub> at midday until the total plant available soil moisture can meet the crop daily water demand.Finally, actual hourly increase in dry matter (ΔDM) is calculated based on TR<sub>water-limited</sub>, as follows:
    ///
    /// ΔDM<sub>t</sub> = (TR<sub>water-limited,t</sub> × TEC) / VPD<sub>t</sub>
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(ILeaf))]

    public class LimitedTranspirationRate : Model, IFunction
    {
        //[Input]
        /// <summary>The weather data</summary>
        [Link]
        private readonly IWeather Weather = null;

        /// <summary>The Clock</summary>
        [Link]
        private readonly IClock Clock = null;

        /// <summary>The Leaf organ</summary>
        [Link]
        private readonly Plant Plant = null;

        /// <summary>The Root organ</summary>
        [Link]
        private readonly Root Root = null;

        /// <summary>The daily radiation intercepted by crop canopy</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private readonly IFunction RadnInt = null;

        /// <summary>The transpiration efficiency coefficient for net biomass assimilate</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        [Description("Transpiration efficiency coefficient for net biomass assimilate")]
        [Units("kPa/gC/m^2/mm water")]
        private readonly IFunction TEC = null;

        /// <summary>The CO2 impact on RUE</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private readonly IFunction FCO2 = null;

        /// <summary>The stress impact on PgMax</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private readonly IFunction LUEStress = null;

        /// <summary>The mean temperature impact on RUE</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private readonly IFunction FT = null;

        /// <summary>The N deficiency impact on RUE</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private readonly IFunction FN = null;

        /// <summary>The radiation use efficiency</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        [Description("Radiation use efficiency")]
        private readonly IFunction RUE = null;

        /// <summary>The daily gross assimilate calculated by a another photosynthesis model</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        private readonly IFunction GrossAssimilateModel = null;

        /// <summary>Plant organs</summary>
        [Link]
        private readonly IOrgan[] organs = null;

        //------------------------------------------------------------------------------------------------

        /// <summary>The photosynthesis type (net/gross)</summary>
        [Description("The photosynthesis type (net or gross)")]
        public string AssimilateType { get; set; } = "net";

        /// <summary>The photosynthesis type (net/gross)</summary>
        [Description("The way soil moisture should be treated (daily or numeric or dcaps)")]
        public string SWType { get; set; } = "numeric";

        /// <summary>Threshold of hourly VPD at which hourly transpiration rate cease to linearly increase by VPD/TEC</summary>
        [Description("Threshold of hourly VPD at which hourly transpiration rate cease to linearly increase by VPD/TEC")]
        [Units("kPa")]
        [Bounds(Lower = 0.1, Upper = 1000)]
        public double VPDThresh { get; set; } = 999;

        /// <summary>Fraction of (hourly VPD - VPDThresh) used in calculating hourly transpiration when VPD is above VPDThresh</summary>
        [Description("Reduction in the part of hourly transpiration above Tr at VPDThresh (0-1; set 1 to cap at VPDThresh)")]
        [Units("-")]
        [Bounds(Lower = 0, Upper = 1)]
        public double HighVPDReduction { get; set; } = 1;

        /// <summary>The maximum hourly transpiration rate</summary>
        [Description("Maximum hourly transpiration rate")]
        [Units("mm/hr")]
        [Bounds(Lower = 0.01, Upper = 1000.0)]
        public double MaxTr { get; set; } = 999;

        /// <summary>The MaximumTempWeight</summary>
        [Description("The weight of maximim temperature for calculating mean temperature (negative to use hourly data)")]
        public double MaximumTempWeight { get; set; } = -1;

        /// <summary>The KDIF</summary>
        [Description("KDIF")]
        public double KDIF { get; set; } = 0.7;

        /// <summary>LUE at low light at 340ppm and 20C</summary>
        [Description("LUE at low light at 340ppm and 20C")]
        [Units("(kgCO2/ha/h / J/m2/s)")]
        public double LUEref { get; set; } = 0.6;

        /// <summary>Leaf gross photosynthesis rate at 340ppm CO2</summary>
        [Description("Maximum gross photosynthesis rate Pmax")]
        [Units("(kgCO2/ha/h)")]
        public double PgMax { get; set; } = 45;

        /// <summary>Photosynthesis Pathway</summary>
        [Description("Photosynthesis pathway C3/C4")]
        public string Pathway { get; set; } = "C3";

        //------------------------------------------------------------------------------------------------
        /// <summary>The maximum hourly VPD without capping (kPa)</summary>
        [XmlIgnore]
        [Units("kPa")]
        public double MaxHrVPD { get; set; } = 0;

        /// <summary>The mean VPD during day time (kPa)</summary>
        [XmlIgnore]
        [Units("kPa")]
        public double MeanDayVPD { get; set; } = 0;

        /// <summary>Growth stress factor (actual biomass assimilate / potential biomass assimilate)</summary>
        [XmlIgnore]
        [Units("-")]
        private double DmStressFactor { get; set; } = 0;
        //------------------------------------------------------------------------------------------------

        private double latR;
        private double transpEffCoef;
        private double minVPD;

        private List<double> hourlyTemp;
        private List<double> hourlyRad;
        private List<double> hourlySVP;
        private List<double> hourlyVPD;
        private List<double> hourlyRUE;
        private List<double> hourlyPotTr;
        private List<double> hourlyPotTr_VPDLimited;
        private List<double> hourlyPotTr_Limited;
        private List<double> hourlyActTr;
        private List<double> hourlyPotDM;
        private List<double> hourlyActDM;

        /// <summary>Total potential daily assimilation in g/m2</summary>
        private double dailyPotDM;

        /// <summary>Total daily assimilation in g/m2</summary>
        private double dailyActDM;

        /// <summary>The ratio of TEC of gross assimilate to TEC of net assimilate</summary>
        private double TECGrossToNet;

        //------------------------------------------------------------------------------------------------

        /// <summary>Daily growth increment of total plant biomass</summary>
        /// <returns>g dry matter/m2 soil/day</returns>
        public double Value(int arrayIndex = -1)
        {
            return dailyActDM;
        }

        //------------------------------------------------------------------------------------------------

        /// <summary>Daily growth increment of total plant biomass</summary>
        /// <returns>g dry matter/m2 soil/day</returns>
        [EventSubscribe("DoUpdateWaterDemand")]
        private void Calculate(object sender, EventArgs e)
        {
            double radiationInterception = RadnInt.Value();
            if (Double.IsNaN(radiationInterception))
                throw new Exception("NaN daily interception value supplied to RUE model");
            if (radiationInterception < 0)
                throw new Exception("Negative daily interception value supplied to RUE model");

            if (Plant.IsEmerged && radiationInterception > 0)
            {
                transpEffCoef = TEC.Value() * 1e3;
                if (!string.Equals(AssimilateType, "net"))
                {
                    double grossAssim = 0;
                    double respiration = 0;

                    foreach (IOrgan o in organs)
                        respiration += o.MaintenanceRespiration;
                    foreach (IArbitration o in organs)
                        grossAssim += o.DMSupply.Fixation;

                    double netAssim = Math.Max(0, grossAssim - respiration);
                    TECGrossToNet = MathUtilities.Divide(grossAssim, netAssim, 1);
                    transpEffCoef *= TECGrossToNet * 30 / 12;
                }

                CalcTemperature();
                CalcSVP();
                CalcVPD();
                if (string.Equals(AssimilateType, "net") || (GrossAssimilateModel != null))
                {
                    CalcRadiation();
                    CalcRUE();
                }
                CalcPotAssimilate();
                CalcPotTr();
                CalcVPDLimitedTr();
                CalcTrCappedTr();
                CalcSoilLimitedTr();
                CalcActAssimilate();
                Plant.Leaf.PotentialEP = hourlyPotTr_Limited.Sum();
                Plant.Leaf.WaterDemand = hourlyActTr.Sum();
            }
            else
            {
                hourlyActDM = new List<double>();
                for (int i = 0; i < 24; i++) hourlyActDM.Add(0.0);
                Plant.Leaf.PotentialEP = 0;
                Plant.Leaf.WaterDemand = 0;
                dailyActDM = 0;
                MeanDayVPD = 0;
                MaxHrVPD = 0;
                DmStressFactor = 0;
            }
        }
        //------------------------------------------------------------------------------------------------

        private void CalcTemperature()
        {
            // Hourly temperatures are only estimated for the day time (sunrise to sunset).
            // At other times, temperature is assumed equal to daily minimum temperature.
            // These times of day do not affect biomass assimilation (i.e. no radiation, no assimilation).

            hourlyTemp = new List<double>();
            for (int i = 0; i < 24; i++) hourlyTemp.Add(Weather.MinT);
            double TempMethod = 1;

            if (TempMethod == 1)
            {
                // A Model for Diurnal Variation in Soil and Air Temperature.
                // William J. Parton and Jesse A. Logan : Agricultural Meteorology, 23 (1991) 205-216.
                // Developed by Greg McLean and adapted/modified by Behnam (Ben) Ababaei.

                double minLag = 1.0;        // 0.17;  // c, Greg=1.0
                double maxLag = 1.5;        // 1.86;  // a, Greg=1.5
                double nightCoef = 4;       // 2.20;  // b, Greg=4.0

                double DayL = DayLength();                                      // Astronomical daylength (hr)
                double nightL = (24.0 - DayL);                                  // night length, hours
                double riseHour = 0.5 * (24 - DayL);
                double setHour = riseHour + DayL;                               // determine if the hour is during the day or night
                double tTmin = riseHour - minLag;                               // time of daily min temperature
                List<double> hrWeights = HourlyWeights(riseHour, setHour);

                // To make sure maximum temperature is alwasy larger than minimum temperature.
                double AddTemp = 0;
                if (Weather.MaxT - Weather.MinT <= 0) AddTemp = Math.Max(Math.Abs(Weather.MaxT - Weather.MinT) - 1, 0) + 1;

                for (int t = 1; t <= 24; t++)
                {
                    if (Math.Ceiling(hrWeights[t - 1]) == 1)
                    {
                        double Hour1 = Math.Min(setHour, Math.Max(riseHour, t - 1));
                        double Hour2 = Math.Min(setHour, Math.Max(riseHour, t));
                        double hr = 0.5 * (Hour1 + Hour2);
                        double tempr;

                        if (hr >= tTmin && hr < setHour)  //day
                        {
                            double m = 0; // the number of hours after the minimum temperature occurs
                            m = hr - tTmin;
                            tempr = (Weather.MaxT - Weather.MinT + AddTemp) * Math.Sin((Math.PI * m) / (DayL + 2 * maxLag)) + Weather.MinT;
                        }
                        else  // night
                        {
                            double n = 0;                       // the number of hours after setHour
                            if (hr > setHour) n = hr - setHour;
                            if (hr < tTmin) n = (24.0 - setHour) + hr;
                            double ddy = DayL - minLag;         // time of setHour after minimum temperature occurs
                            double tsn = (Weather.MaxT - Weather.MinT + AddTemp) * Math.Sin((Math.PI * ddy) / (DayL + 2 * maxLag)) + Weather.MinT;
                            tempr = Weather.MinT + (tsn - Weather.MinT) * Math.Exp(-nightCoef * n / nightL);
                        }
                        hourlyTemp[t - 1] = tempr;
                    }
                }

                double MeanDayTemp = hourlyTemp.Average();
                // for (int i = 0; i < 24; i++) hourlyTemp[i] = hourlyTemp[i] / MeanTemp * Weather.MeanT;
            }
        }
        //------------------------------------------------------------------------------------------------

        private void CalcRadiation()
        {
            // Calculates the ground solar incident radiation per hour and scales it to the actual radiation
            // Developed by Greg McLean and adapted/modified by Behnam (Ben) Ababaei.

            hourlyRad = new List<double>();
            for (int i = 0; i < 24; i++) hourlyRad.Add(0.0);

            List<double> hrWeights;
            latR = Math.PI / 180.0 * Weather.Latitude;       // convert latitude (degrees) to radians
            double GlobalRadiation = Weather.Radn * 1e6;     // solar radiation
            double PI = Math.PI;
            double RAD = PI / 180.0;

            //Declination of the sun as function of Daynumber (vDay)
            double Dec = -Math.Asin(Math.Sin(23.45 * RAD) * Math.Cos(2.0 * PI * ((double)Clock.Today.DayOfYear + 10.0) / 365.0));

            //vSin, vCos and vRsc are intermediate variables
            double Sin = Math.Sin(latR) * Math.Sin(Dec);
            double Cos = Math.Cos(latR) * Math.Cos(Dec);
            double Rsc = Sin / Cos;

            //Astronomical daylength (hr)
            double DayL = 12.0 * (1.0 + 2.0 * Math.Asin(Sin / Cos) / PI);
            double DailySinE = 3600.0 * (DayL * (Sin + 0.4 * (Sin * Sin + Cos * Cos * 0.5))
                     + 12.0 * Cos * (2.0 + 3.0 * 0.4 * Sin) * Math.Sqrt(1.0 - Rsc * Rsc) / PI);

            double riseHour = 0.5 * (24 - DayL);
            double setHour = riseHour + DayL;
            hrWeights = HourlyWeights(riseHour, setHour);

            for (int t = 1; t <= 24; t++)
            {
                double Hour1 = Math.Min(setHour, Math.Max(riseHour, t - 1));
                double SinHeight1 = Math.Max(0.0, Sin + Cos * Math.Cos(2.0 * PI * (Hour1 - 12.0) / 24.0));
                double Hour2 = Math.Min(setHour, Math.Max(riseHour, t));
                double SinHeight2 = Math.Max(0.0, Sin + Cos * Math.Cos(2.0 * PI * (Hour2 - 12.0) / 24.0));
                double SinHeight = 0.5 * (SinHeight1 + SinHeight2);
                hourlyRad[t - 1] = Math.Max(0, GlobalRadiation * SinHeight * (1.0 + 0.4 * SinHeight) / DailySinE);
                hourlyRad[t - 1] *= hrWeights[t - 1];
            }

            // scale to today's radiation
            double TotalRad = hourlyRad.Sum();
            for (int i = 0; i < 24; i++) hourlyRad[i] = hourlyRad[i] / TotalRad * RadnInt.Value();
        }
        //------------------------------------------------------------------------------------------------

        private void CalcSVP()
        {
            // Calculates hourlySVP at the air temperature in hPa
            hourlySVP = new List<double>();
            for (int i = 0; i < 24; i++) hourlySVP.Add(MetUtilities.svp(hourlyTemp[i]));
        }
        //------------------------------------------------------------------------------------------------

        private void CalcVPD()
        {
            // Calculates hourlyVPD at the air temperature in kPa
            hourlyVPD = new List<double>();
            minVPD = -1;
            for (int i = 0; i < 24; i++)
            {
                double vpd = 0.1 * (MetUtilities.svp(hourlyTemp[i]) - MetUtilities.svp(Weather.MinT));
                hourlyVPD.Add(vpd);
                if (vpd > 0 && minVPD < 0) minVPD = vpd;
            }
            MeanDayVPD = hourlyVPD.Where(v => v > 0).Average();
            MaxHrVPD = hourlyVPD.Max();
        }
        //------------------------------------------------------------------------------------------------

        private void CalcRUE()
        {
            // Calculates hourlyRUE as the product of reference RUE, temperature, N and CO2.
            // Hourly total plant "actual" radiation use efficiency corrected by reducing factors
            // (g biomass / MJ global solar radiation)
            double rueFactor;
            double tempResponse = 1.0;
            hourlyRUE = new List<double>();

            for (int i = 0; i < 24; i++)
            {
                if (FT != null) tempResponse = FT.Value();

                rueFactor = tempResponse * FN.Value() * FCO2.Value();
                hourlyRUE.Add(RUE.Value() * rueFactor);
            }
        }
        //------------------------------------------------------------------------------------------------

        private void CalcPotAssimilate()
        {
            // Calculates hourlyPotDM as the product of hourlyRUE, hourlyVPD and transpEffCoef
            hourlyPotDM = new List<double>();

            if (string.Equals(AssimilateType, "net") || (GrossAssimilateModel != null))
            {
                for (int i = 0; i < 24; i++) hourlyPotDM.Add(hourlyRad[i] * hourlyRUE[i]);
            }
            else
            {
                hourlyPotDM = DailyGrossPhotosythesis(Plant.Leaf.LAI, Weather.Latitude, Clock.Today.DayOfYear,
                    Weather.Radn, Weather.CO2, Weather.DiffuseFraction, 1.0);
            }
        }
        //------------------------------------------------------------------------------------------------

        private void CalcPotTr()
        {
            // Calculates hourlyPotTr as the product of hourlyRUE, hourlyVPD and transpEffCoef
            hourlyPotTr = new List<double>();
            for (int i = 0; i < 24; i++) hourlyPotTr.Add(hourlyPotDM[i] * hourlyVPD[i] / transpEffCoef);
        }
        //------------------------------------------------------------------------------------------------

        private void CalcVPDLimitedTr()
        {
            // Calculates hourlyVPDCappedTr as the product of hourlyRUE, capped hourlyVPD and transpEffCoef

            double dmVPDThresh = 0;
            if (hourlyVPD.Max() > VPDThresh)
            {
                hourlyPotTr_VPDLimited = new List<double>();
                XYPairs interpol = new XYPairs
                {
                    X = hourlyVPD.ToArray(),
                    Y = hourlyPotDM.ToArray()
                };
                dmVPDThresh = interpol.ValueIndexed(Math.Max(VPDThresh, minVPD));

                for (int i = 0; i < 24; i++)
                {
                    if (hourlyVPD[i] > VPDThresh)
                    {
                        double trVPDThresh = dmVPDThresh * VPDThresh / transpEffCoef; // Hourly transpiration at VPDThresh
                        double reduction = Math.Max(0, hourlyPotTr[i] - trVPDThresh) * HighVPDReduction; // Reduction in the part of hourly transpiration above trVPDThresh
                        hourlyPotTr_VPDLimited.Add(hourlyPotTr[i] - reduction);
                    }
                    else
                        hourlyPotTr_VPDLimited.Add(hourlyPotTr[i]);
                }
            }
            else
            {
                hourlyPotTr_VPDLimited = new List<double>(hourlyPotTr);
            }
        }
        //------------------------------------------------------------------------------------------------

        private void CalcTrCappedTr()
        {
            // Calculates hourlyTrCappedTr by capping hourlyVPDCappedTr at MaxTr
            hourlyPotTr_Limited = new List<double>();
            for (int i = 0; i < 24; i++) hourlyPotTr_Limited.Add(Math.Min(hourlyPotTr_VPDLimited[i], MaxTr));
        }
        //------------------------------------------------------------------------------------------------

        private void CalcSoilLimitedTr()
        {
            // Sets hourlyTr = hourlyVPDCappedTr and scales value at each hour until sum of hourlyTr = rootWaterSupp
            hourlyActTr = new List<double>(hourlyPotTr_Limited);

            double rootWaterSupp = Root.TotalExtractableWater();
            double reduction = 0.99;
            double maxHourlyT = hourlyActTr.Max();
            double minPercError = 0.5;
            double sumTr = hourlyActTr.Sum();
            double dailyActTr = hourlyActTr.Sum();

            // It's assumed that total extractable water is evenly distributed over the day time.
            // Maximum hourly transpiration cannot exceed the maximum hourly supply.
            // double maxUptake = rootWaterSupp / DayLength();
            // for (int i = 0; i < 24; i++) hourlyActTr[i] = Math.Min(maxUptake, hourlyActTr[i]);

            if (dailyActTr - rootWaterSupp > 1e-5)
            {
                if (string.Equals(SWType, "daily"))
                {
                    for (int i = 0; i < 24; i++)
                        hourlyActTr[i] *= Math.Min(1, MathUtilities.Divide(rootWaterSupp, sumTr, 0));
                }
                else
                {
                    if (rootWaterSupp < 1e-8)
                    {
                        for (int i = 0; i < 24; i++) hourlyActTr[i] = 0;
                    }
                    else if (string.Equals(SWType, "dcaps"))
                    {
                        if (rootWaterSupp < 1e-8)
                            for (int i = 0; i < 24; i++) hourlyActTr[i] = 0;
                        else
                        {
                            while (((hourlyActTr.Sum() - rootWaterSupp) / rootWaterSupp * 100) > minPercError)
                            {
                                maxHourlyT *= reduction;
                                for (int i = 0; i < 24; i++)
                                {
                                    double diff = Math.Max(0, hourlyActTr.Sum() - rootWaterSupp);
                                    if (hourlyActTr[i] >= maxHourlyT) hourlyActTr[i] = Math.Max(maxHourlyT, hourlyActTr[i] - diff);
                                }
                            }
                        }
                    }
                    else if (string.Equals(SWType, "numeric"))
                    {
                        double threshold = hourlyActTr.Max() / 2;
                        double upperLimit = hourlyActTr.Max();
                        double lowerLimit = 0;

                        while (true)
                        {
                            List<double> hourlyActTrTemp = new List<double>(hourlyPotTr_Limited);
                            for (int i = 0; i < 24; i++) hourlyActTrTemp[i] = Math.Min(hourlyActTrTemp[i], threshold);

                            if (Math.Abs((hourlyActTrTemp.Sum() - rootWaterSupp) / rootWaterSupp * 100) < minPercError)
                            {
                                hourlyActTr = new List<double>(hourlyActTrTemp);
                                break;
                            }
                            else
                            {
                                if (hourlyActTrTemp.Sum() < rootWaterSupp)
                                {
                                    lowerLimit = threshold;
                                    threshold = 0.5 * (threshold + upperLimit);
                                }
                                else
                                {
                                    upperLimit = threshold;
                                    threshold = 0.5 * (threshold + lowerLimit);
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("SWType must be one of 'daily', 'dcaps' or 'numeric'");
                    }
                }

                // Final tune
                if (!string.Equals(SWType, "daily"))
                {
                    sumTr = hourlyActTr.Sum();
                    for (int i = 0; i < 24; i++) hourlyActTr[i] *= MathUtilities.Divide(rootWaterSupp, sumTr, 0);
                }
            }
        }
        //------------------------------------------------------------------------------------------------

        private void CalcActAssimilate()
        {
            // Calculates hourlyDM and hourlyPotDM as a product of RUE, TR, TEC and hourlyVPD.
            // If there is a link to a grossAssimilateModel model, it will be used and its outputs
            // will be scaled to follow the same durnal pattern as that of the hourly RUE model.
            // If no such a link exists, the 'net' daily assimilate is calculated.

            hourlyActDM = new List<double>();
            for (int i = 0; i < 24; i++) hourlyActDM.Add(MathUtilities.Divide(hourlyActTr[i] * transpEffCoef, hourlyVPD[i], 0));

            dailyPotDM = hourlyPotDM.Sum();
            dailyActDM = hourlyActDM.Sum();

            if (GrossAssimilateModel != null && hourlyPotDM.Sum() > 0 && hourlyActDM.Sum() > 0)
            {
                double dailyActGrossDM = hourlyActDM.Sum() / hourlyPotDM.Sum() * GrossAssimilateModel.Value();
                if (double.IsNaN(GrossAssimilateModel.Value()))
                    dailyActGrossDM = 0;

                if (double.IsNaN(dailyActGrossDM))
                    dailyActDM = 0;
                else
                {
                    dailyActDM = hourlyActDM.Sum();
                    if (dailyActDM > 0 && !double.IsNaN(dailyActGrossDM))
                        for (int i = 0; i < 24; i++) hourlyActDM[i] = hourlyActDM[i] / dailyActDM * dailyActGrossDM;
                    else
                        for (int i = 0; i < 24; i++) hourlyActDM[i] = 0;
                    dailyActDM = hourlyActDM.Sum();
                }
            }

            DmStressFactor = MathUtilities.Round(MathUtilities.Divide(dailyActDM, dailyPotDM, 0), 3);
        }
        //------------------------------------------------------------------------------------------------

        private List<double> DailyGrossPhotosythesis(double LAI, double Latitude, int Day, double Radn, double CO2, double DifFr, double Fact)
        {
            List<double> HourlyGrossB = new List<double>(24);
            double GrossPs, Temp, PAR, PARDIF, PARDIR;

            //double GlobalRadiation, SinHeight, DayL, AtmTrans, DifFr;
            double GlobalRadiation, SinHeight, DayL, AtmTrans;
            double Dec, Sin, Cos, Rsc, SolarConst, DailySin, DailySinE, RadExt;
            double LUE, PgMax;

            //double[] xGauss = { 0.0469101, 0.2307534, 0.5000000, 0.7692465, 0.9530899 };
            //double[] wGauss = { 0.1184635, 0.2393144, 0.2844444, 0.2393144, 0.1184635 };
            List<double> hrWeights = new List<double>(24);
            double PI = Math.PI;
            double RAD = PI / 180.0;

            GlobalRadiation = (double)Radn * 1E6;    //J/m2.d

            //===========================================================================================
            //Dailenght, Solar constant and daily extraterrestrial radiation
            //===========================================================================================
            //Declination of the sun as function of Daynumber (vDay)

            Dec = -Math.Asin(Math.Sin(23.45 * RAD) * Math.Cos(2.0 * PI * ((double)Day + 10.0) / 365.0));

            //vSin, vCos and vRsc are intermediate variables
            Sin = Math.Sin(RAD * Latitude) * Math.Sin(Dec);
            Cos = Math.Cos(RAD * Latitude) * Math.Cos(Dec);
            Rsc = Sin / Cos;

            //Astronomical daylength (hr)
            DayL = 12.0 * (1.0 + 2.0 * Math.Asin(Sin / Cos) / PI);

            double riseHour = 0.5 * (24 - DayL);
            double setHour = riseHour + DayL;
            hrWeights = HourlyWeights(riseHour, setHour);

            //Sine of solar height(vDailySin), inegral of vDailySin(vDailySin) and integrel of vDailySin
            //with correction for lower atmospheric transmission at low solar elevations (vDailySinE)
            DailySin = 3600.0 * (DayL * Sin + 24.0 * Cos * Math.Sqrt(1.0 - Rsc * Rsc) / PI);
            DailySinE = 3600.0 * (DayL * (Sin + 0.4 * (Sin * Sin + Cos * Cos * 0.5))
                     + 12.0 * Cos * (2.0 + 3.0 * 0.4 * Sin) * Math.Sqrt(1.0 - Rsc * Rsc) / PI);

            //Solar constant(vSolarConst) and daily extraterrestrial (vRadExt)
            SolarConst = 1370.0 * (1.0 + 0.033 * Math.Cos(2.0 * PI * (double)Day / 365.0));   //J/m2.d
            RadExt = SolarConst * DailySin * 1E-6;               //MJ/m2.d

            //===========================================================================================
            //Daily photosynthesis
            //===========================================================================================

            for (int t = 0; t < 24; t++)
            {
                //Sine of solar elevation
                double Hour1 = Math.Min(setHour, Math.Max(riseHour, t));
                double SinHeight1 = Math.Max(0.0, Sin + Cos * Math.Cos(2.0 * PI * (Hour1 - 12.0) / 24.0));
                double Hour2 = Math.Min(setHour, Math.Max(riseHour, t + 1));
                double SinHeight2 = Math.Max(0.0, Sin + Cos * Math.Cos(2.0 * PI * (Hour2 - 12.0) / 24.0));
                SinHeight = 0.5 * (SinHeight1 + SinHeight2);

                //Diffuse light fraction (vDifFr) from atmospheric transmission (vAtmTrans)
                PAR = 0.5 * GlobalRadiation * SinHeight * (1.0 + 0.4 * SinHeight) / DailySinE;

                if (PAR > 1e-10)
                {
                    AtmTrans = PAR / (0.5 * SolarConst * SinHeight);

                    if (DifFr < 0)
                    {
                        if (AtmTrans <= 0.22)
                            DifFr = 1.0;
                        else
                        {
                            if ((AtmTrans > 0.22) && (AtmTrans <= 0.35))
                                DifFr = 1.0 - 6.4 * (AtmTrans - 0.22) * (AtmTrans - 0.22);
                            else
                                DifFr = 1.47 - 1.66 * AtmTrans;
                        }
                        DifFr = Math.Max(DifFr, 0.15 + 0.85 * (1.0 - Math.Exp(-0.1 / SinHeight)));
                    }

                    if (DifFr < 0 | DifFr > 1)
                        throw new Exception("Diffuse fraction should be between 0 and 1.");

                    //Diffuse PAR (PARDIF) and direct PAR (PARDIR)
                    PARDIF = Math.Min(PAR, SinHeight * DifFr * AtmTrans * 0.5 * SolarConst);
                    PARDIR = PAR - PARDIF;

                    if (MaximumTempWeight > 1)
                        throw new Exception("MaximumTempWeight fraction should be between 0 and 1, or negative to be ignored");

                    if (MaximumTempWeight > 0)
                        Temp = MaximumTempWeight * Weather.MaxT + (1 - MaximumTempWeight) * Weather.MinT;
                    else
                        Temp = hourlyTemp[t];

                    //Light response parameters
                    PgMax = MaxGrossPhotosynthesis(CO2, Temp, Fact);
                    LUE = LightUseEfficiency(Temp, CO2);

                    //Canopy gross photosynthesis
                    GrossPs = HourlyGrossPhotosythesis(PgMax, LUE, LAI, Latitude, Day, SinHeight, PARDIR, PARDIF);
                    GrossPs *= hrWeights[t];
                }
                else
                    GrossPs = 0;

                HourlyGrossB.Add(GrossPs * 30 / 44 * 0.1);
            }
            return HourlyGrossB;
        }
        //------------------------------------------------------------------------------------------------

        private double HourlyGrossPhotosythesis(double fPgMax, double fLUE, double fLAI,
                              double fLatitude, int nDay, double fSINB, double fPARdir, double fPARdif)
        {
            int i, j;
            double PMAX, EFF, vLAI, PARDIR, PARDIF;
            double SQV, REFH, REFS, CLUSTF, KDIRBL, KDIRT, FGROS, LAIC, SINB, VISDF, VIST, VISD, VISSHD, FGRSH;
            double FGRSUN, VISSUN, VISPP, FGRS, FSLLA, FGL, LAT, DAY, DEC, vSin, vCos;
            int nGauss = 5;
            double[] xGauss = { 0.0469101, 0.2307534, 0.5000000, 0.7692465, 0.9530899 };
            double[] wGauss = { 0.1184635, 0.2393144, 0.2844444, 0.2393144, 0.1184635 };

            double PI = Math.PI;
            double RAD = PI / 180.0;

            double SCV = 0.20;    //Scattering coefficient of leaves for visible radiation (PAR)

            PMAX = fPgMax;
            EFF = fLUE;
            vLAI = fLAI;
            PARDIR = fPARdir;
            PARDIF = fPARdif;
            LAT = fLatitude;
            DAY = nDay;
            SINB = fSINB;

            //===========================================================================================
            //Sine of the solar height
            //===========================================================================================
            //Declination of the sun as function of Daynumber (vDay)
            DEC = -Math.Asin(Math.Sin(23.45 * RAD) * Math.Cos(2.0 * PI * (DAY + 10.0) / 365.0));

            //vSin, vCos and vRsc are intermediate variables
            vSin = Math.Sin(RAD * LAT) * Math.Sin(DEC);
            vCos = Math.Cos(RAD * LAT) * Math.Cos(DEC);

            //===========================================================================================
            //Reflection of horizontal and spherical leaf angle distribution
            SQV = Math.Sqrt(1.0 - SCV);
            REFH = (1.0 - SQV) / (1.0 + SQV);
            REFS = REFH * 2.0 / (1.0 + 2.0 * SINB);

            //Extinction coefficient for direct radiation and total direct flux
            CLUSTF = KDIF / (0.8 * SQV);
            KDIRBL = (0.5 / SINB) * CLUSTF;
            KDIRT = KDIRBL * SQV;

            //===========================================================================================
            //Selection of depth of canopy, canopy assimilation is set to zero
            FGROS = 0;
            for (i = 0; i < nGauss; i++)
            {
                LAIC = vLAI * xGauss[i];

                //Absorbed fluxes per unit leaf area: diffuse flux, total direct
                //flux, direct component of direct flux.
                VISDF = (1.0 - REFH) * PARDIF * KDIF * Math.Exp(-KDIF * LAIC);
                VIST = (1.0 - REFS) * PARDIR * KDIRT * Math.Exp(-KDIRT * LAIC);
                VISD = (1.0 - SCV) * PARDIR * KDIRBL * Math.Exp(-KDIRBL * LAIC);

                //Absorbed flux (J/M2 leaf/s) for shaded leaves and assimilation of shaded leaves
                VISSHD = VISDF + VIST - VISD;
                if (PMAX > 0.0)
                    FGRSH = PMAX * (1.0 - Math.Exp(-VISSHD * EFF / PMAX));
                else
                    FGRSH = 0.0;

                //Direct flux absorbed by leaves perpendicular on direct beam and
                //assimilation of sunlit leaf area
                VISPP = (1.0 - SCV) * PARDIR / SINB;

                FGRSUN = 0.0;
                for (j = 0; j < nGauss; j++)
                {
                    VISSUN = VISSHD + VISPP * xGauss[j];

                    if (PMAX > 0.0)
                        FGRS = PMAX * (1.0 - Math.Exp(-VISSUN * EFF / PMAX));
                    else
                        FGRS = 0.0;

                    FGRSUN = FGRSUN + FGRS * wGauss[j];
                }

                //Fraction sunlit leaf area (FSLLA) and local assimilation rate (FGL)
                FSLLA = CLUSTF * Math.Exp(-KDIRBL * LAIC);
                FGL = FSLLA * FGRSUN + (1.0 - FSLLA) * FGRSH;

                //Integration of local assimilation rate to canopy assimilation (FGROS)
                FGROS = FGROS + FGL * wGauss[i];
            }

            FGROS = FGROS * vLAI;
            return FGROS;
        }
        //------------------------------------------------------------------------------------------------

        private double LightUseEfficiency(double Temp, double fCO2)
        {
            double CO2PhotoCmp0, CO2PhotoCmp, EffPAR;

            //Efect of CO2 concentration
            if (fCO2 < 0.0) fCO2 = WeatherConstants.DEFAULT_CO2;

            //Check wheather a C3 or C4 crop
            if (Pathway == "C3")   //C3 plants
            {
                CO2PhotoCmp0 = 38.0; //vppm
                CO2PhotoCmp = CO2PhotoCmp0 * Math.Pow(2.0, (Temp - 20.0) / 10.0); //Efect of Temperature on CO2PhotoCmp
                fCO2 = Math.Max(fCO2, CO2PhotoCmp);

                //--------------------------------------------------------------------------------------------------------------
                //Original SPASS version based on Goudriaan & van Laar (1994)
                //LUEref is the LUE at reference temperature of 20C and CO2=340ppm, i.e., LUEref = 0.6 kgCO2/ha/h / J/m2/s
                //EffPAR   = LUEref * (fCO2-fCO2PhotoCmp)/(fCO2+2*fCO2PhotoCmp);

                //--------------------------------------------------------------------------------------------------------------
                //The following equations were from Bauman et al (2001) ORYZA2000
                //The tempeature function was standardised to 20C.
                //LUEref is the LUE at reference temperature of 20C and CO2=340ppm, i.e., LUEref = 0.48 kgCO2/ha/h / J/m2/s
                double Ft = (0.6667 - 0.0067 * Temp) / (0.6667 - 0.0067 * 20);
                EffPAR = LUEref * Ft * (1.0 - Math.Exp(-0.00305 * fCO2 - 0.222)) / (1.0 - Math.Exp(-0.00305 * 340.0 - 0.222));
            }

            else if (Pathway == "C4")
            {
                CO2PhotoCmp = 0.0;

                //--------------------------------------------------------------------------------------------------------------
                //Original SPASS version based on Goudriaan & van Laar (1994)
                //LUEref is the LUE at reference temperature of 20C and CO2=340ppm, i.e., LUEref = 0.5 kgCO2/ha/h / J/m2/s
                EffPAR = LUEref * (fCO2 - CO2PhotoCmp) / (fCO2 + 2 * CO2PhotoCmp);
            }
            else
                throw new ApsimXException(this, "Need to be C3 or C4");

            return EffPAR * LUEStress.Value();
        }
        //------------------------------------------------------------------------------------------------

        private double MaxGrossPhotosynthesis(double CO2, double Temp, double Fact)
        {
            double CO2I, CO2I340, CO2Func, PmaxGross;
            double CO2ref, tempResponse;
            double CO2Cmp, CO2R;

            //------------------------------------------------------------------------
            //Efect of CO2 concentration of the air
            if (CO2 < 0.0) CO2 = WeatherConstants.DEFAULT_CO2;

            //Check wheather a C3 or C4 crop
            if (Pathway == "C3")   //C3 plants
            {
                CO2Cmp = 50; //CO2 compensation point (vppm), value based on Wang (1997) SPASS Table 3.4
                CO2R = 0.7;  //CO2 internal/external ratio of leaf(C3= 0.7, C4= 0.4)
                CO2ref = 340;

                CO2 = Math.Max(CO2, CO2Cmp);
                CO2I = CO2R * CO2;
                CO2I340 = CO2R * CO2ref;

                // For C3 crop, Original Code SPASS
                //   fCO2Func= min((float)2.3,(fCO2I-fCO2Cmp)/(fCO2I340-fCO2Cmp)); //For C3 crops
                //   fCO2Func= min((float)2.3,pow((fCO2I-fCO2Cmp)/(fCO2I340-fCO2Cmp),0.5)); //For C3 crops

                //For C3 rice, based on Bouman et al (2001) ORYZA2000: modelling lowland rice, IRRI Publication
                CO2Func = (49.57 / 34.26) * (1.0 - Math.Exp(-0.208 * (CO2 - 60.0) / 49.57));
            }
            else if (Pathway == "C4")
            {
                CO2Cmp = 5; //CO2 compensation point (vppm), value based on Wang (1997) SPASS Table 3.4
                CO2R = 0.4;  //CO2 internal/external ratio of leaf(C3= 0.7, C4= 0.4)
                CO2ref = 380;

                CO2 = Math.Max(CO2, CO2Cmp);
                CO2I = CO2R * CO2;

                //For C4 crop, AgPasture Proposed by Cullen et al. (2009) based on FACE experiments
                CO2Func = CO2 / (CO2 + 150) * (CO2ref + 150) / CO2ref;
            }

            else
                throw new ApsimXException(this, "Need to be C3 or C4");

            //------------------------------------------------------------------------
            //Temperature response and effect of daytime temperature
            tempResponse = WangEngelTempFunction(Temp);

            //------------------------------------------------------------------------
            //Maximum leaf gross photosynthesis
            double factor = tempResponse * CO2Func * Fact;
            PmaxGross = Math.Max(1.0, PgMax * factor);

            return PmaxGross;
        }
        //------------------------------------------------------------------------------------------------

        private double WangEngelTempFunction(double Temp, double MinTemp = 0, double OptTemp = 27.5, double MaxTemp = 40, double RefTemp = 27.5)
        {
            double RelEff = 0.0;
            double RelEffRefTemp = 1.0;
            double p = 0.0;

            if ((Temp > MinTemp) && (Temp < MaxTemp))
            {
                p = Math.Log(2.0) / Math.Log((MaxTemp - MinTemp) / (OptTemp - MinTemp));
                RelEff = (2 * Math.Pow(Temp - MinTemp, p) * Math.Pow(OptTemp - MinTemp, p) -
                    Math.Pow(Temp - MinTemp, 2 * p)) / Math.Pow(OptTemp - MinTemp, 2 * p);
            }

            if ((RefTemp > MinTemp) && (RefTemp < MaxTemp))
            {
                p = Math.Log(2.0) / Math.Log((MaxTemp - MinTemp) / (OptTemp - MinTemp));
                RelEffRefTemp = (2 * Math.Pow(RefTemp - MinTemp, p) * Math.Pow(OptTemp - MinTemp, p) -
                    Math.Pow(RefTemp - MinTemp, 2 * p)) / Math.Pow(OptTemp - MinTemp, 2 * p);
            }
            return RelEff / RelEffRefTemp;
        }
        //------------------------------------------------------------------------------------------------

        private double DayLength()
        {
            latR = Math.PI / 180.0 * Weather.Latitude;       // convert latitude (degrees) to radians
            double PI = Math.PI;
            double RAD = PI / 180.0;

            //Declination of the sun as function of Daynumber (vDay)
            double Dec = -Math.Asin(Math.Sin(23.45 * RAD) * Math.Cos(2.0 * PI * ((double)Clock.Today.DayOfYear + 10.0) / 365.0));

            //vSin and vCos are intermediate variables
            double Sin = Math.Sin(latR) * Math.Sin(Dec);
            double Cos = Math.Cos(latR) * Math.Cos(Dec);

            //Astronomical daylength (hr)
            double DayL = 12.0 * (1.0 + 2.0 * Math.Asin(Sin / Cos) / PI);
            return DayL;
        }
        //------------------------------------------------------------------------------------------------

        private List<double> HourlyWeights(double riseHour, double setHour)
        {
            List<double> weights = new List<double>(24);
            for (int t = 1; t <= 24; t++)
            {
                if (t < riseHour) weights.Add(0);
                else if (t > riseHour && t - 1 < riseHour) weights.Add(t - riseHour);
                else if (t >= riseHour && t < setHour) weights.Add(1);
                else if (t > setHour && t - 1 < setHour) weights.Add(1 - (t - setHour));
                else if (t > setHour) weights.Add(0);
            }
            return weights;
        }
        //------------------------------------------------------------------------------------------------
    }
}