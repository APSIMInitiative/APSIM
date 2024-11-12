namespace UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using APSIM.Shared.Utilities;
    using Models;
    using Models.Core;
    using Models.Core.Apsim710File;
    using Models.Interfaces;
    using Models.PMF;
    using Models.Soils;
    using Models.Soils.Nutrients;
    using Models.Soils.SoilTemp;
    using Models.Storage;
    using Models.Surface;
    using NUnit.Framework;
    using UserInterface.Presenters;

    /// <summary>This is a test class for the .apsim file importer.</summary>
    [TestFixture]
    public class ImporterTests
    {
        /// <summary>Ensure CLOCK imports OK</summary>
        [Test]
        public void ImporterTests_ClockImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <clock>" +
            "      <start_date type=\"date\" description=\"Enter the start date of the simulation\">01/01/1940</start_date>" +
            "      <end_date type=\"date\" description=\"Enter the end date of the simulation\">31/12/1950</end_date> " +
            "    </clock>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            Assert.That(sims.Children[0] is Simulation, Is.True);
            Assert.That(sims.Children[1] is DataStore, Is.True);

            Clock c = sims.Children[0].Children[0] as Clock;
            Assert.That(c.StartDate, Is.EqualTo(new DateTime(1940, 1, 1)));
            Assert.That(c.EndDate, Is.EqualTo(new DateTime(1950, 12, 31)));
        }

        /// <summary>Ensure shortcuts import OK</summary>
        [Test]
        public void ImporterTests_ShortcutsWork()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <folder name=\"shared\">" +
            "    <clock>" +
            "      <start_date type=\"date\" description=\"Enter the start date of the simulation\">01/01/1940</start_date>" +
            "      <end_date type=\"date\" description=\"Enter the end date of the simulation\">31/12/1950</end_date> " +
            "    </clock>" +
            "  </folder>" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <clock shortcut=\"/simulations/shared/Clock\">" +
            "    </clock>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            Clock c = sims.Children[1].Children[0] as Clock;
            Assert.That(c.StartDate, Is.EqualTo(new DateTime(1940, 1, 1)));
            Assert.That(c.EndDate, Is.EqualTo(new DateTime(1950, 12, 31)));
        }

        /// <summary>Ensure METFILE imports OK</summary>
        [Test]
        public void ImporterTests_MetFileImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <metfile name=\"met\">" +
            "      <filename name=\"filename\" input=\"yes\">%apsim%/Examples/WeatherFiles/Goond.met</filename>" +
            "    </metfile>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            var w = sims.Children[0].Children[0] as Models.Climate.Weather;
            string expected = string.Join("/", new string[] { "/Examples", "WeatherFiles", "AU_Goondiwindi.met" });
            Assert.That(w.FileName, Is.EqualTo(expected));
        }

        /// <summary>Ensure AREA imports OK</summary>
        [Test]
        public void ImporterTests_AreaImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <area name=\"paddock\">" +
            "      <paddock_area>100</paddock_area>" +
            "    </area>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            Zone z = sims.Children[0].Children[0] as Zone;
            Assert.That(z.Area, Is.EqualTo(100));
        }

        /// <summary>Ensure SOIL imports OK</summary>
        [Test]
        public void ImporterTests_SoilImports()
        {
            string oldXml = ReflectionUtilities.GetResourceAsString("UnitTests.ImporterTestsSoilImports.xml");

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            Soil s = sims.Children[0].Children[0] as Soil;
            Assert.That(s.Name, Is.EqualTo("Soil"));

            Water initWater = s.Children[0] as Water;
            Assert.That(initWater.FractionFull, Is.EqualTo(0.5).Within(0.000000001));
            Assert.That(initWater.FilledFromTop, Is.True);

            Physical w = s.Children[1] as Physical;
            Assert.That(w.Thickness, Is.EqualTo(new double[] { 150, 150, 300, 300 }));
            Assert.That(w.BD, Is.EqualTo(new double[] { 1.02, 1.03, 1.02, 1.02 }));
            Assert.That(w.LL15, Is.EqualTo(new double[] { 0.29, 0.29, 0.29, 0.29 }));

            ISoilWater sw = s.Children[2] as ISoilWater;
            Assert.That(sw.Thickness, Is.EqualTo(new double[] { 150, 150, 300, 300 }));

            Assert.That(s.Children[8] is Nutrient, Is.True);
            Assert.That(s.Children[9] is SoilTemperature, Is.True);
            Assert.That(s.Children[3] is Solute, Is.True);
            Assert.That(s.Children[4] is Solute, Is.True);
            Assert.That(s.Children[5] is Solute, Is.True);
            Organic som = s.Children[6] as Organic;
            Assert.That(som.Thickness, Is.EqualTo(new double[] { 150, 150, 300, 300 }));
            Assert.That(som.Carbon, Is.EqualTo(new double[] { 1.04, 0.89, 0.89, 0.89 }));
            Assert.That(som.FBiom, Is.EqualTo(new double[] { 0.025, 0.02, 0.015, 0.01 }));

            Chemical a = s.Children[7] as Chemical;
            Assert.That(a.Thickness, Is.EqualTo(new double[] { 150, 150, 300, 300 }));
            Assert.That(a.EC, Is.EqualTo(new double[] { 0.2, 0.25, 0.31, 0.40 }));
            Assert.That(a.PH, Is.EqualTo(new double[] { 8.4, 8.8, 9.0, 9.2 }));

            SoilCrop crop = s.Children[1].Children[0] as SoilCrop;
            Assert.That(crop.LL, Is.EqualTo(new double[] { 0.29, 0.29, 0.32, 0.38 }));
            Assert.That(crop.KL, Is.EqualTo(new double[] { 0.1, 0.1, 0.08, 0.06 }));
            Assert.That(crop.XF, Is.EqualTo(new double[] { 1, 1, 1, 1 }));
        }

        /// <summary>Ensure WHEAT imports OK</summary>
        [Test]
        public void ImporterTests_WheatImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <wheat />" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            var f = sims.Children[0].Children[0] as Plant;
            Assert.That(f, Is.Not.Null);
        }

        /// <summary>Ensure MANAGER imports OK</summary>
        [Test]
        public void ImporterTests_ManagerImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <manager name=\"Sowing fertiliser\">" +
            "      <ui>" +
            "        <category description=\"When should fertiliser be applied\" type=\"category\" />" +
            "        <modulename type=\"modulename\" description=\"On which module should the event come from : \">wheat</modulename>" +
            "        <eventname type=\"text\" description=\"On which event should fertiliser be applied : \">sowing</eventname>" +
            "        <category description=\"Fertiliser application details\" type=\"category\" />" +
            "        <fertmodule type=\"modulename\" description=\"Module used to apply the fertiliser : \">fertiliser</fertmodule>" +
            "        <fert_amount_sow type=\"text\" description=\"Amount of starter fertiliser at sowing (kg/ha) : \">150</fert_amount_sow>" +
            "        <fert_type_sow type=\"list\" listvalues=\"NO3_N, NH4_N, NH4NO3, urea_N, urea_no3, urea, nh4so4_n, rock_p, banded_p, broadcast_p\" description=\"Sowing fertiliser type : \">urea_N</fert_type_sow>" +
            "      </ui>" +
            "      <script name=\"[modulename].[eventname]\">" +
            "        <text>" +
            "    [fertmodule] apply amount = [fert_amount_sow] (kg/ha), depth = 50 (mm), type = [fert_type_sow]" +
            "        </text>" +
            "        <event>[modulename].[eventname]</event>" +
            "      </script>" +
            "    </manager>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            var m = sims.Children[0].Children[0] as Manager;
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Code != string.Empty, Is.True);
            Assert.That(m.Code, Is.Not.Null);
            Assert.That(m.Children.Count, Is.EqualTo(1));
        }

        /// <summary>Ensure MANAGER2 imports OK</summary>
        [Test]
        public void ImporterTests_Manager2Imports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <manager2 name=\"Economics\">" +
            "      <ui>" +
            "        <category type=\"category\" description=\"Dummy module\" />" +
            "        <labourCost type=\"text\" description=\"Cost of labour($/ hr)\">0.0</labourCost>" +
            "      </ui>" +
            "      <text>" +
            "    using System;" +
            "    using ModelFramework;" +
            "    public class Script" +
            "    {" +
            "       [EventHandler] public void OnBuyCows() {Console.WriteLine(\"Buying a cow\");}" +
            "       [EventHandler] public void OnSellCows() {Console.WriteLine(\"Selling a cow\");}" +
            "       [EventHandler] public void OnBuyShoats() {Console.WriteLine(\"Buying a goat\");}" +
            "       [EventHandler] public void OnSellShoats() {Console.WriteLine(\"Selling a goat\");}" +
            "    }   " +
            "      </text>" +
            "    </manager2>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            var m = sims.Children[0].Children[0] as Manager;
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Code != string.Empty, Is.True);
            Assert.That(m.Code, Is.Not.Null);
            Assert.That(m.Children.Count, Is.EqualTo(1));
        }

        /// <summary>Ensure MANAGER2 with compile errors still imports but returns compile messages.</summary>
        [Test]
        public void ImporterTests_Manager2ImportsAndShowsCompileMessages()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <manager2 name=\"DCAPS\">" +
            "      <ui>" +
            "        <category type=\"category\" description=\"Dummy module\" />" +
            "        <labourCost type=\"text\" description=\"Cost of labour($/ hr)\">0.0</labourCost>" +
            "      </ui>" +
            "      <text>" +
            "      using System;" + Environment.NewLine +
            "      using System.IO;" + Environment.NewLine +
            "      using ModelFramework;" + Environment.NewLine +
            "      using Models.Soils;" + Environment.NewLine +
            "      using System;" + Environment.NewLine +
            "      using System.IO;" + Environment.NewLine +
            "      using DCAPST;" + Environment.NewLine +
            "      using Models.Core;" + Environment.NewLine +
            "      public class Script" + Environment.NewLine +
            "      {" + Environment.NewLine +
            "      }" + Environment.NewLine +
            "</text>" +
            "    </manager2>" +
            "  </simulation>" +
            "</folder>";

            List<Exception> importExceptions = new();
            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => { importExceptions.Add(e); });

            Assert.That(sims, Is.Not.Null);
            Assert.That(importExceptions[0].Message.Contains("Errors found in manager model DCAPS"), Is.True);
        }

        /// <summary>Ensure OUTPUTFILE imports OK</summary>
        [Test]
        public void ImporterTests_OutputFileImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <outputfile>" +
            "      <filename output=\"yes\">Continuous Wheat.out</filename>" +
            "      <title>Continuous Wheat</title>" +
            "      <variables name=\"Variables\">" +
            "        <variable array=\" ?\" description=\"Date (dd/mm/yyyy)\">dd/mm/yyyy as Date</variable>" +
            "        <variable array=\" ?\" description=\"Biomass\">biomass</variable>" +
            "        <variable array=\" ?\" description=\"Yield\">yield</variable>" +
            "        <variable array=\" ?\" description=\"grain protein content\">grain_protein</variable>" +
            "        <variable array=\" ?\" description=\"Size of each grain\">grain_size</variable>" +
            "        <variable array=\" ?\" description=\"Extractable Soil Water (mm)\">esw</variable>" +
            "      </variables>" +
            "      <events name=\"Reporting Frequency\">" +
            "        <event description=\"\">harvesting</event>" +
            "      </events>" +
            "    </outputfile>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            var r = sims.Children[0].Children[0] as Models.Report;
            Assert.That(r, Is.Not.Null);
            Assert.That(r.VariableNames[0], Is.EqualTo("[Clock].Today"));
            Assert.That(r.VariableNames[1], Is.EqualTo("biomass"));
            Assert.That(r.VariableNames[2], Is.EqualTo("yield"));
            Assert.That(r.VariableNames[3], Is.EqualTo("grain_protein"));
            Assert.That(r.VariableNames[4], Is.EqualTo("grain_size"));
            Assert.That(r.VariableNames[5], Is.EqualTo("esw"));

            Assert.That(r.EventNames[0], Is.EqualTo("[Clock].DoReport"));
        }

        /// <summary>Ensure SURFACEORGANICMATTER imports OK</summary>
        [Test]
        public void ImporterTests_SurfaceOMImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <surfaceom name=\"SurfaceOrganicMatter\">" +
            "      <PoolName type=\"text\" description=\"Organic Matter pool name\">wheat</PoolName >" +
            "      <type type=\"list\" listvalues=\"TropicalPasture,barley,base_type,broccoli,camaldulensis,canola,centro,chickpea,chikenmanure_base,cm,cmA,cmB,constants,cotton,cowpea,danthonia,fababean,fieldpea,fym,gbean,globulus,goatmanure,grandis,grass,horsegram,inert,lablab,lentil,lucerne,lupin,maize,manB,manure,medic,millet,mucuna,nativepasture,navybean,oats,orobanche,peanut,pigeonpea,potato,rice,sorghum,soybean,stylo,sugar,sunflower,sweetcorn,sweetsorghum,tillage,tithonia,vetch,weed,wheat\" description=\"Organic Matter type\">wheat</type>" +
            "      <mass type=\"text\" description=\"Initial surface residue (kg/ha)\">1000</mass>" +
            "      <cnr type=\"text\" description=\"C:N ratio of initial residue\">80</cnr>" +
            "      <standing_fraction type=\"text\" description=\"Fraction of residue standing\">0</standing_fraction>" +
            "    </surfaceom>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            var som = sims.Children[0].Children[0] as SurfaceOrganicMatter;
            Assert.That(som, Is.Not.Null);
            Assert.That(som.InitialResidueMass, Is.EqualTo(1000));
            Assert.That(som.InitialCNR, Is.EqualTo(80));
            Assert.That(som.InitialResidueName, Is.EqualTo("wheat"));
        }

        /// <summary>Ensure MICROMET imports OK</summary>
        [Test]
        public void ImporterTests_MicroMetImports()
        {
            string oldXml =
            "<folder version=\"36\" creator=\"Apsim 7.5-r3183\" name=\"simulations\">" +
            "  <simulation name=\"Continuous Wheat\">" +
            "    <micromet name=\"MicroMet\">" +
            "      <soilalbedo name=\"soilalbedo\">0.23</soilalbedo>" +
            "      <a_interception name=\"a_interception\">0.1</a_interception>" +
            "      <b_interception name=\"b_interception\">0.2</b_interception>" +
            "      <c_interception name=\"c_interception\">0.3</c_interception>" +
            "      <d_interception name=\"d_interception\">0.4</d_interception>" +
            "    </micromet>" +
            "  </simulation>" +
            "</folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            var m = sims.Children[0].Children[0] as MicroClimate;
            Assert.That(m, Is.Not.Null);
            Assert.That(m.a_interception, Is.EqualTo(0.1));
            Assert.That(m.b_interception, Is.EqualTo(0.2));
            Assert.That(m.c_interception, Is.EqualTo(0.3));
            Assert.That(m.d_interception, Is.EqualTo(0.4));
            Assert.That(m.ReferenceHeight, Is.EqualTo(2));
        }

        /// <summary>
        /// This test ensures that failures in the importer do not cause the UI
        /// to crash.
        /// </summary>
        [Test]
        public void EnsureNoCrash()
        {
            // First, write the faulty .apsimx file to a temp file on disk.
            string defective = ReflectionUtilities.GetResourceAsString("UnitTests.defective.apsim");
            string fileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "-defective.apsim");
            File.WriteAllText(fileName, defective);
            MainPresenter presenter = new MainPresenter();
            Assert.DoesNotThrow(() => presenter.Import(fileName));
        }

        /// <summary>Ensure entire old APSIM file loads OK.</summary>
        [Test]
        public void EnsureOldAPSIMFileLoads()
        {
            string oldXml = ReflectionUtilities.GetResourceAsString("UnitTests.ImporterTestsOldAPSIM.xml");

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());

            Assert.That(sims, Is.Not.Null);
        }

        [Test]
        public void EnsureMemoImports()
        {
            string oldXml = "<folder><simulation><memo>hello there</memo></simulation></folder>";

            var importer = new Importer();
            Simulations sims = importer.CreateSimulationsFromXml(oldXml, e => Assert.Fail());
            Memo memo = sims.Children[0].Children[0] as Memo;
            Assert.That(memo, Is.Not.Null);
            Assert.That(memo.Text, Is.EqualTo("hello there"), "Failed to import memo message from .apsim file");
        }
    }
}
