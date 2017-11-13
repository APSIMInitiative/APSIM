using System;
using Models.Core;
using Models.PMF.Functions;
using Models.PMF.Interfaces;
using Models.PMF.Library;

namespace Models.PMF.Organs
{
    /// <summary>
    /// A harvest index reproductive organ
    /// </summary>
    [Serializable]
    public class HIReproductiveOrgan : BaseOrgan, IArbitration
    {
        /// <summary>Gets or sets the above ground.</summary>
        [Link]
        IFunction AboveGroundWt = null;

        /// <summary>The water content</summary>
        [Link]
        IFunction WaterContent = null;
        /// <summary>The hi increment</summary>
        [Link]
        IFunction HIIncrement = null;
        /// <summary>The n conc</summary>
        [Link]
        IFunction NConc = null;

        /// <summary>Link to biomass removal model</summary>
        [ChildLink]
        public BiomassRemoval biomassRemovalModel = null;

        /// <summary>The daily growth</summary>
        private double DailyGrowth = 0;

        /// <summary>The live biomass</summary>
        public Biomass Live { get; set; }

        /// <summary>The dead biomass</summary>
        public Biomass Dead { get; set; }

        /// <summary>Gets the live f wt.</summary>
        /// <value>The live f wt.</value>
        [Units("g/m^2")]
        public double LiveFWt
        {
            get
            {

                if (WaterContent != null)
                    return Live.Wt / (1 - WaterContent.Value());
                else
                    return 0.0;
            }
        }

        /// <summary>Initializes a new instance of the <see cref="HIReproductiveOrgan"/> class.</summary>
        public HIReproductiveOrgan()
        {
            Live = new Biomass();
            Dead = new Biomass();
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="data">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantSowing")]
        private void OnPlantSowing(object sender, SowPlant2Type data)
        {
            if (data.Plant == Plant)
                Clear();
        }

        /// <summary>Called when crop is ending</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("PlantEnding")]
        private void OnPlantEnding(object sender, EventArgs e)
        {
            Biomass total = Live + Dead;
            if (total.Wt > 0.0)
            {
                Detached.Add(Live);
                Detached.Add(Dead);
                SurfaceOrganicMatter.Add(total.Wt * 10, total.N * 10, 0, Plant.CropType, Name);
            }

            Clear();
        }

        /// <summary>
        /// Execute harvest logic for HI reproductive organ
        /// </summary>
        public override void DoHarvest()
        {
                double YieldDW = (Live.Wt + Dead.Wt);

                string message = "Harvesting " + Name + " from " + Plant.Name + "\r\n" +
                                 "  Yield DWt: " + YieldDW.ToString("f2") + " (g/m^2)";
                Summary.WriteMessage(this, message);

                Live.Clear();
                Dead.Clear();
        }

        /// <summary>Gets the hi.</summary>
        /// <value>The hi.</value>
        public double HI
        {
            get
            {
                double CurrentWt = (Live.Wt + Dead.Wt);
                if (AboveGroundWt.Value() > 0)
                    return CurrentWt / AboveGroundWt.Value();
                else
                    return 0.0;
            }
        }

        /// <summary>Sets the dry matter allocation.</summary>
        public override void SetDryMatterAllocation(BiomassAllocationType dryMatter)
        {
            Live.StructuralWt += dryMatter.Structural; DailyGrowth = dryMatter.Structural;
        }

        /// <summary>Sets the n allocation.</summary>
        public override void SetNitrogenAllocation(BiomassAllocationType nitrogen)
        {
            Live.StructuralN += nitrogen.Structural;
        }

        /// <summary>Gets the total biomass</summary>
        public Biomass Total { get { return Live + Dead; } }

        /// <summary>Gets the total grain weight</summary>
        [Units("g/m2")]
        public double Wt { get { return Total.Wt; } }

        /// <summary>Gets the total grain N</summary>
        [Units("g/m2")]
        public double N { get { return Total.N; } }

        /// <summary>Calculate and return the dry matter demand (g/m2)</summary>
        public override BiomassPoolType CalculateDryMatterDemand()
        {
            double currentWt = (Live.Wt + Dead.Wt);
            double newHI = HI + HIIncrement.Value();
            double newWt = newHI * AboveGroundWt.Value();
            double demand = Math.Max(0.0, newWt - currentWt);
            dryMatterDemand.Structural = demand;
            return dryMatterDemand;
        }

        /// <summary>Calculate and return the nitrogen demand (g/m2)</summary>
        public override BiomassPoolType CalculateNitrogenDemand()
        {
            double demand = Math.Max(0.0, (NConc.Value() * Live.Wt) - Live.N);
            nitrogenDemand.Structural = demand;
            return nitrogenDemand;
        }

        /// <summary>Removes biomass from organs when harvest, graze or cut events are called.</summary>
        /// <param name="biomassRemoveType">Name of event that triggered this biomass remove call.</param>
        /// <param name="value">The fractions of biomass to remove</param>
        public override void DoRemoveBiomass(string biomassRemoveType, OrganBiomassRemovalType value)
        {
            biomassRemovalModel.RemoveBiomass(biomassRemoveType, value, Live, Dead, Removed, Detached);
        }

        /// <summary>Clears this instance.</summary>
        private void Clear()
        {
            Live.Clear();
            Dead.Clear();
            dryMatterDemand.Clear();
            dryMatterSupply.Clear();
            nitrogenDemand.Clear();
            nitrogenSupply.Clear();
        }
    }
}
