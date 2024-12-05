using System;
using Models.Core;
using Models.Interfaces;

namespace Models.Functions.SupplyFunctions
{
    /// <summary>
    /// Biomass fixation is modelled as the product of intercepted radiation and its conversion efficiency, the radiation use efficiency (RUE) ([Monteith1977]).  
    /// This approach simulates net photosynthesis rather than providing separate estimates of growth and respiration.  
    /// The potential photosynthesis calculated using RUE is then adjusted according to stress factors, these account for plant nutrition (FN), air temperature (FT), vapour pressure deficit (FVPD), water supply (FW) and atmospheric CO~2~ concentration (FCO2).  
    /// NOTE: RUE in this model is expressed as g/MJ for a whole plant basis, including both above and below ground growth.
    /// </summary>
    [Serializable]
    [ValidParent(ParentType = typeof(ICanopy))]
    public class RUEModel : Model, IFunction
    {
        /// <summary>The rue</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("g/MJ")]
        IFunction RUE = null;

        /// <summary>The fc o2</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("0-1")]
        IFunction FCO2 = null;

        /// <summary>The function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("0-1")]
        IFunction FN = null;

        /// <summary>The ft</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("0-1")]
        public IFunction FT = null;

        /// <summary>The fw</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("0-1")]
        IFunction FW = null;

        /// <summary>The FVPD</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("0-1")]
        public IFunction FVPD = null;

        /// <summary>The met data</summary>
        [Link]
        IWeather MetData = null;

        /// <summary>The radiation interception data</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("MJ/m^2/d")]
        public IFunction RadnInt = null;


        /// <summary>Gets the VPD.</summary>
        /// <value>The VPD.</value>
        public double VPD
        {
            get
            {
                if (MetData != null)
                    return MetData.VPD;
                else
                    return 0;
            }
        }

        /// <summary>
        /// Combined stress effects on biomass production
        /// </summary>
        /// <value>The rue act.</value>
        public double RueReductionFactor
        {
            get
            {
                return Math.Min(FT.Value(), Math.Min(FN.Value(), FVPD.Value())) * FW.Value() * FCO2.Value();
            }
        }

        /// <summary>
        /// Total plant "actual" radiation use efficiency (for the day) corrected by reducing factors (g biomass/MJ global solar radiation) CHCK-EIT
        /// </summary>
        /// <value>The rue act.</value>
        [Units("gDM/MJ")]
        public double RueAct
        {
            get
            {
                return RUE.Value() * RueReductionFactor;
            }
        }
        /// <summary>Daily growth increment of total plant biomass</summary>
        /// <returns>g dry matter/m2 soil/day</returns>
        public double Value(int arrayIndex = -1)
        {
            double radiationInterception = RadnInt.Value(arrayIndex);
            if (Double.IsNaN(radiationInterception))
                throw new Exception("NaN Radiation interception value supplied to RUE model");
            if (radiationInterception < -0.000000000001)
                throw new Exception("Negative Radiation interception value supplied to RUE model");
            return radiationInterception * RueAct;
        }
    }
}