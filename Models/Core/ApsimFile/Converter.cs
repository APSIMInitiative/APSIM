using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using APSIM.Shared.Documentation.Extensions;
using APSIM.Shared.Utilities;
using Models.Climate;
using Models.Factorial;
using Models.Functions;
using Models.PMF;
using Models.Soils;
using Newtonsoft.Json.Linq;

namespace Models.Core.ApsimFile
{

    /// <summary>
    /// Converts the .apsim file from one version to the next
    /// </summary>
    public class Converter
    {
        /// <summary>Gets the latest .apsimx file format version.</summary>
        public static int LatestVersion { get { return 184; } }

        /// <summary>Converts a .apsimx string to the latest version.</summary>
        /// <param name="st">XML or JSON string to convert.</param>
        /// <param name="toVersion">The optional version to convert to.</param>
        /// <param name="fileName">The optional filename where the string came from.</param>
        /// <returns>Returns true if something was changed.</returns>
        public static ConverterReturnType DoConvert(string st, int toVersion = -1, string fileName = null)
        {
            ConverterReturnType returnData = new ConverterReturnType();

            if (toVersion == -1)
                toVersion = LatestVersion;

            int offset = st.TakeWhile(c => char.IsWhiteSpace(c)).Count();
            char firstNonBlankChar = st[offset];

            if (firstNonBlankChar == '<')
            {
                bool changed = XmlConverters.DoConvert(ref st, Math.Min(toVersion, XmlConverters.LastVersion), fileName);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(st);
                int fileVersion = Convert.ToInt32(XmlUtilities.Attribute(doc.DocumentElement, "Version"), CultureInfo.InvariantCulture);
                if (fileVersion == toVersion)
                    return new ConverterReturnType()
                    { DidConvert = changed, RootXml = doc };

                st = ConvertToJSON(st, fileName);
                returnData.Root = JObject.Parse(st);
            }
            else if (firstNonBlankChar == '{')
            {
                // json
                returnData.Root = JObject.Parse(st);
            }
            else
            {
                throw new Exception("Unknown string encountered. Not JSON or XML. String: " + st);
            }

            if (returnData.Root.ContainsKey("Version"))
            {
                int fileVersion = (int)returnData.Root["Version"];

                if (fileVersion > LatestVersion)
                    throw new Exception(string.Format("Unable to open file '{0}'. File version is greater than the latest file version. Has this file been opened in a more recent version of Apsim?", fileName));

                // Run converters if not at the latest version.
                while (fileVersion < toVersion)
                {
                    returnData.DidConvert = true;

                    // Find the method to call to upgrade the file by one version.
                    int versionFunction = fileVersion + 1;
                    MethodInfo method = typeof(Converter).GetMethod("UpgradeToVersion" + versionFunction, BindingFlags.NonPublic | BindingFlags.Static);
                    if (method == null)
                        throw new Exception("Cannot find converter to go to version " + versionFunction);

                    // Found converter method so call it.
                    method.Invoke(null, new object[] { returnData.Root, fileName });

                    fileVersion++;
                }

                if (returnData.DidConvert)
                {
                    returnData.Root["Version"] = fileVersion;
                    st = returnData.Root.ToString();
                }
            }
            returnData.DidConvert = EnsureSoilHasInitWaterAndSample(returnData.Root) || returnData.DidConvert;

            return returnData;
        }

        /// <summary>
        /// If root is a soil then make sure it has a sample or init water.
        /// </summary>
        /// <param name="root">The root node of the JSON to look at.</param>
        /// <returns>True if model was changed.</returns>
        private static bool EnsureSoilHasInitWaterAndSample(JObject root)
        {
            JObject soilRoot = root;
            string rootType = JsonUtilities.Type(soilRoot, true);

            //ASRIS soils are held below the parent when in xml form, so we should check for that.
            if (rootType == null && (root["Children"] as JArray != null) && root["Children"].Count() > 0)
            {
                soilRoot = root["Children"][0] as JObject;
                rootType = JsonUtilities.Type(soilRoot, true);
            }

            if (rootType != null && rootType == "Models.Soils.Soil")
            {
                JArray soilChildren = soilRoot["Children"] as JArray;
                if (soilChildren != null && soilChildren.Count > 0)
                {
                    var initWater = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".InitWater"));
                    if (initWater == null)
                    {
                        initWater = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".InitialWater"));
                        if (initWater != null)
                        {
                            // Models.Soils.InitialWater doesn't exist anymore
                            initWater["$type"] = "Models.Soils.Water, Models";
                            JsonUtilities.RenameModel(initWater as JObject, "Water");
                        }
                    }
                    if (initWater == null)
                        initWater = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".Sample") && string.Equals("Initial Water", c["Name"].Value<string>(), StringComparison.InvariantCultureIgnoreCase));
                    if (initWater == null)
                        initWater = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".Water,"));
                    var sample = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".Sample"));

                    if (sample == null)
                        sample = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".Solute"));

                    var soilNitrogenSample = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".SoilNitrogen"));

                    bool res = false;
                    if (initWater == null)
                    {
                        // Add in an initial water and initial conditions models.
                        initWater = new JObject();
                        initWater["$type"] = "Models.Soils.Water, Models";
                        JsonUtilities.RenameModel(initWater as JObject, "Water");
                        initWater["FilledFromTop"] = true;
                        initWater["FractionFull"] = 1;
                        soilChildren.Add(initWater);
                        res = true;
                    }

                    var physical = JsonUtilities.ChildWithName(soilRoot, "Physical");
                    bool hasPhysical = false;
                    int nLayers = 1;

                    if (physical != null)
                    {
                        nLayers = physical["Thickness"].Count();
                        hasPhysical = true;
                    }

                    if (initWater["Thickness"] == null && hasPhysical)
                    {
                        initWater["Thickness"] = physical["Thickness"];
                        initWater["InitialValues"] = physical["DUL"];
                    }

                    if (sample == null && soilNitrogenSample == null)
                    {
                        soilChildren.Add(new JObject
                        {
                            ["$type"] = "Models.Soils.Solute, Models",
                            ["Name"] = "NO3",
                            ["Thickness"] = hasPhysical ? physical["Thickness"] : new JArray(new double[] { 1800 }),
                            ["InitialValues"] = new JArray(Enumerable.Repeat(0.0, nLayers).ToArray())
                        });

                        soilChildren.Add(new JObject
                        {
                            ["$type"] = "Models.Soils.Solute, Models",
                            ["Name"] = "NH4",
                            ["Thickness"] = hasPhysical ? physical["Thickness"] : new JArray(new double[] { 1800 }),
                            ["InitialValues"] = new JArray(Enumerable.Repeat(0.0, nLayers).ToArray())
                        });

                        soilChildren.Add(new JObject
                        {
                            ["$type"] = "Models.Soils.Solute, Models",
                            ["Name"] = "Urea",
                            ["Thickness"] = hasPhysical ? physical["Thickness"] : new JArray(new double[] { 1800 }),
                            ["InitialValues"] = new JArray(Enumerable.Repeat(0.0, nLayers).ToArray())
                        });
                        res = true;
                    }

                    var soilNitrogenNO3Sample = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".SoilNitrogenNO3"));
                    var soilNitrogenNH4Sample = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".SoilNitrogenNH4"));
                    var soilNitrogenUreaSample = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".SoilNitrogenUrea"));

                    if (soilNitrogenSample != null)
                    {
                        if (soilNitrogenNO3Sample == null)
                        {
                            soilChildren.Add(new JObject
                            {
                                ["$type"] = "Models.Soils.SoilNitrogenNO3, Models",
                                ["Name"] = "NO3",
                                ["Thickness"] = hasPhysical ? physical["Thickness"] : new JArray(new double[] { 1800 }),
                                ["InitialValues"] = new JArray(Enumerable.Repeat(0.0, nLayers).ToArray())
                            });
                        }
                        if (soilNitrogenNO3Sample == null)
                        {
                            soilChildren.Add(new JObject
                            {
                                ["$type"] = "Models.Soils.SoilNitrogenNH4, Models",
                                ["Name"] = "NH4",
                                ["Thickness"] = hasPhysical ? physical["Thickness"] : new JArray(new double[] { 1800 }),
                                ["InitialValues"] = new JArray(Enumerable.Repeat(0.0, nLayers).ToArray())
                            });
                        }
                        if (soilNitrogenNO3Sample == null)
                        {
                            soilChildren.Add(new JObject
                            {
                                ["$type"] = "Models.Soils.SoilNitrogenUrea, Models",
                                ["Name"] = "Urea",
                                ["Thickness"] = hasPhysical ? physical["Thickness"] : new JArray(new double[] { 1800 }),
                                ["InitialValues"] = new JArray(Enumerable.Repeat(0.0, nLayers).ToArray())
                            });
                        }
                    }

                    // Add a soil temperature model.
                    var soilTemperature = JsonUtilities.ChildWithName(soilRoot, "Temperature");
                    if (soilTemperature == null)
                        JsonUtilities.AddModel(soilRoot, typeof(CERESSoilTemperature), "Temperature");

                    // Add a nutrient model.
                    var nutrient = JsonUtilities.ChildWithName(soilRoot, "Nutrient");
                    if (nutrient == null)
                    {
                        JsonUtilities.AddModel(soilRoot, typeof(Models.Soils.Nutrients.Nutrient), "Nutrient");
                        nutrient = JsonUtilities.ChildWithName(soilRoot, "Nutrient");
                        nutrient["ResourceName"] = "Nutrient";
                    }

                    return res;
                }
            }

            return false;
        }

        /// <summary>Upgrades to version 47 - the first JSON version.</summary>
        private static string ConvertToJSON(string st, string fileName)
        {
            string json = XmlToJson.Convert(st);
            JObject j = JObject.Parse(json);
            j["Version"] = 47;
            return j.ToString();
        }

        private static void UpgradeToVersion47(JObject root, string fileName)
        {
            // Nothing to do as conversion to JSON has already happened.
        }

        /// <summary>
        /// Upgrades to version 48. Iterates through all manager scripts, and replaces
        /// all instances of the text "DisplayTypeEnum" with "DisplayType".
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion48(JObject root, string fileName)
        {
            foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
                JsonUtilities.ReplaceManagerCode(manager, "DisplayTypeEnum", "DisplayType");
        }


        /// <summary>
        /// Upgrades to version 49. Renames Models.Morris+Parameter to Models.Sensitivity.Parameter.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion49(JObject root, string fileName)
        {
            foreach (JObject morris in JsonUtilities.ChildrenRecursively(root, "Models.Morris"))
                foreach (var parameter in morris["Parameters"])
                    parameter["$type"] = parameter["$type"].ToString().Replace("Models.Morris+Parameter", "Models.Sensitivity.Parameter");
        }

        ///<summary>
        /// Upgrades to version 50. Fixes the RelativeTo property of
        /// InitialWater components of soils copied from Apsim Classic.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        /// <remarks>
        /// ll15 must be renamed to LL15.
        /// Wheat must be renamed to WheatSoil.
        /// Maize must be renamed to MaizeSoil.
        /// </remarks>
        private static void UpgradeToVersion50(JObject root, string fileName)
        {
            foreach (JObject initialWater in JsonUtilities.ChildrenRecursively(root, "InitialWater"))
            {
                if (initialWater["RelativeTo"] != null)
                {
                    if (initialWater["RelativeTo"].ToString().ToUpper().Contains("LL15"))
                        initialWater["RelativeTo"] = initialWater["RelativeTo"].ToString().Replace("ll15", "LL15");
                    else if (!string.IsNullOrEmpty(initialWater["RelativeTo"].ToString()) && !initialWater["RelativeTo"].ToString().EndsWith("Soil"))
                        initialWater["RelativeTo"] = initialWater["RelativeTo"].ToString() + "Soil";
                }
            }
        }
        /// <summary>
        /// Changes GsMax to Gsmax350 in all models that implement ICanopy.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion51(JObject root, string fileName)
        {
            // Create a list of models that might have gsmax.
            // Might need to add in other models that implement ICanopy
            // e.g. OilPalm, AgPastureSpecies, SimpleTree, Sugarcane

            var models = new List<JObject>();
            models.AddRange(JsonUtilities.ChildrenOfType(root, "Leaf"));
            models.AddRange(JsonUtilities.ChildrenOfType(root, "SimpleLeaf"));
            models.AddRange(JsonUtilities.ChildrenOfType(root, "PerennialLeaf"));
            models.AddRange(JsonUtilities.ChildrenOfType(root, "SorghumLeaf"));

            // Loop through all models and rename Gsmax to Gsmax350.
            foreach (var model in models)
            {
                JsonUtilities.RenameProperty(model, "Gsmax", "Gsmax350");
                JsonUtilities.AddConstantFunctionIfNotExists(model, "StomatalConductanceCO2Modifier", "1.0");
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion52(JObject root, string fileName)
        {
            foreach (var SOM in JsonUtilities.ChildrenOfType(root, "SoilOrganicMatter"))
            {
                double rootWt;
                if (SOM["RootWt"] is JArray)
                    rootWt = Convert.ToDouble(SOM["RootWt"][0], CultureInfo.InvariantCulture); // This can happen when importing old APSIM file.
                else
                    rootWt = Convert.ToDouble(SOM["RootWt"], CultureInfo.InvariantCulture);
                SOM.Remove("RootWt");
                double[] thickness = MathUtilities.StringsToDoubles(JsonUtilities.Values(SOM, "Thickness"));

                double profileDepth = MathUtilities.Sum(thickness);
                double cumDepth = 0;
                double[] rootWtFraction = new double[thickness.Length];

                for (int layer = 0; layer < thickness.Length; layer++)
                {
                    double fracLayer = Math.Min(1.0, MathUtilities.Divide(profileDepth - cumDepth, thickness[layer], 0.0));
                    cumDepth += thickness[layer];
                    rootWtFraction[layer] = fracLayer * Math.Exp(-3.0 * Math.Min(1.0, MathUtilities.Divide(cumDepth, profileDepth, 0.0)));
                }
                // get the actuall FOM distribution through layers (adds up to one)
                double totFOMfraction = MathUtilities.Sum(rootWtFraction);
                for (int layer = 0; layer < thickness.Length; layer++)
                    rootWtFraction[layer] /= totFOMfraction;
                double[] rootWtVector = MathUtilities.Multiply_Value(rootWtFraction, rootWt);

                JsonUtilities.SetValues(SOM, "RootWt", rootWtVector);
            }

        }

        /// <summary>
        /// Adds solutes under SoilNitrogen.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion53(JObject root, string fileName)
        {
            foreach (var soilNitrogen in JsonUtilities.ChildrenOfType(root, "SoilNitrogen"))
            {
                JsonUtilities.CreateNewChildModel(soilNitrogen, "NO3", "Models.Soils.SoilNitrogenNO3");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "NH4", "Models.Soils.SoilNitrogenNH4");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "Urea", "Models.Soils.SoilNitrogenUrea");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "PlantAvailableNO3", "Models.Soils.SoilNitrogenPlantAvailableNO3");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "PlantAvailableNH4", "Models.Soils.SoilNitrogenPlantAvailableNH4");
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NO3", "SoilNitrogen.NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NH4", "SoilNitrogen.NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.urea", "SoilNitrogen.Urea.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.PlantAvailableNO3", "SoilNitrogen.PlantAvailableNO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.PlantAvailableNH4", "SoilNitrogen.PlantAvailableNH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].no3", "[SoilNitrogen].NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].nh4", "[SoilNitrogen].NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].urea", "[SoilNitrogen].Urea.kgha");
            }
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                var originalCode = manager.ToString();
                if (originalCode != null)
                {
                    if (originalCode.Contains("SoilNitrogen.NO3"))
                    {
                        manager.Replace("Soil.SoilNitrogen.NO3", "NO3.kgha");
                        manager.Replace("SoilNitrogen.NO3", "NO3.kgha");
                        manager.AddDeclaration("ISolute", "NO3", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.NH4"))
                    {
                        manager.Replace("Soil.SoilNitrogen.NH4", "NH4.kgha");
                        manager.Replace("SoilNitrogen.NH4", "NH4.kgha");
                        manager.AddDeclaration("ISolute", "NH4", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.urea"))
                    {
                        manager.Replace("Soil.SoilNitrogen.urea", "Urea.kgha");
                        manager.Replace("SoilNitrogen.urea", "Urea.kgha");
                        manager.AddDeclaration("ISolute", "Urea", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.PlantAvailableNO3"))
                    {
                        manager.Replace("Soil.SoilNitrogen.PlantAvailableNO3", "PlantAvailableNO3.kgha");
                        manager.Replace("SoilNitrogen.PlantAvailableNO3", "PlantAvailableNO3.kgha");
                        manager.AddDeclaration("ISolute", "PlantAvailableNO3", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.PlantAvailableNH4"))
                    {
                        manager.Replace("Soil.SoilNitrogen.PlantAvailableNH4", "PlantAvailableNH4.kgha");
                        manager.Replace("SoilNitrogen.PlantAvailableNH4", "PlantAvailableNH4.kgha");
                        manager.AddDeclaration("ISolute", "PlantAvailableNH4", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode != manager.ToString())
                    {
                        var usingLines = manager.GetUsingStatements().ToList();
                        usingLines.Add("Models.Interfaces");
                        manager.SetUsingStatements(usingLines);
                        manager.Save();
                    }
                }
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NO3", "SoilNitrogen.NO3.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NH4", "SoilNitrogen.NH4.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.urea", "SoilNitrogen.Urea.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNO3", "SoilNitrogen.PlantAvailableNO3.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNH4", "SoilNitrogen.PlantAvailableNH4.kgha");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NO3", "SoilNitrogen.NO3.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NH4", "SoilNitrogen.NH4.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.urea", "SoilNitrogen.Urea.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNO3", "SoilNitrogen.PlantAvailableNO3.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNH4", "SoilNitrogen.PlantAvailableNH4.kgha");
                }
            }
        }

        /// <summary>
        /// Remove SoluteManager.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion54(JObject root, string fileName)
        {
            foreach (var soluteManager in JsonUtilities.ChildrenOfType(root, "SoluteManager"))
                soluteManager.Remove();

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].NO3N", "[Soil].SoilNitrogen.NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].NH4N", "[Soil].SoilNitrogen.NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].UreaN", "[Soil].SoilNitrogen.Urea.kgha");
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                bool managerChanged = false;
                if (manager.Replace("mySoil.NO3N", "NO3.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NO3", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("mySoil.NH4N", "NH4.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NH4", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("mySoil.UreaN", "Urea.kgha"))
                {
                    manager.AddDeclaration("ISolute", "Urea", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.NO3N", "NO3.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NO3", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.NH4N", "NH4.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NH4", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.UreaN", "Urea.kgha"))
                {
                    manager.AddDeclaration("ISolute", "Urea", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("mySoil.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("soil.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("soil1.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                var declarations = manager.GetDeclarations();
                if (declarations.RemoveAll(declaration => declaration.TypeName == "SoluteManager") > 0)
                {
                    manager.SetDeclarations(declarations);
                    managerChanged = true;
                }

                if (managerChanged)
                {
                    var usingLines = manager.GetUsingStatements().ToList();
                    usingLines.Add("Models.Interfaces");
                    manager.SetUsingStatements(usingLines);
                    manager.Save();
                }
            }
        }


        /// <summary>
        /// Changes initial Root Wt to an array.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion55(JObject root, string fileName)
        {
            foreach (var SOM in JsonUtilities.ChildrenOfType(root, "SoilOrganicMatter"))
            {
                double soilcnr;
                if (SOM["SoilCN"] is JArray)
                    soilcnr = Convert.ToDouble(SOM["SoilCN"][0], CultureInfo.InvariantCulture); // This can happen when importing old APSIM file.
                else
                    soilcnr = Convert.ToDouble(SOM["SoilCN"], CultureInfo.InvariantCulture);
                SOM.Remove("SoilCN");
                double[] thickness = MathUtilities.StringsToDoubles(JsonUtilities.Values(SOM, "Thickness"));

                double[] SoilCNVector = new double[thickness.Length];

                for (int layer = 0; layer < thickness.Length; layer++)
                    SoilCNVector[layer] = soilcnr;

                JsonUtilities.SetValues(SOM, "SoilCN", SoilCNVector);
            }

        }

        /// <summary>
        /// Change Factor.Specifications to Factor.Specification. Also FactorValue
        /// becomes CompositeFactor.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion56(JToken root, string fileName)
        {
            foreach (var factor in JsonUtilities.ChildrenRecursively(root as JObject, "Factor"))
            {
                var parent = JsonUtilities.Parent(factor);

                string parentModelType = JsonUtilities.Type(parent);
                if (parentModelType == "Factors")
                {
                    var specifications = factor["Specifications"] as JArray;
                    if (specifications != null)
                    {
                        if (specifications.Count > 1)
                        {
                            // must be a compound factor.

                            // Change our Factor to a CompositeFactor
                            factor["$type"] = "Models.Factorial.CompositeFactor, Models";

                            // Remove the Factor from it's parent.
                            var parentChildren = parent["Children"] as JArray;
                            parentChildren.Remove(factor);

                            // Create a new site factor and add our CompositeFactor to the children list.
                            var siteFactor = JsonUtilities.ChildWithName(parent as JObject, "Site") as JObject;
                            if (siteFactor == null)
                            {
                                // Create a site factor
                                siteFactor = new JObject();
                                siteFactor["$type"] = "Models.Factorial.Factor, Models";
                                JsonUtilities.RenameModel(siteFactor, "Site");
                                JArray siteFactorChildren = new JArray();
                                siteFactor["Children"] = siteFactorChildren;

                                // Add our new site factor to our models parent.
                                parentChildren.Add(siteFactor);
                            }
                            (siteFactor["Children"] as JArray).Add(factor);

                        }
                        else
                        {
                            // Convert array to string.
                            if (specifications.Count > 0)
                                factor["Specification"] = specifications[0].ToString();
                            else
                                factor["Specification"] = new JArray();
                        }
                    }
                }
                else if (parentModelType == "Factor")
                {
                    factor["$type"] = "Models.Factorial.CompositeFactor, Models";
                }
            }

            foreach (var series in JsonUtilities.ChildrenRecursively(root as JObject, "Series"))
            {
                var factorToVaryColours = series["FactorToVaryColours"];
                if (factorToVaryColours != null && factorToVaryColours.Value<string>() == "Simulation")
                    series["FactorToVaryColours"] = "SimulationName";
                var factorToVaryMarkers = series["FactorToVaryMarkers"];
                if (factorToVaryMarkers != null && factorToVaryMarkers.Value<string>() == "Simulation")
                    series["FactorToVaryMarkers"] = "SimulationName";
                var factorToVaryLines = series["FactorToVaryLines"];
                if (factorToVaryLines != null && factorToVaryLines.Value<string>() == "Simulation")
                    series["FactorToVaryLines"] = "SimulationName";
            }
        }

        /// <summary>
        /// Upgrades to version 57. Adds a RetranslocateNonStructural node to
        /// all GenericOrgans which do not have a child called
        /// RetranslocateNitrogen.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion57(JObject root, string fileName)
        {
            foreach (JObject organ in JsonUtilities.ChildrenRecursively(root, "GenericOrgan"))
                if (JsonUtilities.ChildWithName(organ, "RetranslocateNitrogen") == null)
                    JsonUtilities.AddModel(organ, typeof(RetranslocateNonStructural), "RetranslocateNitrogen");
        }

        /// <summary>
        /// Upgrades to version 58. Renames 'ParamThickness' to 'Thickness' in Weirdo.
        /// Also change calls to property soil.SWAtWaterThickness to soil.Thickness.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion58(JObject root, string fileName)
        {
            foreach (JObject weirdo in JsonUtilities.ChildrenRecursively(root, "WEIRDO"))
            {
                var paramThicknessNode = weirdo["ParamThickness"];
                if (paramThicknessNode != null)
                {
                    weirdo["Thickness"] = paramThicknessNode;
                    weirdo.Remove("ParamThickness");
                }
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                if (manager.Replace(".SWAtWaterThickness", ".Thickness"))
                    manager.Save();
            }
        }

        /// <summary>
        /// Upgrades to version 59. Renames 'SoilCropOilPalm' to 'SoilCrop'.
        /// Renames Soil.SoilOrganicMatter.OC to Soil.Initial.OC
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion59(JObject root, string fileName)
        {
            foreach (var sample in JsonUtilities.ChildrenRecursively(root, "Sample"))
            {
                var array = sample["NO3"] as JArray;
                if (array != null)
                {
                    var nitrogenValue = new JObject();
                    nitrogenValue["$type"] = "Models.Soils.NitrogenValue, Models";

                    var storedAsPPM = sample["NO3Units"]?.ToString() == "0" ||
                                      sample["NO3Units"]?.ToString() == "ppm" ||
                                      sample["NO3Units"] == null;

                    nitrogenValue["Values"] = array;
                    nitrogenValue["StoredAsPPM"] = storedAsPPM;
                    sample.Remove("NO3");
                    sample["NO3N"] = nitrogenValue;
                }

                array = sample["NH4"] as JArray;
                if (array != null)
                {
                    var nitrogenValue = new JObject();
                    nitrogenValue["$type"] = "Models.Soils.NitrogenValue, Models";

                    var storedAsPPM = sample["NH4Units"]?.ToString() == "0" ||
                                      sample["NH4Units"]?.ToString() == "ppm" ||
                                      sample["NH4Units"] == null;

                    nitrogenValue["Values"] = array;
                    nitrogenValue["StoredAsPPM"] = storedAsPPM;
                    sample.Remove("NH4");
                    sample["NH4N"] = nitrogenValue;
                }
            }
            foreach (var soilCropOilPalmNode in JsonUtilities.ChildrenRecursively(root, "SoilCropOilPalm"))
                soilCropOilPalmNode["$type"] = "Models.Soils.SoilCrop, Models";

            foreach (var report in JsonUtilities.ChildrenRecursively(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilOrganicMatter.OC", ".Initial.OC");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].PH", "[Soil].Initial.PH");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].EC", "[Soil].Initial.EC");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].ESP", "[Soil].Initial.ESP");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].Cl", "[Soil].Initial.CL");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].OC", "[Soil].Initial.OC");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].InitialNO3N", "[Soil].Initial.NO3N.PPM");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].InitialNH4N", "[Soil].Initial.NH4N.PPM");
            }

            foreach (var series in JsonUtilities.ChildrenRecursively(root, "Series"))
            {
                if (series["XFieldName"] != null)
                    series["XFieldName"] = series["XFieldName"].ToString().Replace(".SoilOrganicMatter.OC", ".Initial.OC");
                if (series["YFieldName"] != null)
                    series["YFieldName"] = series["YFieldName"].ToString().Replace(".SoilOrganicMatter.OC", ".Initial.OC");
            }

            foreach (var expressionFunction in JsonUtilities.ChildrenRecursively(root, "ExpressionFunction"))
            {
                var expression = expressionFunction["Expression"].ToString();
                expression = expression.Replace(".SoilOrganicMatter.OC", ".Initial.OC");
                expressionFunction["Expression"] = expression;
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                var changeMade = manager.Replace("Soil.ToCumThickness(soil.Thickness)", "soil.ThicknessCumulative");

                if (manager.Replace("mySoil.Depth.Length", "mySoil.Thickness.Length"))
                    changeMade = true;

                if (manager.Replace("soil.Depth.Length", "soil.Thickness.Length"))
                    changeMade = true;

                if (changeMade)
                    manager.Save();
            }
        }

        /// <summary>
        /// Convert no3 and nh4 parameters from ppm to kg/ha.
        /// </summary>
        /// <param name="values"></param>
        private static void ConvertToPPM(JArray values)
        {
            var sample = JsonUtilities.Parent(JsonUtilities.Parent(values));
            var soil = JsonUtilities.Parent(sample) as JObject;
            var water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "Water");
            if (water == null)
                water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "WEIRDO");

            // Get soil thickness and bulk density.
            var soilThickness = water["Thickness"].Values<double>().ToArray();
            var soilBD = water["BD"].Values<double>().ToArray();

            // Get sample thickness and bulk density.
            var sampleThickness = sample["Thickness"].Values<double>().ToArray();
            var sampleBD = SoilUtilities.MapConcentration(soilBD, soilThickness, sampleThickness, soilBD.Last(), true);

            for (int i = 0; i < values.Count; i++)
                values[i] = values[i].Value<double>() * 100 / (sampleBD[i] * sampleThickness[i]);
        }

        /// <summary>
        /// Does the specified array have non NaN values?
        /// </summary>
        /// <param name="no3Values">The array to remove them from.</param>
        private static bool HasValues(JArray no3Values)
        {
            foreach (var value in no3Values)
                if (value.ToString() != "NaN")
                    return true;
            return false;
        }

        /// <summary>
        /// Upgrades to version 60. Move NO3 and NH4 from sample to Analaysis node
        /// and always store as ppm.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion60(JObject root, string fileName)
        {
            foreach (var sample in JsonUtilities.ChildrenRecursively(root, "Sample"))
            {
                var soil = JsonUtilities.Parent(sample) as JObject;
                var analysis = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "Analysis");
                var water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "Water");
                if (water == null)
                    water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "WEIRDO");

                var no3Node = sample["NO3N"];
                if (no3Node != null && no3Node.HasValues)
                {
                    if (analysis == null)
                        throw new Exception("Cannot find an analysis node while converting a soil sample.");

                    // Convert units to ppm if necessary.
                    var no3Values = no3Node["Values"] as JArray;

                    // Only overlay values if they are not NaN values.
                    if (HasValues(no3Values))
                    {
                        if (!no3Node["StoredAsPPM"].Value<bool>())
                            ConvertToPPM(no3Values);

                        // Make sure layers match analysis layers.
                        var analysisThickness = analysis["Thickness"].Values<double>().ToArray();
                        var sampleThickness = sample["Thickness"].Values<double>().ToArray();
                        var values = no3Values.Values<double>().ToArray();
                        var mappedValues = SoilUtilities.MapConcentration(values, sampleThickness, analysisThickness, 1.0, true);
                        no3Values = new JArray(mappedValues);

                        // Move from sample to analysis
                        analysis["NO3N"] = no3Values;
                    }
                }
                sample["NO3N"] = null;
                var nh4Node = sample["NH4N"];
                if (nh4Node != null && nh4Node.HasValues)
                {
                    if (analysis == null)
                        throw new Exception("Cannot find an analysis node while converting a soil sample.");

                    // Convert units to ppm if necessary.
                    var nh4Values = nh4Node["Values"] as JArray;

                    // Only overlay values if they are not NaN values.
                    if (HasValues(nh4Values))
                    {
                        if (!nh4Node["StoredAsPPM"].Value<bool>())
                            ConvertToPPM(nh4Values);

                        // Make sure layers match analysis layers.
                        var analysisThickness = analysis["Thickness"].Values<double>().ToArray();
                        var sampleThickness = sample["Thickness"].Values<double>().ToArray();
                        var values = nh4Values.Values<double>().ToArray();
                        var mappedValues = SoilUtilities.MapConcentration(values, sampleThickness, analysisThickness, 0.2, true);
                        nh4Values = new JArray(mappedValues);

                        // Move from sample to analysis
                        analysis["NH4N"] = nh4Values;
                    }
                }
                sample["NH4N"] = null;
            }
        }

        /// <summary>
        /// Upgrade to version 60. Ensures that a micromet model is within every simulation.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion61(JObject root, string fileName)
        {
            foreach (JObject Sim in JsonUtilities.ChildrenRecursively(root, "Simulation"))
            {
                List<JObject> MicroClimates = JsonUtilities.ChildrenRecursively(root, "MicroClimate");
                if (MicroClimates.Count == 0)
                    AddMicroClimate(Sim);
            }

        }

        /// <summary>
        /// Add a MicroClimate model to the specified JSON model token.
        /// </summary>
        /// <param name="simulation">An APSIM Simulation</param>
        public static void AddMicroClimate(JObject simulation)
        {
            JArray children = simulation["Children"] as JArray;
            if (children == null)
            {
                children = new JArray();
                simulation["Children"] = children;
            }

            JObject microClimateModel = new JObject();
            microClimateModel["$type"] = "Models.MicroClimate, Models";
            JsonUtilities.RenameModel(microClimateModel, "MicroClimate");
            microClimateModel["a_interception"] = "0.0";
            microClimateModel["b_interception"] = "1.0";
            microClimateModel["c_interception"] = "0.0";
            microClimateModel["d_interception"] = "0.0";
            microClimateModel["soil_albedo"] = "0.13";
            microClimateModel["SoilHeatFluxFraction"] = "0.4";
            microClimateModel["NightInterceptionFraction"] = "0.5";
            microClimateModel["ReferenceHeight"] = "2.0";
            microClimateModel["IncludeInDocumentation"] = "true";
            microClimateModel["Enabled"] = "true";
            microClimateModel["ReadOnly"] = "false";
            var weathers = JsonUtilities.ChildrenOfType(simulation, "Weather");

            // Don't bother with microclimate if no weather component
            if (weathers.Count != 0)
            {
                var weather = weathers.First();
                int index = children.IndexOf(weather);
                children.Insert(index + 1, microClimateModel);
            }
        }

        /// <summary>
        /// Upgrades to version 62. Fixes SimpleLeaf variable names
        /// following a refactor of this class.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion62(JObject root, string fileName)
        {
            // We renamed a lot of IFunctions and removed the 'Function' suffix.
            // ie HeightFunction -> Height.
            Dictionary<string, string> changedProperties = new Dictionary<string, string>();
            changedProperties.Add("Tallness", "HeightFunction");
            changedProperties.Add("Area", "LAIFunction");
            changedProperties.Add("LaiDead", "LaiDeadFunction");
            changedProperties.Add("WaterDemand", "WaterDemandFunction");
            changedProperties.Add("Cover", "CoverFunction");
            changedProperties.Add("ExtinctionCoefficient", "ExtinctionCoefficientFunction");
            changedProperties.Add("BaseHeight", "BaseHeightFunction");
            changedProperties.Add("Wideness", "WidthFunction");
            changedProperties.Add("DetachmentRate", "DetachmentRateFunction");
            changedProperties.Add("InitialWt", "InitialWtFunction");
            changedProperties.Add("MaintenanceRespiration", "MaintenanceRespirationFunction");
            changedProperties.Add("FRGR", "FRGRFunction");

            // Names of nodes which are probably simple leaf. The problem is that
            // in released models, the model is stored in a separate file to the
            // simulations. Therefore when we parse/convert the simulation file,
            // we don't know the names of the simple leaf models, so we are forced
            // take a guess.
            List<string> modelNames = new List<string>() { "Leaf", "Stover" };

            // Names of nodes which are definitely simple leaf.
            List<string> definiteSimpleLeaves = new List<string>();

            // Go through all SimpleLeafs and rename the appropriate children.
            foreach (JObject leaf in JsonUtilities.ChildrenRecursively(root, "SimpleLeaf"))
            {
                modelNames.Add(leaf["Name"].ToString());
                definiteSimpleLeaves.Add(leaf["Name"].ToString());
                // We removed the Leaf.AppearedCohortNo property.
                JObject relativeArea = JsonUtilities.FindFromPath(leaf, "DeltaLAI.Vegetative.Delta.RelativeArea");
                if (relativeArea != null && relativeArea["XProperty"].ToString() == "[Leaf].AppearedCohortNo")
                    relativeArea["XProperty"] = "[Leaf].NodeNumber";

                foreach (var change in changedProperties)
                {
                    string newName = change.Key;
                    string old = change.Value;
                    JsonUtilities.RenameChildModel(leaf, old, newName);
                }
            }

            foreach (JObject reference in JsonUtilities.ChildrenRecursively(root, "VariableReference"))
            {
                foreach (string leafName in definiteSimpleLeaves)
                {
                    foreach (KeyValuePair<string, string> property in changedProperties)
                    {
                        string oldName = property.Value;
                        string newName = property.Key;

                        string toReplace = $"{leafName}.{oldName}";
                        string replaceWith = $"{leafName}.{newName}";
                        reference["VariableName"] = reference["VariableName"].ToString().Replace(toReplace, replaceWith);

                        toReplace = $"[{leafName}].{oldName}";
                        replaceWith = $"[{leafName}].{newName}";
                        reference["VariableName"] = reference["VariableName"].ToString().Replace(toReplace, replaceWith);
                    }
                }
            }

            // Attempt some basic find/replace in manager scripts.
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                foreach (var change in changedProperties)
                {
                    string newName = change.Key;
                    string old = change.Value;

                    bool changed = false;
                    foreach (string modelName in modelNames)
                    {
                        string toReplace = $"{modelName}.{old}";
                        string replaceWith = $"{modelName}.{newName}";
                        changed |= manager.Replace(toReplace, replaceWith, true);

                        foreach (KeyValuePair<string, string> parameter in manager.Parameters)
                        {
                            string newParam = parameter.Value.Replace(toReplace, replaceWith);
                            manager.UpdateParameter(parameter.Key, newParam);
                        }

                        toReplace = $"[{modelName}].{old}";
                        replaceWith = $"[{modelName}].{newName}";
                        changed |= manager.Replace(toReplace, replaceWith, true);

                        foreach (KeyValuePair<string, string> parameter in manager.Parameters)
                        {
                            string newParam = parameter.Value.Replace(toReplace, replaceWith);
                            manager.UpdateParameter(parameter.Key, newParam);
                        }
                    }
                    if (changed)
                        manager.Save();
                }
            }

            // Fix some cultivar commands.
            foreach (JObject cultivar in JsonUtilities.ChildrenRecursively(root, "Cultivar"))
            {
                if (!cultivar["Command"].HasValues)
                    continue;

                foreach (JValue command in cultivar["Command"].Children())
                {
                    foreach (var change in changedProperties)
                    {
                        string newName = change.Key;
                        string old = change.Value;
                        foreach (string modelName in modelNames)
                        {
                            command.Value = command.Value.ToString().Replace($"{modelName}.{old}", $"{modelName}.{newName}");
                            command.Value = command.Value.ToString().Replace($"[{modelName}].{old}", $"[{modelName}].{newName}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Upgrades to version 63. Rename the 'Water' node under soil to 'Physical'
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion63(JObject root, string fileName)
        {
            foreach (var water in JsonUtilities.ChildrenRecursively(root, "Water"))
            {
                water["$type"] = "Models.Soils.Physical, Models";
                JsonUtilities.RenameModel(water, "Physical");
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Water.", ".Physical.");
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "Factor"))
            {
                var specification = factor["Specification"];
                if (specification != null)
                {
                    var specificationString = specification.ToString();
                    specificationString = specificationString.Replace(".Water.", ".Physical.");
                    specificationString = specificationString.Replace("[Water]", "[Physical]");
                    factor["Specification"] = specificationString;
                }
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "CompositeFactor"))
            {
                var specifications = factor["Specifications"];
                if (specifications != null)
                {
                    for (int i = 0; i < specifications.Count(); i++)
                    {
                        var specificationString = specifications[i].ToString();
                        specificationString = specificationString.Replace(".Water.", ".Physical.");
                        specificationString = specificationString.Replace("[Water]", "[Physical]");
                        specifications[i] = specificationString;
                    }
                }
            }
        }


        /// <summary>
        /// Upgrades to version 64. Rename the 'SoilOrganicMatter' node under soil to 'Organic'
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion64(JObject root, string fileName)
        {
            foreach (var organic in JsonUtilities.ChildrenRecursively(root, "SoilOrganicMatter"))
            {
                organic["$type"] = "Models.Soils.Organic, Models";
                JsonUtilities.RenameModel(organic, "Organic");
                organic["FOMCNRatio"] = organic["RootCN"];
                organic["FOM"] = organic["RootWt"];
                organic["SoilCNRatio"] = organic["SoilCN"];
                organic["Carbon"] = organic["OC"];
                var ocUnits = organic["OCUnits"];
                if (ocUnits != null)
                {
                    string ocUnitsString = ocUnits.ToString();
                    if (ocUnitsString == "1" || ocUnitsString == "WalkleyBlack")
                    {
                        var oc = organic["Carbon"].Values<double>().ToArray();
                        oc = MathUtilities.Multiply_Value(oc, 1.3);
                        organic["Carbon"] = new JArray(oc);
                    }
                }
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilOrganicMatter.", ".Organic.");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".RootCN", ".FOMCNRatio");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".RootWt", ".FOM");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilCN", ".SoilCNRatio");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Organic.OC", ".Organic.Carbon");
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "Factor"))
            {
                var specification = factor["Specification"];
                if (specification != null)
                {
                    var specificationString = specification.ToString();
                    specificationString = specificationString.Replace(".SoilOrganicMatter.", ".Organic.");
                    specificationString = specificationString.Replace("[SoilOrganicMatter]", "[Organic]");
                    specificationString = specificationString.Replace(".Organic.OC", ".Organic.Carbon");
                    specificationString = specificationString.Replace(".RootCN", ".FOMCNRatio");
                    specificationString = specificationString.Replace(".RootWt", ".FOM");
                    specificationString = specificationString.Replace(".SoilCN", ".SoilCNRatio");
                    factor["Specification"] = specificationString;
                }
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "CompositeFactor"))
            {
                var specifications = factor["Specifications"];
                if (specifications != null)
                {
                    for (int i = 0; i < specifications.Count(); i++)
                    {
                        var specificationString = specifications[i].ToString();
                        specificationString = specificationString.Replace(".SoilOrganicMatter.", ".Organic.");
                        specificationString = specificationString.Replace("[SoilOrganicMatter]", "[Organic]");
                        specificationString = specificationString.Replace(".OC", ".Carbon");
                        specificationString = specificationString.Replace(".RootCN", ".FOMCNRatio");
                        specificationString = specificationString.Replace(".RootWt", ".FOM");
                        specificationString = specificationString.Replace(".SoilCN", ".SoilCNRatio");
                        specifications[i] = specificationString;
                    }
                }
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilOrganicMatter", "Organic");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace(".Organic.OC", ".Organic.Carbon");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilOrganicMatter", "Organic");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace(".Organic.OC", ".Organic.Carbon");
                }
            }

            foreach (var child in JsonUtilities.ChildrenRecursively(root))
            {
                if (JsonUtilities.Type(child) == "Morris" || JsonUtilities.Type(child) == "Sobol")
                {
                    var parameters = child["Parameters"];
                    for (int i = 0; i < parameters.Count(); i++)
                    {
                        var parameterString = parameters[i]["Path"].ToString();
                        parameterString = parameterString.Replace(".SoilOrganicMatter.", ".Organic.");
                        parameterString = parameterString.Replace("[SoilOrganicMatter]", "[Organic]");
                        parameterString = parameterString.Replace(".OC", ".Carbon");
                        parameters[i]["Path"] = parameterString;
                    }
                }
            }
        }

        /// <summary>
        /// Upgrades to version 65. Rename the 'Analysis' node under soil to 'Chemical'
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion65(JObject root, string fileName)
        {
            foreach (var chemical in JsonUtilities.ChildrenRecursively(root, "Analysis"))
            {
                var soil = JsonUtilities.Parent(chemical);
                var physical = JsonUtilities.ChildWithName(soil as JObject, "Physical");

                chemical["$type"] = "Models.Soils.Chemical, Models";
                JsonUtilities.RenameModel(chemical, "Chemical");
                if (physical != null && physical["Thickness"] != null)
                {
                    // Move particle size numbers from chemical to physical and make sure layers are mapped.
                    var physicalThickness = physical["Thickness"].Values<double>().ToArray();
                    var chemicalThickness = chemical["Thickness"].Values<double>().ToArray();

                    if (chemical["ParticleSizeSand"] != null && chemical["ParticleSizeSand"].HasValues)
                    {
                        var values = chemical["ParticleSizeSand"].Values<double>().ToArray();
                        if (values.Length != chemicalThickness.Length)
                            Array.Resize(ref values, chemicalThickness.Length);
                        var mappedValues = SoilUtilities.MapConcentration(values, chemicalThickness, physicalThickness, values.Last(), true);
                        physical["ParticleSizeSand"] = new JArray(mappedValues);
                    }

                    if (chemical["ParticleSizeSilt"] != null && chemical["ParticleSizeSilt"].HasValues)
                    {
                        var values = chemical["ParticleSizeSilt"].Values<double>().ToArray();
                        if (values.Length != chemicalThickness.Length)
                            Array.Resize(ref values, chemicalThickness.Length);
                        var mappedValues = SoilUtilities.MapConcentration(values, chemicalThickness, physicalThickness, values.Last(), true);
                        physical["ParticleSizeSilt"] = new JArray(mappedValues);
                    }

                    if (chemical["ParticleSizeClay"] != null && chemical["ParticleSizeClay"].HasValues)
                    {
                        var values = chemical["ParticleSizeClay"].Values<double>().ToArray();
                        if (values.Length != chemicalThickness.Length)
                            Array.Resize(ref values, chemicalThickness.Length);
                        var mappedValues = SoilUtilities.MapConcentration(values, chemicalThickness, physicalThickness, values.Last(), true);
                        physical["ParticleSizeClay"] = new JArray(mappedValues);
                    }

                    if (chemical["Rocks"] != null && chemical["Rocks"].HasValues)
                    {
                        //Some soils from APSoil have NaN in their rock values
                        var values = chemical["Rocks"].Values<double>().ToArray();
                        for (int i = 0; i < values.Length; i++) {
                            if (double.IsNaN(values[i]))
                                values[i] = 0;
                        }
                        if (values.Length != chemicalThickness.Length)
                            Array.Resize(ref values, chemicalThickness.Length);
                        var mappedValues = SoilUtilities.MapConcentration(values, chemicalThickness, physicalThickness, values.Last(), true);
                        physical["Rocks"] = new JArray(mappedValues);
                    }

                    // convert ph units
                    var phUnits = physical["PHUnits"];
                    if (phUnits != null)
                    {
                        string phUnitsString = phUnits.ToString();
                        if (phUnitsString == "1")
                        {
                            // pH in water = (pH in CaCl X 1.1045) - 0.1375
                            var ph = physical["PH"].Values<double>().ToArray();
                            ph = MathUtilities.Subtract_Value(MathUtilities.Multiply_Value(ph, 1.1045), 0.1375);
                            chemical["PH"] = new JArray(ph);
                        }
                    }
                }
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Analysis.", ".Chemical.");
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "Factor"))
            {
                var specification = factor["Specification"];
                if (specification != null)
                {
                    var specificationString = specification.ToString();
                    specificationString = specificationString.Replace(".Analysis.", ".Chemical.");
                    specificationString = specificationString.Replace("[Analysis]", "[Chemical]");
                    factor["Specification"] = specificationString;
                }
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "CompositeFactor"))
            {
                var specifications = factor["Specifications"];
                if (specifications != null)
                {
                    for (int i = 0; i < specifications.Count(); i++)
                    {
                        var specificationString = specifications[i].ToString();
                        specificationString = specificationString.Replace(".Analysis.", ".Chemical.");
                        specificationString = specificationString.Replace("[Analysis]", "[Chemical]");
                        specifications[i] = specificationString;
                    }
                }
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("Analysis", "Chemical");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("Analysis", "Chemical");
                }
            }

            foreach (var child in JsonUtilities.ChildrenRecursively(root))
            {
                if (JsonUtilities.Type(child) == "Morris" || JsonUtilities.Type(child) == "Sobol")
                {
                    var parameters = child["Parameters"];
                    for (int i = 0; i < parameters.Count(); i++)
                    {
                        var parameterString = parameters[i]["Path"].ToString();
                        parameterString = parameterString.Replace(".Analysis.", ".Chemical.");
                        parameterString = parameterString.Replace("[Analysis]", "[Chemical]");
                        parameters[i]["Path"] = parameterString;
                    }
                }
            }
        }

        /// <summary>
        /// When a factor is under a factors model, insert a permutation model.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion66(JToken root, string fileName)
        {
            foreach (var factors in JsonUtilities.ChildrenRecursively(root as JObject, "Factors"))
            {
                if (JsonUtilities.Children(factors).Count > 1)
                {
                    var permutationsNode = new JObject();
                    permutationsNode["$type"] = "Models.Factorial.Permutation, Models";
                    JsonUtilities.RenameModel(permutationsNode, "Permutation");
                    permutationsNode["Children"] = factors["Children"];
                    var children = new JArray(permutationsNode);
                    factors["Children"] = children;
                }
            }
        }

        /// <summary>
        /// Upgrades to version 67. Sets the Start and End properties
        /// in clock to the values previously stored in StartDate and EndDate.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion67(JObject root, string fileName)
        {
            foreach (JObject clock in JsonUtilities.ChildrenRecursively(root, "Clock"))
            {
                clock["Start"] = clock["StartDate"];
                clock["End"] = clock["EndDate"];
            }
        }

        /// <summary>
        /// Upgrades to version 68. Removes AgPasture.Sward
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion68(JObject root, string fileName)
        {
            foreach (JObject sward in JsonUtilities.ChildrenRecursively(root, "Sward"))
            {
                foreach (JObject pastureSpecies in JsonUtilities.Children(sward))
                {
                    if (pastureSpecies["Name"].ToString().Equals("Ryegrass", StringComparison.InvariantCultureIgnoreCase))
                        JsonUtilities.RenameModel(pastureSpecies, "AGPRyegrass");
                    if (pastureSpecies["Name"].ToString().Equals("WhiteClover", StringComparison.InvariantCultureIgnoreCase))
                        JsonUtilities.RenameModel(pastureSpecies, "AGPWhiteClover");
                    pastureSpecies["ResourceName"] = pastureSpecies["Name"];

                    var swardParentChildren = JsonUtilities.Parent(sward)["Children"] as JArray;
                    swardParentChildren.Add(pastureSpecies);
                }
                sward.Remove();
            }

            bool foundAgPastureWhiteClover = false; // as opposed to a PMF whiteclover
            foreach (JObject pastureSpecies in JsonUtilities.ChildrenRecursively(root, "PastureSpecies"))
            {
                if (pastureSpecies["Name"].ToString().Equals("Ryegrass", StringComparison.InvariantCultureIgnoreCase))
                    JsonUtilities.RenameModel(pastureSpecies, "AGPRyegrass");
                if (pastureSpecies["Name"].ToString().Equals("WhiteClover", StringComparison.InvariantCultureIgnoreCase))
                {
                    JsonUtilities.RenameModel(pastureSpecies, "AGPWhiteClover");
                    foundAgPastureWhiteClover = true;
                }

                pastureSpecies["ResourceName"] = pastureSpecies["Name"];
            }

            foreach (JObject soilCrop in JsonUtilities.ChildrenRecursively(root, "SoilCrop"))
            {
                if (soilCrop["Name"].ToString().Equals("SwardSoil", StringComparison.InvariantCultureIgnoreCase))
                    soilCrop.Remove();
                if (soilCrop["Name"].ToString().Equals("RyegrassSoil", StringComparison.InvariantCultureIgnoreCase))
                    JsonUtilities.RenameModel(soilCrop, "AGPRyegrassSoil");
                if (foundAgPastureWhiteClover && soilCrop["Name"].ToString().Equals("WhiteCloverSoil", StringComparison.InvariantCultureIgnoreCase))
                    JsonUtilities.RenameModel(soilCrop, "AGPWhiteCloverSoil");
            }
        }

        /// <summary>
        /// Upgrades to version 69. Fixes link attributes in manager scripts after
        /// link refactoring.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the .apsimx file.</param>
        private static void UpgradeToVersion69(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                // The linking code previously had a hack which automatically enabled link by name if the target
                // model type is IFunction or Biomass (or any inherited class thereof). I've removed this hack
                // from the link resolution code, which means that all such links must be adjusted accordingly.

                // [Link(...)] [Units] [...] Biomass -> [Link(ByName = true, ...)] [Units] [...] Biomass
                manager.ReplaceRegex(@"\[Link\(([^\)]+)\)\]((\s*\[[^\]]+\])*\s*(public|private|protected|internal|static|readonly| )*\s*(IFunction|Biomass|CNReductionForCover|CNReductionForTillage|RunoffModel|WaterTableModel|HeightFunction|DecumulateFunction|EndOfDayFunction|LiveOnEventFunction|AccumulateAtEvent|DailyMeanVPD|CERESDenitrificationWaterFactor|CERESDenitrificationTemperatureFactor|CERESMineralisationFOMCNRFactor|DayCentN2OFractionModel|CERESNitrificationpHFactor|CERESNitrificationWaterFactor|CERESUreaHydrolysisModel|CERESMineralisationWaterFactor|CERESMineralisationTemperatureFactor|CERESNitrificationModel|StringComparisonFunction|AccumulateByDate|AccumulateByNumericPhase|TrackerFunction|ArrayFunction|WangEngelTempFunction|BoundFunction|LinearAfterThresholdFunction|SoilWaterScale|MovingAverageFunction|HoldFunction|DeltaFunction|MovingSumFunction|QualitativePPEffect|AccumulateFunction|AddFunction|AgeCalculatorFunction|AirTemperatureFunction|BellCurveFunction|Constant|DivideFunction|ExponentialFunction|ExpressionFunction|ExternalVariable|LessThanFunction|LinearInterpolationFunction|MaximumFunction|MinimumFunction|MultiplyFunction|OnEventFunction|PhaseBasedSwitch|PhaseLookup|PhaseLookupValue|PhotoperiodDeltaFunction|PhotoperiodFunction|PowerFunction|SigmoidFunction|SoilTemperatureDepthFunction|SoilTemperatureFunction|SoilTemperatureWeightedFunction|SplineInterpolationFunction|StageBasedInterpolation|SubtractFunction|VariableReference|WeightedTemperatureFunction|XYPairs|CanopyPhotosynthesis|RUECO2Function|RUEModel|StorageDMDemandFunction|StorageNDemandFunction|InternodeCohortDemandFunction|BerryFillingRateFunction|TEWaterDemandFunction|FillingRateFunction|AllometricDemandFunction|InternodeDemandFunction|PartitionFractionDemandFunction|PopulationBasedDemandFunction|PotentialSizeDemandFunction|RelativeGrowthRateDemandFunction))", @"[Link(Type = LinkType.Child, ByName = true, $1)]$2");

                // [Link] IFunction -> [Link(ByName = true)] IFunction
                manager.ReplaceRegex(@"\[Link\]((\s*\[[^\]]+\])*\s*(public|private|protected|internal|static|readonly| )*\s*(IFunction|Biomass|CNReductionForCover|CNReductionForTillage|RunoffModel|WaterTableModel|HeightFunction|DecumulateFunction|EndOfDayFunction|LiveOnEventFunction|AccumulateAtEvent|DailyMeanVPD|CERESDenitrificationWaterFactor|CERESDenitrificationTemperatureFactor|CERESMineralisationFOMCNRFactor|DayCentN2OFractionModel|CERESNitrificationpHFactor|CERESNitrificationWaterFactor|CERESUreaHydrolysisModel|CERESMineralisationWaterFactor|CERESMineralisationTemperatureFactor|CERESNitrificationModel|StringComparisonFunction|AccumulateByDate|AccumulateByNumericPhase|TrackerFunction|ArrayFunction|WangEngelTempFunction|BoundFunction|LinearAfterThresholdFunction|SoilWaterScale|MovingAverageFunction|HoldFunction|DeltaFunction|MovingSumFunction|QualitativePPEffect|AccumulateFunction|AddFunction|AgeCalculatorFunction|AirTemperatureFunction|BellCurveFunction|Constant|DivideFunction|ExponentialFunction|ExpressionFunction|ExternalVariable|LessThanFunction|LinearInterpolationFunction|MaximumFunction|MinimumFunction|MultiplyFunction|OnEventFunction|PhaseBasedSwitch|PhaseLookup|PhaseLookupValue|PhotoperiodDeltaFunction|PhotoperiodFunction|PowerFunction|SigmoidFunction|SoilTemperatureDepthFunction|SoilTemperatureFunction|SoilTemperatureWeightedFunction|SplineInterpolationFunction|StageBasedInterpolation|SubtractFunction|VariableReference|WeightedTemperatureFunction|XYPairs|CanopyPhotosynthesis|RUECO2Function|RUEModel|StorageDMDemandFunction|StorageNDemandFunction|InternodeCohortDemandFunction|BerryFillingRateFunction|TEWaterDemandFunction|FillingRateFunction|AllometricDemandFunction|InternodeDemandFunction|PartitionFractionDemandFunction|PopulationBasedDemandFunction|PotentialSizeDemandFunction|RelativeGrowthRateDemandFunction))", @"[Link(Type = LinkType.Child, ByName = true)]$1");

                // Here I assume that all [LinkByPath] links will have a path argument supplied.
                // [LinkByPath(...)] -> [Link(Type = LinkType.Path, ...)]
                manager.ReplaceRegex(@"\[LinkByPath\(([^\)]+)\)", @"[Link(Type = LinkType.Path, $1)");

                // [ParentLink(...)] -> [Link(Type = LinkType.Ancestor, ...)]
                manager.ReplaceRegex(@"\[ParentLink\(([^\)]+)\)", @"[Link(Type = LinkType.Ancestor, $1)");

                // [ParentLink] -> [Link(Type = LinkType.Ancestor, ByName = false)]
                manager.Replace("[ParentLink]", "[Link(Type = LinkType.Ancestor)]", caseSensitive: true);

                // [ScopedLinkByName(...)] -> [Link(ByName = true, ...)]
                manager.ReplaceRegex(@"\[ScopedLinkByName\(([^\)]+)\)", @"[Link(ByName = true, $1)");

                // [ScopedLinkByName] -> [Link(ByName = true)]
                manager.Replace("[ScopedLinkByName]", "[Link(ByName = true)]", caseSensitive: true);

                // [ScopedLink(...)] -> [Link(...)]
                manager.ReplaceRegex(@"\[ScopedLink\(([^\)]+)\)", @"[Link($1)");

                // [ScopedLink] -> [Link]
                manager.Replace("[ScopedLink]", "[Link]", caseSensitive: true);

                // [ChildLinkByName(...)] -> [Link(Type = LinkType.Child, ByName = true, ...)]
                manager.ReplaceRegex(@"\[ChildLinkByName\(([^\)]+)\)", @"[Link(Type = LinkType.Child, ByName = true, $1)");

                // [ChildLinkByName] -> [Link(Type = LinkType.Child, ByName = true)]
                manager.Replace("[ChildLinkByName]", "[Link(Type = LinkType.Child, ByName = true)]", caseSensitive: true);

                // [ChildLink(...)] -> [Link(Type = LinkType.Child, ...)]
                manager.ReplaceRegex(@"\[ChildLink\(([^\)]+)\)", @"[Link(Type = LinkType.Child, $1)");

                // [ChildLink] -> [Link(Type = LinkType.Child)]
                manager.Replace("[ChildLink]", "[Link(Type = LinkType.Child)]", caseSensitive: true);

                manager.Save();
            }
        }

        /// <summary>
        /// Changes the type of the Stock component inital values genotypes array
        /// from StockGeno to SingleGenotypeInits.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion70(JObject root, string fileName)
        {
            foreach (var stockNode in JsonUtilities.ChildrenOfType(root, "Stock"))
            {
                // for each element of GenoTypes[]
                var genotypes = stockNode["GenoTypes"];
                for (int i = 0; i < genotypes.Count(); i++)
                {
                    genotypes[i]["$type"] = "Models.GrazPlan.SingleGenotypeInits, Models";
                    double dr = Convert.ToDouble(genotypes[i]["DeathRate"]);
                    double drw = Convert.ToDouble(genotypes[i]["WnrDeathRate"]);
                    genotypes[i]["DeathRate"] = new JArray(new double[] { dr, drw });
                    genotypes[i]["PotFleeceWt"] = genotypes[i]["RefFleeceWt"];
                    genotypes[i]["Conceptions"] = genotypes[i]["Conception"];
                    genotypes[i]["GenotypeName"] = genotypes[i]["Name"];
                }
            }
        }

        /// <summary>
        /// Alters all existing linint functions to have a child variable reference IFunction called XValue instead of a
        /// string property called XProperty that IFunction then had to locate
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion71(JObject root, string fileName)
        {
            foreach (JObject linint in JsonUtilities.ChildrenRecursively(root, "LinearInterpolationFunction"))
            {
                VariableReference varRef = new VariableReference();
                varRef.Name = "XValue";
                varRef.VariableName = linint["XProperty"].ToString();
                JsonUtilities.AddModel(linint, varRef);
                linint.Remove("XProperty");
            }
        }

        /// <summary>
        /// Remove .Value() from all variable references because it is redundant
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion72(JObject root, string fileName)
        {
            foreach (var varRef in JsonUtilities.ChildrenRecursively(root, "VariableReference"))
                varRef["VariableName"] = varRef["VariableName"].ToString().Replace(".Value()", "");

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Value()", "");

            foreach (var graph in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (graph["XFieldName"] != null)
                    graph["XFieldName"] = graph["XFieldName"].ToString().Replace(".Value()", "");
                if (graph["X2FieldName"] != null)
                    graph["X2FieldName"] = graph["X2FieldName"].ToString().Replace(".Value()", "");
                if (graph["YFieldName"] != null)
                    graph["YFieldName"] = graph["YFieldName"].ToString().Replace(".Value()", "");
                if (graph["Y2FieldName"] != null)
                    graph["Y2FieldName"] = graph["Y2FieldName"].ToString().Replace(".Value()", "");
            }
        }

        /// <summary>
        /// Alters all existing AllometricDemand functions to have a child variable reference IFunction called XValue and YValue instead of
        /// string property called XProperty and YProperty that it then had to locate.  Aiming to get all things using get for properties
        /// to be doing it via Variable reference so we can stream line scoping rules
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion73(JObject root, string fileName)
        {
            foreach (JObject Alomet in JsonUtilities.ChildrenRecursively(root, "AllometricDemandFunction"))
            {
                VariableReference XvarRef = new VariableReference();
                XvarRef.Name = "XValue";
                XvarRef.VariableName = Alomet["XProperty"].ToString();
                JsonUtilities.AddModel(Alomet, XvarRef);
                Alomet.Remove("XProperty");
                VariableReference YvarRef = new VariableReference();
                YvarRef.Name = "YValue";
                YvarRef.VariableName = Alomet["YProperty"].ToString();
                JsonUtilities.AddModel(Alomet, YvarRef);
                Alomet.Remove("YProperty");

            }
        }

        /// <summary>
        /// Changes the Surface Organic Matter property FractionFaecesAdded to 1.0
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion74(JObject root, string fileName)
        {
            foreach (JObject som in JsonUtilities.ChildrenRecursively(root, "SurfaceOrganicMatter"))
                som["FractionFaecesAdded"] = "1.0";
        }

        /// <summary>
        /// Change TreeLeafAreas to ShadeModiers in Tree Proxy
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion75(JObject root, string fileName)
        {
            foreach (JObject TreeProxy in JsonUtilities.ChildrenRecursively(root, "TreeProxy"))
            {
                TreeProxy["ShadeModifiers"] = TreeProxy["TreeLeafAreas"];
                // ShadeModifiers is sometimes null (not sure why) so fill it with 1s using Heights to get array length
                var SM = TreeProxy["Heights"].Values<double>().ToArray();
                for (int i = 0; i < SM.Count(); i++)
                    SM[i] = 1.0;
                TreeProxy["ShadeModifiers"] = new JArray(SM);
            }
        }

        /// <summary>
        /// Change flow_urea to FlowUrea in soil water
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion76(JObject root, string fileName)
        {
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                if (manager.Replace(".flow_urea", ".FlowUrea"))
                    manager.Save();
            }
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".flow_urea", ".FlowUrea");
            }
        }

        /// <summary>
        /// Change the property in Stock to Genotypes
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion77(JObject root, string fileName)
        {
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                if (manager.Replace(".GenoTypes", ".Genotypes"))
                    manager.Save();
            }
            foreach (var stock in JsonUtilities.ChildrenOfType(root, "Stock"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(stock, ".GenoTypes", ".Genotypes");
            }
        }

        /// <summary>
        /// Change the namespace for SimpleGrazing
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion78(JObject root, string fileName)
        {
            foreach (var simpleGrazing in JsonUtilities.ChildrenOfType(root, "SimpleGrazing"))
            {
                simpleGrazing["$type"] = "Models.AgPasture.SimpleGrazing, Models";
            }
        }

        /// <summary>
        /// Change manager method and AgPasture variable names.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion79(JObject root, string fileName)
        {
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".Graze(", ".RemoveBiomass("),
                new Tuple<string, string>(".EmergingTissuesWt",   ".EmergingTissue.Wt"),
                new Tuple<string, string>(".EmergingTissuesN",    ".EmergingTissue.N"),
                new Tuple<string, string>(".DevelopingTissuesWt", ".DevelopingTissue.Wt"),
                new Tuple<string, string>(".DevelopingTissuesN",  ".DevelopingTissue.N"),
                new Tuple<string, string>(".MatureTissuesWt", ".MatureTissue.Wt"),
                new Tuple<string, string>(".MatureTissuesN",  ".MatureTissue.N"),
                new Tuple<string, string>(".DeadTissuesWt", ".DeadTissue.Wt"),
                new Tuple<string, string>(".DeadTissuesN",  ".DeadTissue.N")
            };

            JsonUtilities.RenameVariables(root, changes);
        }

        /// <summary>
        /// Replace ExcelMultiInput with ExcelInput.
        /// Change ExcelInput.FileName from a string into a string[].
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion80(JObject root, string fileName)
        {
            // Rename ExcelInput.FileName to FileNames and make it an array.
            foreach (JObject excelInput in JsonUtilities.ChildrenRecursively(root, "ExcelInput"))
            {
                if (excelInput["FileName"] != null)
                    excelInput["FileNames"] = new JArray(excelInput["FileName"].Value<string>());
            }

            // Replace ExcelMultiInput with an ExcelInput.
            foreach (JObject excelMultiInput in JsonUtilities.ChildrenRecursively(root, "ExcelMultiInput"))
            {
                excelMultiInput["$type"] = "Models.PostSimulationTools.ExcelInput, Models";
            }
        }


        /// <summary>
        /// Seperate life cycle process class into Growth, Transfer and Mortality classes.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion81(JObject root, string fileName)
        {
            // Rename ExcelInput.FileName to FileNames and make it an array.
            foreach (JObject LSP in JsonUtilities.ChildrenRecursively(root, "LifeStageProcess"))
            {
                if (int.Parse(LSP["ProcessAction"].ToString()) == 0) //Process is Transfer
                {
                    LSP["$type"] = "Models.LifeCycle.LifeStageTransfer, Models";
                }
                else if (int.Parse(LSP["ProcessAction"].ToString()) == 1) //Process is PhysiologicalGrowth
                {
                    LSP["$type"] = "Models.LifeCycle.LifeStageGrowth, Models";
                }
                else if (int.Parse(LSP["ProcessAction"].ToString()) == 2) //Process is Mortality
                {
                    LSP["$type"] = "Models.LifeCycle.LifeStageMortality, Models";
                }

            }

            foreach (JObject LSRP in JsonUtilities.ChildrenRecursively(root, "LifeStageReproductionProcess"))
            {
                LSRP["$type"] = "Models.LifeCycle.LifeStageReproduction, Models";
            }

            foreach (JObject LSIP in JsonUtilities.ChildrenRecursively(root, "LifeStageImmigrationProcess"))
            {
                LSIP["$type"] = "Models.LifeCycle.LifeStageImmigration, Models";
            }
        }

        /// <summary>
        /// Add Critical N Conc (if not existing) to all Root Objects by copying the maximum N conc
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion82(JObject root, string fileName)
        {
            foreach (JObject r in JsonUtilities.ChildrenRecursively(root, "Root"))
            {
                if (JsonUtilities.ChildWithName(r, "CriticalNConc") == null)
                {
                    JObject minNConc = JsonUtilities.ChildWithName(r, "MinimumNConc");
                    if (minNConc == null)
                        throw new Exception("Root has no CriticalNConc or MaximumNConc");

                    VariableReference varRef = new VariableReference();
                    varRef.Name = "CriticalNConc";
                    varRef.VariableName = "[Root].MinimumNConc";
                    JsonUtilities.AddModel(r, varRef);
                }
            }
        }

        /// <summary>
        /// Remove .Value() from everywhere possible.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion83(JObject root, string fileName)
        {
            // 1. Report
            foreach (JObject report in JsonUtilities.ChildrenRecursively(root, "Report"))
            {
                JArray variables = report["VariableNames"] as JArray;
                if (variables == null)
                    continue;

                for (int i = 0; i < variables.Count; i++)
                    variables[i] = variables[i].ToString().Replace(".Value()", "");
            }

            // 2. ExpressionFunction
            foreach (JObject function in JsonUtilities.ChildrenRecursively(root, "ExpressionFunction"))
                function["Expression"] = function["Expression"].ToString().Replace(".Value()", "");
        }

        /// <summary>
        /// Renames the Input.FileName property to FileNames and makes it an array.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion84(JObject root, string fileName)
        {
            foreach (JObject input in JsonUtilities.ChildrenRecursively(root, "Input"))
                if (input["FileName"] != null)
                    input["FileNames"] = new JArray(input["FileName"]);
        }

        /// <summary>
        /// Add a field to the Checkpoints table.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion85(JObject root, string fileName)
        {
            SQLite db = new SQLite();
            var dbFileName = Path.ChangeExtension(fileName, ".db");
            if (File.Exists(dbFileName))
            {
                try
                {
                    db.OpenDatabase(dbFileName, false);
                    if (db.TableExists("_Checkpoints"))
                    {
                        if (!db.GetTableColumns("_Checkpoints").Contains("OnGraphs"))
                        {
                            db.AddColumn("_Checkpoints", "OnGraphs", "integer");
                        }
                    }
                }
                finally
                {
                    db.CloseDatabase();
                }
            }
        }

        /// <summary>
        /// Add new methods structure to OrganArbitrator.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion86(JObject root, string fileName)
        {
            foreach (var arbitrator in JsonUtilities.ChildrenOfType(root, "OrganArbitrator"))
            {
                //remove DMArbitrator, and NArbitrator
                var children = JsonUtilities.Children(arbitrator);
                var exdm = children.Find(c => JsonUtilities.Name(c).Equals("dmArbitrator", StringComparison.OrdinalIgnoreCase));
                JsonUtilities.RemoveChild(arbitrator, JsonUtilities.Name(exdm));

                var exn = children.Find(c => JsonUtilities.Name(c).Equals("nArbitrator", StringComparison.OrdinalIgnoreCase));
                JsonUtilities.RemoveChild(arbitrator, JsonUtilities.Name(exn));

                JsonUtilities.RenameModel(exdm, "ArbitrationMethod");
                JsonUtilities.RenameModel(exn, "ArbitrationMethod");

                //Add DMArbitration
                var dm = JsonUtilities.CreateNewChildModel(arbitrator, "DMArbitration", "Models.PMF.BiomassTypeArbitrator");
                var folder = JsonUtilities.CreateNewChildModel(dm, "PotentialPartitioningMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "ReallocationMethod", "Models.PMF.Arbitrator.ReallocationMethod");
                JsonUtilities.CreateNewChildModel(folder, "AllocateFixationMethod", "Models.PMF.Arbitrator.AllocateFixationMethod");
                JsonUtilities.CreateNewChildModel(folder, "RetranslocationMethod", "Models.PMF.Arbitrator.RetranslocationMethod");
                JsonUtilities.CreateNewChildModel(folder, "SendPotentialDMAllocationsMethod", "Models.PMF.Arbitrator.SendPotentialDMAllocationsMethod");

                folder = JsonUtilities.CreateNewChildModel(dm, "AllocationMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "NutrientConstrainedAllocationMethod", "Models.PMF.Arbitrator.NutrientConstrainedAllocationMethod");
                JsonUtilities.CreateNewChildModel(folder, "DryMatterAllocationsMethod", "Models.PMF.Arbitrator.DryMatterAllocationsMethod");

                JArray dmChildren = dm["Children"] as JArray;
                dmChildren.Add(exdm);

                //Add N Arbitration
                var n = JsonUtilities.CreateNewChildModel(arbitrator, "NArbitration", "Models.PMF.BiomassTypeArbitrator");
                folder = JsonUtilities.CreateNewChildModel(n, "PotentialPartitioningMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "ReallocationMethod", "Models.PMF.Arbitrator.ReallocationMethod");

                folder = JsonUtilities.CreateNewChildModel(n, "ActualPartitioningMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "AllocateFixationMethod", "Models.PMF.Arbitrator.AllocateFixationMethod");
                JsonUtilities.CreateNewChildModel(folder, "RetranslocationMethod", "Models.PMF.Arbitrator.RetranslocationMethod");

                folder = JsonUtilities.CreateNewChildModel(n, "AllocationMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "NitrogenAllocationsMethod", "Models.PMF.Arbitrator.NitrogenAllocationsMethod");

                JArray nChildren = n["Children"] as JArray;
                nChildren.Add(exn);
                var allocatesMethod = JsonUtilities.CreateNewChildModel(n, "AllocateUptakesMethod", "Models.PMF.Arbitrator.AllocateUptakesMethod");

                var water = JsonUtilities.CreateNewChildModel(arbitrator, "WaterUptakeMethod", "Models.PMF.Arbitrator.WaterUptakeMethod");
                var nitrogen = JsonUtilities.CreateNewChildModel(arbitrator, "NitrogenUptakeMethod", "Models.PMF.Arbitrator.NitrogenUptakeMethod");
            }
        }

        /// <summary>
        /// Remove Models.Report namespace.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion87(JObject root, string fileName)
        {
            // Fix type of Report nodes
            foreach (JObject report in JsonUtilities.ChildrenRecursively(root, "Report"))
                report["$type"] = report["$type"].ToString().Replace("Report.Report", "Report");

            // Fix type of all models in the now-removed Models.Graph namespace
            foreach (JObject model in JsonUtilities.ChildrenRecursively(root))
                model["$type"] = model["$type"].ToString().Replace("Models.Graph.", "Models.");

            // Fix graph axes - these are a property of graphs, not a model themselves
            foreach (JObject graph in JsonUtilities.ChildrenRecursively(root, "Graph"))
            {
                JArray axes = graph["Axis"] as JArray;
                if (axes != null)
                    foreach (JObject axis in axes)
                        if (axis["$type"] != null)
                            axis["$type"] = axis["$type"].ToString().Replace("Models.Graph", "Models");
            }

            // Fix nutrient directed graphs - the nodes/arcs are not children, but
            // need to have their types fixed.
            foreach (JObject nutrient in JsonUtilities.ChildrenRecursively(root, "Nutrient"))
            {
                JObject directedGraph = nutrient["DirectedGraphInfo"] as JObject;
                if (directedGraph != null)
                {
                    directedGraph["$type"] = directedGraph["$type"].ToString().Replace("Models.Graph.", "Models.");
                    JArray nodes = directedGraph["Nodes"] as JArray;
                    if (nodes != null)
                        foreach (JObject node in nodes)
                            node["$type"] = node["$type"].ToString().Replace("Models.Graph.", "Models.");
                    JArray arcs = directedGraph["Arcs"] as JArray;
                    if (arcs != null)
                        foreach (JObject arc in arcs)
                            arc["$type"] = arc["$type"].ToString().Replace("Models.Graph.", "Models.");
                }
            }

            // Replace ExcelMultiInput with an ExcelInput.
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                manager.Replace("Models.Report", "Models");
                manager.Replace("using Report", "using Models");
                //manager.ReplaceRegex("(using Models.+)using Models", "$1");
                manager.Replace("Report.Report", "Report");

                manager.Replace("Models.Graph", "Models");
                manager.Replace("Graph.Graph", "Graph");

                List<string> usingStatements = manager.GetUsingStatements().ToList();
                usingStatements.Remove("Models.Graph");
                usingStatements.Remove("Graph");

                manager.SetUsingStatements(usingStatements.Distinct());

                manager.Save();
            }
        }

        /// <summary>
        /// Replace SoilWater model with WaterBalance model.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion88(JObject root, string fileName)
        {
            foreach (JObject soilWater in JsonUtilities.ChildrenRecursively(root, "SoilWater"))
            {
                soilWater["$type"] = "Models.WaterModel.WaterBalance, Models";
                soilWater["ResourceName"] = "WaterBalance";
                if (soilWater["discharge_width"] != null)
                    soilWater["DischargeWidth"] = soilWater["discharge_width"];
                if (soilWater["catchment_area"] != null)
                    soilWater["CatchmentArea"] = soilWater["catchment_area"];
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                bool managerChanged = false;

                var declarations = manager.GetDeclarations();
                foreach (var declaration in declarations)
                {
                    if (declaration.TypeName == "SoilWater")
                    {
                        declaration.TypeName = "ISoilWater";
                        managerChanged = true;
                    }
                }

                if (managerChanged)
                {
                    manager.SetDeclarations(declarations);

                    var usings = manager.GetUsingStatements().ToList();
                    if (!usings.Contains("Models.Interfaces"))
                    {
                        usings.Add("Models.Interfaces");
                        manager.SetUsingStatements(usings);
                    }
                }

                if (manager.Replace(" as SoilWater", ""))
                    managerChanged = true;
                if (manager.Replace("solute_flow_eff", "SoluteFlowEfficiency"))
                    managerChanged = true;
                if (manager.Replace("solute_flux_eff", "SoluteFluxEfficiency"))
                    managerChanged = true;
                if (manager.Replace("[EventSubscribe(\"Commencing\")", "[EventSubscribe(\"StartOfSimulation\")"))
                    managerChanged = true;

                if (managerChanged)
                    manager.Save();
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "solute_flow_eff", "SoluteFlowEfficiency");
                JsonUtilities.SearchReplaceReportVariableNames(report, "solute_flux_eff", "SoluteFluxEfficiency");
            }

        }

        /// <summary>
        /// Replace 'avg' with 'mean' in report variables.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion89(JObject root, string fileName)
        {
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
                JsonUtilities.SearchReplaceReportVariableNames(report, "avg of ", "mean of ");
        }

        /// <summary>
        /// Fixes a bug where a manager script's children were being serialized.
        /// When attempting to reopen the file, the script's type cannot be resolved.
        /// This converter will strip out all script children of managers under replacements.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion90(JObject root, string fileName)
        {
            foreach (JObject replacements in JsonUtilities.ChildrenRecursively(root, "Replacements"))
                foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
                    JsonUtilities.RemoveChild(manager, "Script");
        }

        /// <summary>
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion91(JObject root, string fileName)
        {
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".HarvestableWt",          ".Harvestable.Wt"),
                new Tuple<string, string>(".HarvestableN",           ".Harvestable.N"),
                new Tuple<string, string>(".StandingHerbageWt",      ".Standing.Wt"),
                new Tuple<string, string>(".StandingHerbageN",       ".Standing.N"),
                new Tuple<string, string>(".StandingHerbageNConc",   ".Standing.NConc"),
                new Tuple<string, string>(".StandingLiveHerbageWt",  ".StandingLive.Wt"),
                new Tuple<string, string>(".StandingLiveHerbageN",   ".StandingLive.N"),
                new Tuple<string, string>(".StandingDeadHerbageWt",  ".StandingDead.Wt"),
                new Tuple<string, string>(".StandingDeadHerbageN",   ".StandingDead.N"),
                new Tuple<string, string>(".HerbageDigestibility",   ".Standing.Digestibility"),
                new Tuple<string, string>(".RootDepthMaximum",       ".Root.RootDepthMaximum"),
                new Tuple<string, string>("[AGPRyeGrass].RootLengthDensity", "[AGPRyeGrass].Root.RootLengthDensity"),
                new Tuple<string, string>("[AGPWhiteClover].RootLengthDensity", "[AGPWhiteClover].Root.RootLengthDensity"),
                new Tuple<string, string>("[AGPLucerne].RootLengthDensity", "[AGPLucerne].Root.RootLengthDensity")
            };
            JsonUtilities.RenameVariables(root, changes);
        }

        /// <summary>
        /// Change names of a couple of parameters in SimpleGrazing.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion92(JObject root, string fileName)
        {
            foreach (JObject simpleGrazing in JsonUtilities.ChildrenRecursively(root, "SimpleGrazing"))
            {
                simpleGrazing["FractionExcretedNToDung"] = simpleGrazing["FractionOfBiomassToDung"];
                if (simpleGrazing["FractionNExportedInAnimal"] == null)
                    simpleGrazing["FractionNExportedInAnimal"] = 0.75;
            }

            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".AmountDungCReturned",  ".AmountDungWtReturned")
            };
            JsonUtilities.RenameVariables(root, changes);
        }

        /// <summary>
        /// In SimpleGrazin, Turn "Fraction of defoliated N leaving the system" into a fraction of defoliated N going to soil.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion93(JObject root, string fileName)
        {
            foreach (JObject simpleGrazing in JsonUtilities.ChildrenRecursively(root, "SimpleGrazing"))
            {
                if (simpleGrazing["FractionNExportedInAnimal"] != null)
                {
                    var fractionNExportedInAnimal = Convert.ToDouble(simpleGrazing["FractionNExportedInAnimal"].Value<double>());
                    simpleGrazing["FractionDefoliatedNToSoil"] = 1 - fractionNExportedInAnimal;

                }
                if (simpleGrazing["FractionExcretedNToDung"] != null)
                {
                    var fractionExcretedNToDung = simpleGrazing["FractionExcretedNToDung"] as JArray;
                    if (fractionExcretedNToDung.Count > 0)
                        simpleGrazing["CNRatioDung"] = "NaN";
                }
            }
        }

        /// <summary>
        /// Convert stock genotypes array into GenotypeCross child models.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion94(JObject root, string fileName)
        {
            foreach (JObject stock in JsonUtilities.ChildrenRecursively(root, "Stock"))
            {
                var oldGenotypes = stock["Genotypes"] as JArray;
                if (oldGenotypes != null)
                {
                    var newGenotypes = new JArray();
                    foreach (var oldgenotype in oldGenotypes)
                    {
                        var genotypeCross = new JObject();
                        genotypeCross["$type"] = "Models.GrazPlan.GenotypeCross, Models";
                        genotypeCross["Name"] = oldgenotype["GenotypeName"];
                        var oldgenotypeDeathRates = oldgenotype["DeathRate"] as JArray;
                        if (oldgenotypeDeathRates != null && oldgenotypeDeathRates.Count == 2)
                        {
                            genotypeCross["MatureDeathRate"] = oldgenotypeDeathRates[0];
                            genotypeCross["WeanerDeathRate"] = oldgenotypeDeathRates[1];
                        }
                        genotypeCross["Conception"] = oldgenotype["Conceptions"];

                        if (string.IsNullOrEmpty(oldgenotype["DamBreed"].ToString()))
                            genotypeCross["PureBredBreed"] = oldgenotype["GenotypeName"];
                        else if (string.IsNullOrEmpty(oldgenotype["SireBreed"].ToString()))
                            genotypeCross["PureBredBreed"] = oldgenotype["DamBreed"];
                        else
                            genotypeCross["DamBreed"] = oldgenotype["DamBreed"];
                        genotypeCross["SireBreed"] = oldgenotype["SireBreed"];
                        genotypeCross["Generation"] = oldgenotype["Generation"];
                        genotypeCross["SRW"] = oldgenotype["SRW"];
                        genotypeCross["PotFleeceWt"] = oldgenotype["PotFleeceWt"];
                        genotypeCross["MaxFibreDiam"] = oldgenotype["MaxFibreDiam"];
                        genotypeCross["FleeceYield"] = oldgenotype["FleeceYield"];
                        genotypeCross["PeakMilk"] = oldgenotype["PeakMilk"];
                        newGenotypes.Add(genotypeCross);
                    }
                    stock.Remove("Genotypes");
                    if (newGenotypes.Count > 0)
                        stock["Children"] = newGenotypes;
                }
            }
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".GenotypeNamesAll()",  ".Genotypes.Names.ToArray()")
            };
            if (JsonUtilities.RenameVariables(root, changes))
            {
                // The replacement is in a manager. Need to make sure that LINQ is added as a using
                // because the .ToArray() depends on it.
                foreach (var manager in JsonUtilities.ChildManagers(root))
                {
                    var usings = manager.GetUsingStatements().ToList();
                    if (usings.Find(u => u.Contains("System.Linq")) == null)
                    {
                        usings.Add("System.Linq");
                        manager.SetUsingStatements(usings);
                        manager.Save();
                    }
                }
            }
        }

        /// <summary>
        /// Change initialDM on Generic organ and root from a single value to a BiomassPoolType so each type can be sepcified
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion95(JObject root, string fileName)
        {
            // Remove initalDM model and replace with initialBiomass object
            foreach (string org in new List<string>() { "GenericOrgan", "Root" })
            {
                foreach (JObject O in JsonUtilities.ChildrenRecursively(root, org))
                {
                    if (O["Enabled"].ToString() == "True")
                    {
                        string initName = org == "GenericOrgan" ? "InitialWtFunction" : "InitialDM";
                        JObject InitialWt = JsonUtilities.CreateNewChildModel(O, "InitialWt", "Models.PMF.BiomassDemand");
                        JObject Structural = JsonUtilities.ChildWithName(O, initName).DeepClone() as JObject;
                        Structural["Name"] = "Structural";
                        JArray ChildFunctions = new JArray();
                        ChildFunctions.Add(Structural);
                        InitialWt["Children"] = ChildFunctions;
                        JsonUtilities.AddConstantFunctionIfNotExists(InitialWt, "Metabolic", "0.0");
                        JsonUtilities.AddConstantFunctionIfNotExists(InitialWt, "Storage", "0.0");
                        JsonUtilities.RemoveChild(O, initName);
                    }
                }
            }
            // Altermanager code where initial root wt is being set.
            foreach (var manager in JsonUtilities.ChildrenOfType(root, "Manager"))
            {
                string code = manager["Code"].ToString();
                string[] lines = code.Split('\n');
                bool ContainsZoneInitalDM = false;
                for (int i = 0; i < lines.Count(); i++)
                {
                    if (lines[i].Contains("Root.ZoneInitialDM.Add(") && (ContainsZoneInitalDM == false))
                    {
                        ContainsZoneInitalDM = true;
                        string InitialDM = lines[i].Split('(')[1].Replace(";", "").Replace(")", "").Replace("\r", "").Replace("\n", "");
                        string NewCode = "BiomassDemand InitialDM = new BiomassDemand();\r\n" +
                                         "Constant InitStruct = new Constant();\r\n" +
                                         "InitStruct.FixedValue = " + InitialDM + ";\r\n" +
                                         "InitialDM.Structural = InitStruct;\r\n" +
                                         "Constant InitMetab = new Constant();\r\n" +
                                         "InitMetab.FixedValue = 0;\r\n" +
                                         "InitialDM.Metabolic = InitMetab;\r\n" +
                                         "Constant InitStor = new Constant();\r\n" +
                                         "InitStor.FixedValue = 0;\r\n" +
                                         "InitialDM.Storage = InitStor;\r\n" +
                                         lines[i].Split('(')[0] + "(InitialDM);\r\n";
                        lines[i] = NewCode;
                    }
                    else if (lines[i].Contains("Root.ZoneInitialDM.Add("))
                    {
                        string InitialDM = lines[i].Split('(')[1].Replace(";", "").Replace(")", "").Replace("\r", "").Replace("\n", "");
                        lines[i] = "InitStruct.FixedValue = " + InitialDM + ";\r\n" +
                                    lines[i].Split('(')[0] + "(InitialDM);\r\n";
                    }
                }

                if (ContainsZoneInitalDM)
                {
                    string newCode = "using Models.Functions;\r\n";
                    foreach (string line in lines)
                    {
                        newCode += line + "\n";
                    }
                    manager["Code"] = newCode;
                }
            }
        }

        /// <summary>
        /// Add RootShape to all simulations.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion96(JObject root, string fileName)
        {
            foreach (JObject thisRoot in JsonUtilities.ChildrenRecursively(root, "Root"))
            {
                if (JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeCylindre").Count == 0 &&
                    JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeSemiCircle").Count == 0 &&
                    JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeSemiCircleSorghum").Count == 0 &&
                    JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeSemiEllipse").Count == 0)
                {
                    JArray rootChildren = thisRoot["Children"] as JArray;
                    if (rootChildren != null && rootChildren.Count > 0)
                    {
                        JToken thisPlant = JsonUtilities.Parent(thisRoot);

                        JArray rootShapeChildren = new JArray();
                        string type;

                        if (thisPlant["CropType"].ToString() == "Sorghum")
                        {
                            type = "Models.Functions.RootShape.RootShapeSemiCircleSorghum, Models";
                        }
                        else if (thisPlant["CropType"].ToString() == "C4Maize")
                        {
                            type = "Models.Functions.RootShape.RootShapeSemiCircle, Models";
                        }
                        else
                        {
                            type = "Models.Functions.RootShape.RootShapeCylindre, Models";
                        }

                        JObject rootShape = new JObject
                        {
                            ["$type"] = type,
                            ["Name"] = "RootShape",
                            ["Children"] = rootShapeChildren
                        };
                        rootChildren.AddFirst(rootShape);
                    }
                }
            }
        }

        /// <summary>
        /// Add RootShape to all simulations.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion97(JObject root, string fileName)
        {
            foreach (JObject AirTempFunc in JsonUtilities.ChildrenOfType(root, "AirTemperatureFunction"))
            {
                JObject tempResponse = JsonUtilities.ChildWithName(AirTempFunc, "XYPairs");
                tempResponse["Name"] = "TemperatureResponse";
            }
        }

        /// <summary>
        /// Convert stock animalparamset
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion98(JObject root, string fileName)
        {
            foreach (JObject paramSet in JsonUtilities.ChildrenRecursively(root, "AnimalParamSet"))
            {
                paramSet["$type"] = "Models.GrazPlan.Genotype, Models";
            }
        }

        /// <summary>
        /// Add InterpolationMethod to AirTemperature Function.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion99(JObject root, string fileName)
        {
            foreach (JObject AirTempFunc in JsonUtilities.ChildrenOfType(root, "AirTemperatureFunction"))
            {
                AirTempFunc["agregationMethod"] = "0";
                JsonUtilities.AddModel(AirTempFunc, typeof(ThreeHourAirTemperature), "InterpolationMethod");
            }
        }

        /// <summary>
        /// Change SimpleGrazing.FractionDefoliatedNToSoil from a scalar to an array.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion100(JObject root, string fileName)
        {
            foreach (var simpleGrazing in JsonUtilities.ChildrenOfType(root, "SimpleGrazing"))
            {
                if (simpleGrazing["FractionDefoliatedNToSoil"] != null)
                {
                    var arr = new JArray();
                    arr.Add(simpleGrazing["FractionDefoliatedNToSoil"].Value<double>());
                    simpleGrazing["FractionDefoliatedNToSoil"] = arr;
                }
            }
        }

        /// <summary>
        /// Add canopy width Function.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion101(JObject root, string fileName)
        {
            foreach (JObject Leaf in JsonUtilities.ChildrenOfType(root, "Leaf"))
            {
                JsonUtilities.AddConstantFunctionIfNotExists(Leaf, "WidthFunction", "0");

                VariableReference varRef = new VariableReference();
                varRef.Name = "DepthFunction";
                varRef.VariableName = "[Leaf].Height";

                JsonUtilities.AddModel(Leaf, varRef);
            }
        }

        /// <summary>
        /// Rename Models.Sensitivity.CroptimizR to Models.Optimisation.CroptimizR.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion102(JObject root, string fileName)
        {
            foreach (JObject croptimizR in JsonUtilities.ChildrenRecursively(root, "CroptimizR"))
                croptimizR["$type"] = croptimizR["$type"].ToString().Replace("Sensitivity", "Optimisation");
        }

        /// <summary>
        /// Rename TemperatureResponse to Response on Interpolate functions.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion103(JObject root, string fileName)
        {
            foreach (JObject atf in JsonUtilities.ChildrenRecursively(root, "AirTemperatureFunction"))
            {
                atf["$type"] = "Models.Functions.SubDailyInterpolation, Models";
                foreach (JObject c in atf["Children"])
                {
                    if (c["Name"].ToString() == "TemperatureResponse")
                    {
                        c["Name"] = "Response";
                    }
                }
                foreach (JObject cultivar in JsonUtilities.ChildrenRecursively(root, "Cultivar"))
                {
                    if (!cultivar["Command"].HasValues)
                        continue;

                    foreach (JValue command in cultivar["Command"].Children())
                    {
                        command.Value = command.Value.ToString().Replace(".TemperatureResponse", ".Response");
                    }
                }
            }
        }


        /// <summary>
        /// Add expression function to replace direct call to structure in nodenumberphase
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion104(JObject root, string fileName)
        {
            foreach (JObject NNP in JsonUtilities.ChildrenRecursively(root, "NodeNumberPhase"))
            {
                VariableReference varRef = new VariableReference();
                varRef.Name = "LeafTipNumber";
                varRef.VariableName = "[Structure].LeafTipsAppeared";
                JsonUtilities.AddModel(NNP, varRef);
            }
        }


        /// <summary>
        /// Add expression function to replace direct call to structure in nodenumberphase
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion105(JObject root, string fileName)
        {
            foreach (JObject LAP in JsonUtilities.ChildrenRecursively(root, "LeafAppearancePhase"))
            {
                VariableReference varRef = new VariableReference();
                varRef.Name = "FinalLeafNumber";
                varRef.VariableName = "[Structure].FinalLeafNumber";
                JsonUtilities.AddModel(LAP, varRef);
            }
        }

        /// <summary>
        /// Change Nutrient.FOMC and Nutrient.FOMN to Nutrient.FOM.C and Nutrient.FOM.N
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion106(JObject root, string fileName)
        {
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>("utrient.FOMC",  "utrient.FOM.C"),
                new Tuple<string, string>("utrient.FOMN",  "utrient.FOM.N")
            };

            JsonUtilities.RenameVariables(root, changes);

            // Add Models.Soils.Nutrients namespace to all manager files that
            // reference Nutrient or Solute.

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                var code = manager.ToString();
                if (code != null && (code.Contains("Nutrient") || code.Contains("Solute")))
                {
                    var usingLines = manager.GetUsingStatements().ToList();
                    usingLines.Add("Models.Soils.Nutrients");
                    manager.SetUsingStatements(usingLines);
                    manager.Save();
                }
            }
        }

        /// <summary>
        /// Add expression function to replace direct call to structure in LeafAppearancePhase
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion107(JObject root, string fileName)
        {
            foreach (JObject LAP in JsonUtilities.ChildrenRecursively(root, "LeafAppearancePhase"))
            {
                ExpressionFunction expFunction = new ExpressionFunction();
                expFunction.Name = "LeafNumber";
                expFunction.Expression = "[Leaf].ExpandedCohortNo + [Leaf].NextExpandingLeafProportion";
                JsonUtilities.AddModel(LAP, expFunction);

                VariableReference varRef1 = new VariableReference();
                varRef1.Name = "FullyExpandedLeafNo";
                varRef1.VariableName = "[Leaf].ExpandedCohortNo";
                JsonUtilities.AddModel(LAP, varRef1);

                VariableReference varRef2 = new VariableReference();
                varRef2.Name = "InitialisedLeafNumber";
                varRef2.VariableName = "[Leaf].InitialisedCohortNo";
                JsonUtilities.AddModel(LAP, varRef2);

            }
        }

        /// <summary>
        /// Upgrade to version 108.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion108(JObject root, string fileName)
        {
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>("Wheat.Structure.HaunStage",          "Wheat.Phenology.HaunStage"),
                new Tuple<string, string>("[Wheat].Structure.HaunStage",          "[Wheat].Phenology.HaunStage"),
                new Tuple<string, string>("Wheat.Structure.PTQ",          "Wheat.Phenology.PTQ"),
                new Tuple<string, string>("[Wheat].Structure.PTQ",          "[Wheat].Phenology.PTQ")
            };
            JsonUtilities.RenameVariables(root, changes);
        }

        /// <summary>
        /// Create Models.Climate namespace.
        /// The following types will be moved into Models.Climate:
        /// - ControlledEnvironment
        /// - SlopeEffectsOnWeather
        /// - Weather
        /// - WeatherSampler
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion109(JObject root, string fileName)
        {
            Type[] typesToMove = new Type[] { typeof(ControlledEnvironment), typeof(SlopeEffectsOnWeather), typeof(Weather), typeof(WeatherSampler) };

            foreach (Type type in typesToMove)
                foreach (JObject instance in JsonUtilities.ChildrenRecursively(root, type.Name))
                    if (!instance["$type"].ToString().Contains("Models.Climate"))
                        instance["$type"] = instance["$type"].ToString().Replace("Models.", "Models.Climate.");

            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                string code = manager.ToString();
                if (code == null)
                    continue;
                foreach (Type type in typesToMove)
                {
                    if (code.Contains(type.Name))
                    {
                        List<string> usings = manager.GetUsingStatements().ToList();
                        usings.Add("Models.Climate");
                        manager.SetUsingStatements(usings);
                        manager.Save();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Add canopy width Function.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion110(JObject root, string fileName)
        {
            foreach (JObject Root in JsonUtilities.ChildrenOfType(root, "Root"))
                JsonUtilities.AddConstantFunctionIfNotExists(Root, "RootDepthStressFactor", "1");
        }

        /// <summary>
        /// Modify manager scripts to use the new generic model locator API.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion111(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                // Apsim.Get(model, path) -> model.FindByPath(path)?.Value
                FixApsimGet(manager);

                // Apsim.FullPath(model) -> model.FullPath
                FixFullPath(manager);

                // Apsim.GetVariableObject(model, path) -> model.FindByPath(path)
                FixGetVariableObject(manager);

                // Apsim.Ancestor<T>(model) -> model.FindAncestor<T>()
                FixAncestor(manager);

                // Apsim.Siblings(model) -> model.FindAllSiblings()
                FixSiblings(manager);

                // Apsim.ParentAllChildren(model) -> model.ParentAllDescendants()
                FixParentAllChildren(manager);

                // This one will fail if using a runtime type.
                // Apsim.Parent(model, type) -> model.FindAncestor<Type>()
                FixParent(manager);

                // Apsim.Set(model, path, value) -> model.FindByPath(path).Value = value
                FixSet(manager);

                // Apsim.Find(model, x) -> model.FindInScope(x)
                // Apsim.Find(model, typeof(Y)) -> model.FindInScope<Y>()
                // This one will fail if the second argument is a runtime type which doesn't
                // use the typeof() syntax. E.g. if it's a variable of type Type.
                // Will probably fail in other cases too. It's really not ideal.
                FixFind(manager);

                // Apsim.FindAll(model) -> model.FindAllInScope().ToList()
                // Apsim.FindAll(model, typeof(X)) -> model.FindAllInScope<X>().ToList()
                FixFindAll(manager);

                // Apsim.Child(model, "Wheat") -> model.FindChild("Wheat")
                // Apsim.Child(model, typeof(IOrgan)) -> model.FindChild<IOrgan>()
                FixChild(manager);

                // Apsim.Children(model, typeof(IFunction)) -> model.FindAllChildren<IFunction>().ToList<IModel>()
                // Apsim.Children(model, obj.GetType()) -> model.FindAllChildren().Where(c => obj.GetType().IsAssignableFrom(c.GetType())).ToList<IModel>()
                // This will add "using System.Linq;" if necessary.
                FixChildren(manager);

                // Apsim.ChildrenRecursively(model) -> model.FindAllDescendants().ToList()
                // Apsim.ChildrenRecursively(model, typeof(IOrgan)) -> model.FindAllDescendants<IOrgan>().OfType<IModel>().ToList()
                // Apsim.ChildrenRecursively(model, GetType()) -> model.FindAllDescendants().Where(d => GetType().IsAssignableFrom(d.GetType())).ToList()
                FixChildrenRecursively(manager);

                // Apsim.ChildrenRecursivelyVisible(model) -> model.FindAllDescendants().Where(m => !m.IsHidden).ToList()
                FixChildrenRecursivelyVisible(manager);

                manager.Save();
            }

            void FixApsimGet(ManagerConverter manager)
            {
                string pattern = @"Apsim\.Get\(([^,]+),\s*((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string replace = @"$1.FindByPath($2).Value";
                    if (match.Groups[1].Value.Contains(" "))
                        replace = replace.Replace("$1", "($1)");

                    return Regex.Replace(match.Value, pattern, replace);
                });
            }

            void FixFullPath(ManagerConverter manager)
            {
                string pattern = @"Apsim\.FullPath\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                string replacement = "$1.FullPath";
                manager.ReplaceRegex(pattern, match =>
                {
                    if (match.Groups[1].Value.Contains(" "))
                        replacement = replacement.Replace("$1", "($1)");

                    return Regex.Replace(match.Value, pattern, replacement);
                });
            }

            void FixGetVariableObject(ManagerConverter manager)
            {
                string pattern = @"Apsim\.GetVariableObject\(([^,]+),\s*((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string replace = @"$1.FindByPath($2)";
                    if (match.Groups[1].Value.Contains(" "))
                        replace = replace.Replace("$1", "($1)");

                    return Regex.Replace(match.Value, pattern, replace);
                });
            }


            void FixAncestor(ManagerConverter manager)
            {
                string pattern = @"Apsim\.Ancestor<([^>]+)>\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string replace = @"$2.FindAncestor<$1>($3)";
                    if (match.Groups[2].Value.Contains(" "))
                        replace = replace.Replace("$2", "($2)");

                    return Regex.Replace(match.Value, pattern, replace);
                });
            }

            void FixSiblings(ManagerConverter manager)
            {
                bool replaced = false;
                string pattern = @"Apsim\.Siblings\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    replaced = true;

                    string replace = @"$1.FindAllSiblings().ToList<IModel>()";
                    if (match.Groups[1].Value.Contains(" "))
                        replace = replace.Replace("$1", "($1)");

                    return Regex.Replace(match.Value, pattern, replace);
                });

                if (replaced)
                {
                    List<string> usings = manager.GetUsingStatements().ToList();
                    if (!usings.Contains("System.Linq"))
                        usings.Add("System.Linq");
                    manager.SetUsingStatements(usings);
                }
            }

            void FixParentAllChildren(ManagerConverter manager)
            {
                string pattern = @"Apsim\.ParentAllChildren\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string replace = @"$1.ParentAllDescendants()";
                    if (match.Groups[1].Value.Contains(" "))
                        replace = replace.Replace("$1", "($1)");

                    return Regex.Replace(match.Value, pattern, replace);
                });
            }

            void FixParent(ManagerConverter manager)
            {
                string pattern = @"Apsim\.Parent\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count != 2)
                        throw new Exception($"Incorrect number of arguments passed to Apsim.Parent()");

                    string modelName = args[0].Value;
                    if (modelName.Contains(" "))
                        modelName = $"({modelName})";

                    string type = args[1].Value.Replace("typeof(", "").TrimEnd(')').Trim();

                    return $"{modelName}.FindAncestor<{type}>()";
                });
            }

            void FixSet(ManagerConverter manager)
            {
                string pattern = @"Apsim\.Set\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count != 3)
                        throw new Exception($"Incorrect number of arguments passed to Apsim.Set()");

                    string model = args[0].Value.Trim();
                    if (model.Contains(" "))
                        model = $"({model})";

                    string path = args[1].Value.Trim();
                    string value = args[2].Value.Trim();

                    return $"{model}.FindByPath({path}).Value = {value}";
                });
            }

            void FixFind(ManagerConverter manager)
            {
                string pattern = @"Apsim\.Find\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count != 2)
                        throw new Exception($"Incorrect number of arguments passed to Apsim.Find()");

                    string model = args[0].Value.Trim();
                    if (model.Contains(" "))
                        model = $"({model})";

                    string pathOrType = args[1].Value.Trim();
                    if (pathOrType.Contains("typeof("))
                    {
                        string type = pathOrType.Replace("typeof(", "").TrimEnd(')');
                        return $"{model}.FindInScope<{type}>()";
                    }
                    else
                        return $"{model}.FindInScope({pathOrType})";
                });
            }

            void FixFindAll(ManagerConverter manager)
            {
                string pattern = @"Apsim\.FindAll\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                bool replaced = manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count == 1)
                    {
                        string model = args[0].Value.Trim();
                        if (model.Contains(" "))
                            model = $"({model})";

                        return $"{model}.FindAllInScope().ToList()";
                    }
                    else if (args.Count == 2)
                    {
                        string model = args[0].Value.Trim();
                        if (model.Contains(" "))
                            model = $"({model})";

                        string type = args[1].Value.Trim().Replace("typeof(", "").TrimEnd(')');

                        // See comment in the FixChildren() method. This really isn't ideal.
                        return $"{model}.FindAllInScope<{type}>().OfType<IModel>().ToList()";
                    }
                    else
                        throw new Exception($"Incorrect number of arguments passed to Apsim.FindAll()");
                });

                if (replaced)
                    AddLinqIfNotExist(manager);
            }

            void FixChild(ManagerConverter manager)
            {
                string pattern = @"Apsim\.Child\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count != 2)
                        throw new Exception($"Incorrect number of arguments passed to Apsim.Find()");

                    string model = args[0].Value.Trim();
                    if (model.Contains(" "))
                        model = $"({model})";

                    string pathOrType = args[1].Value.Trim();
                    if (pathOrType.Contains("typeof("))
                    {
                        string type = pathOrType.Replace("typeof(", "").TrimEnd(')');
                        return $"{model}.FindChild<{type}>()";
                    }
                    else
                        return $"{model}.FindChild({pathOrType})";
                });

                pattern = @"(FindChild<([^>]+)>\(\)) as \2";
                manager.ReplaceRegex(pattern, "$1");
            }

            void FixChildren(ManagerConverter manager)
            {

                string pattern = @"Apsim\.Children\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                bool replaced = manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count != 2)
                        throw new Exception($"Incorrect number of arguments passed to Apsim.Children()");

                    string model = args[0].Value.Trim();
                    if (model.Contains(" "))
                        model = $"({model})";

                    string type = args[1].Value.Trim();

                    Match simplify = Regex.Match(type, @"typeof\(([^\)]+)\)");
                    if (simplify.Groups.Count == 2)
                    {
                        type = simplify.Groups[1].Value;

                        // Unfortunately we need some sort of cast here, in case the type
                        // being searched for is an interface such as IPlant which doesn't
                        // implement IModel, even though realistically the only results
                        // which FindAllChildren() will return are guaranteed to be IModels.
                        // In an ideal world, the user wouldn't access any members of IModel,
                        // and we could just happily return an IEnumerable<IPlant> or
                        // List<IPlant>, but that's obviously not a guarantee we can make.
                        return $"{model}.FindAllChildren<{type}>().OfType<IModel>().ToList()";
                    }
                    else
                    {
                        // Need to ensure that we're using System.Linq;
                        return $"{model}.FindAllChildren().Where(c => {type}.IsAssignableFrom(c.GetType())).ToList()";
                    }
                });

                if (replaced)
                    AddLinqIfNotExist(manager);
            }

            void FixChildrenRecursively(ManagerConverter manager)
            {
                string pattern = @"Apsim\.ChildrenRecursively\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                bool replaced = manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count == 1)
                    {

                        string model = args[0].Value.Trim();
                        if (model.Contains(" "))
                            model = $"({model})";

                        return $"{model}.FindAllDescendants().ToList()";
                    }
                    else if (args.Count == 2)
                    {
                        string model = args[0].Value.Trim();
                        if (model.Contains(" "))
                            model = $"({model})";

                        string type = args[1].Value.Trim();

                        Match simplify = Regex.Match(type, @"typeof\(([^\)]+)\)");
                        if (simplify.Groups.Count == 2)
                        {
                            // Code uses a simple typeof(X) to reference the type. This can be converted to the generic usage.
                            type = simplify.Groups[1].Value;
                            return $"{model}.FindAllDescendants<{type}>().OfType<IModel>().ToList()";
                        }
                        else
                            // This is a bit uglier and we need to use a qnd linq query to fix it up.
                            return $"{model}.FindAllDescendants().Where(d => {type}.IsAssignableFrom(d.GetType())).ToList()";
                    }
                    else
                        throw new Exception($"Incorrect number of arguments passed to Apsim.ChildrenRecursively()");
                });

                if (replaced)
                    AddLinqIfNotExist(manager);
            }

            void AddLinqIfNotExist(ManagerConverter manager)
            {
                List<string> usings = manager.GetUsingStatements().ToList();
                if (!usings.Contains("System.Linq"))
                    usings.Add("System.Linq");
                manager.SetUsingStatements(usings);
            }

            void FixChildrenRecursivelyVisible(ManagerConverter manager)
            {
                string pattern = @"Apsim\.ChildrenRecursivelyVisible\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\)";
                bool replaced = manager.ReplaceRegex(pattern, match =>
                {
                    string argsRegex = @"(?:[^,()]+((?:\((?>[^()]+|\((?<c>)|\)(?<-c>))*\)))*)+";
                    var args = Regex.Matches(match.Groups[1].Value, argsRegex);

                    if (args.Count == 1)
                    {
                        string model = args[0].Value.Trim();
                        if (model.Contains(" "))
                            model = $"({model})";

                        return $"{model}.FindAllDescendants().Where(m => !m.IsHidden).ToList()";
                    }
                    else
                        throw new Exception($"Incorrect number of arguments passed to Apsim.ChildrenRecursivelyVisible()");
                });

                if (replaced)
                    AddLinqIfNotExist(manager);
            }
        }

        /// <summary>
        /// Upgrade to version 112. Lots of breaking changes to class Plant.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion112(JObject root, string fileName)
        {
            var changes = new Tuple<string, string>[]
            {
                new Tuple<string, string>("Plant.Canopy", "Plant.Leaf"),
                new Tuple<string, string>("[Plant].Canopy", "[Plant].Leaf"),
                new Tuple<string, string>("plant.Canopy", "plant.Leaf"),
                new Tuple<string, string>("[plant].Canopy", "[plant].Leaf"),
                new Tuple<string, string>("Wheat.Canopy", "Wheat.Leaf"),
                new Tuple<string, string>("[Wheat].Canopy", "[Wheat].Leaf"),
                new Tuple<string, string>("wheat.Canopy", "wheat.Leaf"),
                new Tuple<string, string>("[wheat].Canopy", "[wheat].Leaf"),
                new Tuple<string, string>("[Phenology].CurrentPhaseName", "[Phenology].CurrentPhase.Name"),
                new Tuple<string, string>("[phenology].CurrentPhaseName", "[phenology].CurrentPhase.Name"),
                new Tuple<string, string>("Phenology.CurrentPhaseName", "Phenology.CurrentPhase.Name"),
                new Tuple<string, string>("phenology.CurrentPhaseName", "phenology.CurrentPhase.Name"),
                new Tuple<string, string>("[Plant].Phenology.DaysAfterSowing", "[Plant].DaysAfterSowing"),
                new Tuple<string, string>("Plant.Phenology.DaysAfterSowing", "Plant.DaysAfterSowing"),
                new Tuple<string, string>("Phenology.DaysAfterSowing", "DaysAfterSowing"),
                new Tuple<string, string>("[Phenology].DaysAfterSowing", "[Plant].DaysAfterSowing"),
            };
            JsonUtilities.RenameVariables(root, changes);

            // Some more complicated changes to manager code.
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                ConvertPlantPropertyToDirectLink(manager, "Root", "Models.PMF.Organs");
                ConvertPlantPropertyToDirectLink(manager, "Structure", "Models.PMF.Struct");
                ConvertPlantPropertyToDirectLink(manager, "Phenology", "Models.PMF.Phen");
            }

            void ConvertPlantPropertyToDirectLink(ManagerConverter manager, string property, string nameSpace)
            {
                string code = manager.ToString();
                if (code == null)
                    return;
                if (code.Contains($".{property}."))
                {
                    string plantName = Regex.Match(code, $@"(\w+)\.{property}\.").Groups[1].Value;
                    JObject zone = JsonUtilities.Ancestor(manager.Token, typeof(Zone));
                    if (zone == null)
                    {
                        JObject replacements = JsonUtilities.Ancestor(manager.Token, "Replacements");
                        if (replacements != null)
                        {
                            JObject replacement = JsonUtilities.ChildrenRecursively(root).Where(j => j != manager.Token && j["Name"].ToString() == manager.Token["Name"].ToString()).FirstOrDefault();
                            if (replacement != null)
                                zone = JsonUtilities.Ancestor(replacement, typeof(Zone));
                            else
                                // This manager script is under replacements, but is not replacing any models.
                                // It is also likely to contain compilation errors due to API changes. Therefore
                                // we will disable it to suppress these errors.
                                manager.Token["Enabled"] = false;
                        }
                    }

                    int numPlantsInZone = JsonUtilities.ChildrenRecursively(zone, "Plant").Count;
                    if (numPlantsInZone > 0)
                    {
                        manager.AddUsingStatement(nameSpace);

                        bool isOptional = false;
                        Declaration plantLink = manager.GetDeclarations().Find(d => d.InstanceName == plantName);
                        if (plantLink != null)
                        {
                            string linkAttribute = plantLink.Attributes.Find(a => a.Contains("[Link"));
                            if (linkAttribute != null && linkAttribute.Contains("IsOptional = true"))
                                isOptional = true;
                        }

                        string link;
                        int numPlantsWithCorrectName = JsonUtilities.ChildrenRecursively(zone, "Plant").Count(p => p["Name"].ToString() == plantName);
                        if (string.IsNullOrEmpty(plantName) || numPlantsWithCorrectName == 0)
                            link = $"[Link{(isOptional ? "(IsOptional = true)" : "")}]";
                        else
                            link = $"[Link(Type = LinkType.Path, Path = \"[{plantName}].{property}\"{(isOptional ? ", IsOptional = true" : "")})]";

                        string memberName = property[0].ToString().ToLower() + property.Substring(1);
                        manager.AddDeclaration(property, memberName, new string[1] { link });

                        if (!string.IsNullOrEmpty(plantName))
                            manager.ReplaceRegex($"([^\"]){plantName}\\.{property}", $"$1{memberName}");
                        manager.Save();
                    }
                }
            }
        }

        /// <summary>
        /// Upgrade to version 113. Rename SowPlant2Type to SowingParameters.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion113(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
                if (manager.Replace("SowPlant2Type", "SowingParameters"))
                    manager.Save();
        }

        /// <summary>
        /// Upgrade to version 114. Remove references to Plant.IsC4.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion114(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
                if (manager.ReplaceRegex(@"(\w+)\.IsC4", "$1.FindByPath(\"Leaf.Photosynthesis.FCO2.PhotosyntheticPathway\")?.Value?.ToString() == \"C4\""))
                    manager.Save();
        }

        /// <summary>
        /// Upgrade to version 115. Add mortality rate constant of 0 to any plants
        /// which do not already have a mortality rate.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        /// <remarks>
        /// This is part of the change to make mortality rate non-optional.
        /// </remarks>
        private static void UpgradeToVersion115(JObject root, string fileName)
        {
            foreach (JObject plant in JsonUtilities.ChildrenRecursively(root, nameof(Plant)))
            {
                if ((plant["ResourceName"] == null || JsonUtilities.Ancestor(plant, "Replacements") != null) && JsonUtilities.ChildWithName(plant, "MortalityRate", ignoreCase: true) == null)
                {
                    Constant mortalityRate = new Constant();
                    mortalityRate.Name = "MortalityRate";
                    mortalityRate.FixedValue = 0;
                    JsonUtilities.AddModel(plant, mortalityRate);
                }
            }
        }

        /// <summary>
        /// Upgrade to version 115. Add PlantType to IPlants.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion116(JObject root, string fileName)
        {
            foreach (JObject pasture in JsonUtilities.ChildrenRecursively(root, "PastureSpecies"))
            {
                string plantType = pasture["ResourceName"]?.ToString();//?.Substring("AGP".Length);
                if (string.IsNullOrEmpty(plantType))
                    plantType = pasture["Name"]?.ToString();
                pasture["PlantType"] = plantType;
            }

            foreach (JObject plant in JsonUtilities.ChildrenRecursively(root, "Plant"))
                plant["PlantType"] = plant["CropType"]?.ToString();

            JsonUtilities.RenameVariables(root, new Tuple<string, string>[] { new Tuple<string, string>("CropType", "PlantType") });
        }

        /// <summary>
        /// Upgrade to version 115. Add PlantType to IPlants.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion117(JObject root, string fileName)
        {
            foreach (JObject croptimizr in JsonUtilities.ChildrenRecursively(root, "CroptimizR"))
            {
                string variableName = croptimizr["VariableName"]?.ToString();
                JArray variableNames = new JArray(new object[1] { variableName });
                croptimizr.Remove("VariableName");
                croptimizr["VariableNames"] = variableNames;
            }
        }


        /// <summary>
        /// Renames Initial NO3N and NH4N to NO3 and NH4
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion118(JObject root, string fileName)
        {
            foreach (var sample in JsonUtilities.ChildrenRecursively(root, "Sample"))
            {
                JsonUtilities.RenameChildModel(sample, "NO3N", "NO3");
                JsonUtilities.RenameChildModel(sample, "NH4N", "NH4");
            }

            foreach (var chloride in JsonUtilities.ChildrenRecursively(root, "Chloride"))
            {
                chloride["$type"] = "Models.Soils.Nutrients.Solute, Models";
                chloride["Name"] = "CL";
            }
        }

        /// <summary>
        /// Change report and manager scripts for soil variables that have been out of soil class.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion119(JObject root, string fileName)
        {
            var changes = new Tuple<string, string>[]
            {
                new Tuple<string, string>("[ISoil]", "[Soil]"),
                new Tuple<string, string>("[Soil].Temperature", "[Soil].Temperature.Value"),
                new Tuple<string, string>("Soil.Temperature", "Soil.Temperature.Value"),
                new Tuple<string, string>("[Soil].FBiom", "[Soil].Organic.FBiom"),
                new Tuple<string, string>("[Soil].FInert", "[Soil].Organic.FInert"),
                new Tuple<string, string>("[Soil].InitialRootWt", "[Soil].Organic.FOM"),
                new Tuple<string, string>("[Soil].DepthMidPoints", "[Soil].Physical.DepthMidPoints"),
                new Tuple<string, string>("[Soil].Thickness", "[Soil].Physical.Thickness"),
                new Tuple<string, string>("[Soil].BD", "[Soil].Physical.BD"),
                new Tuple<string, string>("[Soil].AirDry", "[Soil].Physical.AirDry"),
                new Tuple<string, string>("[Soil].LL15", "[Soil].Physical.LL15"),   // will also convert LL15mm
                new Tuple<string, string>("[Soil].DUL", "[Soil].Physical.DUL"),     // will also convert DULmm
                new Tuple<string, string>("[Soil].SAT", "[Soil].Physical.SAT"),     // will also convert SATmm
                new Tuple<string, string>("[Soil].PAWC", "[Soil].Physical.PAWC"),   // will also convert PAWCmm
                new Tuple<string, string>("[Soil].PAW", "[Soil].SoilWater.PAW"),    // will also convert PAWmm
                new Tuple<string, string>("[Soil].Water", "[Soil].SoilWater.SWmm"),
                new Tuple<string, string>("[Soil].KS", "[Soil].Physical.KS"),
            };
            JsonUtilities.RenameVariables(root, changes);

            // Look in manager scripts and move some soil properties to the soil physical instance.
            var variablesToMove = new ManagerReplacement[]
            {
                new ManagerReplacement("Soil.ThicknessCumulative", "soilPhysical.ThicknessCumulative", "IPhysical"),
                new ManagerReplacement("Soil.Thickness", "soilPhysical.Thickness", "IPhysical"),
                new ManagerReplacement("Soil.BD", "soilPhysical.BD", "IPhysical"),
                new ManagerReplacement("Soil.AirDry", "soilPhysical.AirDry", "IPhysical"),
                new ManagerReplacement("Soil.LL15", "soilPhysical.LL15", "IPhysical"),
                new ManagerReplacement("Soil.LL15mm", "soilPhysical.LL15mm", "IPhysical"),
                new ManagerReplacement("Soil.DUL", "soilPhysical.DUL", "IPhysical"),
                new ManagerReplacement("Soil.DULmm", "soilPhysical.DULmm", "IPhysical"),
                new ManagerReplacement("Soil.SAT", "soilPhysical.SAT", "IPhysical"),
                new ManagerReplacement("Soil.KS", "soilPhysical.KS", "IPhysical"),
                new ManagerReplacement("Soil.PAWC", "soilPhysical.PAWC", "IPhysical"),
                new ManagerReplacement("Soil.PAWCmm", "soilPhysical.PAWCmm", "IPhysical"),
                new ManagerReplacement("Soil.SoilWater", "waterBalance", "ISoilWater"),
                new ManagerReplacement("Soil.Water", "waterBalance.SWmm", "ISoilWater"),
            };
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                bool changesMade = manager.MoveVariables(variablesToMove);

                if (changesMade)
                {
                    manager.AddUsingStatement("Models.Interfaces");
                    manager.Save();
                }
            }

            // Rename the CERESSoilTemperature model to SoilTemperature
            foreach (var soil in JsonUtilities.ChildrenRecursively(root, "Soil"))
                JsonUtilities.RenameChildModel(soil, "CERESSoilTemperature", "Temperature");
        }

        /// <summary>
        /// Remove empty samples from soils.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion120(JObject root, string fileName)
        {
            foreach (JObject sample in JsonUtilities.ChildrenRecursively(root, "Sample"))
            {
                if ((sample["NO3N"] == null || !sample["NO3N"].HasValues)
                 && (sample["NH4N"] == null || !sample["NH4N"].HasValues)
                 && (sample["SW"] == null || !sample["SW"].HasValues)
                 && (sample["OC"] == null || !sample["OC"].HasValues)
                 && (sample["EC"] == null || !sample["EC"].HasValues)
                 && (sample["CL"] == null || !sample["CL"].HasValues)
                 && (sample["ESP"] == null || !sample["ESP"].HasValues)
                 && (sample["PH"] == null || !sample["PH"].HasValues))
                {
                    // The sample is empty. If it is not being overridden by a factor
                    // or replacements, get rid of it.
                    JObject expt = JsonUtilities.Ancestor(sample, typeof(Experiment));
                    if (expt != null)
                    {
                        // The sample is in an experiment. If it's being overriden by a factor,
                        // ignore it.
                        JObject factors = JsonUtilities.ChildWithName(expt, "Factors");
                        if (factors != null && JsonUtilities.DescendantOfType(factors, "Sample") != null)
                            continue;
                    }

                    JObject replacements = JsonUtilities.DescendantOfType(root, "Replacements");
                    if (replacements != null && JsonUtilities.DescendantOfType(replacements, "Sample") != null)
                        continue;

                    JObject parent = JsonUtilities.Parent(sample) as JObject;
                    string name = sample["Name"]?.ToString();
                    if (parent != null && !string.IsNullOrEmpty(name))
                        JsonUtilities.RemoveChild(parent, name);
                }
            }
        }

        /// <summary>
        /// Replace all instances of PhaseBasedSwitch with PhaseLookupValues.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion121(JObject root, string fileName)
        {
            foreach (JObject phaseSwitch in JsonUtilities.ChildrenRecursively(root, "PhaseBasedSwitch"))
            {
                phaseSwitch["$type"] = "Models.Functions.PhaseLookupValue, Models";
                Constant value = new Constant();
                value.FixedValue = 1;
                JsonUtilities.AddModel(phaseSwitch, value);
            }
        }

        /// <summary>
        /// Set maps' default zoom level to 360.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion122(JObject root, string fileName)
        {
            foreach (JObject map in JsonUtilities.ChildrenRecursively(root, nameof(Map)))
            {
                map["Zoom"] = 360;
                map["Center"]["Latitude"] = 0;
                map["Center"]["Longitude"] = 0;
            }
        }

        /// <summary>
        /// Remove all references to Arbitrator.WDemand, Arbitrator.WSupply, and Arbitrator.WAllocated.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion123(JObject root, string fileName)
        {
            string[] patterns = new[]
            {
                "Arbitrator.WSupply",
                "[Arbitrator].WSupply",
                "Arbitrator.WDemand",
                "[Arbitrator].WDemand",
                "Arbitrator.WAllocated",
                "[Arbitrator].WAllocated",
            };
            foreach (JObject report in JsonUtilities.ChildrenRecursively(root, typeof(Report).Name))
            {
                if (report["VariableNames"] is JArray variables)
                {
                    for (int i = variables.Count - 1; i >= 0; i--)
                        if (patterns.Any(p => variables[i].ToString().Contains(p)))
                            variables.RemoveAt(i);
                    report["VariableNames"] = variables;
                }
            }
        }

        /// <summary>
        /// Rename RadIntTot to RadiationIntercepted.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion124(JObject root, string fileName)
        {
            Tuple<string, string>[] changes = new Tuple<string, string>[1]
            {
                new Tuple<string, string>("RadIntTot", "RadiationIntercepted")
            };
            JsonUtilities.RenameVariables(root, changes);
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                bool changed = false;
                foreach (Tuple<string, string> change in changes)
                    changed |= manager.Replace(change.Item1, change.Item2, true);
                if (changed)
                    manager.Save();
            }
        }

        /// <summary>
        /// Add a default value for Sobol's variable to aggregate.
        /// This was previously assumed to be Clock.Today.Year but
        /// has been extracted to a variable.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion125(JObject root, string fileName)
        {
            foreach (JObject sobol in JsonUtilities.ChildrenRecursively(root, "Sobol"))
            {
                sobol["TableName"] = "Report";
                sobol["AggregationVariableName"] = "Clock.Today.Year";
            }
        }

        /// <summary>
        /// Add progeny destination phase and mortality function.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion900(JObject root, string fileName)
        {
            foreach (JObject LC in JsonUtilities.ChildrenRecursively(root, "LifeCycle"))
            {
                foreach (JObject LP in JsonUtilities.ChildrenRecursively(LC, "LifeCyclePhase"))
                {
                    JsonUtilities.AddConstantFunctionIfNotExists(LP, "Migration", "0.0");
                    JObject ProgDest = JsonUtilities.CreateNewChildModel(LP, "ProgenyDestination", "Models.LifeCycle.ProgenyDestinationPhase");
                    ProgDest["NameOfLifeCycleForProgeny"] = LC["Name"].ToString();
                    ProgDest["NameOfPhaseForProgeny"] = LP["NameOfPhaseForProgeny"].ToString();
                }
            }
        }

        /// <summary>
        /// Move physical properties off Weirdo class and use Physical class instead.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion126(JObject root, string fileName)
        {

            foreach (var soil in JsonUtilities.ChildrenRecursively(root, "Soil"))
            {
                var weirdo = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "WEIRDO");
                if (weirdo != null)
                {
                    Physical physical = new Physical();
                    physical.Name = "Physical";
                    if (weirdo["BD"].ToArray().Length > 0)
                        physical.BD = weirdo["BD"].Values<double>().ToArray();
                    if (weirdo["DUL"].ToArray().Length > 0)
                        physical.DUL = weirdo["DUL"].Values<double>().ToArray();
                    if (weirdo["LL15"].ToArray().Length > 0)
                        physical.LL15 = weirdo["LL15"].Values<double>().ToArray();
                    if (weirdo["SAT"].ToArray().Length > 0)
                        physical.SAT = weirdo["SAT"].Values<double>().ToArray();
                    if (weirdo["Thickness"].ToArray().Length > 0)
                        physical.Thickness = weirdo["Thickness"].Values<double>().ToArray();
                    JsonUtilities.AddModel(soil, physical);
                }
            }
        }

        /// <summary>
        /// </summary>
        /// <remarks>
        /// Previously, we used a custom markdown extension to implement support
        /// for markup superscript/subscripts, but given how slow this is, we've
        /// decided to just stick with the built-in extensions, so we need to
        /// change the syntax in all existing files.
        /// </remarks>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion127(JObject root, string fileName)
        {
            foreach (JObject memo in JsonUtilities.ChildrenRecursively(root, "Memo"))
            {
                string text = memo["Text"]?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    text = Regex.Replace(text, "<sup>([^<]+)</sup>", "^$1^");
                    text = Regex.Replace(text, "<sub>([^<]+)</sub>", "~$1~");
                    memo["Text"] = text;
                }
            }
            foreach (var TrModelNode in JsonUtilities.ChildrenRecursively(root, "MaximumHourlyTrModel"))
                TrModelNode["$type"] = "Models.Functions.SupplyFunctions.LimitedTranspirationRate, Models";
        }

        /// <summary>
        /// Upgrade to version 128. Add ResourceName property to Fertiliser models.
        /// </summary>
        /// <param name="root">The root json token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion128(JObject root, string fileName)
        {
            foreach (JObject fertiliser in JsonUtilities.ChildrenRecursively(root, nameof(Fertiliser)))
                fertiliser["ResourceName"] = "Fertiliser";
        }

        /// <summary>
        /// Add canopy width Function.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion129(JObject root, string fileName)
        {
            foreach (JObject Root in JsonUtilities.ChildrenOfType(root, "EnergyBalance"))
            {
                JsonUtilities.RenameChildModel(Root, "FRGRFunction", "FRGRer");
                JsonUtilities.RenameChildModel(Root, "GAIFunction", "GreenAreaIndex");
                JsonUtilities.RenameChildModel(Root, "ExtinctionCoefficientFunction", "GreenExtinctionCoefficient");
                JsonUtilities.RenameChildModel(Root, "ExtinctionCoefficientDeadFunction", "DeadExtinctionCoefficient");
                JsonUtilities.RenameChildModel(Root, "HeightFunction", "Tallness");
                JsonUtilities.RenameChildModel(Root, "DepthFunction", "Deepness");
                JsonUtilities.RenameChildModel(Root, "WidthFunction", "Wideness");
                JsonUtilities.RenameChildModel(Root, "GAIDeadFunction", "DeadAreaIndex");
                JsonUtilities.AddConstantFunctionIfNotExists(Root, "Wideness", "0");
                JsonUtilities.AddConstantFunctionIfNotExists(Root, "DeadExtinctionCoefficient", "0");
                JsonUtilities.AddConstantFunctionIfNotExists(Root, "GreenExtinctionCoefficient", "0");
                JsonUtilities.AddConstantFunctionIfNotExists(Root, "GreenAreaIndex", "0");
                JsonUtilities.AddConstantFunctionIfNotExists(Root, "DeadAreaIndex", "0");
            }
        }

        /// <summary>
        /// Add some extra constants to GenericOrgan to make
        /// optional functions non-optional.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion130(JObject root, string fileName)
        {
            foreach (JObject organ in JsonUtilities.ChildrenRecursively(root, "GenericOrgan"))
            {
                JArray organChildren = organ["Children"] as JArray;
                if (organChildren == null)
                {
                    organChildren = new JArray();
                    organ["Children"] = organChildren;
                }

                // Add a photosynthesis constant with a value of 0.
                JsonUtilities.AddConstantFunctionIfNotExists(organ, "Photosynthesis", "0");

                // Add an initial nconc which points to minimum NConc.
                JsonUtilities.AddVariableReferenceIfNotExists(organ, "initialNConcFunction", $"[{organ["Name"]}].MinimumNConc");

                // Add a BiomassDemand with 3 child constants (structural, metabolic, storage)
                // each with a value of 1.
                if (JsonUtilities.ChildWithName(organ, "dmDemandPriorityFactors", true) == null)
                {
                    JObject demand = new JObject();
                    demand["$type"] = "Models.PMF.BiomassDemand, Models";
                    demand["Name"] = "dmDemandPriorityFactors";
                    JsonUtilities.AddConstantFunctionIfNotExists(demand, "Structural", "1");
                    JsonUtilities.AddConstantFunctionIfNotExists(demand, "Metabolic", "1");
                    JsonUtilities.AddConstantFunctionIfNotExists(demand, "Storage", "1");
                    organChildren.Add(demand);
                }
            }
        }

        /// <summary>
        /// Rename DroughtInducedSenescence and Lag functions so they can be used for other stresses
        /// optional functions non-optional.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion131(JObject root, string fileName)
        {
            foreach (JObject Root in JsonUtilities.ChildrenOfType(root, "Leaf+LeafCohortParameters"))
            {
                JsonUtilities.RenameChildModel(Root, "DroughtInducedLagAcceleration", "LagAcceleration");
                JsonUtilities.RenameChildModel(Root, "DroughtInducedSenAcceleration", "SenescenceAcceleration");
            }
        }

        /// <summary>
        /// Replace all XmlIgnore attributes with JsonIgnore attributes in manager scripts.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion132(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                bool changed = manager.Replace("[XmlIgnore]", "[JsonIgnore]");
                changed |= manager.Replace("[System.Xml.Serialization.XmlIgnore]", "[JsonIgnore]");
                if (changed)
                {
                    manager.AddUsingStatement("Newtonsoft.Json");
                    manager.Save();
                }
            }
        }

        /// <summary>
        /// Remove the WaterAvailableMethod from PastureSpecies.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion133(JObject root, string fileName)
        {
            foreach (JObject pasturSpecies in JsonUtilities.ChildrenRecursively(root, "PastureSpecies"))
                pasturSpecies.Remove("WaterAvailableMethod");
        }

        /// <summary>
        /// Set MicroClimate's reference height to 2 if it's 0.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion134(JObject root, string fileName)
        {
            const string propertyName = "ReferenceHeight";
            foreach (JObject microClimate in JsonUtilities.ChildrenRecursively(root, "MicroClimate"))
            {
                JToken property = microClimate[propertyName];
                if (property == null || property.Value<double>() <= 0)
                    microClimate[propertyName] = 2;
            }
        }

        /// <summary>
        /// Rename memos' MemoText property to Text. This is only relevant when
        /// importing files from old apsim (hopefully). It's really a cludge to
        /// work around a bug in the xml to json converter which I'm not brave
        /// enough to change.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion135(JObject root, string fileName)
        {
            foreach (JObject memo in JsonUtilities.ChildrenRecursively(root, "Memo"))
                JsonUtilities.RenameProperty(memo, "MemoText", "Text");
        }

        /// <summary>
        /// Replace XmlIgnore attributes with JsonIgnore attributes in manager scripts.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion136(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                bool changed = manager.Replace("[XmlIgnore]", "[JsonIgnore]");
                changed |= manager.Replace("[System.Xml.Serialization.XmlIgnore]", "[JsonIgnore]");
                if (changed)
                {
                    manager.AddUsingStatement("Newtonsoft.Json");
                    manager.Save();
                }
            }
        }

        /// <summary>
        /// Rename RootShapeCylindre to RootShapeCylinder.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion137(JObject root, string fileName)
        {
            foreach (JObject cylinder in JsonUtilities.ChildrenRecursively(root, "RootShapeCylindre"))
                cylinder["$type"] = "Models.Functions.RootShape.RootShapeCylinder, Models";
        }

        /// <summary>
        /// Remove all parameters from sugarcane and change it to use the sugarcane resource.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion138(JObject root, string fileName)
        {
            foreach (JObject sugar in JsonUtilities.ChildrenRecursively(root, "Sugarcane"))
            {
                if (sugar["ResourceName"] == null || sugar["ResourceName"].ToString() != "Sugarcane")
                {
                    sugar.RemoveAll();
                    sugar["$type"] = "Models.Sugarcane, Models";
                    sugar["Name"] = "Sugarcane";
                    sugar["ResourceName"] = "Sugarcane";
                    sugar["IncludeInDocumentation"] = true;
                    sugar["Enabled"] = true;
                    sugar["ReadOnly"] = false;
                }
            }
        }

        /// <summary>
        /// Add priority factor functions into each demand function
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion139(JObject root, string fileName)
        {
            foreach (JObject organ in JsonUtilities.ChildrenInNameSpace(root, "Models.PMF.Organs"))
            {
                // Add priority factors to leaf and reproductive organ where they are currently optional
                if ((JsonUtilities.Type(organ) == "Leaf") || (JsonUtilities.Type(organ) == "ReproductiveOrgan"))
                {
                    JObject PriorityFactors = JsonUtilities.ChildWithName(organ, "dmDemandPriorityFactors");
                    if (PriorityFactors == null)
                    {
                        PriorityFactors = JsonUtilities.ChildWithName(organ, "DMDemandPriorityFactors");
                    }
                    if (PriorityFactors == null)
                    {
                        JObject PFactors = new JObject();
                        PFactors["$type"] = "Models.PMF.BiomassDemand, Models";
                        PFactors["Name"] = "DMDemandPriorityFactors";
                        JsonUtilities.AddConstantFunctionIfNotExists(PFactors, "Structural", "1");
                        JsonUtilities.AddConstantFunctionIfNotExists(PFactors, "Metabolic", "1");
                        JsonUtilities.AddConstantFunctionIfNotExists(PFactors, "Storage", "1");
                        (organ["Children"] as JArray).Add(PFactors);
                    }

                    JObject NPFactors = new JObject();
                    NPFactors["$type"] = "Models.PMF.BiomassDemand, Models";
                    NPFactors["Name"] = "NDemandPriorityFactors";
                    JsonUtilities.AddConstantFunctionIfNotExists(NPFactors, "Structural", "1");
                    JsonUtilities.AddConstantFunctionIfNotExists(NPFactors, "Metabolic", "1");
                    JsonUtilities.AddConstantFunctionIfNotExists(NPFactors, "Storage", "1");
                    (organ["Children"] as JArray).Add(NPFactors);
                }
                else if ((JsonUtilities.Type(organ) == "SimpleLeaf") || (JsonUtilities.Type(organ) == "GenericOrgan")
                    || (JsonUtilities.Type(organ) == "Root"))
                // Move proority factors into Demand node and add if not currently there
                {
                    JObject PriorityFactors = JsonUtilities.ChildWithName(organ, "DMDemandPriorityFactors");
                    if (PriorityFactors != null)
                    {
                        JsonUtilities.RemoveChild(organ, "DMDemandPriorityFactors");
                    }
                    if (PriorityFactors == null)
                    {
                        PriorityFactors = JsonUtilities.ChildWithName(organ, "dmDemandPriorityFactors");

                        if (PriorityFactors != null)
                        {
                            JsonUtilities.RemoveChild(organ, "dmDemandPriorityFactors");
                        }
                    }
                    JObject DMDemands = JsonUtilities.ChildWithName(organ, "DMDemands");
                    if (DMDemands != null)
                    {
                        DMDemands["$type"] = "Models.PMF.BiomassDemandAndPriority, Models";
                        if (PriorityFactors != null)
                        {
                            JObject Structural = JsonUtilities.ChildWithName(PriorityFactors, "Structural");
                            Structural["Name"] = "QStructuralPriority";
                            (DMDemands["Children"] as JArray).Add(Structural);
                            JObject Metabolic = JsonUtilities.ChildWithName(PriorityFactors, "Metabolic");
                            Metabolic["Name"] = "QMetabolicPriority";
                            (DMDemands["Children"] as JArray).Add(Metabolic);
                            JObject Storage = JsonUtilities.ChildWithName(PriorityFactors, "Storage");
                            Storage["Name"] = "QStoragePriority";
                            (DMDemands["Children"] as JArray).Add(Storage);
                        }
                        else
                        {
                            JsonUtilities.AddConstantFunctionIfNotExists(DMDemands, "QStructuralPriority", "1");
                            JsonUtilities.AddConstantFunctionIfNotExists(DMDemands, "QMetabolicPriority", "1");
                            JsonUtilities.AddConstantFunctionIfNotExists(DMDemands, "QStoragePriority", "1");
                        }
                    }
                    JObject NDemands = JsonUtilities.ChildWithName(organ, "NDemands");
                    if (NDemands != null)
                    {
                        NDemands["$type"] = "Models.PMF.BiomassDemandAndPriority, Models";
                        JsonUtilities.AddConstantFunctionIfNotExists(NDemands, "QStructuralPriority", "1");
                        JsonUtilities.AddConstantFunctionIfNotExists(NDemands, "QMetabolicPriority", "1");
                        JsonUtilities.AddConstantFunctionIfNotExists(NDemands, "QStoragePriority", "1");
                    }
                }
            }
        }

        /// <summary>
        /// Remove all occurences of SoilNitrogenPlantAvailable NO3 and NH4 types.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion140(JObject root, string fileName)
        {

            foreach (var PAN in JsonUtilities.ChildrenOfType(root, "SoilNitrogenPlantAvailableNO3"))
                PAN.Remove();
            foreach (var PAN in JsonUtilities.ChildrenOfType(root, "SoilNitrogenPlantAvailableNH4"))
                PAN.Remove();

        }


        /// <summary>
        /// Convert CompositeBiomass from a Propertys property to OrganNames.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion141(JObject root, string fileName)
        {
            foreach (var compositeBiomass in JsonUtilities.ChildrenRecursively(root, "CompositeBiomass"))
            {
                var properties = compositeBiomass["Propertys"] as JArray;
                if (properties != null)
                {
                    bool includeLive = false;
                    bool includeDead = false;
                    var organNames = new List<string>();

                    foreach (var property in properties.Values<string>())
                    {
                        var match = Regex.Match(property, @"\[(\w+)\]\.(\w+)");
                        if (match.Success)
                        {
                            organNames.Add(match.Groups[1].Value);
                            if (match.Groups[2].Value.Equals("Live", StringComparison.InvariantCultureIgnoreCase))
                                includeLive = true;
                            else
                                includeDead = true;
                        }
                    }
                    compositeBiomass["Propertys"] = null;
                    compositeBiomass["OrganNames"] = new JArray(organNames.Distinct());
                    compositeBiomass["IncludeLive"] = includeLive;
                    compositeBiomass["IncludeDead"] = includeDead;
                }
            }
        }

        /// <summary>
        /// Change OilPalm.NUptake to OilPalm.NitrogenUptake
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion142(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                bool changed1 = manager.Replace("OilPalm.NUptake", "OilPalm.NitrogenUptake");
                bool changed2 = manager.Replace("OilPalm.SWUptake", "OilPalm.WaterUptake");
                if (changed1 || changed2)
                    manager.Save();
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "[OilPalm].NUptake", "[OilPalm].NitrogenUptake");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[OilPalm].SWUptake", "[OilPalm].WaterUptake");
                JsonUtilities.SearchReplaceReportVariableNames(report, "OilPalm.NUptake", "OilPalm.NitrogenUptake");
                JsonUtilities.SearchReplaceReportVariableNames(report, "OilPalm.SWUptake", "OilPalm.WaterUptake");
            }
        }

        /// <summary>
        /// Changes to facilitate the autodocs refactor:
        /// - Rename Models.Axis to APSIM.Shared.Graphing.Axis.
        /// - Copy the value of all folders' IncludeInDocumentation property
        ///   into their new ShowInDocs property.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion143(JObject root, string fileName)
        {
            if (JsonUtilities.Type(root) == "Graph")
                FixGraph(root);
            foreach (JObject graph in JsonUtilities.ChildrenRecursively(root, "Graph"))
                FixGraph(graph);

            foreach (JObject folder in JsonUtilities.ChildrenRecursively(root, "Folder"))
            {
                JToken showInDocs = folder["ShowPageOfGraphs"];
                bool show = showInDocs != null && showInDocs.Value<bool>();
                folder["ShowInDocs"] = show && ShouldShowInDocs(folder);
            }

            void FixGraph(JObject graph)
            {
                JToken axes = graph["Axis"];
                if (axes == null)
                    return;
                foreach (JObject axis in axes)
                {
                    // Class moved into APSIM.Shared.Graphing namespace.
                    axis["$type"] = "APSIM.Shared.Graphing.Axis, APSIM.Shared";

                    // Type property renamed to Position.
                    JsonUtilities.RenameProperty(axis, "Type", "Position");

                    // Min/Max/Interval properties are now nullable doubles.
                    // null is used to indicate no value, rather than NaN.
                    RemoveAxisNaNs(axis, "Minimum");
                    RemoveAxisNaNs(axis, "Maximum");
                    RemoveAxisNaNs(axis, "Interval");
                }
            }

            void RemoveAxisNaNs(JObject axis, string propertyName)
            {
                JToken value = axis[propertyName];
                if (value == null)
                    return;
                if (value.Value<string>() == "NaN")
                    axis[propertyName] = null;
            }

            bool ShouldShowInDocs(JObject folder)
            {
                JToken includeInDocumentation = folder["IncludeInDocumentation"];
                if (includeInDocumentation == null || !includeInDocumentation.Value<bool>())
                    return false;
                // bool isFolder = JsonUtilities.Type(folder) == "Folder";
                // if (isFolder)
                // {
                //     JToken showPageOfGraphs = folder["ShowPageOfGraphs"];
                //     if (showPageOfGraphs == null || !showPageOfGraphs.Value<bool>())
                //         return false;
                // }
                JObject parent = (JObject)JsonUtilities.Parent(folder);
                if (parent == null)
                    return true;
                else
                    return ShouldShowInDocs(parent);
            }
        }

        /// <summary>
        /// Change the namespace of the Coordinate type.
        /// Change the namespace of the DirectedGraph type.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion144(JObject root, string fileName)
        {
            foreach (JObject map in JsonUtilities.ChildrenRecursively(root, "Map"))
            {
                JObject center = map["Center"] as JObject;
                if (center != null)
                    center["$type"] = "Models.Mapping.Coordinate, Models";
            }
            foreach (JObject nutrient in JsonUtilities.ChildrenRecursively(root, "Nutrient"))
            {
                JToken graph = nutrient["DirectedGraphInfo"];
                if (graph != null)
                {
                    graph["$type"] = "APSIM.Shared.Graphing.DirectedGraph, APSIM.Shared";
                    JArray nodes = graph["Nodes"] as JArray;
                    if (nodes != null)
                        foreach (JToken node in nodes)
                            node["$type"] = "APSIM.Shared.Graphing.Node, APSIM.Shared";
                    JArray arcs = graph["Arcs"] as JArray;
                    if (arcs != null)
                        foreach (JToken arc in arcs)
                            arc["$type"] = "APSIM.Shared.Graphing.Arc, APSIM.Shared";
                }
            }
        }

        /// <summary>
        /// Add in a Forages model at the simulation level if Stock or SimpleGrazing
        /// are in the simulation.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion145(JObject root, string fileName)
        {
            foreach (JObject simulation in JsonUtilities.ChildrenRecursively(root, "Simulation"))
            {
                List<JObject> stockModels = JsonUtilities.ChildrenRecursively(simulation, "Stock");
                JObject stock = null;
                if (stockModels.Any())
                    stock = stockModels.First();

                List<JObject> simpleGrazing = JsonUtilities.ChildrenRecursively(simulation, "SimpleGrazing");
                if (stock != null || simpleGrazing.Any())
                {
                    // Add in a Forages model.
                    JObject forages = new JObject();
                    forages["$type"] = "Models.ForageDigestibility.Forages, Models";
                    forages["Name"] = "Forages";

                    JArray simulationChildren = simulation["Children"] as JArray;
                    int position = simulationChildren.IndexOf(stock);
                    if (position == -1)
                        simulationChildren.Add(forages);
                    else
                        simulationChildren.Insert(position + 1, forages);
                }
            }
        }

        /// <summary>
        /// Fix API calls to summary.WriteX, and pass in an appropriate message type.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion146(JObject root, string fileName)
        {
            const string infoPattern = @"\.WriteMessage\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\);";
            const string warningPattern = @"\.WriteWarning\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\);";
            const string errorPattern = @"\.WriteError\(((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))\);";
            const string infoReplace = ".WriteMessage($1, MessageType.Diagnostic);";
            const string warningReplace = ".WriteMessage($1, MessageType.Warning);";
            const string errorReplace = ".WriteMessage($1, MessageType.Error);";
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                bool replace = manager.ReplaceRegex(infoPattern, infoReplace);
                replace |= manager.ReplaceRegex(warningPattern, warningReplace);
                replace |= manager.ReplaceRegex(errorPattern, errorReplace);
                if (replace)
                    manager.Save();
            }
        }

        /// <summary>
        /// Rename report function log to log10.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion147(JObject root, string fileName)
        {
            foreach (JObject report in JsonUtilities.ChildrenRecursively(root, "Report"))
            {
                JArray variables = report["VariableNames"] as JArray;
                if (variables != null)
                    foreach (JValue variable in variables)
                        if (variable.Value is string)
                            variable.Value = ((string)variable.Value).Replace("log(", "log10(");
            }
        }

        /// <summary>
        /// Remove all graphs which are children of XYPairs. An older version
        /// contained a bug which inserted duplicate graphs here. (Duplicate
        /// models will now cause a file to fail to run.)
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion148(JObject root, string fileName)
        {
            foreach (JObject xyPairs in JsonUtilities.ChildrenRecursively(root, "XYPairs"))
                foreach (JObject graph in JsonUtilities.ChildrenOfType(xyPairs, "Graph"))
                    JsonUtilities.RemoveChild(xyPairs, JsonUtilities.Name(graph));
        }

        /// <summary>
        /// Change EmergingPhase to use a child Target IFunction rather than built in shootlag, shootrate.
        /// Also add a seed mortality function to plant models.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion149(JObject root, string fileName)
        {
            foreach (JObject emergingPhase in JsonUtilities.ChildrenRecursively(root, "EmergingPhase"))
            {
                var shootLag = emergingPhase["ShootLag"].ToString();
                var shootRate = emergingPhase["ShootRate"].ToString();
                emergingPhase.Remove("ShootLag");
                emergingPhase.Remove("ShootRate");

                var target = JsonUtilities.CreateNewChildModel(emergingPhase, "Target", "Models.Functions.AddFunction");
                JsonUtilities.AddConstantFunctionIfNotExists(target, "ShootLag", shootLag);
                var depthxRate = JsonUtilities.CreateNewChildModel(target, "DepthxRate", "Models.Functions.MultiplyFunction");

                var sowingDepthReference = JsonUtilities.CreateNewChildModel(depthxRate, "SowingDepth", "Models.Functions.VariableReference");
                sowingDepthReference["VariableName"] = "[Plant].SowingData.Depth";

                JsonUtilities.AddConstantFunctionIfNotExists(depthxRate, "ShootRate", shootRate);
            }

            foreach (JObject plant in JsonUtilities.ChildrenRecursively(root, "Plant"))
            {
                if (JsonUtilities.ChildWithName(plant, "MortalityRate") != null)
                    JsonUtilities.AddConstantFunctionIfNotExists(plant, "SeedMortality", "0.0");
            }
        }

        /// <summary>
        /// The previous converter function added a constant called
        /// SeedMortality, which should have been called SeedMortalityRate.
        /// Unfortunately, the cat is already out of the bag, so I've fixed this
        /// by writing a new converter function.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion150(JObject root, string fileName)
        {
            const string correctName = "SeedMortalityRate";
            foreach (JObject plant in JsonUtilities.ChildrenRecursively(root, "Plant"))
            {
                if (JsonUtilities.Children(plant).Count > 0)
                {
                    JObject seedMortality = JsonUtilities.ChildWithName(plant, "SeedMortality", ignoreCase: true);
                    if (seedMortality == null)
                        // If no seed mortality exists, add it in with the right name.
                        JsonUtilities.AddConstantFunctionIfNotExists(plant, correctName, 0);
                    else
                        // We already have a seed mortality. Just rename it.
                        JsonUtilities.RenameModel(seedMortality, correctName);
                }
            }
        }

        /// <summary>
        /// Update modified models to new CLEM refactor with Comparable child models.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion151(JObject root, string fileName)
        {
            Dictionary<string, string> searchReplaceStrings = new Dictionary<string, string>()
            {
                { "Models.CLEM.Groupings.LabourFilterGroup", "Models.CLEM.Groupings.LabourGroup" },
                { "Models.CLEM.Activities.RuminantActivityFee", "Models.CLEM.Activities.ActivityFee" },
                { "Models.CLEM.Activities.CropActivityFee", "Models.CLEM.Activities.ActivityFee" },
                { "Models.CLEM.Activities.ResourceActivityFee", "Models.CLEM.Activities.ActivityFee" },
                { "Models.CLEM.Activities.LabourActivityFee", "Models.CLEM.Activities.ActivityFee" },
                { "Models.CLEM.Activities.TruckingSettings", "Models.CLEM.Activities.RuminantTrucking" },
                { "Models.CLEM.Activities.ActivityCutAndCarryLimiter", "Models.CLEM.Limiters.ActivityCarryLimiter" },
                { "Models.CLEM.Activities.ActivityTimerBreedForMilking", "Models.CLEM.Timers.ActivityTimerBreedForMilking" },
                { "Models.CLEM.Activities.ActivityTimerCropHarvest", "Models.CLEM.Timers.ActivityTimerCropHarvest" },
                { "Models.CLEM.Activities.ActivityTimerDateRange", "Models.CLEM.Timers.ActivityTimerDateRange" },
                { "Models.CLEM.Activities.ActivityTimerInterval", "Models.CLEM.Timers.ActivityTimerInterval" },
                { "Models.CLEM.Activities.ActivityTimerLinked", "Models.CLEM.Timers.ActivityTimerLinked" },
                { "Models.CLEM.Activities.ActivityTimerMonthRange", "Models.CLEM.Timers.ActivityTimerMonthRange" },
                { "Models.CLEM.Activities.ActivityTimerPastureLevel", "Models.CLEM.Timers.ActivityTimerPastureLevel" },
                { "Models.CLEM.Activities.ActivityTimerResourceLevel", "Models.CLEM.Timers.ActivityTimerResourceLevel" },
                { "Models.CLEM.Activities.ActivityTimerSequence", "Models.CLEM.Timers.ActivityTimerSequence" },
            };

            foreach (var item in searchReplaceStrings)
                JsonUtilities.ReplaceChildModelType(root, item.Key, item.Value);
        }

        /// <summary>
        /// Move solutes out from under nutrient into soil.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion152(JObject root, string fileName)
        {
            foreach (JObject soil in JsonUtilities.ChildrenRecursively(root, "Soil"))
            {
                var nutrient = JsonUtilities.ChildWithName(soil, "Nutrient");
                var soilNitrogen = JsonUtilities.ChildWithName(soil, "SoilNitrogen");
                var nutrientPatchManager = JsonUtilities.ChildWithName(soil, "NutrientPatchManager");
                var physical = JsonUtilities.ChildWithName(soil, "Physical", ignoreCase: true);
                var chemical = JsonUtilities.ChildWithName(soil, "Chemical", ignoreCase: true);
                var organic = JsonUtilities.ChildWithName(soil, "Organic", ignoreCase: true);
                var samples = JsonUtilities.ChildrenOfType(soil, "Sample");

                if (soil != null && physical != null && chemical != null && organic != null)
                {
                    var soilChildren = soil["Children"] as JArray;
                    var chemicalChildren = chemical["Children"] as JArray;
                    var bdToken = physical["BD"] as JArray;

                    // Add a nutrient model if neither Nutrient or SoilNitrogen exists.
                    if (nutrient == null && soilNitrogen == null)
                    {
                        soilChildren.Add(new JObject()
                        {
                            ["$type"] = "Models.Soils.Nutrients.Nutrient, Models",
                            ["Name"] = "Nutrient",
                            ["ResourceName"] = "Nutrient"
                        });
                    }

                    if (soilChildren != null && bdToken != null)
                    {
                        var bd = bdToken.Values<double>().ToArray();
                        var bdThickness = physical["Thickness"].Values<double>().ToArray();
                        var organicThickness = organic["Thickness"].Values<double>().ToArray();
                        var chemicalThickness = chemical["Thickness"].Values<double>().ToArray();

                        string soluteTypeName = "Models.Soils.Solute, Models";
                        if (soilNitrogen != null)
                            soluteTypeName = "Models.Soils.SoilNitrogen{soluteName}, Models";
                        else if (nutrientPatchManager != null)
                            soluteTypeName = "Models.Soils.NutrientPatching.SolutePatch, Models";

                        // create a collection of JTokens to search for solute initialisation values.
                        var tokensContainingValues = new JObject[] { organic, chemical }
                                                     .Concat(samples.Reverse<JObject>());

                        var oc = GetValues(tokensContainingValues, "OC", 1.0, bd, bdThickness, organicThickness);
                        StoreValuesInToken(oc, organic, "Carbon", "CarbonUnits");

                        var ph = GetValues(tokensContainingValues, "PH", 7.0, bd, bdThickness, chemicalThickness);
                        StoreValuesInToken(ph, chemical, "PH", "PHUnits");

                        var ec = GetValues(tokensContainingValues, "EC", 0.0, bd, bdThickness, chemicalThickness);
                        StoreValuesInToken(ec, chemical, "EC", "PHUnits");

                        var esp = GetValues(tokensContainingValues, "ESP", 0.0, bd, bdThickness, chemicalThickness);
                        StoreValuesInToken(esp, chemical, "ESP", "PHUnits");

                        // iterate through existing solutes (e.g. CL) and store their initial values in the solute.
                        foreach (var solute in JsonUtilities.ChildrenOfType(soil, "Solute"))
                        {
                            var soluteName = solute["Name"].ToString();
                            var soluteValues = GetValues(tokensContainingValues, soluteName, 0.0, bd, bdThickness, null);

                            if (soluteValues.Item1 != null)
                            {
                                solute["$type"] = soluteTypeName;
                                solute["InitialValues"] = new JArray(soluteValues.Item1);
                                solute["InitialValuesUnits"] = soluteValues.Item2;
                                solute["Thickness"] = new JArray(soluteValues.Item3);
                            }
                        }

                        // Move solutes from nutrient to soil.
                        var no3Token = JsonUtilities.ChildWithName(soil, "NO3");
                        if (no3Token == null)
                        {
                            var no3 = GetValues(tokensContainingValues, "NO3", 0.1, bd, bdThickness, null);
                            soilChildren.Add(CreateSoluteToken(no3, soluteTypeName, "NO3"));
                        }

                        var nh4Token = JsonUtilities.ChildWithName(soil, "NH4");
                        if (nh4Token == null)
                        {
                            var nh4 = GetValues(tokensContainingValues, "NH4", 0.01, bd, bdThickness, null);
                            soilChildren.Add(CreateSoluteToken(nh4, soluteTypeName, "NH4"));
                        }

                        var labileP = GetValues(tokensContainingValues, "LabileP", 0.0, bd, bdThickness, null);
                        var unavailableP = GetValues(tokensContainingValues, "UnavailableP", 0.0, bd, bdThickness, null);
                        if (labileP.Item1 != null && unavailableP.Item1 != null)
                        {
                            soilChildren.Add(CreateSoluteToken(labileP, soluteTypeName, "LabileP"));
                            soilChildren.Add(CreateSoluteToken(unavailableP, soluteTypeName, "UnavailableP"));
                        }
                        if (nutrientPatchManager != null)
                        {
                            soilChildren.Add(CreateSoluteToken((null, null, null), soluteTypeName, "PlantAvailableNO3"));
                            soilChildren.Add(CreateSoluteToken((null, null, null), soluteTypeName, "PlantAvailableNH4"));
                        }

                        // Remove solutes from under nutrient model
                        var nutrientModel = nutrient;
                        if (soilNitrogen != null)
                            nutrientModel = soilNitrogen;
                        if (nutrientPatchManager != null)
                            nutrientModel = nutrientPatchManager;
                        if (nutrientModel != null)
                        {
                            var token = JsonUtilities.ChildWithName(nutrientModel, "NO3");
                            if (token != null)
                                token.Remove();
                            token = JsonUtilities.ChildWithName(nutrientModel, "NH4");
                            if (token != null)
                                token.Remove();
                            token = JsonUtilities.ChildWithName(nutrientModel, "Urea");
                            if (token != null)
                                token.Remove();
                            token = JsonUtilities.ChildWithName(nutrientModel, "PlantAvailableNO3");
                            if (token != null)
                                token.Remove();
                            token = JsonUtilities.ChildWithName(nutrientModel, "PlantAvailableNH4");
                            if (token != null)
                                token.Remove();
                        }

                        // Add a urea solute to soil
                        var ureaToken = JsonUtilities.ChildWithName(soil, "Urea");
                        if (ureaToken == null)
                        {
                            var urea = (double[])Array.CreateInstance(typeof(double), chemicalThickness.Length);
                            soilChildren.Add(CreateSoluteToken((urea, "kgha", chemicalThickness), soluteTypeName, "Urea"));
                        }
                    }


                    // By this point any remaining samples should just have SW values or be blank.
                    // Delete the blank samples and move the remaining ones to under the Physical node.
                    JObject water = null;
                    foreach (JObject sample in JsonUtilities.ChildrenRecursively(soil, "Sample"))
                    {
                        var sw = sample["SW"] as JArray;
                        if (sw != null && MathUtilities.ValuesInArray(sw.Values<double>()))
                        {
                            // Does a water node already exist?
                            water = JsonUtilities.ChildWithName(soil, "Water");
                            if (water == null)
                            {
                                water = sample;
                                // Turn a sample into a Water node.
                                sample.Remove("NO3");
                                sample.Remove("NH4");
                                sample.Remove("Urea");
                                sample.Remove("LabileP");
                                sample.Remove("UnavailableP");
                                sample.Remove("OC");
                                sample.Remove("EC");
                                sample.Remove("PH");
                                sample.Remove("CL");
                                sample.Remove("ESP");
                                water["Name"] = "Water";
                                water["$type"] = "Models.Soils.Water, Models";
                            }
                            else
                                sample.Remove();  // remove the sample.

                            water["InitialValues"] = sample["SW"];
                            sample.Remove("SW");
                        }
                        else
                            sample.Remove();
                    }

                    // Convert InitWater to a Water node.
                    foreach (var initWater in JsonUtilities.ChildrenOfType(soil, "InitialWater"))
                    {
                        var percentMethod = initWater["PercentMethod"].Value<string>();
                        bool filledFromTop = percentMethod == "0" || percentMethod == "FilledFromTop";
                        double fractionFull = Math.Min(1.0, initWater["FractionFull"].Value<double>());
                        double depthWetSoil = double.NaN;
                        if (initWater["DepthWetSoil"] != null)
                            depthWetSoil = initWater["DepthWetSoil"].Value<double>();
                        string relativeTo = "LL15";
                        if (initWater["RelativeTo"] != null)
                            relativeTo = initWater["RelativeTo"].ToString();
                        double[] thickness = physical["Thickness"].Values<double>().ToArray();
                        double[] airdry = physical["AirDry"].Values<double>().ToArray();
                        double[] ll15 = physical["LL15"].Values<double>().ToArray();
                        double[] dul = physical["DUL"].Values<double>().ToArray();
                        double[] ll;
                        double[] xf = null;
                        double[] sat = physical["SAT"].Values<double>().ToArray();
                        if (relativeTo == "LL15")
                            ll = ll15;
                        else
                        {
                            var nameToFind = relativeTo + "Soil";
                            var plantCrop = JsonUtilities.ChildrenOfType(physical, "SoilCrop")
                                                            .Find(sc => sc["Name"].ToString().Equals(relativeTo, StringComparison.InvariantCultureIgnoreCase));
                            if (plantCrop == null)
                            {
                                relativeTo = "LL15";
                                ll = ll15;
                            }
                            else
                            {
                                ll = plantCrop["LL"].Values<double>().ToArray();
                                xf = plantCrop["XF"].Values<double>().ToArray();
                            }
                        }
                        if (xf == null)
                            xf = Enumerable.Repeat(1.0, thickness.Length).ToArray();

                        if (water == null)
                        {
                            water = initWater;
                            water["Name"] = "Water";
                            water["$type"] = "Models.Soils.Water, Models";
                            water.Remove("PercentMethod");
                            water.Remove("FractionFull");
                            water.Remove("DepthWetSoil");
                        }
                        else
                        {
                            initWater.Remove();
                        }

                        if (!double.IsNaN(depthWetSoil))
                            water["InitialValues"] = new JArray(Water.DistributeToDepthOfWetSoil(depthWetSoil, thickness, ll, dul));
                        else
                        {
                            if (filledFromTop)
                                water["InitialValues"] = new JArray(Water.DistributeWaterFromTop(fractionFull, thickness, airdry, ll, dul, sat, xf));
                            else
                                water["InitialValues"] = new JArray(Water.DistributeWaterEvenly(fractionFull, thickness, airdry, ll, dul, sat, xf));
                        }
                        water["Thickness"] = new JArray(thickness);
                        water["FilledFromTop"] = filledFromTop;
                    }

                    // If there is no water node, then create one.
                    if (JsonUtilities.ChildWithName(soil, "Water") == null)
                    {
                        if (soilChildren != null)
                        {
                            soilChildren.Add(new JObject()
                            {
                                ["$type"] = "Models.Soils.Water, Models",
                                ["Name"] = "Water",
                                ["Thickness"] = physical["Thickness"]
                            });
                        }
                    }
                }
            }

            // Convert all SwimSoluteParameters into regular solutes.
            foreach (JObject swimSolute in JsonUtilities.ChildrenRecursively(root, "SwimSoluteParameters"))
            {
                var parent = JsonUtilities.Parent(swimSolute);
                if (parent["$type"].ToString().Contains(".Swim3"))
                {
                    var soil = JsonUtilities.Parent(parent) as JObject;
                    string soluteName = swimSolute["Name"].ToString();
                    var solute = JsonUtilities.ChildWithName(soil, soluteName, true);

                    if (solute != null)
                    {
                        solute["WaterTableConcentration"] = swimSolute["WaterTableConcentration"];
                        solute["D0"] = swimSolute["D0"];
                        solute["Exco"] = swimSolute["Exco"];
                        solute["FIP"] = swimSolute["FIP"];
                        if (solute["Thickness"] == null)
                        {
                            solute["Thickness"] = swimSolute["Thickness"];
                            int numLayers = (solute["Thickness"] as JArray).Count;
                            solute["InitialValues"] = new JArray(Enumerable.Repeat(0.0, numLayers));
                        }
                    }
                    swimSolute.Remove();
                }
                else
                    swimSolute["$type"] = "Models.Soils.Solute, Models";
            }

            foreach (JObject swimWT in JsonUtilities.ChildrenRecursively(root, "SwimWaterTable"))
                swimWT.Remove();

            // Make sure all solutes have the new $type
            foreach (JObject solute in JsonUtilities.ChildrenRecursively(root, "Solute"))
                solute["$type"] = "Models.Soils.Solute, Models";

            // Rename variables.
            var variableRenames = new Tuple<string, string>[]
            {
                new Tuple<string, string>("[Soil].Swim3.SWmm", "[Soil].Water.MM"),
                new Tuple<string, string>("[Soil].Swim3.SW", "[Soil].Water.Volumetric"),
                new Tuple<string, string>("[Swim3].SWmm", "[Soil].Water.MM"),
                new Tuple<string, string>("[Swim3].SW", "[Soil].Water.Volumetric"),

                new Tuple<string, string>("[Soil].SoilWater.SWmm", "[Soil].Water.MM"),
                new Tuple<string, string>("[Soil].SoilWater.SW", "[Soil].Water.Volumetric"),
                new Tuple<string, string>("[SoilWater].SWmm", "[Soil].Water.MM"),
                new Tuple<string, string>("[SoilWater].SW", "[Soil].Water.Volumetric"),
                new Tuple<string, string>("[Soil].Initialwater.SW", "[Soil].Water.InitialValues"),
                new Tuple<string, string>("[Soil].InitialWater.SW", "[Soil].Water.InitialValues"),
                new Tuple<string, string>("[Soil].Initial.SW", "[Soil].Water.InitialValues"),
                new Tuple<string, string>("[Soil].Initial Water.SW", "[Soil].Water.InitialValues"),
                new Tuple<string, string>("[Soil].Initial water.SW", "[Soil].Water.InitialValues"),
                new Tuple<string, string>("[Soil].InitialWater.FractionFull", "[Soil].Water.FractionFull"),
                new Tuple<string, string>("[Soil].Initial.OC", "[Soil].Organic.Carbon"),

                new Tuple<string, string>("[Swim3].Cl", "[Soil].Cl"),

                new Tuple<string, string>("[Soil].Nutrient.NO3.Denitrification", "[Nutrient].Denitrification"),
                new Tuple<string, string>("[Soil].Nutrient.NH4.Nitrification", "[Nutrient].Nitrification"),
                new Tuple<string, string>("[Soil].Nutrient.LabileP.PFlow", "[Nutrient].LabileToUnavailablePFlow"),
                new Tuple<string, string>("[Soil].Nutrient.UnavailableP.PFlow", "[Nutrient].UnavailableToLabilePFlow"),
                new Tuple<string, string>("[Soil].Nutrient.NO3", "[Soil].NO3"),
                new Tuple<string, string>("[Soil].Nutrient.NH4", "[Soil].NH4"),
                new Tuple<string, string>("[Soil].Nutrient.Urea", "[Soil].Urea"),
                new Tuple<string, string>("[Nutrient].NO3.Denitrification", "[Nutrient].Denitrification"),
                new Tuple<string, string>("[Nutrient].NH4.Nitrification", "[Nutrient].Nitrification"),
                new Tuple<string, string>("[Nutrient].LabileP.PFlow", "[Nutrient].LabileToUnavailablePFlow"),
                new Tuple<string, string>("[Nutrient].UnavailableP.PFlow", "[Nutrient].UnavailableToLabilePFlow"),
                new Tuple<string, string>("[Nutrient].NO3", "[Soil].NO3"),
                new Tuple<string, string>("[Nutrient].NH4", "[Soil].NH4"),
                new Tuple<string, string>("[Nutrient].Urea", "[Soil].Urea"),

                new Tuple<string, string>("[Soil].Chemical.LabileP", "[LabileP].InitialValues"),
                new Tuple<string, string>("[Soil].Chemical.UnavailableP", "[UnavailableP].InitialValues"),
                new Tuple<string, string>("[Chemical].LabileP", "[LabileP].InitialValues"),
                new Tuple<string, string>("[Chemical].UnavailableP", "[UnavailableP].InitialValues"),

                new Tuple<string, string>("[Soil].Nutrient.LabileP", "[Soil].LabileP"),
                new Tuple<string, string>("[Soil].Nutrient.UnavailableP", "[Soil].UnavailableP"),
                new Tuple<string, string>("[Nutrient].LabileP", "[Soil].LabileP"),
                new Tuple<string, string>("[Nutrient].UnavailableP", "[Soil].UnavailableP"),

                // SoilNitrogen variables
                new Tuple<string, string>("[Soil].SoilNitrogen.NO3", "[Soil].NO3"),
                new Tuple<string, string>("[Soil].SoilNitrogen.NH4", "[Soil].NH4"),
                new Tuple<string, string>("[Soil].SoilNitrogen.Urea", "[Soil].Urea"),
                new Tuple<string, string>("[SoilNitrogen].NO3", "[Soil].NO3"),
                new Tuple<string, string>("[SoilNitrogen].NH4", "[Soil].NH4"),
                new Tuple<string, string>("[SoilNitrogen].Urea", "[Soil].Urea"),

                new Tuple<string, string>(".Chemical.NO3N", ".NO3.InitialValues"),
            };
            JsonUtilities.RenameVariables(root, variableRenames);

            // Add a "using Models.Soils" to manager models if they reference solute.
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                var usingStatements = manager.GetUsingStatements();
                var found = usingStatements.Where(u => u == "Models.Soils").Any();
                if (!found)
                {
                    usingStatements = usingStatements.Append("Models.Soils");
                    manager.SetUsingStatements(usingStatements);
                    manager.Save();
                }
            }

            // Go through all samples under CompositeFactor and convert to parameter sets rather
            // than model replacements.
            foreach (JObject compositeFactor in JsonUtilities.ChildrenRecursively(root, "CompositeFactor"))
            {
                foreach (var sample in JsonUtilities.ChildrenOfType(compositeFactor, "Sample"))
                {
                    var thickness = sample["Thickness"];
                    if (thickness != null)
                    {
                        var sw = sample["SW"];
                        if (sw != null && sw is JArray)
                        {
                            StoreValuesInCompositeFactor(compositeFactor, thickness, $"[Water].Thickness");
                            StoreValuesInCompositeFactor(compositeFactor, sw, $"[Water].InitialValues");
                        }
                        var no3 = sample["NO3"];
                        if (no3 != null && no3 is JArray)
                        {
                            StoreValuesInCompositeFactor(compositeFactor, thickness, $"[{sample["Name"]}].Thickness");
                            StoreValuesInCompositeFactor(compositeFactor, no3, $"[{sample["Name"]}].InitialValues");
                            StoreStringInCompositeFactor(compositeFactor, "ppm", $"[{sample["Name"]}].InitialValuesUnits");
                        }
                        var nh4 = sample["NH4"];
                        if (nh4 != null && nh4 is JArray)
                        {
                            StoreValuesInCompositeFactor(compositeFactor, thickness, $"[{sample["Name"]}].Thickness");
                            StoreValuesInCompositeFactor(compositeFactor, nh4, $"[{sample["Name"]}].InitialValues");
                            StoreStringInCompositeFactor(compositeFactor, "ppm", $"[{sample["Name"]}].InitialValuesUnits");
                        }
                    }
                    sample.Remove();
                    JArray specifications = compositeFactor["Specifications"] as JArray;
                    var specificationStrings = specifications.Values<string>().ToArray();
                    int indexOfItemToRemove = Array.IndexOf(specificationStrings, $"[{sample["Name"]}]");
                    if (indexOfItemToRemove == -1)
                        indexOfItemToRemove = Array.IndexOf(specificationStrings, "[InitialWater]");
                    if (indexOfItemToRemove != -1)
                        specifications.RemoveAt(indexOfItemToRemove);
                }
            }
        }

        /// <summary>
        /// Store values into a CompositeFactor as a property set.
        /// </summary>
        /// <param name="compositeFactor">The composite factor token.</param>
        /// <param name="values">Values to store.</param>
        /// <param name="variableName">Name of variable.</param>
        private static void StoreValuesInCompositeFactor(JObject compositeFactor, JToken values, string variableName)
        {
            JArray specifications = compositeFactor["Specifications"] as JArray;
            var doubleValues = values.Values<string>();
            if (MathUtilities.ValuesInArray(doubleValues))
            {
                string valuesAsString = StringUtilities.BuildString(doubleValues.ToArray(), ",");
                specifications.Add($"{variableName}={valuesAsString}");
            }
        }

        /// <summary>
        /// Store string value into a CompositeFactor as a property set.
        /// </summary>
        /// <param name="compositeFactor">The composite factor token.</param>
        /// <param name="st">Values to store.</param>
        /// <param name="variableName">Name of variable.</param>
        private static void StoreStringInCompositeFactor(JObject compositeFactor, string st, string variableName)
        {
            JArray specifications = compositeFactor["Specifications"] as JArray;
            specifications.Add($"{variableName}={st}");
        }

        /// <summary>
        /// Store values in a token.
        /// </summary>
        /// <param name="value">Tuple of (values, units, thickness).</param>
        /// <param name="token">The token to store the value into.</param>
        /// <param name="elementName"></param>
        /// <param name="unitsElementName"></param>
        private static void StoreValuesInToken((double[], string, double[]) value, JObject token, string elementName, string unitsElementName)
        {
            if (value.Item1 != null)
            {
                token[elementName] = new JArray(value.Item1);
                if (value.Item2 != null)
                    token[unitsElementName] = value.Item2;
            }
        }

        /// <summary>
        /// Create a solute JToken
        /// </summary>
        /// <param name="value">Tuple of (values, units, thickness).</param>
        /// <param name="soluteTypeName">Type name of the solute.</param>
        /// <param name="soluteName">Name of the solute.</param>
        /// <returns></returns>
        private static JObject CreateSoluteToken((double[], string, double[]) value, string soluteTypeName, string soluteName)
        {
            var token = new JObject()
            {
                ["$type"] = soluteTypeName.Replace("{soluteName}", soluteName),
                ["Name"] = soluteName
            };
            if (value.Item1 != null)
            {
                token["InitialValues"] = new JArray(value.Item1);
                token["Thickness"] = new JArray(value.Item3);
            }
            if (value.Item2 != null)
                token["InitialValuesUnits"] = value.Item2;
            return token;
        }

        /// <summary>
        /// Get values of a property. Looks through tokens and finds first occurrance and returns it.
        /// </summary>
        /// <param name="tokens">Tokens to search through.</param>
        /// <param name="nodeName"></param>
        /// <param name="defaultValue"></param>
        /// <param name="bd">Bulk density</param>
        /// <param name="bdThickness">Bulk density thickness.</param>
        /// <param name="thicknessToReturn">The target thickness.</param>
        /// <returns>Tuple of (values, units, thickness).</returns>
        public static (double[], string, double[]) GetValues(IEnumerable<JObject> tokens, string nodeName, double defaultValue,
                                                             double[] bd, double[] bdThickness,
                                                             double[] thicknessToReturn)
        {
            foreach (var token in tokens)
            {
                string units = null;

                var valuesToken = token[nodeName] as JArray;
                if (nodeName == "NO3" || nodeName == "NH4")
                {
                    if (valuesToken == null)
                    {
                        valuesToken = token[nodeName + "N"] as JArray;
                        units = "ppm";
                    }
                    else
                        units = "kgha";
                }
                else if (valuesToken == null && nodeName == "OC")
                {
                    valuesToken = token["Carbon"] as JArray;
                    units = "Total";
                }
                else if (nodeName == "CL")
                    units = "ppm";

                if (valuesToken != null)
                {
                    var values = valuesToken.Values<double>().ToArray();
                    if (MathUtilities.ValuesInArray(values))
                    {
                        // Found values - convert to same layer structure.
                        var sampleToken = JsonUtilities.Parent(valuesToken);
                        var valuesThickness = sampleToken["Thickness"].Values<double>().ToArray();
                        var unitsToken = sampleToken[$"{nodeName}Units"];
                        if (unitsToken != null)
                            units = unitsToken.ToString();

                        if (thicknessToReturn != null)
                        {
                            if (units == "kgha")
                                values = SoilUtilities.MapMass(values, valuesThickness,
                                                               thicknessToReturn,
                                                               allowMissingValues: true);
                            else
                                values = SoilUtilities.MapConcentration(values, valuesThickness,
                                                                        thicknessToReturn,
                                                                        defaultValue,
                                                                        allowMissingValues: true);
                        }

                        return (values, units, valuesThickness);
                    }
                }
            }

            return (null, null, null);
        }

        /// <summary>
        /// Replace replacements with a simple folder.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion153(JObject root, string fileName)
        {
            foreach (JObject replacements in JsonUtilities.ChildrenRecursively(root, "Replacements"))
            {
                replacements["$type"] = "Models.Core.Folder, Models";
            }
        }

        /// <summary>
        /// Change .psi to .PSI (uppercase)
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion154(JObject root, string fileName)
        {
            foreach (JObject report in JsonUtilities.ChildrenRecursively(root, "Report"))
                JsonUtilities.SearchReplaceReportVariableNames(report, ".psi", ".PSI");
            foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
                JsonUtilities.ReplaceManagerCode(manager, ".psi", ".PSI");
        }

        /// <summary>
        /// Replace CultivarFolder with a simple folder.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion155(JObject root, string fileName)
        {
            foreach (JObject cultivarFolder in JsonUtilities.ChildrenRecursively(root, "CultivarFolder"))
                cultivarFolder["$type"] = "Models.Core.Folder, Models";
        }


        /// <summary>
        /// Change PredictedObserved to make SimulationName an explicit first field to match on.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion156(JObject root, string fileName)
        {
            foreach (JObject predictedObserved in JsonUtilities.ChildrenRecursively(root, "PredictedObserved"))
            {
                var field = predictedObserved["FieldName3UsedForMatch"];
                if (!String.IsNullOrEmpty(field?.Value<string>()))
                    predictedObserved["FieldName4UsedForMatch"] = field.Value<string>();

                field = predictedObserved["FieldName2UsedForMatch"];
                if (!String.IsNullOrEmpty(field?.Value<string>()))
                    predictedObserved["FieldName3UsedForMatch"] = field.Value<string>();

                field = predictedObserved["FieldNameUsedForMatch"];
                if (!String.IsNullOrEmpty(field?.Value<string>()))
                    predictedObserved["FieldName2UsedForMatch"] = field.Value<string>();

                predictedObserved["FieldNameUsedForMatch"] = "SimulationName";
            }
        }

        /// <summary>
        /// Rename 'Plantain' model to 'PlantainForage'
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion157(JObject root, string fileName)
        {
            // change the name of any Plantain plant
            foreach (JObject crop in JsonUtilities.ChildrenRecursively(root, "Plant"))
            {
                if (crop["Name"].ToString().Equals("Plantain", StringComparison.InvariantCultureIgnoreCase))
                {
                    crop["Name"] = "PlantainForage";
                    crop["ResourceName"] = "PlantainForage";
                }
            }

            // change all references to the model in the soil-plant params table
            foreach (JObject soilCrop in JsonUtilities.ChildrenRecursively(root, "SoilCrop"))
            {
                if (soilCrop["Name"].ToString().Equals("PlantainSoil", StringComparison.InvariantCultureIgnoreCase))
                {
                    JsonUtilities.RenameModel(soilCrop, "PlantainForageSoil");
                }
            }

            // change all references to the model in any report table
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Plantain]", "[PlantainForage]");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Plantain.", ".PlantainForage.");
            }

            foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
            {
                JsonUtilities.ReplaceManagerCode(manager, "[Plantain]", "[PlantainForage]");
                JsonUtilities.ReplaceManagerCode(manager, ".Plantain.", ".PlantainForage.");
                JsonUtilities.ReplaceManagerCode(manager, "Plantain.", "PlantainForage.");
            }

            // change all references to the model in any operations table
            foreach (var operations in JsonUtilities.ChildrenOfType(root, "Operations"))
            {
                var operation = operations["Operation"];
                if (operation != null && operation.HasValues)
                {
                    for (int i = 0; i < operation.Count(); i++)
                    {
                        var specification = operation[i]["Action"];
                        var specificationString = specification.ToString();
                        specificationString = specificationString.Replace("[Plantain]", "[PlantainForage]");
                        specificationString = specificationString.Replace(".Plantain.", ".PlantainForage.");
                        operation[i]["Action"] = specificationString;
                    }
                }
            }

            // change all references to the model in any experiment.factor
            foreach (var factor in JsonUtilities.ChildrenOfType(root, "Factor"))
            {
                var specification = factor["Specification"];
                if (specification != null)
                {
                    var specificationString = specification.ToString();
                    specificationString = specificationString.Replace("[Plantain]", "[PlantainForage]");
                    specificationString = specificationString.Replace(".Plantain.", ".PlantainForage.");
                    factor["Specification"] = specificationString;
                }
            }

            // change all references to the model in any experiment.compositefactor
            foreach (var factor in JsonUtilities.ChildrenOfType(root, "CompositeFactor"))
            {
                var specifications = factor["Specifications"];
                if (specifications != null)
                {
                    for (int i = 0; i < specifications.Count(); i++)
                    {
                        var specificationString = specifications[i].ToString();
                        specificationString = specificationString.Replace("[Plantain]", "[PlantainForage]");
                        specificationString = specificationString.Replace(".Plantain.", ".PlantainForage.");
                        specifications[i] = specificationString;
                    }
                }
            }
        }

        /// <summary>
        /// Change [Root].LayerMidPointDepth to [Physical].LayerMidPointDepth
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">File name.</param>
        private static void UpgradeToVersion158(JObject root, string fileName)
        {
            //Fix variable references
            foreach (JObject varref in JsonUtilities.ChildrenOfType(root, "VariableReference"))
            {
                if (varref["VariableName"].ToString() == "[Root].LayerMidPointDepth")
                    varref["VariableName"] = "[Physical].DepthMidPoints";
            }
        }

        /// <summary>
        /// Changes to some arbitrator structures and types to tidy up and make new arbitration approach possible.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion159(JObject root, string fileName)
        {
            foreach (JObject demand in JsonUtilities.ChildrenRecursively(root, "BiomassDemandAndPriority"))
            {
                demand["$type"] = "Models.PMF.NutrientDemandFunctions, Models";
            }
            foreach (JObject demand in JsonUtilities.ChildrenRecursively(root, "BiomassDemand"))
            {
                demand["$type"] = "Models.PMF.NutrientPoolFunctions, Models";
            }
            foreach (JObject demand in JsonUtilities.ChildrenRecursively(root, "EnergyBalance"))
            {
                demand["$type"] = "Models.PMF.EnergyBalance, Models";
            }

            foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
            {
                JsonUtilities.ReplaceManagerCode(manager, "BiomassDemand", "NutrientPoolFunctions");
                JsonUtilities.ReplaceManagerCode(manager, "BiomassDemandAndPriority", "NutrientDemandFunctions");
                JsonUtilities.ReplaceManagerCode(manager, "Reallocation", "ReAllocation");
                JsonUtilities.ReplaceManagerCode(manager, "Retranslocation", "ReTranslocation");
            }

        }

        /// <summary>
        /// Changes to some arbitrator structures and types to tidy up and make new arbitration approach possible.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion160(JObject root, string fileName)
        {
            foreach (JObject demand in JsonUtilities.ChildrenRecursively(root, "ThreeHourSin"))
            {
                demand["$type"] = "Models.Functions.ThreeHourAirTemperature, Models";
            }
            foreach (JObject demand in JsonUtilities.ChildrenRecursively(root, "HourlyInterpolation"))
            {
                demand["$type"] = "Models.Functions.SubDailyInterpolation, Models";
            }
        }

        /// <summary>
        /// Change SimpleLeaf.Tallness to Height
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion161(JObject root, string fileName)
        {
            foreach (JObject leaf in JsonUtilities.ChildrenRecursively(root, "SimpleLeaf"))
            {
                JObject tallness = JsonUtilities.ChildWithName(leaf, "Tallness");
                if (tallness != null)
                    tallness["Name"] = "HeightFunction";
            }

            // Remove tallness from manager scripts.
            foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
            {
                JsonUtilities.ReplaceManagerCode(manager, ".Tallness", ".HeightFunction");
            }


            // Remove tallness from report variables.
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Tallness", ".HeightFunction");
            }

            // Remove tallness from cultivars.
            foreach (JObject cultivar in JsonUtilities.ChildrenRecursively(root, "Cultivar"))
            {
                if (!cultivar["Command"].HasValues)
                    continue;

                foreach (JValue command in cultivar["Command"].Children())
                    command.Value = command.Value.ToString().Replace("[Leaf].Tallness", "[Leaf].HeightFunction");
            }
        }

        /// <summary>
        /// Move SetEmergenceDate and SetGerminationDate to Phenology.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion162(JObject root, string fileName)
        {
            // Move SetEmergenceDate and SetGerminationDat in manager scripts.
            foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
            {
                JsonUtilities.ReplaceManagerCode(manager, ".SetEmergenceDate", ".Phenology.SetEmergenceDate");
                JsonUtilities.ReplaceManagerCode(manager, ".SetGerminationDate", ".Phenology.SetGerminationDate");
            }

            // Move SetEmergenceDate and SetGerminationDate in operations.
            foreach (JObject operations in JsonUtilities.ChildrenRecursively(root, "Operations"))
            {
                var operation = operations["Operation"];
                if (operation != null && operation.HasValues)
                {
                    for (int i = 0; i < operation.Count(); i++)
                    {
                        var specification = operation[i]["Action"];
                        var specificationString = specification.ToString();
                        specificationString = specificationString.Replace(".SetEmergenceDate", ".Phenology.SetEmergenceDate");
                        specificationString = specificationString.Replace(".SetGerminationDate", ".Phenology.SetGerminationDate");
                        operation[i]["Action"] = specificationString;
                    }
                }
            }
        }

        /// <summary>
        /// Rearrange the BiomassRemoval defaults in the plant models and manager scripts.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion163(JObject root, string fileName)
        {
            foreach (JObject biomassRemoval in JsonUtilities.ChildrenRecursively(root, "BiomassRemoval"))
            {
                // Find a harvest OrganBiomassRemovalType child
                JObject harvest = JsonUtilities.ChildWithName(biomassRemoval, "Harvest");
                if (harvest != null)
                {
                    biomassRemoval["HarvestFractionLiveToRemove"] = harvest["FractionLiveToRemove"];
                    biomassRemoval["HarvestFractionDeadToRemove"] = harvest["FractionDeadToRemove"];
                    biomassRemoval["HarvestFractionLiveToResidue"] = harvest["FractionLiveToResidue"];
                    biomassRemoval["HarvestFractionDeadToResidue"] = harvest["FractionDeadToResidue"];
                }
                biomassRemoval["Children"] = new JArray();
            }
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root)
                                                              .Where(man => !man.IsEmpty))
            {
                string managerName = manager.Name;

                // Remove the 'RemoveFractions' declaration
                string declarationPattern = @$".+RemovalFractions\s+(\w+).+";
                Match declarationMatch = Regex.Match(manager.ToString(), declarationPattern);
                if (declarationMatch.Success)
                {
                    // Remove the declaration
                    string declarationInstanceName = declarationMatch.Groups[1].Value;
                    manager.ReplaceRegex(declarationPattern, string.Empty);

                    // Remove the 'RemovalFractions' instance creation.
                    manager.ReplaceRegex(@$"{declarationInstanceName}\W*new.+;", string.Empty);

                    // Find all biomass removal fractions.
                    var matches = manager.FindRegexMatches(@$" +{declarationInstanceName}.SetFractionTo(\w+)\(""(\w+)""\s*,\s*([\w\d.,\(\)+\-*]+)(?:\s*,\s*""(\w+)"")*\);[\s\r]*\n")
                                         .Where(man => !manager.PositionIsCommented(man.Index));
                    List<OrganFractions> organs = new List<OrganFractions>();

                    foreach (Match match in matches)
                    {
                        if (!manager.PositionIsCommented(match.Index))
                        {
                            bool remove = match.Groups[1].Value == "Remove";
                            string organName = match.Groups[2].Value;
                            string fractionObjectName = match.Groups[3].Value;
                            bool isLive = true;
                            if (match.Groups[5].Value == "Dead")
                                isLive = false;
                            var organ = organs.Find(o => o.Name == organName);
                            if (organ == null)
                            {
                                organ = new OrganFractions(organName);
                                organs.Add(organ);
                            }
                            if (isLive)
                            {
                                if (remove)
                                    organ.FractionLiveToRemove = fractionObjectName;
                                else
                                    organ.FractionLiveToResidue = fractionObjectName;
                            }
                            else
                            {
                                if (remove)
                                    organ.FractionDeadToRemove = fractionObjectName;
                                else
                                    organ.FractionDeadToResidue = fractionObjectName;
                            }
                        }
                    }

                    string code = manager.ToString();

                    // Calculate the level of indentation based on the first match.
                    int indent = 0;
                    if (matches.Any())
                    {
                        int pos = matches.First().Index;
                        indent = code.IndexOf(code.Substring(pos).First(ch => ch != ' '), pos) - pos;
                    }

                    // Delete the removal fraction matches lines.
                    // Do it in reverse order so that match.Index remains valid.
                    foreach (Match match in matches.Reverse())
                        code = code.Remove(match.Index, match.Length);

                    // Find the RemoveBiomass method call.
                    Match removeBiomassMatch = Regex.Match(code, @" +(\w+).RemoveBiomass\(.+\);");
                    if (removeBiomassMatch.Success)
                    {
                        var modelName = removeBiomassMatch.Groups[1].Value;

                        // Add in code to get each organ
                        string codeToInsert = null;
                        foreach (var organ in organs)
                        {
                            codeToInsert += new string(' ', indent);
                            codeToInsert += $"var {organ.Name} = {modelName}.FindChild<IHasDamageableBiomass>(\"{organ.Name}\");" + Environment.NewLine;
                        }

                        // Add in code to remove biomass from organ.
                        foreach (var organ in organs)
                        {
                            codeToInsert += new string(' ', indent);
                            codeToInsert += $"{organ.Name}.RemoveBiomass(liveToRemove: {organ.FractionLiveToRemove}, deadToRemove: {organ.FractionDeadToRemove}, " +
                                                                        $"liveToResidue: {organ.FractionLiveToResidue}, deadToResidue: {organ.FractionDeadToResidue});" + Environment.NewLine;
                        }

                        // Remove unwanted code and replace with new code.
                        code = code.Remove(removeBiomassMatch.Index, removeBiomassMatch.Length);
                        if (codeToInsert != null)
                            code = code.Insert(removeBiomassMatch.Index, codeToInsert);

                        // Replace 'SetThinningProportion'.
                        Match thinningMatch = Regex.Match(code, $@" +\w+\.SetThinningProportion\s*=\s*(.+);");
                        if (thinningMatch.Success)
                        {
                            string newThinningCode = new string(' ', indent) +
                                                    $"{modelName}.structure?.DoThin({thinningMatch.Groups[1].Value});";
                            code = code.Remove(thinningMatch.Index, thinningMatch.Length);
                            code = code.Insert(thinningMatch.Index, newThinningCode);
                        }


                        // Replace 'SetPhenologyStage'.
                        Match stageMatch = Regex.Match(code, $@" +\w+\.SetPhenologyStage\s*=\s*(.+);");
                        if (stageMatch.Success)
                        {
                            string newStageCode = new string(' ', indent) +
                                                    $"{modelName}.Phenology?.SetToStage({stageMatch.Groups[1].Value});";
                            code = code.Remove(stageMatch.Index, stageMatch.Length);
                            code = code.Insert(stageMatch.Index, newStageCode);
                        }

                        manager.Read(code);

                        // Add in a using statement.
                        var usings = manager.GetUsingStatements();
                        usings = usings.Append("Models.PMF.Interfaces");
                        manager.SetUsingStatements(usings);
                    }

                    // Save the manager.
                    manager.Save();
                }
            }
        }

        private class OrganFractions
        {
            public OrganFractions(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public string FractionLiveToRemove { get; set; } = "0.0";
            public string FractionDeadToRemove { get; set; } = "0.0";
            public string FractionLiveToResidue { get; set; } = "0.0";
            public string FractionDeadToResidue { get; set; } = "0.0";
        }

        /// <summary>
        /// Change Manger Code from String into Array of Strings (each line is an element)
        /// For better readability of apsim files.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion164(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                string[] code = manager.Token["Code"].ToString().Split('\n');
                manager.Token["CodeArray"] = new JArray(code);
                manager.Save();
            }
        }

        /// <summary>
        /// Adds a line property to the Operation object. This stores the input that is given,
        /// even if it is not able to be parsed as an Operation
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion165(JObject root, string fileName)
        {
            foreach (JObject operations in JsonUtilities.ChildrenRecursively(root, "Operations"))
            {
                var operation = operations["Operation"];
                if (operation != null && operation.HasValues)
                {
                    for (int i = 0; i < operation.Count(); i++)
                    {
                        bool enabled = false;
                        if (operation[i]["Enabled"] != null)
                            enabled = (bool)operation[i]["Enabled"];

                        string commentChar = enabled ? "" : "//";

                        string dateStr = "";
                        if (enabled)
                            if (operation[i]["Date"] != null)
                                dateStr = DateTime.Parse(operation[i]["Date"].ToString()).ToString("yyyy-MM-dd");

                        operation[i]["Line"] = commentChar + dateStr + " " + operation[i]["Action"];
                    }
                }
            }
        }

        /// <summary>
        /// Change SoilNitrogen to Nutrient
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="_">The name of the apsimx file.</param>
        private static void UpgradeToVersion166(JObject root, string _)
        {
            foreach (var soilNitrogen in JsonUtilities.ChildrenOfType(root, "SoilNitrogen"))
            {
                var parent = JsonUtilities.Parent(soilNitrogen);
                // check for an existing Nutrient node. If it exists, do not add another one.
                JObject parentObject = parent.ToObject<JObject>();
                var existingNutrient = JsonUtilities.ChildrenOfType(parentObject, "Nutrient");
                if (existingNutrient.Count == 0)
                {
                    var nutrient = JsonUtilities.CreateNewChildModel(parent, "Nutrient", "Models.Soils.Nutrients.Nutrient");
                    nutrient["ResourceName"] = "Nutrient";
                }
                soilNitrogen.Remove();
            }

            foreach (var solute in JsonUtilities.ChildrenOfType(root, "SoilNitrogenNH4"))
                solute["$type"] = "Models.Soils.Solute, Models";

            foreach (var solute in JsonUtilities.ChildrenOfType(root, "SoilNitrogenNO3"))
                solute["$type"] = "Models.Soils.Solute, Models";

            foreach (var solute in JsonUtilities.ChildrenOfType(root, "SoilNitrogenUrea"))
                solute["$type"] = "Models.Soils.Solute, Models";


            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                manager.Replace("using Models.Soils;", "using Models.Soils;\r\nusing Models.Soils.Nutrients;");

                bool changeMade = false;

                var declarations = manager.GetDeclarations();
                foreach (var declaration in declarations)
                {
                    if (declaration.TypeName == "SoilNitrogenNO3" || declaration.TypeName == "SoilNitrogenNH4" || declaration.TypeName == "SoilNitrogenUrea")
                    {
                        declaration.TypeName = "Solute";
                        var linkAttributeIndex = declaration.Attributes.IndexOf("[Link]");
                        if (linkAttributeIndex != -1)
                        {
                            declaration.Attributes[linkAttributeIndex] = "[Link(Path=\"[NO3]\")]";
                        }

                        manager.SetDeclarations(declarations);
                        changeMade = true;
                    }
                    else if (declaration.TypeName == "SoilNitrogen")
                    {
                        declaration.TypeName = "Nutrient";
                        manager.SetDeclarations(declarations);
                        changeMade = true;
                    }
                }

                changeMade = manager.Replace(".FindInScope<SoilNitrogen>() as SoilNitrogen;", ".FindInScope<Nutrient>() as Nutrient;") || changeMade;
                changeMade = manager.Replace("SoilNitrogenNO3 SoilNitrogenNO3;", "Solute SoilNitrogenNO3;") || changeMade;


                changeMade = manager.Replace("SoilNitrogen nitrogen;", "Nutrient nitrogen;") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.FOMN", "Nutrient.FOM.N") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.FOMC", "Nutrient.FOM.C") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.HumicN", "Nutrient.Humic.N") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.HumicC", "Nutrient.Humic.C") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.MicrobialN", "Nutrient.Microbial.N") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.MicrobialC", "Nutrient.Microbial.C") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.MineralisedN", "Nutrient.MineralisedN") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.TotalN", "Nutrient.TotalN") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.TotalC", "Nutrient.TotalC") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.mineral_n", "Nutrient.MineralN") || changeMade;
                changeMade = manager.Replace(".mineral_n", ".MineralN") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.Denitrification", "Nutrient.Natm") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.n2o_atm", "Nutrient.N2Oatm") || changeMade;
                changeMade = manager.Replace("SoilNitrogen.dlt_n_min_res", "ResidueDecomposition.MineralisedN") || changeMade;

                if (changeMade)
                {
                    manager.AddDeclaration("Nutrient", "Nutrient", new string[] { "[Link]" });
                    manager.AddDeclaration("CarbonFlow", "ResidueDecomposition", new string[] { "[Link(Path=\"[Nutrient].SurfaceResidue.Decomposition\")]" });
                    manager.Save();
                }
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NO3.kgha", "[NO3].kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NH4.kgha", "[NH4].kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.Urea.kgha", "[Urea].kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].NO3.kgha", "[NO3].kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].NH4.kgha", "[NH4].kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].Urea.kgha", "[Urea].kgha");

                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.FOM.N", ".Nutrient.FOM.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.FOM.C", ".Nutrient.FOM.C");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Humic.N", ".Nutrient.Humic.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Humic.C", ".Nutrient.Humic.C");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Microbial.N", ".Nutrient.Microbial.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Microbial.C", ".Nutrient.Microbial.C");

                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.FOMN", ".Nutrient.FOM.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.FOMC", ".Nutrient.FOM.C");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.HumicN", ".Nutrient.Humic.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.HumicC", ".Nutrient.Humic.C");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.MicrobialN", ".Nutrient.Microbial.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.MicrobialC", ".Nutrient.Microbial.C");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.urea", "[Urea].kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.dlt_n_min_res", ".Nutrient.SurfaceResidue.Decomposition.MineralisedN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.MineralisedN", ".Nutrient.MineralisedN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Denitrification", ".Nutrient.Natm");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.n2o_atm", ".Nutrient.N2Oatm");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.TotalC", ".Nutrient.TotalC");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.TotalN", ".Nutrient.TotalN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.mineral_n", ".Nutrient.MineralN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Nitrification", ".Nutrient.NH4.Nitrification");
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NO3.kgha", "NO3.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NH4.kgha", "NH4.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.Urea.kgha", "Urea.kgha");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NO3.kgha", "NO3.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NH4.kgha", "NH4.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.Urea.kgha", "Urea.kgha");
                }
            }
        }

        /// <summary>
        /// Change SoilNitrogen to Nutrient
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="_">The name of the apsimx file.</param>
        private static void UpgradeToVersion167(JObject root, string _)
        {
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                bool changeMade = manager.Replace("[Nutrient].SurfaceResidue.Decomposition", "[SurfaceOrganicMatter].SurfaceResidue.Decomposition");

                if (changeMade)
                    manager.Save();
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].Nutrient.SurfaceResidue.Decomposition", "[SurfaceOrganicMatter].SurfaceResidue.Decomposition");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Nutrient].SurfaceResidue.Decomposition", "[SurfaceOrganicMatter].SurfaceResidue.Decomposition");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].Nutrient.MineralisedNSurfaceResidue", "[SurfaceOrganicMatter].SurfaceResidue.Decomposition.MineralisedN");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Nutrient].MineralisedNSurfaceResidue", "[SurfaceOrganicMatter].SurfaceResidue.Decomposition.MineralisedN");
            }
        }

        /// <summary>
        /// Change NutrientPool to OrganicPool and CarbonFlow to OrganicFlow
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="_">The name of the apsimx file.</param>
        private static void UpgradeToVersion168(JObject root, string _)
        {
            foreach (var nutrientPool in JsonUtilities.ChildrenOfType(root, "NutrientPool"))
                nutrientPool["$type"] = "Models.Soils.Nutrients.OrganicPool, Models";
            foreach (var carbonFlow in JsonUtilities.ChildrenOfType(root, "CarbonFlow"))
                carbonFlow["$type"] = "Models.Soils.Nutrients.OrganicFlow, Models";

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                bool changeMade = manager.Replace("NutrientPool", "OrganicPool");
                changeMade = manager.Replace("CarbonFlow", "OrganicFlow") || changeMade;
                changeMade = manager.Replace("OrganicPoolFunctions", "NutrientPoolFunctions") || changeMade;

                if (changeMade)
                    manager.Save();
            }
        }

        /// <summary>
        /// Set TopLevel flag in any Rotation managers
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="_">The name of the apsimx file.</param>
        private static void UpgradeToVersion169(JObject root, string _)
        {
            foreach (var rotationManager in JsonUtilities.ChildrenOfType(root, "RotationManager"))
            {
                rotationManager["TopLevel"] = true;
            }
        }

        /// <summary>
        /// Change the namespace for scrum to SimplePlantModels
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion170(JObject root, string fileName)
        {
            foreach (var scrum in JsonUtilities.ChildrenOfType(root, "ScrumCrop"))
            {
                scrum["$type"] = "Models.PMF.SimplePlantModels.ScrumCrop, Models";
            }
            foreach (var strum in JsonUtilities.ChildrenOfType(root, "StrumTree"))
            {
                strum["$type"] = "Models.PMF.SimplePlantModels.StrumTree, Models";
            }
            foreach (var scrumMGMT in JsonUtilities.ChildrenOfType(root, "ScrumManagement"))
            {
                scrumMGMT["$type"] = "Models.PMF.SimplePlantModels.ScrumManagement, Models";
            }

            // scrum name space refs in managers.
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                manager.Replace("Models.PMF.Scrum", "Models.PMF.SimplePlantModels");
                manager.Save();
            }
        }

        /// <summary>
        /// Add minimum germination temperature to GerminatingPhase under Phenology.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion171(JObject root, string fileName)
        {
            foreach (JObject NNP in JsonUtilities.ChildrenRecursively(root, "GerminatingPhase"))
            {
                //check if child already has a MinSoilTemperature
                if (JsonUtilities.ChildWithName(NNP, "MinSoilTemperature") == null)
                {
                    Constant value = new Constant();
                    value.Name = "MinSoilTemperature";
                    value.FixedValue = 0.0;
                    JsonUtilities.AddModel(NNP, value);
                }
            }
        }

		/// <summary>
		/// Changes example met file names in Weather.FileName to conform to new naming.
		/// </summary>
		/// <param name="root"></param>
		/// <param name="fileName"></param>
		private static void UpgradeToVersion172(JObject root, string fileName)
		{
			Dictionary<string, string> newWeatherFileNames = new()
			{
				{"/Examples/WeatherFiles/Dalby.met", "/Examples/WeatherFiles/AU_Dalby.met"},
				{"/Examples/WeatherFiles/Gatton.met", "/Examples/WeatherFiles/AU_Gatton.met"},
				{"/Examples/WeatherFiles/Goond.met", "/Examples/WeatherFiles/AU_Goondiwindi.met"},
				{"/Examples/WeatherFiles/Ingham.met", "/Examples/WeatherFiles/AU_Ingham.met"},
				{"/Examples/WeatherFiles/Kingaroy.met", "/Examples/WeatherFiles/AU_Kingaroy.met"},
				{"/Examples/WeatherFiles/WaggaWagga.met", "/Examples/WeatherFiles/AU_WaggaWagga.met"},
				{"/Examples/WeatherFiles/Curvelo.met", "/Examples/WeatherFiles/BR_Curvelo.met"},
				{"/Examples/WeatherFiles/1000_39425.met", "/Examples/WeatherFiles/KE_Gubatu.met"},
				{"/Examples/WeatherFiles/75_34825.met", "/Examples/WeatherFiles/KE_Kapsotik.met"},
				{"/Examples/WeatherFiles/-1025_34875.met", "/Examples/WeatherFiles/KE_Kinyoro.met"},
				{"/Examples/WeatherFiles/-1375_37985.met", "/Examples/WeatherFiles/KE_Kitui.met"},
				{"/Examples/WeatherFiles/-2500_39425.met", "/Examples/WeatherFiles/KE_Kone.met"},
				{"/Examples/WeatherFiles/-225_36025.met", "/Examples/WeatherFiles/KE_MajiMoto.met"},
				{"/Examples/WeatherFiles/4025_36675.met", "/Examples/WeatherFiles/KE_Sabaret.met"},
				{"/Examples/WeatherFiles/VCS_Ruakura.met", "/Examples/WeatherFiles/NZ_Hamilton.met"},
				{"/Examples/WeatherFiles/lincoln.met", "/Examples/WeatherFiles/NZ_Lincoln"},
				{"/Examples/WeatherFiles/Makoka.met", "/Examples/WeatherFiles/NZ_Makoka.met"},
				{"/Examples/WeatherFiles/Site1003_SEA.met","/Examples/WeatherFiles/NZ_Seddon.met"},
				{"/Examples/WeatherFiles/Popondetta.met", "/Examples/WeatherFiles/PG_Popondetta.met"}
			};

			List<string> splits = new List<string>();
			foreach(var weather in JsonUtilities.ChildrenOfType(root, "Weather"))
			{
				foreach(KeyValuePair<string,string> pair in newWeatherFileNames)
				{
					if(weather["FileName"] != null)
					{
                        string fixedFileNameString = weather["FileName"].ToString();
                        fixedFileNameString = fixedFileNameString.Replace("\\\\", "/");
                        fixedFileNameString = fixedFileNameString.Replace("\\", "/");
                        fixedFileNameString = fixedFileNameString.Replace(pair.Key, pair.Value);
                        weather["FileName"] = fixedFileNameString;
					}
				}
			}
		}

        /// <summary>
        /// Change references to ScriptModel to Script in manager scripts
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion173(JObject root, string fileName)
        {
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                //rename uses of ScriptModel to Script
                bool changeMade = manager.Replace(".ScriptModel as ", ".Script as ", true);
                if (changeMade)
                    manager.Save();
            }
        }

        /// <summary>
        /// Change name based system to id based system in directed graphs
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="_">The name of the apsimx file.</param>
        private static void UpgradeToVersion174(JObject root, string _)
        {
            foreach (var rotationManager in JsonUtilities.ChildrenOfType(root, "RotationManager"))
            {
                //give each node an id
                int id = 0;
                foreach (var node in rotationManager["Nodes"])
                {
                    id += 1;
                    node["ID"] = id;
                    node["$type"] = "APSIM.Shared.Graphing.Node, APSIM.Shared";
                }

                //give each arc an id
                foreach (var arc in rotationManager["Arcs"])
                {
                    id += 1;
                    arc["ID"] = id;
                    arc["$type"] = "APSIM.Shared.Graphing.Arc, APSIM.Shared";

                    //connect up arc source/dest with ids instead of names
                    string sourceName = arc["SourceName"].ToString();
                    int sourceID = 0;
                    foreach (var node in rotationManager["Nodes"])
                        if (node["Name"].ToString() == sourceName)
                            sourceID = (int)node["ID"];

                    string destinationName = arc["DestinationName"].ToString();
                    int destinationID = 0;
                    foreach (var node in rotationManager["Nodes"])
                        if (node["Name"].ToString() == destinationName)
                            destinationID = (int)node["ID"];

                    arc["SourceID"] = sourceID;
                    arc["DestinationID"] = destinationID;
                }
            }
        }

        /// <summary>
        /// Add ResourceName to MicroClimate
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion175(JObject root, string fileName)
        {
            foreach (JObject microClimate in JsonUtilities.ChildrenRecursively(root, "MicroClimate"))
                microClimate["ResourceName"] = "MicroClimate";
        }

        /// <summary>
        /// Rename Wheat Report Variables
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion176(JObject root, string fileName)
        {
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Leaf.AppearedCohortNo", "[Wheat].Leaf.Tips");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Leaf.ExpandedCohortNo", "[Wheat].Leaf.Ligules");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Structure.Height", "[Wheat].Leaf.Height");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Structure.LeafTipsAppeared", "[Wheat].Leaf.Tips");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Structure.FinalLeafNumber", "[Wheat].Leaf.FinalLeafNumber");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Structure.MainStemPopn", "[Wheat].Leaf.MainStemPopulation");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Structure.TotalStemPopn", "[Wheat].Leaf.StemPopulation");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Structure.BranchNumber", "[Wheat].Leaf.StemNumberPerPlant");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Wheat].Structure.Phyllochron", "[Wheat].Phenology.Phyllochron");
            }
            foreach (var graph in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Leaf.AppearedCohortNo", "Wheat.Leaf.Tips");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Leaf.ExpandedCohortNo", "Wheat.Leaf.Ligules");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Structure.Height", "Wheat.Leaf.Height");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Structure.LeafTipsAppeared", "Wheat.Leaf.Tips");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Structure.FinalLeafNumber", "Wheat.Leaf.FinalLeafNumber");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Structure.MainStemPopn", "Wheat.Leaf.MainStemPopulation");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Structure.TotalStemPopn", "Wheat.Leaf.StemPopulation");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Structure.BranchNumber", "Wheat.Leaf.StemNumberPerPlant");
                JsonUtilities.SearchReplaceGraphVariableNames(graph, "Wheat.Structure.Phyllochron", "Wheat.Phenology.Phyllochron");
            }
        }

        /// <summary>
        /// Change BiomassRemovalEvents.PlantToRemoveFrom property from IModel to a string.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion177(JObject root, string fileName)
        {
            foreach (var biomassRemovalEvents in JsonUtilities.ChildrenOfType(root, "BiomassRemovalEvents"))
            {
                var plantToRemoveFromObj = biomassRemovalEvents["PlantToRemoveFrom"];
                if (plantToRemoveFromObj.Any())
                {
                    string plantName = biomassRemovalEvents["PlantToRemoveFrom"]["Name"].ToString();
                    biomassRemovalEvents["PlantToRemoveBiomassFrom"] = plantName;
                }
            }
        }

        /// <summary>
        /// Adds a NitrificationInhibition model to CERESNitrificationModel.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion178(JObject root, string fileName)
        {
            foreach (var rate in JsonUtilities.ChildrenOfType(root, "CERESNitrificationModel"))
            {
                JsonUtilities.AddConstantFunctionIfNotExists(rate, "NitrificationInhibition", "1.0");
            }
        }

        private class ForageParameter
        {
            public string LiveDigestibility { get; set; }
            public string DeadDigestibility { get; set; }
            public double LiveFractionConsumable { get; set; }
            public double DeadFractionConsumable { get; set; }
            public double LiveMinimumAmount { get; set;}
            public double DeadMinimumAmount { get; set; }
        }

        /// <summary>
        /// Rearrange the forage parameters.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion179(JObject root, string fileName)
        {
            foreach (var forage in JsonUtilities.ChildrenOfType(root, "Forages"))
            {
                Dictionary<string, ForageParameter> parameters = new();
                JArray oldForageParameters = forage["Parameters"] as JArray;
                if (oldForageParameters != null)
                {
                    foreach (JObject forageParameters in oldForageParameters)
                    {
                        // get values of existing parameters.
                        string name = forageParameters["Name"].Value<string>();
                        bool isLive = forageParameters["IsLive"].Value<bool>();
                        string digestibilityString = forageParameters["DigestibilityString"].Value<string>();
                        double fractionConsumable = forageParameters["FractionConsumable"].Value<double>();
                        double minimumAmount = forageParameters["MinimumAmount"].Value<double>();
                        bool useDigestibilityFromModel = forageParameters["UseDigestibilityFromModel"].Value<bool>();
                        if (useDigestibilityFromModel)
                            digestibilityString = "FromModel";

                        if (!string.IsNullOrEmpty(name))
                        {
                            // remove old parameters.
                            forageParameters.Remove("IsLive");
                            forageParameters.Remove("DigestibilityString");
                            forageParameters.Remove("FractionConsumable");
                            forageParameters.Remove("MinimumAmount");
                            forageParameters.Remove("UseDigestibilityFromModel");

                            // store parameters in dictionary.
                            if (!parameters.TryGetValue(name, out var value))
                            {
                                parameters.Add(name, new());
                                value = parameters[name];
                            }
                            if (isLive)
                            {
                                value.LiveDigestibility = digestibilityString;
                                value.LiveFractionConsumable = fractionConsumable;
                                value.LiveMinimumAmount = minimumAmount;
                            }
                            else
                            {
                                value.DeadDigestibility = digestibilityString;
                                value.DeadFractionConsumable = fractionConsumable;
                                value.DeadMinimumAmount = minimumAmount;
                            }
                        }
                    }

                    // Write all parameters to JSON
                    JArray parametersArray = new();
                    foreach (var parameter in parameters)
                    {
                        parametersArray.Add(new JObject()
                        {
                            ["Name"] = parameter.Key,
                            ["LiveDigestibility"] = parameter.Value.LiveDigestibility,
                            ["DeadDigestibility"] = parameter.Value.DeadDigestibility,
                            ["LiveFractionConsumable"] = parameter.Value.LiveFractionConsumable,
                            ["DeadFractionConsumable"] = parameter.Value.DeadFractionConsumable,
                            ["LiveMinimumBiomass"] = parameter.Value.LiveMinimumAmount,
                            ["DeadMinimumBiomass"] = parameter.Value.DeadMinimumAmount,
                        });
                    }
                    forage["Parameters"] = parametersArray;
                }
            }

            // Convert TreeProxy parameters.
            foreach (var treeProxy in JsonUtilities.ChildrenOfType(root, "TreeProxy"))
            {
                JArray tables = treeProxy["Table"] as JArray;

                JArray parameters = new();

                // add shade
                CreateTreeProxyParameterObj(parameters, "Shade (%)", tables.Skip(2).Select(table => (table as JArray)[0].Value<string>()).ToArray());

                // add two documentation lines.
                //CreateTreeProxyParameterObj(parameters, "Root Length Density (cm/cm3)", Enumerable.Repeat(string.Empty, 10).ToArray());
                //CreateTreeProxyParameterObj(parameters, "Depth (cm)", Enumerable.Repeat(string.Empty, 10).ToArray());

                // add depths
                var depths = (tables[1] as JArray).Skip(3).Select(i => i).ToArray();
                for (int i = 0; i < depths.Length; i++)
                {
                    string depth = depths[i].Value<string>();
                    string parameterName;
                    if (i == 0)
                        parameterName = $"Root Length Density (cm/cm3): {depth}cm";
                    else
                        parameterName = $"{depth}cm";
                    CreateTreeProxyParameterObj(parameters, parameterName, tables.Skip(2).Select(table => (table as JArray)[i + 3].Value<string>()).ToArray());
                }

                JObject spatial = new();
                treeProxy["Spatial"] = spatial;
                spatial["Parameters"] = parameters;
            }
        }

        /// <summary>
        /// Create a TreeProxy parameter instance.
        /// </summary>
        /// <param name="parameters">The JSON array to add the instance to.</param>
        /// <param name="name">The name of the parameter to add.</param>
        /// <param name="values">The values of the parameters.</param>
        private static void CreateTreeProxyParameterObj(JArray parameters, string name, string[] values)
        {
            parameters.Add(new JObject()
            {
                ["Name"] = name,
                ["THCutOff0"] = values[0],
                ["THCutOff05"] = values[1],
                ["THCutOff1"] = values[2],
                ["THCutOff15"] = values[3],
                ["THCutOff2"] = values[4],
                ["THCutOff25"] = values[5],
                ["THCutOff3"] = values[6],
                ["THCutOff4"] = values[7],
                ["THCutOff5"] = values[8],
                ["THCutOff6"] = values[9],
            });
        }

        /// <summary>
        /// Renames the Operation property of Operations to OperationsList to avoid name conficts with the Operation class
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion180(JObject root, string fileName)
        {
            foreach (JObject operations in JsonUtilities.ChildrenRecursively(root, "Operations"))
            {
                operations["OperationsList"] = operations["Operation"];
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                //rename uses of ScriptModel to Script
                bool changeMade = manager.Replace(".Operation.", ".OperationsList.", true);
                if (changeMade)
                    manager.Save();
            }
        }

        /// <summary>
        /// Renames Models.PMF.Organs.Leaf+LeafCohortParameters to Models.PMF.Organs.LeafCohortParameters.
        /// LeafCohortParameters class was moved from the Leaf.cs to LeafCohortParameters.cs.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion181(JObject root, string fileName)
        {
            foreach (JObject leafCohortParametersObject in JsonUtilities.ChildrenRecursively(root, "Models.PMF.Organs.Leaf+LeafCohortParameters"))
                leafCohortParametersObject["$type"] = leafCohortParametersObject["$type"].ToString().Replace("Models.PMF.Organs.Leaf+LeafCohortParameters", "Models.PMF.Organs.LeafCohortParameters");
        }

        /// <summary>
        /// Renames Models.PMF.Organs.Leaf+LeafCohortParameters to Models.PMF.Organs.LeafCohortParameters.
        /// LeafCohortParameters class was moved from the Leaf.cs to LeafCohortParameters.cs.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion182(JObject root, string fileName)
        {
            foreach (JToken water in JsonUtilities.ChildrenRecursively(root, "Water"))
            {
                JToken relTo = water.SelectToken("RelativeTo");
                if (relTo != null)
                {
                    string cropsoil = water["RelativeTo"].ToString();
                    if (cropsoil.EndsWith("Soil"))
                    {
                        cropsoil = cropsoil.Substring(0, cropsoil.Length-4);
                        water["RelativeTo"] = cropsoil;
                    }
                }
            }
        }

        /// <summary>
        /// Reparents graphs incorrectly placed under a Simulation under an Experiment
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion183(JObject root, string fileName)
        {
            foreach (JObject graph in JsonUtilities.ChildrenRecursively(root, "Graph"))
            {
                var graphParent = JsonUtilities.Parent(graph);
                if(JsonUtilities.Type(graphParent) == "Simulation")
                {
                    var simParent = JsonUtilities.Parent(graphParent);
                    if(JsonUtilities.Type(simParent) == "Experiment")
                    {
                        JsonUtilities.RemoveChild((JObject)graphParent, graph["Name"].ToString());
                        var experimentChildren = (simParent as JObject).Children();

                        bool duplicateGraphExists = false;
                        var experiment = FileFormat.ReadFromString<Experiment>(simParent.ToString(), e => throw e, false).NewModel as Experiment;
                        foreach(IModel child in experiment.Children)
                        {
                            // TODO: Needs to not add a graph to an experiment if another object
                            // has the same name. Slurp has an existing irrigation graph (that doesn't work)
                            // that causes issues.
                            if (child.Name.Equals(graph["Name"].ToString()))
                                duplicateGraphExists = true;
                        }

                        if (duplicateGraphExists == false)
                            JsonUtilities.AddChild((JObject)simParent, graph);
                    }
                }
            }
        }

        /// <summary>
        /// Replace old CERES soil temperature model with new one.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion184(JObject root, string fileName)
        {
            foreach (JObject soil in JsonUtilities.ChildrenRecursively(root, "Soil"))
            {
                // Remove all ceres soil temperature models.
                string name = null;
                var soilChildren = soil["Children"] as JArray;
                foreach (var ceresChild in JsonUtilities.ChildrenOfType(soil, "CERESSoilTemperature"))
                {
                    if (name == null)
                        name = ceresChild["Name"].ToString();
                    soilChildren.Remove(ceresChild);
                }

                // Add new soil temperature model if necessary
                if (JsonUtilities.ChildrenOfType(soil, "SoilTemperature").Count == 0)
                {
                    // Need to make sure NutrientPatchManger is last child.
                    // Try and get index of NutrientPatchManger
                    int i;
                    for (i = 0; i < soilChildren.Count; i++)
                    {
                        if (soilChildren[i]["$type"].ToString().Contains(".NutrientPatchManager"))
                            break;
                    }

                    soilChildren.Insert(i, new JObject()
                    {
                        ["$type"] = "Models.Soils.SoilTemp.SoilTemperature, Models",
                        ["Name"] = name ?? "Temperature"
                    });
                }
            }
        }
    }

}

