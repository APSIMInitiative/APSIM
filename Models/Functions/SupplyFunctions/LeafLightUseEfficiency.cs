using System;
using Models.Climate;
using Models.Core;

namespace Models.Functions.SupplyFunctions
{
    /// <summary>
    /// This function calculate the light use efficiency (LUE) of leaf at low light (Eff).
    /// For C3 crops, LUE increases with CO2 concentration while decreases with rising temperature.
    /// For C4 crops, LUE does not change with CO2 concentration and temperature.
    /// The current version for C3 crop was based on Bauman et al (2001) ORYZA2000.
    /// </summary>

    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CanopyPhotosynthesis))]

    public class LeafLightUseEfficiency : Model
    {
        /// <summary>LUE at low light at 340ppm and 20C </summary>
        [Description("LUE at low light at 340ppm and 20C (kgCO2/ha/h / J/m2/s)")]
        public double LUEref { get; set; }
        /// <summary>Photosynthesis pathway (C3/C4)</summary>
        [Description("Photosynthesis pathway (C3/C4)")]
        public string pathway { get; set; }

        /// <param name="Temp"></param>
        /// <param name="fCO2"></param>
        /// <returns></returns>

        public double Value(double Temp, double fCO2)
        {
            double CO2PhotoCmp0, CO2PhotoCmp, EffPAR;

            //Efect of CO2 concentration
            if (fCO2 < 0.0) fCO2 = WeatherConstants.DEFAULT_CO2;

            //Check wheather a C3 or C4 crop
            if (pathway == "C3")   //C3 plants
            {
                CO2PhotoCmp0 = 38.0; //vppm
                CO2PhotoCmp = CO2PhotoCmp0 * Math.Pow(2.0, (Temp - 20.0) / 10.0); //Efect of Temperature on CO2PhotoCmp
                fCO2 = Math.Max(fCO2, CO2PhotoCmp);

                //--------------------------------------------------------------------------------------------------------------
                //Original SPASS version based on Goudriaan && van Laar (1994)
                //LUEref is the LUE at reference temperature of 20C and CO2=340ppm, i.e., LUEref = 0.6 kgCO2/ha/h / J/m2/s
                //EffPAR   = LUEref * (fCO2-fCO2PhotoCmp)/(fCO2+2*fCO2PhotoCmp);

                //--------------------------------------------------------------------------------------------------------------
                //The following equations were from Bauman et al (2001) ORYZA2000 
                //The tempeature function was standardised to 20C.
                //LUEref is the LUE at reference temperature of 20C and CO2=340ppm, i.e., LUEref = 0.48 kgCO2/ha/h / J/m2/s
                double Ft = (0.6667 - 0.0067 * Temp) / (0.6667 - 0.0067 * 20);
                EffPAR = LUEref * Ft * (1.0 - Math.Exp(-0.00305 * fCO2 - 0.222)) / (1.0 - Math.Exp(-0.00305 * 340.0 - 0.222));
            }

            else if (pathway == "C4")
            {
                CO2PhotoCmp = 0.0;

                //--------------------------------------------------------------------------------------------------------------
                //Original SPASS version based on Goudriaan && van Laar (1994)
                //LUEref is the LUE at reference temperature of 20C and CO2=340ppm, i.e., LUEref = 0.5 kgCO2/ha/h / J/m2/s
                EffPAR = LUEref * (fCO2 - CO2PhotoCmp) / (fCO2 + 2 * CO2PhotoCmp);

            }
            else
                throw new ApsimXException(this, "Need to be C3 or C4");


            return EffPAR;
        }
    }
}