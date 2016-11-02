﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml;
using System.Xml.Serialization;

namespace SWIMFrame
{
    /// <summary>
    /// Originally stand alone flux table generation code.
    /// Now integrated into Input.cs.
    /// Likely won't be need once APSIM inputs are coded.
    /// </summary>
    public class Program
    {
        public static void GenerateFlux()
        {

            MVG.TestParams(103, 9.0, 0.99670220130280185, 9.99999999999998460E-003);
            SoilProps sp = Soil.gensptbl(1.0, new SoilParam(10, 103, 0.4, 2.0, -2.0, -10.0, 1.0 / 3.0, 1.0), true);
            Fluxes.FluxTable(5.0, sp);
            // FluxTable ft = Fluxes.ft;

            // string output = string.Empty;
            // define test soils
            SoilParam[] soils = new SoilParam[2];
            soils[0] = new SoilParam(10, 103, 0.4, 2.0, -2.0, -10.0, 1.0 / 3.0, 1.0);
            soils[1] = new SoilParam(10, 109, 0.6, 0.2, -2.0, -40.0, 1.0 / 9.0, 1.0);

            // string[] ftname = new string[2];
            // int[] sidx;
            int i, j;
            int[] ndz;
            double dzmin;
            // double[] x;
            double[,] dz = new double[2, 10]; //only for testing? if not will need to change hardcoded dimensions.
            bool Kgiven = true;
            SoilProps sp1, sp2;
            FluxTable ft1, ft2;

            // define soil profile
            // x = new double[] { 10, 20, 30, 40, 60, 80, 100, 120, 160, 200 }; //length = num soil layers
            // sidx = new int[] { 103, 103, 103, 103, 109, 109, 109, 109, 109, 109 }; //soil ident of layers
            dzmin = 1.0; // smallest likely path length
            ndz = new int[] { 2, 4 }; // for the two soil types - gives six flux tables
            //can be done in loops, but clearer this way and will only be used for testing
            dz[0, 0] = 5;
            dz[0, 1] = 10;
            dz[1, 0] = 10;
            dz[1, 1] = 20;
            dz[1, 2] = 30;
            dz[1, 4] = 40;
            for (i = 0; i < 2; i++)
            {
                MVG.Params(soils[i].sid, soils[i].ths, soils[i].ks, soils[i].he, soils[i].hd, soils[i].p, soils[i].hg, soils[i].em, soils[i].en);
                soils[i].sp = Soil.gensptbl(dzmin, soils[i], Kgiven);
                Soil.SoilProperties.Add("soil" + soils[i].sid, soils[i].sp);
                for (j = 0; j <= ndz[i]; j++)
                {
                    Fluxes.FluxTable(dz[i, j], soils[i].sp);
                    Fluxes.FluxTables.Add("soil" + soils[i].sid + "dz" + (dz[i, j] * 10), Fluxes.ft);
                }
            }
            Fluxes.WriteDiags();

            //generate and write composite flux table for path with two soil types
            sp1 = Soil.ReadProps("soil103");
            sp2 = Soil.ReadProps("soil109");
            ft1 = Fluxes.ReadFluxTable("soil103dz50");
            ft2 = Fluxes.ReadFluxTable("soil109dz100");

            FluxTable ftwo = TwoFluxes.TwoTables(ft1, sp1, ft2, sp2);
            Fluxes.FluxTables.Add("soil0103dz0050_soil0109dz0100", ftwo);
        }
    }
}