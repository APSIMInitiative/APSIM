﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Models.Core;
using System.Xml.Serialization;
using Models.Interfaces;
using APSIM.Shared.Utilities;
using Models.Soils.Arbitrator;


namespace Models
{
    /// <summary>
    /// A simple agroforestry model
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.StaticForestrySystemView")]
    [PresenterName("UserInterface.Presenters.StaticForestrySystemPresenter")]
    public class StaticForestrySystem : Zone,IUptake
    {
        [Link]
        IWeather weather = null;
        
        /// <summary>Gets or sets the table data.</summary>
        /// <value>The table.</value>
        public List<List<string>> Table { get; set; }

        /// <summary>Allows the user to set a nitrogen demand for the tree.</summary>
        /// <value>The nitrogen demand.</value>
        [Summary]
        public double NDemand { get; set; }

        /// <summary>The root radius.</summary>
        /// <value>The root radius.</value>
        [Summary]
        public double RootRadius { get; set; }

        /// <summary>
        /// A list containing forestry information for each zone.
        /// </summary>
        [XmlIgnore]
        public List<ZoneInfo> ZoneInfoList;

        /// <summary>
        /// Return the %Shade for a given zone
        /// </summary>
        /// <param name="z">Zone</param>
        /// <returns>%Shade</returns>
        public double GetShade(Zone z)
        {
            foreach (ZoneInfo zi in ZoneInfoList)
                if (zi.zone == z)
                    return zi.Shade;
            throw new ApsimXException(this, "Could not find a shade value for zone called " + z.Name);
        }
        /// <summary>
        /// Return the %Wind Reduction for a given zone
        /// </summary>
        /// <param name="z">Zone</param>
        /// <returns>%Wind Reduction</returns>
        public double GetWindReduction(Zone z)
        {
            foreach (ZoneInfo zi in ZoneInfoList)
                if (zi.zone == z)
                    return zi.WindReduction;
            throw new ApsimXException(this, "Could not find a shade value for zone called " + z.Name);
        }
        /// <summary>
        /// Return the area of the zone.
        /// </summary>
        [XmlIgnore]
        public new double Area
        {
            get
            {
                double A = 0;
                foreach(Zone Z in Apsim.Children(this,typeof(Zone)))
                    A=+Z.Area;
                return A;
            }
            set
            {
            }
        }

        /// <summary>
        /// Calculate the total intercepted radiation by the tree canopy (MJ)
        /// </summary>
        public double InterceptedRadiation
        {
            get
            {
                double IR = 0;
                foreach (ZoneInfo ZI in ZoneInfoList)
                    IR += ZI.zone.Area * weather.Radn;
                return IR;
            }
        }

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            ZoneInfoList = new List<ZoneInfo>();
            for (int i = 2; i < Table.Count; i++)
            {
                ZoneInfo newZone = new ZoneInfo();
                newZone.zone = Apsim.Child(this,Table[0][i - 1]) as Zone;
                newZone.WindReduction = Convert.ToDouble(Table[i][0]);
                newZone.Shade = Convert.ToDouble(Table[i][1]);
                newZone.RLD = new double[Table[1].Count - 4];
                for (int j = 4; j < Table[1].Count; j++)
                    newZone.RLD[j - 4] = Convert.ToDouble(Table[i][j]);
                ZoneInfoList.Add(newZone);
            }
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

            foreach (ZoneInfo ZI in ZoneInfoList)
            {
                Soils.SoilWater S = Apsim.Find(ZI.zone, typeof(Soils.SoilWater)) as Soils.SoilWater;
                SWDemand += S.Eo*(1/(1-ZI.Shade/100)-1)*ZI.zone.Area*10000;
            }

            List<ZoneWaterAndN> Uptakes = new List<ZoneWaterAndN>();
            
            foreach (ZoneWaterAndN Z in soilstate.Zones)
            {
                foreach (ZoneInfo ZI in ZoneInfoList)
                {
                    if (Z.Name == ZI.zone.Name)
                    {
                        ZoneWaterAndN Uptake = new ZoneWaterAndN();
                        //Find the soil for this zone
                        Zone ThisZone = new Zone();
                        Soils.Soil ThisSoil = new Soils.Soil();

                        foreach (Zone SearchZ in Apsim.ChildrenRecursively(Parent, typeof(Zone)))
                            if (SearchZ.Name == Z.Name)
                                ThisSoil = Apsim.Find(SearchZ, typeof(Soils.Soil)) as Soils.Soil;

                        Uptake.Name = Z.Name;
                        double[] SW = Z.Water;
                        Uptake.NO3N = new double[SW.Length];
                        Uptake.NH4N = new double[SW.Length];
                        Uptake.Water = new double[SW.Length];
                        for (int i = 0; i <= SW.Length - 1; i++)
                        {
                            double[] LL15mm = MathUtilities.Multiply(ThisSoil.LL15,ThisSoil.Thickness);
                            Uptake.Water[i] = (SW[i] - LL15mm[i]) * ZI.RLD[i];
                            PotSWSupply += Uptake.Water[i] * ZI.zone.Area * 10000;
                        }
                        Uptakes.Add(Uptake);
                    }
                }
            }
            // Now scale back uptakes if supply > demand
            double F = 0;  // Uptake scaling factor
            if (SWDemand > 0)
                F = PotSWSupply / SWDemand;
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
                foreach (ZoneInfo ZI in ZoneInfoList)
                {
                    if (Z.Name == ZI.zone.Name)
                    {
                        ZoneWaterAndN Uptake = new ZoneWaterAndN();
                        //Find the soil for this zone
                        Zone ThisZone = new Zone();
                        Soils.Soil ThisSoil = new Soils.Soil();

                        foreach (Zone SearchZ in Apsim.ChildrenRecursively(Parent, typeof(Zone)))
                            if (SearchZ.Name == Z.Name)
                                ThisSoil = Apsim.Find(SearchZ, typeof(Soils.Soil)) as Soils.Soil;

                        Uptake.Name = Z.Name;
                        double[] SW = Z.Water;
                        Uptake.NO3N = new double[SW.Length];
                        Uptake.NH4N = new double[SW.Length];
                        Uptake.Water = new double[SW.Length];
                        //for (int i = 0; i <= SW.Length-1; i++)
                        //    Uptake.NO3N[i] = Z.NO3N[i] * ZI.RLD[i];

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
                    Soils.Soil ThisSoil = new Soils.Soil();
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
    public struct ZoneInfo
    {
        /// <summary>
        /// The name of the zone.
        /// </summary>
        public Zone zone;

        /// <summary>
        /// Wind value.
        /// </summary>
        public double WindReduction;

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

