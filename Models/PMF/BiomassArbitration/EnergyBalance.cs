using System;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Functions;
using Models.Interfaces;
using Models.PMF.Interfaces;
using Models.PMF.Organs;
using Newtonsoft.Json;

namespace Models.PMF
{

    /// <summary>
    /// This organ is simulated using a  organ type.  It provides the core functions of intercepting radiation
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(GenericOrgan))]
    [ValidParent(ParentType = typeof(Organ))]
    public class EnergyBalance : Model, ICanopy, IHasWaterDemand
    {
        /// <summary>The plant</summary>
        [Link]
        private Plant Plant = null;

        /// <summary>The met data</summary>
        [Link]
        public IWeather MetData = null;

        /// <summary>The parent plant</summary>
        [Link]
        private Plant parentPlant = null;

        /// <summary>The FRGR function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction FRGRer = null;

        /// <summary>The effect of CO2 on stomatal conductance</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction StomatalConductanceCO2Modifier = null;

        /// <summary>The green area index</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction GreenAreaIndex = null;

        /// <summary>The extinction coefficient of green material</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction GreenExtinctionCoefficient = null;

        /// <summary>The extinction coefficient of dead material function</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction DeadExtinctionCoefficient = null;

        /// <summary>The height of the top of the canopy</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction Tallness = null;

        /// <summary>TThe depth of canopy which organ resides in</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction Deepness = null;

        /// <summary>The width of canopy which organ resides in</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction Wideness = null;

        /// <summary>The area index of dead material</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction DeadAreaIndex = null;

        /// <summary>Gets the canopy. Should return null if no canopy present.</summary>
        public string CanopyType { get { return Plant.PlantType + "_" + this.Parent.Name; } }

        /// <summary>Albedo.</summary>
        [Description("Albedo")]
        public double Albedo { get; set; }

        /// <summary>Gets or sets the gsmax.</summary>
        [Description("Daily maximum stomatal conductance(m/s)")]
        public double Gsmax { get { return Gsmax350 * FRGR * StomatalConductanceCO2Modifier.Value(); } }

        /// <summary>Gets or sets the gsmax.</summary>
        [Description("Maximum stomatal conductance at CO2 concentration of 420 ppm (m/s)")]
        public double Gsmax350 { get; set; }

        /// <summary>Gets or sets the R50.</summary>
        [Description("R50")]
        public double R50 { get; set; }

        /// <summary>Gets the LAI</summary>
        [JsonIgnore]
        [Units("m^2/m^2")]
        public double LAI { get; set; }

        /// <summary>Gets the LAI live + dead (m^2/m^2)</summary>
        public double LAITotal { get { return LAI + LAIDead; } }

        /// <summary>Gets the cover green.</summary>
        [Units("0-1")]
        public double CoverGreen { get { return 1.0 - Math.Exp(-GreenExtinctionCoefficient.Value() * LAI); } }

        /// <summary>Gets the cover total.</summary>
        [Units("0-1")]
        public double CoverTotal { get { return 1.0 - (1 - CoverGreen) * (1 - CoverDead); } }

        /// <summary>Gets or sets the height.</summary>
        [JsonIgnore]
        [Units("mm")]
        public double Height { get; set; }
        /// <summary>Gets the depth.</summary>
        [Units("mm")]
        [JsonIgnore]
        public double Depth { get; set; }

        /// <summary>Gets the width of the canopy (mm).</summary>
        [Units("mm")]
        [JsonIgnore]
        public double Width { get; set; }

        /// <summary>Gets or sets the FRGR.</summary>
        [Units("mm")]
        [JsonIgnore]
        public double FRGR { get; set; }

        private double _PotentialEP = 0;
        /// <summary>Sets the potential evapotranspiration. Set by MICROCLIMATE.</summary>
        [Units("mm")]
        [JsonIgnore]
        public double PotentialEP
        {
            get { return _PotentialEP; }
            set
            {
                _PotentialEP = value;
            }
        }

        /// <summary>Sets the actual water demand.</summary>
        [Units("mm")]
        [JsonIgnore]
        public double WaterDemand { get; set; }

        private double waterAllocation = 0;
        /// <summary>Gets or sets the water allocation.</summary>
        [JsonIgnore]
        public double WaterAllocation
        {
            get { return waterAllocation; }
            set { waterAllocation = value; AllocationMade(); }
        }

        private void AllocationMade()
        {
            Fw = MathUtilities.Divide(WaterAllocation, PotentialEP, 1);
        }

        /// <summary>Sets the light profile. Set by MICROCLIMATE.</summary>
        [JsonIgnore]
        public CanopyEnergyBalanceInterceptionlayerType[] LightProfile { get; set; }

        /// <summary>Gets or sets the k dead.</summary>
        public double KDead { get; set; }                  // Extinction Coefficient (Dead)
        /// <summary>Calculates the water demand.</summary>
        public double CalculateWaterDemand()
        {

            return WaterDemand;

        }
        /// <summary>Gets the transpiration.</summary>
        public double Transpiration { get { return WaterAllocation; } }




        /// <summary>Gets or sets the lai dead.</summary>
        public double LAIDead { get; set; }


        /// <summary>Gets the cover dead.</summary>
        public double CoverDead { get { return 1.0 - Math.Exp(-KDead * LAIDead); } }

        /// <summary>Gets the total radiation intercepted.</summary>
        [Units("MJ/m^2/day")]
        [Description("This is the intercepted radiation value that is passed to the RUE class to calculate DM supply")]
        public double RadiationIntercepted
        {
            get
            {
                double TotalRadn = 0;
                if (LightProfile != null)
                    for (int i = 0; i < LightProfile.Length; i++)
                        TotalRadn += LightProfile[i].AmountOnGreen;
                return TotalRadn;
            }
        }

        /// <summary>
        /// Radiation intercepted by the dead components of the canopy.
        /// </summary>
        [Units("MJ/m^2/day")]
        public double RadiationInterceptedByDead
        {
            get
            {
                if (LightProfile == null)
                    return 0;

                double totalRadn = 0;
                for (int i = 0; i < LightProfile.Length; i++)
                    totalRadn += LightProfile[i].AmountOnDead;
                return totalRadn;
            }
        }


        /// <summary>
        /// Water stress factor.
        /// </summary>
        public double Fw { get; private set; }


        /// <summary>Clears this instance.</summary>
        private void Clear()
        {
            FRGR = 0.0;
            Height = 0;
            Depth = 0;
            Width = 0.0;
            LAI = 0;
            LAIDead = 0.0;
            KDead = 0.0;
            WaterAllocation = 0.0;
            WaterDemand = 0.0;
            _PotentialEP = 0.0;
            LightProfile = new CanopyEnergyBalanceInterceptionlayerType[0];
        }

        /// <summary>Event from sequencer telling us to do our potential growth.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoPotentialPlantGrowth")]
        private void OnDoPotentialPlantGrowth(object sender, EventArgs e)
        {
            // save current state
            if (parentPlant.IsAlive)
            {
                FRGR = FRGRer.Value();
                Height = Tallness.Value();
                Depth = Deepness.Value();
                Width = Wideness.Value();
                LAI = GreenAreaIndex.Value();
                LAIDead = DeadAreaIndex.Value();
                KDead = DeadExtinctionCoefficient.Value();
            }
        }

        /// <summary>Constructor</summary>
        public EnergyBalance()
        {
        }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        protected void OnSimulationCommencing(object sender, EventArgs e)
        {
            Height = 0.0;
            LAI = 0.0;
            Depth = 0.0;
            Width = 0.0;
            LAIDead = 0.0;
        }

        /// <summary>Called when crop is sowed</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        protected void OnPlantSowing(object sender, SowingParameters data)
        {
            if (data.Plant == parentPlant)
                resetCanopy();
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantEnding")]
        protected void OnPlantEnding(object sender, EventArgs e)
        {
            resetCanopy();
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("EndCrop")]
        protected void OnEndCrop(object sender, EventArgs e)
        {
            resetCanopy();
        }

        /// <summary>
        /// Called when canopy is reset but crop not ended.  Used for deciduious crops
        /// </summary>
        public void resetCanopy()
        {
            Clear();
        }

    }
}