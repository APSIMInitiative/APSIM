using System;
using Models.Climate;
using Models.Core;

namespace Models.Functions.SupplyFunctions
{
    /// <summary>
    /// Leaf gross photosynthesis rate is determined using an input gross photosynthesis rate at reference CO2 concentration (340~350ppm) and at optimal temperature of 27.5C, 
    /// together with the CO2 concentrartion in the air and the daily daytime temperature. 
    /// For C3 crop, the Ps-CO2 relationship used is from ORYZA2000 Bauman et al (2001)
    /// For C4 crop, the Ps-CO2 relationship is based on what used for AgPasture Proposed by Cullen et al. (2009) based on FACE experiments
    /// The temperature response function is the WangEngel fucntion with cardinal tempearture of 0, 27.5 and 35C based on Wang et al (2017)
    /// </summary>
    /// 
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CanopyPhotosynthesis))]

    public class LeafMaxGrossPhotosynthesis : Model
    {
        /// <summary>Leaf gross photosynthesis rate at 340ppm CO2 </summary>
        [Description("Maximum gross photosynthesis rate Pmax (kgCO2/ha/h)")]
        public double Pgmmax { get; set; }
        /// <summary>Photosynthesis pathway (C3/C4)</summary>
        [Description("Photosynthesis pathway C3/C4")]
        public string pathway { get; set; }

        /// <summary>Function to return a temperature factor for a given function </summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private WangEngelTempFunction TemperatureResponse = null;

        /// <param name="CO2"></param>
        /// <param name="Fact"></param>
        /// <returns></returns>
        /// 

        public double Value(double CO2, double Fact)
        {
            double TempFunc, CO2I, CO2I340, CO2Func, PmaxGross;
            double CO2ref;
            double CO2Cmp, CO2R;

            //------------------------------------------------------------------------
            //Efect of CO2 concentration of the air
            if (CO2 < 0.0) CO2 = WeatherConstants.DEFAULT_CO2;

            //Check wheather a C3 or C4 crop
            if (pathway == "C3")   //C3 plants
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
            else if (pathway == "C4")
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
            //Temperature response and Efect of daytime temperature

            TempFunc = TemperatureResponse.Value();

            //------------------------------------------------------------------------
            //Maximum leaf gross photosynthesis
            PmaxGross = Math.Max((float)1.0, Pgmmax * (CO2Func * TempFunc * Fact));

            return PmaxGross;
        }
    }
}