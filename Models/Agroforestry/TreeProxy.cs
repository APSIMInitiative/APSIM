﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using MathNet.Numerics.LinearAlgebra.Double;
using Models.Core;
using System.Xml.Serialization;
using Models.Interfaces;
using APSIM.Shared.Utilities;
using Models.Soils.Arbitrator;
using Models.Zones;

namespace Models.Agroforestry
{
    /// <summary>
    /// A simple tree model
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.TreeProxyView")]
    [PresenterName("UserInterface.Presenters.TreeProxyPresenter")]
    [ValidParent(ParentModels = new Type[] { typeof(Simulation), typeof(Zone) })]
    public class TreeProxy : Model, IUptake
    {
        [Link]
        IWeather weather = null;
        [Link]
        Clock clock = null;

        /// <summary>Gets or sets the table data.</summary>
        /// <value>The table.</value>
        public List<List<string>> Table { get; set; }

        /// <summary>Allows the user to set a nitrogen demand for the tree.</summary>
        /// <value>The nitrogen demand.</value>
        [Summary]
        public double NDemand { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public double Urel { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public double H { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public double heightToday { get; set; }

        /// <summary>The root radius.</summary>
        /// <value>The root radius.</value>
        [Summary]
        public double RootRadius { get; set; }

        /// <summary>
        /// A list containing forestry information for each zone.
        /// </summary>
        [XmlIgnore]
        public List<IModel> ZoneList;

        /// <summary>
        /// Return an array of shade values.
        /// </summary>
        [XmlIgnore]
        [Summary]
        public double[] Shade { get { return shade.Values.ToArray(); } }

        /// <summary>
        /// Date list for tree heights over lime
        /// </summary>
        [Summary]
        public DateTime[] dates { get; set; }

        /// <summary>
        /// Tree heights
        /// </summary>
        [Summary]
        public double[] heights { get; set; }

        private Dictionary<double, double> shade = new Dictionary<double, double>();
        private Dictionary<double, double> nDemand = new Dictionary<double, double>();
        private Dictionary<double, double[]> rld = new Dictionary<double, double[]>();

        /// <summary>
        /// Return the distance from the tree for a given zone. The tree is assumed to be in the first Zone.
        /// </summary>
        /// <param name="z">Zone</param>
        /// <returns>Distance from a static tree</returns>
        public double GetDistanceFromTrees(Zone z)
        {
            double D = 0;
            foreach (Zone zone in ZoneList)
            {
                if (zone is RectangularZone)
                    D += (zone as RectangularZone).Width;
                else if (zone is CircularZone)
                    D += (zone as CircularZone).Width;
                else
                    throw new ApsimXException(this, "Cannot calculate distance for trees for zone of given type.");

                if (zone == z)
                    return D;
            }
        
            throw new ApsimXException(this, "Could not find zone called " + z.Name);
        }

        /// <summary>
        /// Return the width of the given zone.
        /// </summary>
        /// <param name="z">The width.</param>
        /// <returns></returns>
        private double GetZoneWidth(Zone z)
        {
            double D = 0;
            if (z is RectangularZone)
                D = (z as RectangularZone).Width;
            else if (z is CircularZone)
                D = (z as CircularZone).Width;
            else
                throw new ApsimXException(this, "Cannot calculate distance for trees for zone of given type.");
            return D;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="z"></param>
        /// <returns></returns>
        private double ZoneDistanceInTreeHeights(Zone z)
        {
            double treeHeight = GetHeightToday();
            double distFromTree = GetDistanceFromTrees(z);

            return (distFromTree + GetZoneWidth(z) / 2) / treeHeight;
        }

        /// <summary>
        /// Return the %Shade for a given zone
        /// </summary>
        /// <param name="z"></param>
        public double GetShade(Zone z)
        {
            double distInTH = ZoneDistanceInTreeHeights(z);
            bool didInterp = false;
            return MathUtilities.LinearInterpReal(distInTH, shade.Keys.ToArray(), shade.Values.ToArray(), out didInterp);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="z"></param>
        /// <returns></returns>
        public double[] GetRLD(Zone z)
        {
            double distInTH = ZoneDistanceInTreeHeights(z);
            bool didInterp = false;
            DenseMatrix rldM = DenseMatrix.OfColumnArrays(rld.Values);
            double[] rldInterp = new double[rldM.RowCount];

            for (int i=0;i< rldM.RowCount;i++)
            {
                rldInterp[i] = MathUtilities.LinearInterpReal(distInTH, rld.Keys.ToArray(), rldM.Row(i).ToArray(), out didInterp);
            }
                       
            return rldInterp;
        }

        /// <summary>
        /// Setup the tree properties so they can be mapped to a zone.
        /// </summary>
        private void SetupTreeProperties()
        {
            //These need to match the column names in the UI
            double[] THCutoffs = new double[] { 0, 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6 };

            for (int i = 2; i < Table.Count; i++)
            {
                shade.Add(THCutoffs[i - 2], Convert.ToDouble(Table[i][0]));
                nDemand.Add(THCutoffs[i - 2], Convert.ToDouble(Table[i][1]));
                List<double> getRLDs = new List<double>();
                for (int j = 4; j < Table[0].Count; j++)
                    getRLDs.Add(Convert.ToDouble(Table[i][j]));
                rld.Add(THCutoffs[i - 2], getRLDs.ToArray());
            }
        }

        private double GetHeightToday()
        {
            double[] OADates = new double[dates.Count()];
            bool didInterp;

            for (int i = 0; i < dates.Count(); i++)
                OADates[i] = dates[i].ToOADate();
            return MathUtilities.LinearInterpReal(clock.Today.ToOADate(), OADates, heights, out didInterp) / 1000;
        }
        /// <summary>
        /// Return the %Wind Reduction for a given zone
        /// </summary>
        /// <param name="z">Zone</param>
        /// <returns>%Wind Reduction</returns>
        public double GetWindReduction(Zone z)
        {
            foreach (Zone zone in ZoneList)
                if (zone == z)
                {
                    double UrelMin = Math.Max(0.0, 1.14 * 0.5 - 0.16); // 0.5 is porosity, will be dynamic in the future
                    //double Urel;
                  //  double H;
                   // double heightToday;

                    heightToday = GetHeightToday();

                    if (heightToday < 1)
                        Urel = 1;
                    else
                    {
                        H = GetDistanceFromTrees(z) / heightToday;
                        if (H < 6)
                            Urel = UrelMin + (1 - UrelMin) / 2 - H / 6 * (1 - UrelMin) / 2;
                        else if (H < 6.1)
                            Urel = UrelMin;
                        else
                            Urel = UrelMin + (1 - UrelMin) / (1 + 0.000928 * Math.Exp(12.9372 * Math.Pow((H - 6), -0.26953)));
                    }
                    return Urel;
                }
            throw new ApsimXException(this, "Could not find zone called " + z.Name);
        }

        /// <summary>
        /// Calculate the total intercepted radiation by the tree canopy (MJ)
        /// </summary>
        public double InterceptedRadiation
        {
            get
            {
                double IR = 0;
                foreach (Zone zone in ZoneList)
                    IR += zone.Area * weather.Radn;
                return IR;
            }
        }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            ZoneList = Apsim.Children(this.Parent, typeof(Zone));
            SetupTreeProperties();
        }

        /// <summary>
        /// Returns soil water uptake from each zone by the static tree model
        /// </summary>
        /// <param name="soilstate"></param>
        /// <returns></returns>
        public List<Soils.Arbitrator.ZoneWaterAndN> GetSWUptakes(Soils.Arbitrator.SoilState soilstate)
        {
            double SWDemand = 0;  // Tree water demand (L)
            double PotSWSupply = 0; // Total water supply (L)

            foreach (Zone ZI in ZoneList)
            {
                Soils.SoilWater S = Apsim.Find(ZI, typeof(Soils.SoilWater)) as Soils.SoilWater;
                SWDemand += S.Eo * (1 / (1 - GetShade(ZI) / 100) - 1) * ZI.Area * 10000;
            }

            List<ZoneWaterAndN> Uptakes = new List<ZoneWaterAndN>();
            
            foreach (ZoneWaterAndN Z in soilstate.Zones)
            {
                foreach (Zone ZI in ZoneList)
                {
                    if (Z.Name == ZI.Name)
                    {
                        ZoneWaterAndN Uptake = new ZoneWaterAndN();
                        //Find the soil for this zone
                        Soils.Soil ThisSoil = null;

                        foreach (Zone SearchZ in Apsim.ChildrenRecursively(Parent, typeof(Zone)))
                            if (SearchZ.Name == Z.Name)
                            {
                                ThisSoil = Apsim.Find(SearchZ, typeof(Soils.Soil)) as Soils.Soil;
                                break;
                            }

                        Uptake.Name = Z.Name;
                        double[] SW = Z.Water;
                        Uptake.NO3N = new double[SW.Length];
                        Uptake.NH4N = new double[SW.Length];
                        Uptake.Water = new double[SW.Length];
                        for (int i = 0; i <= SW.Length - 1; i++)
                        {
                            double[] LL15mm = MathUtilities.Multiply(ThisSoil.LL15,ThisSoil.Thickness);
                            Uptake.Water[i] = (SW[i] - LL15mm[i]) * GetRLD(ZI)[i];
                            PotSWSupply += Uptake.Water[i] * ZI.Area * 10000;
                        }
                        Uptakes.Add(Uptake);
                    }
                }
            }
            // Now scale back uptakes if supply > demand
            double F = 0;  // Uptake scaling factor
            if (PotSWSupply > 0)
            {
                F = SWDemand / PotSWSupply;
                if (F > 1)
                    F = 1;
            }
            else
                F = 1;


            foreach (ZoneWaterAndN Z in Uptakes)
                Z.Water = MathUtilities.Multiply_Value(Z.Water, F);
            return Uptakes;


        }
        /// <summary>
        /// Returns soil Nitrogen uptake from each zone by the static tree model
        /// </summary>
        /// <param name="soilstate"></param>
        /// <returns></returns>
        public List<Soils.Arbitrator.ZoneWaterAndN> GetNUptakes(Soils.Arbitrator.SoilState soilstate)
        {
            List<ZoneWaterAndN> Uptakes = new List<ZoneWaterAndN>();

            foreach (ZoneWaterAndN Z in soilstate.Zones)
            {
                foreach (Zone zone in ZoneList)
                {
                    if (Z.Name == zone.Name)
                    {
                        ZoneWaterAndN Uptake = new ZoneWaterAndN();
                        //Find the soil for this zone
                        Soils.Soil ThisSoil = null;

                        foreach (Zone SearchZ in ZoneList)
                            if (SearchZ.Name == Z.Name)
                            {
                                ThisSoil = Apsim.Find(SearchZ, typeof(Soils.Soil)) as Soils.Soil;
                                break;
                            }

                        Uptake.Name = Z.Name;
                        double[] SW = Z.Water;
                        Uptake.NO3N = new double[SW.Length];
                        Uptake.NH4N = new double[SW.Length];
                        Uptake.Water = new double[SW.Length];
                        Uptakes.Add(Uptake);
                    }
                }
            }
            return Uptakes;
        }
        double PotentialNO3Uptake(double thickness, double NO3N, double theta, double RLD, double RootRadius)
        {

            double L = RLD / 100 * 1000000;   // Root Length Density (m/m3)
            double D0 = 0.05 /10000*24; // Diffusion Coefficient (m2/d)
            double tau = theta;         //  Tortuosity (unitless)
            double H = thickness / 1000;  // Layer thickness (m)
            double R0 = RootRadius / 100;  // Root Radius (m)
            double Nstock = NO3N / 10;  // Concentration in solution (g/m2)

            //Potential Uptake (g/m2)
            double U = (Math.PI * L * D0 * tau * theta * H * Nstock)/(theta*(-3/8 + 1/2*Math.Log(1/(R0*Math.Pow(Math.PI*L,0.5)))));
            return U;
        }

        /// <summary>
        ///  Accepts the actual soil water uptake from the soil arbitrator.
        /// </summary>
        /// <param name="info"></param>
        public void SetSWUptake(List<Soils.Arbitrator.ZoneWaterAndN> info)
        {
            foreach (ZoneWaterAndN ZI in info)
            {
                foreach (Zone SearchZ in Apsim.ChildrenRecursively(Parent, typeof(Zone)))
                {
                    Soils.Soil ThisSoil = null;
                    if (SearchZ.Name == ZI.Name)
                    {
                        ThisSoil = Apsim.Find(SearchZ, typeof(Soils.Soil)) as Soils.Soil;
                        ThisSoil.SoilWater.dlt_sw_dep = MathUtilities.Multiply_Value(ZI.Water, -1); ;
                    }
                }
            }
        }

        /// <summary>
        /// Accepts the actual soil Nitrogen uptake from the soil arbitrator.
        /// </summary>
        /// <param name="info"></param>
        public void SetNUptake(List<Soils.Arbitrator.ZoneWaterAndN> info)
        {
            // Do nothing with uptakes
            // throw new NotImplementedException();
        }
    }

    /// <summary>
    /// A structure holding forestry information for a single zone.
    /// </summary>
    [Serializable]
    public struct ZoneInfo
    {
        /// <summary>
        /// The name of the zone.
        /// </summary>
        public Zone zone;

        /// <summary>
        /// Shade value.
        /// </summary>
        public double Shade;

        /// <summary>
        /// Root Length Density information for each soil layer in a zone.
        /// </summary>
        public double[] RLD;
    }
}

