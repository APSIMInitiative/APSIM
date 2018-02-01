﻿// -----------------------------------------------------------------------
// <copyright file="Converter.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
// -----------------------------------------------------------------------
namespace Models.Core.ApsimFile
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Xml;
    using APSIM.Shared.Utilities;
    using System.Reflection;
    using System.IO;
    using System.Text.RegularExpressions;
    using PMF;
    using System.Data;

    /// <summary>
    /// Converts the .apsim file from one version to the next
    /// </summary>
    public class Converter
    {
        /// <summary>Gets the lastest .apsimx file format version.</summary>
        public static int LastestVersion { get { return 24; } }

        /// <summary>Converts to file to the latest version.</summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>Returns true if something was changed.</returns>
        public static bool ConvertToLatestVersion(string fileName)
        {
            // Load the file.
            XmlDocument doc = new XmlDocument();
            doc.Load(fileName);

            // Apply converter.
            bool changed = ConvertToLatestVersion(doc.DocumentElement, fileName);

            if (changed)
            {
                // Make a backup or original file.
                string bakFileName = Path.ChangeExtension(fileName, ".bak");
                if (!File.Exists(bakFileName))
                    File.Copy(fileName, bakFileName);

                // Save file.
                doc.Save(fileName);
            }
            return changed;
        }

        /// <summary>Converts XML to the latest version.</summary>
        /// <param name="rootNode">The root node.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        /// <returns>Returns true if something was changed.</returns>
        public static bool ConvertToLatestVersion(XmlNode rootNode, string fileName)
        {
            return ConvertToVersion(rootNode, fileName, LastestVersion);
        }

        /// <summary>Converts XML to the latest version.</summary>
        /// <param name="rootNode">The root node.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        /// <param name="toVersion">Version number to convert to</param>
        /// <returns>Returns true if something was changed.</returns>
        public static bool ConvertToVersion(XmlNode rootNode, string fileName, int toVersion)
        {
            string fileVersionString = XmlUtilities.Attribute(rootNode, "Version");
            int fileVersion = 0;
            if (fileVersionString != string.Empty)
                fileVersion = Convert.ToInt32(fileVersionString);

            // Update the xml if not at the latest version.
            bool changed = false;
            while (fileVersion < toVersion)
            {
                changed = true;

                // Find the method to call to upgrade the file by one version.
                int versionFunction = fileVersion + 1;
                MethodInfo method = typeof(Converter).GetMethod("UpgradeToVersion" + versionFunction, BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                    throw new Exception("Cannot find converter to go to version " + versionFunction);

                // Found converter method so call it.
                method.Invoke(null, new object[] { rootNode, fileName });

                fileVersion++;
            }

            if (changed)
                XmlUtilities.SetAttribute(rootNode, "Version", fileVersion.ToString());
            return changed;
        }

        /// <summary>Upgrades to version 1.</summary>
        /// <remarks>
        ///    Converts:
        ///     <Series>
        ///        <X>
        ///          <TableName>HarvestReport</TableName>
        ///          <FieldName>Maize.Population</FieldName>
        ///        </X>
        ///        <Y>
        ///          <TableName>HarvestReport</TableName>
        ///          <FieldName>GrainWt</FieldName>
        ///        </Y>
        ///      </Series>
        ///     to:
        ///      <Series>
        ///         <TableName>HarvestReport</TableName>
        ///         <XFieldName>Maize.Population</XFieldName>
        ///         <YFieldName>GrainWt</YFieldName>
        ///      </Series>
        /// </remarks>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion1(XmlNode node, string fileName)
    {
        foreach (XmlNode seriesNode in XmlUtilities.FindAllRecursivelyByType(node, "Series"))
        {
            XmlUtilities.Rename(seriesNode, "Title", "Name");
            XmlUtilities.Move(seriesNode, "X/TableName", seriesNode, "TableName");
            XmlUtilities.Move(seriesNode, "X/FieldName", seriesNode, "XFieldName");
            XmlUtilities.Move(seriesNode, "Y/FieldName", seriesNode, "YFieldName");
            XmlUtilities.Move(seriesNode, "X2/FieldName", seriesNode, "X2FieldName");
            XmlUtilities.Move(seriesNode, "Y2/FieldName", seriesNode, "Y2FieldName");

            bool showRegression = XmlUtilities.Value(seriesNode.ParentNode, "ShowRegressionLine") == "true";
            if (showRegression)
                seriesNode.AppendChild(seriesNode.OwnerDocument.CreateElement("Regression"));

            string seriesType = XmlUtilities.Value(seriesNode, "Type");
            if (seriesType == "Line")
                XmlUtilities.SetValue(seriesNode, "Type", "Scatter");

            XmlUtilities.DeleteValue(seriesNode, "X");
            XmlUtilities.DeleteValue(seriesNode, "Y");

        }
    }

        /// <summary>Upgrades to version 2.</summary>
        /// <remarks>
        ///    Converts:
        ///      <Cultivar>
        ///        <Alias>Cultivar1</Alias>
        ///        <Alias>Cultivar2</Alias>
        ///      </Cultivar>
        ///     to:
        ///      <Cultivar>
        ///        <Alias>
        ///          <Name>Cultivar1</Name>
        ///        </Alias>
        ///        <Alias>
        ///          <Name>Cultivar2</Name>
        ///        </Alias>
        ///      </Cultivar>
        /// </remarks>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion2(XmlNode node, string fileName)
        {
            foreach (XmlNode cultivarNode in XmlUtilities.FindAllRecursivelyByType(node, "Cultivar"))
            {
                List<string> aliases = XmlUtilities.Values(cultivarNode, "Alias");

                // Delete all alias children.
                foreach (XmlNode alias in XmlUtilities.ChildNodes(cultivarNode, "Alias"))
                    alias.ParentNode.RemoveChild(alias);

                foreach (string alias in aliases)
                {
                    XmlNode aliasNode = cultivarNode.AppendChild(cultivarNode.OwnerDocument.CreateElement("Alias"));
                    XmlUtilities.SetValue(aliasNode, "Name", alias);
                }
            }
        }

        /// <summary>Upgrades to version 3. Make sure all area elements are greater than zero.</summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion3(XmlNode node, string fileName)
        {
            foreach (XmlNode zoneNode in XmlUtilities.FindAllRecursivelyByType(node, "Zone"))
            {
                string areaString = XmlUtilities.Value(zoneNode, "Area");

                try
                {
                    double area = Convert.ToDouble(areaString,
                                                   System.Globalization.CultureInfo.InvariantCulture);
                    if (area <= 0)
                        XmlUtilities.SetValue(zoneNode, "Area", "1");
                }
                catch (Exception)
                {
                    XmlUtilities.SetValue(zoneNode, "Area", "1");
                }
            }
        }

        /// <summary>Upgrades to version 4. Make sure all zones have a SoluteManager model.</summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion4(XmlNode node, string fileName)
        {
            foreach (XmlNode zoneNode in XmlUtilities.FindAllRecursivelyByType(node, "Zone"))
                XmlUtilities.EnsureNodeExists(zoneNode, "SoluteManager");
            foreach (XmlNode zoneNode in XmlUtilities.FindAllRecursivelyByType(node, "RectangularZone"))
                XmlUtilities.EnsureNodeExists(zoneNode, "SoluteManager");
            foreach (XmlNode zoneNode in XmlUtilities.FindAllRecursivelyByType(node, "CircularZone"))
                XmlUtilities.EnsureNodeExists(zoneNode, "SoluteManager");
        }

        /// <summary>Upgrades to version 5. Make sure all zones have a CERESSoilTemperature model.</summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion5(XmlNode node, string fileName)
        {
            foreach (XmlNode soilNode in XmlUtilities.FindAllRecursivelyByType(node, "Soil"))
                XmlUtilities.EnsureNodeExists(soilNode, "CERESSoilTemperature");
        }

        /// <summary>
        /// Upgrades to version 6. Make sure all KLModifier, KNO3, KNH4 nodes have value
        /// XProperty values.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion6(XmlNode node, string fileName)
        {
            foreach (XmlNode n in XmlUtilities.FindAllRecursivelyByType(node, "XProperty"))
            {
                if (n.InnerText == "[Root].RootLengthDensity" ||
                    n.InnerText == "[Root].RootLengthDenisty" ||
                    n.InnerText == "[Root].LengthDenisty")
                    n.InnerText = "[Root].LengthDensity";
            }
        }

        /// <summary>
        /// Upgrades to version 7. Find all occurrences of ESW
        /// XProperty values.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion7(XmlNode node, string fileName)
        {
            foreach (XmlNode manager in XmlUtilities.FindAllRecursivelyByType(node, "manager"))
                ConverterUtilities.SearchReplaceManagerCodeUsingRegEx(manager, @"([\[\]\.\w]+\.ESW)", "MathUtilities.Sum($1)", "using APSIM.Shared.Utilities;");
            foreach (XmlNode report in XmlUtilities.FindAllRecursivelyByType(node, "report"))
                ConverterUtilities.SearchReplaceReportCodeUsingRegEx(report, @"([\[\]\.\w]+\.ESW)", "sum($1)");
        }

        /// <summary>
        /// Upgrades to version 8. Create ApexStandard node.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion8(XmlNode node, string fileName)
        {
            XmlNode apex = XmlUtilities.CreateNode(node.OwnerDocument, "ApexStandard", "");
            XmlNode stemSen = XmlUtilities.CreateNode(node.OwnerDocument, "Constant", "");
            XmlElement name = node.OwnerDocument.CreateElement("Name");
            XmlElement element = node.OwnerDocument.CreateElement("FixedValue");
            name.InnerText = "StemSenescenceAge";
            element.InnerText = "0";
            stemSen.AppendChild(name);
            stemSen.AppendChild(element);

            foreach (XmlNode n in XmlUtilities.FindAllRecursivelyByType(node, "Leaf"))
            {
                n.AppendChild(apex);
            }
            foreach (XmlNode n in XmlUtilities.FindAllRecursivelyByType(node, "Structure"))
            {
                n.AppendChild(stemSen);
            }
        }

        /// <summary>
        /// Add a DMDemandFunction constant function to all Root nodes that don't have one
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion9(XmlNode node, string fileName)
        {
            foreach (XmlNode root in XmlUtilities.FindAllRecursivelyByType(node, "Root"))
            {
                XmlNode partitionFraction = ConverterUtilities.FindModelNode(root, "PartitionFraction");
                if (partitionFraction != null)
                {
                    root.RemoveChild(partitionFraction);
                    XmlNode demandFunction = root.AppendChild(root.OwnerDocument.CreateElement("PartitionFractionDemandFunction"));
                    XmlUtilities.SetValue(demandFunction, "Name", "DMDemandFunction");
                    demandFunction.AppendChild(partitionFraction);
                }
            }
        }

        /// <summary>
        /// Add default values for generic organ parameters that were previously optional
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion10(XmlNode node, string fileName)
        {
            List<XmlNode> organs = XmlUtilities.FindAllRecursivelyByType(node, "GenericOrgan");
            organs.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "SimpleLeaf"));
            organs.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Nodule"));

            foreach (XmlNode organ in organs)
            {
                ConverterUtilities.AddConstantFuntionIfNotExists(organ, "NRetranslocationFactor", "0.0");
                ConverterUtilities.AddConstantFuntionIfNotExists(organ, "NitrogenDemandSwitch", "1.0");
                ConverterUtilities.AddConstantFuntionIfNotExists(organ, "DMReallocationFactor", "0.0");
                ConverterUtilities.AddConstantFuntionIfNotExists(organ, "DMRetranslocationFactor", "0.0");
                ConverterUtilities.AddVariableReferenceFuntionIfNotExists(organ, "CriticalNConc", "[" + organ.FirstChild.InnerText + "].MinimumNConc.Value()");
            }
        }

        /// <summary>
        /// Rename NonStructural to Storage in Biomass organs
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion11(XmlNode node, string fileName)
        {
            ConverterUtilities.RenameVariable(node, ".NonStructural", ".Storage");
            ConverterUtilities.RenameVariable(node, ".NonStructuralDemand", ".StorageDemand");
            ConverterUtilities.RenameVariable(node, ".TotalNonStructuralDemand", ".TotalStorageDemand");
            ConverterUtilities.RenameVariable(node, ".NonStructuralAllocation", ".StorageAllocation");
            ConverterUtilities.RenameVariable(node, ".NonStructuralFraction", ".StorageFraction");
            ConverterUtilities.RenameVariable(node, ".NonStructuralWt", ".StorageWt");
            ConverterUtilities.RenameVariable(node, ".NonStructuralN", ".StorageN");
            ConverterUtilities.RenameVariable(node, ".NonStructuralNConc", ".StorageNConc");
            ConverterUtilities.RenameVariable(node, "NonStructuralFraction", "StorageFraction");
            ConverterUtilities.RenameVariable(node, "LeafStartNonStructuralNReallocationSupply", "LeafStartStorageFractionNReallocationSupply");
            ConverterUtilities.RenameVariable(node, "LeafStartNonStructuralNRetranslocationSupply", "LeafStartStorageNRetranslocationSupply");
            ConverterUtilities.RenameVariable(node, "LeafStartNonStructuralDMReallocationSupply", "LeafStartStorageDMReallocationSupply");
            ConverterUtilities.RenameVariable(node, "NonStructuralDMDemand", "StorageDMDemand");
            ConverterUtilities.RenameVariable(node, "NonStructuralNDemand", "StorageNDemand");

            // renames
            ConverterUtilities.RenamePMFFunction(node, "LeafCohortParameters", "NonStructuralFraction", "StorageFraction");
            ConverterUtilities.RenameNode(node, "NonStructuralNReallocated", "StorageNReallocated");
            ConverterUtilities.RenameNode(node, "NonStructuralWtReallocated", "StorageWtReallocated");
            ConverterUtilities.RenameNode(node, "NonStructuralNRetrasnlocated", "StorageNRetrasnlocated");
        }

        /// <summary>
        /// Rename MainStemNodeAppearanceRate to Phyllochron AND 
        ///        MainStemFinalNodeNumber to FinalLeafNumber in Structure
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion12(XmlNode node, string fileName)
        {
            ConverterUtilities.RenamePMFFunction(node, "Structure", "MainStemNodeAppearanceRate", "Phyllochron");
            ConverterUtilities.RenameVariable(node, ".MainStemNodeAppearanceRate", ".Phyllochron");

            ConverterUtilities.RenamePMFFunction(node, "Structure", "MainStemFinalNodeNumber", "FinalLeafNumber");
            ConverterUtilities.RenameVariable(node, ".MainStemFinalNodeNumber", ".FinalLeafNumber");
        }

        /// <summary>
        /// Rename Plant15 to Plant.
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion13(XmlNode node, string fileName)
        {
            ConverterUtilities.RenameNode(node, "Plant15", "Plant");
            ConverterUtilities.RenameVariable(node, "using Models.PMF.OldPlant;", "using Models.PMF;");
            ConverterUtilities.RenameVariable(node, "Plant15", "Plant");

            foreach (XmlNode manager in XmlUtilities.FindAllRecursivelyByType(node, "manager"))
                ConverterUtilities.SearchReplaceManagerCodeUsingRegEx(manager, @"(\w+).plant.status *== *\042out\042", @"!$1.IsAlive", null);  // /042 is a "

            foreach (XmlNode simulationNode in XmlUtilities.FindAllRecursivelyByType(node, "Simulation"))
            {
                List<XmlNode> plantModels = XmlUtilities.FindAllRecursivelyByType(simulationNode, "Plant");
                plantModels.RemoveAll(p => p.Name == "plant"); // remove lowercase plant nodes - these are in sugarcane

                if (plantModels.Count > 0)
                {
                    XmlUtilities.EnsureNodeExists(simulationNode, "SoilArbitrator");
                    XmlUtilities.EnsureNodeExists(plantModels[0].ParentNode, "MicroClimate");
                }
            }
        }

        /// <summary>
        /// Rename the "Simulations", "Messages", "InitialConditions" .db tables to be
        /// prefixed with an underscore.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion14(XmlNode node, string fileName)
        {
            string dbFileName = Path.ChangeExtension(fileName, ".db");
            if (File.Exists(dbFileName))
            {
                SQLite connection = new SQLite();
                connection.OpenDatabase(dbFileName, false);
                try
                {
                    DataTable tableData = connection.ExecuteQuery("SELECT * FROM sqlite_master");
                    foreach (string tableName in DataTableUtilities.GetColumnAsStrings(tableData, "Name"))
                    {
                        if (tableName == "Simulations" || tableName == "Messages" || tableName == "InitialConditions")
                            connection.ExecuteNonQuery("ALTER TABLE " + tableName + " RENAME TO " + "_" + tableName);
                    }
                }
                finally
                {
                    connection.CloseDatabase();
                }
            }
        }

        /// <summary>
        /// Ensure report variables have a square bracket around the first word.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion15(XmlNode node, string fileName)
        {
            List<string> modelNames = ConverterUtilities.GetAllModelNames(node);
            foreach (XmlNode report in XmlUtilities.FindAllRecursivelyByType(node, "report"))
            {
                List<string> variables = XmlUtilities.Values(report, "VariableNames/string");

                for (int i = 0; i < variables.Count; i++)
                {
                    // If the first word (delimited by '.') is a model name then make sure it has
                    // square brackets around it.
                    int indexPeriod = variables[i].IndexOf('.');
                    if (indexPeriod != -1)
                    {
                        string firstWord = variables[i].Substring(0, indexPeriod);

                        if (modelNames.Contains(firstWord) && !firstWord.StartsWith("["))
                            variables[i] = "[" + firstWord + "]" + variables[i].Substring(indexPeriod);
                    }
                }
                XmlUtilities.SetValues(report, "VariableNames/string", variables);
            }
        }

        /// <summary>
        /// Add nodes for new leaf tiller model
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion16(XmlNode node, string fileName)
        {
            foreach (XmlNode n in XmlUtilities.FindAllRecursivelyByType(node, "LeafCohortParameters"))
            {
                XmlNode LagDurationAgeMultiplier = XmlUtilities.CreateNode(node.OwnerDocument, "ArrayFunction", "");
                XmlNode SenescenceDurationAgeMultiplier = XmlUtilities.CreateNode(node.OwnerDocument, "ArrayFunction", "");
                XmlNode LeafSizeAgeMultiplier = XmlUtilities.CreateNode(node.OwnerDocument, "ArrayFunction", "");
                XmlElement name = node.OwnerDocument.CreateElement("Name");
                XmlElement element = node.OwnerDocument.CreateElement("Values");

                name.InnerText = "LagDurationAgeMultiplier";
                element.InnerText = "1 1 1";
                LagDurationAgeMultiplier.AppendChild(name);
                LagDurationAgeMultiplier.AppendChild(element);

                name = node.OwnerDocument.CreateElement("Name");
                name.InnerText = "SenescenceDurationAgeMultiplier";
                element = node.OwnerDocument.CreateElement("Values");
                element.InnerText = "1 1 1";
                SenescenceDurationAgeMultiplier.AppendChild(name);
                SenescenceDurationAgeMultiplier.AppendChild(element);

                name = node.OwnerDocument.CreateElement("Name");
                element = node.OwnerDocument.CreateElement("Values");
                name.InnerText = "LeafSizeAgeMultiplier";
                element.InnerText = "1 1 1 1 1 1 1 1 1 1 1 1";
                LeafSizeAgeMultiplier.AppendChild(name);
                LeafSizeAgeMultiplier.AppendChild(element);

                if (ConverterUtilities.FindModelNode(n, "LagDurationAgeMultiplier") == null)
                    n.AppendChild(LagDurationAgeMultiplier);
                if (ConverterUtilities.FindModelNode(n, "SenescenceDurationAgeMultiplier") == null)
                    n.AppendChild(SenescenceDurationAgeMultiplier);
                if (ConverterUtilities.FindModelNode(n, "LeafSizeAgeMultiplier") == null)
                    n.AppendChild(LeafSizeAgeMultiplier);
            }
        }

        /// <summary>
        /// Rename CohortLive. to Live.
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion17(XmlNode node, string fileName)
        {
            // Rename .CohortLive to .Live in all compositebiomass nodes and report variables.
            foreach (XmlNode biomass in XmlUtilities.FindAllRecursivelyByType(node, "CompositeBiomass"))
            {
                List<string> variables = XmlUtilities.Values(biomass, "Propertys/string");
                for (int i = 0; i < variables.Count; i++)
                {
                    variables[i] = variables[i].Replace(".CohortLive", ".Live");
                    variables[i] = variables[i].Replace(".CohortDead", ".Dead");
                }
                XmlUtilities.SetValues(biomass, "Propertys/string", variables);
            }
            foreach (XmlNode report in XmlUtilities.FindAllRecursivelyByType(node, "report"))
            {
                List<string> variables = XmlUtilities.Values(report, "VariableNames/string");
                for (int i = 0; i < variables.Count; i++)
                {
                    variables[i] = variables[i].Replace(".CohortLive", ".Live");
                    variables[i] = variables[i].Replace(".CohortDead", ".Dead");
                }
                XmlUtilities.SetValues(report, "VariableNames/string", variables);
            }

            // remove all live and dead nodes.
            foreach (XmlNode childToDelete in ConverterUtilities.FindModelNodes(node, "CompositeBiomass", "Live"))
                childToDelete.ParentNode.RemoveChild(childToDelete);
            foreach (XmlNode childToDelete in ConverterUtilities.FindModelNodes(node, "CompositeBiomass", "Dead"))
                childToDelete.ParentNode.RemoveChild(childToDelete);

            foreach (XmlNode childToDelete in ConverterUtilities.FindModelNodes(node, "Biomass", "Live"))
                childToDelete.ParentNode.RemoveChild(childToDelete);
            foreach (XmlNode childToDelete in ConverterUtilities.FindModelNodes(node, "Biomass", "Dead"))
                childToDelete.ParentNode.RemoveChild(childToDelete);

        }

        /// <summary>
        /// Rename CohortLive. to Live.
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion18(XmlNode node, string fileName)
        {
            foreach (XmlNode manager in XmlUtilities.FindAllRecursivelyByType(node, "manager"))
            {
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.dlayer", ".Thickness");
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.Thickness", ".Thickness");
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.LL15", ".LL15");
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.LL15mm", ".LL15mm");
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.DUL", ".DUL");
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.DULmm", ".DULmm");
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.SAT", ".SAT");
                ConverterUtilities.SearchReplaceManagerCode(manager, ".SoilWater.SATmm", ".SATmm");
            }

            foreach (XmlNode report in XmlUtilities.FindAllRecursivelyByType(node, "report"))
            {
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.dlayer", ".Thickness");
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.Thickness", ".Thickness");
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.LL15", ".LL15");
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.LL15mm", ".LL15mm");
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.DUL", ".DUL");
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.DULmm", ".DULmm");
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.SAT", ".SAT");
                ConverterUtilities.SearchReplaceReportCode(report, ".SoilWater.SATmm", ".SATmm");
            }
        }

        /// <summary>
        /// Add DMConversionEfficiency node
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion19(XmlNode node, string fileName)
        {
            //Rename existing DMConversionEfficiencyFunction nodes
            foreach (XmlNode n in XmlUtilities.FindAllRecursivelyByType(node, "Leaf"))
            {
                XmlNode dmFunction = ConverterUtilities.FindModelNode(n, "DMConversionEfficiencyFunction");
                if (dmFunction != null)
                {
                    XmlUtilities.SetValue(dmFunction, "Name", "DMConversionEfficiency");
                }
            }

            List<XmlNode> nodeList = new List<XmlNode>();

            XmlUtilities.FindAllRecursively(node, "DMConversionEfficiencyFunction", ref nodeList);
            foreach (XmlNode n in nodeList)
                ConverterUtilities.RenameNode(n, "DMConversionEfficiencyFunction", "DMConversionEfficiency");

            nodeList.Clear();
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Root"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Leaf"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "GenericOrgan"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "ReproductiveOrgan"));

            foreach (XmlNode n in nodeList)
            {
                XmlNode DMnode;
                DMnode = XmlUtilities.CreateNode(node.OwnerDocument, "Constant", "");
                XmlElement name = node.OwnerDocument.CreateElement("Name");
                XmlElement element = node.OwnerDocument.CreateElement("FixedValue");
                name.InnerText = "DMConversionEfficiency";
                element.InnerText = "1.0";
                DMnode.AppendChild(name);
                DMnode.AppendChild(element);

                if (ConverterUtilities.FindModelNode(n, "DMConversionEfficiency") == null)
                    n.AppendChild(DMnode);
            }
        }

        private static void UpgradeToVersion20(XmlNode node, string filename)
        {
            List<XmlNode> nodeList = new List<XmlNode>(XmlUtilities.FindAllRecursivelyByType(node, "Root"));

            foreach (XmlNode n in nodeList)
            {
                XmlNode MRFnode;
                MRFnode = XmlUtilities.CreateNode(node.OwnerDocument, "Constant", "");
                XmlElement name = node.OwnerDocument.CreateElement("Name");
                XmlElement element = node.OwnerDocument.CreateElement("FixedValue");
                name.InnerText = "MaintenanceRespirationFunction";
                element.InnerText = "1.0";
                MRFnode.AppendChild(name);
                MRFnode.AppendChild(element);

                if (ConverterUtilities.FindModelNode(n, "MaintenanceRespirationFunction") == null)
                    n.AppendChild(MRFnode);
            }
        }

        /// <summary>
        /// Add RemobilisationCost to all organs
        /// </summary>
        /// <param name="node"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion21(XmlNode node, string fileName)
        {
            List<XmlNode> nodeList = new List<XmlNode>();

            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Root"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Leaf"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "GenericOrgan"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "ReproductiveOrgan"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "LeafCohortParameters"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "SimpleLeaf"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Nodule"));

            foreach (XmlNode n in nodeList)
            {
                XmlNode DMnode;
                DMnode = XmlUtilities.CreateNode(node.OwnerDocument, "Constant", "");
                XmlElement name = node.OwnerDocument.CreateElement("Name");
                XmlElement element = node.OwnerDocument.CreateElement("FixedValue");
                name.InnerText = "RemobilisationCost";
                element.InnerText = "0";
                DMnode.AppendChild(name);
                DMnode.AppendChild(element);

                if (ConverterUtilities.FindModelNode(n, "RemobilisationCost") == null)
                    n.AppendChild(DMnode);
            }

        }
        /// <summary>
        /// Upgrades to version 22. Alter MovingAverage Function
        /// XProperty values.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion22(XmlNode node, string fileName)
        {
            string StartStage = "";
            foreach (XmlNode EmergePhase in XmlUtilities.FindAllRecursivelyByType(node, "EmergingPhase"))
            {
                StartStage = XmlUtilities.Value(EmergePhase, "End");
            }
            ConverterUtilities.RenameVariable(node, "InitialValue", "StageToStartMovingAverage");
            foreach (XmlNode MovingAverageFunction in XmlUtilities.FindAllRecursivelyByType(node, "MovingAverageFunction"))
            {
                XmlUtilities.SetValue(MovingAverageFunction, "StageToStartMovingAverage", StartStage);
            }

        }

        /// <summary>
        /// Upgrades to version 23. Add CarbonConcentration property to all organs.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion23(XmlNode node, string fileName)
        {
            List<XmlNode> nodeList = new List<XmlNode>();

            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Root"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Leaf"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "GenericOrgan"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "ReproductiveOrgan"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "LeafCohortParameters"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "SimpleLeaf"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Nodule"));
            nodeList.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "PerennialLeaf"));

            foreach (XmlNode n in nodeList)
            {
                XmlNode DMnode;
                DMnode = XmlUtilities.CreateNode(node.OwnerDocument, "Constant", "");
                XmlElement name = node.OwnerDocument.CreateElement("Name");
                XmlElement element = node.OwnerDocument.CreateElement("FixedValue");
                name.InnerText = "CarbonConcentration";
                element.InnerText = "0.4";
                DMnode.AppendChild(name);
                DMnode.AppendChild(element);

                if (ConverterUtilities.FindModelNode(n, "CarbonConcentration") == null)
                    n.AppendChild(DMnode);
            }

        }

        /// <summary>
        /// Upgrades to version 24. Add second argument to SoluteManager.Add method
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        /// <param name="fileName">The name of the .apsimx file</param>
        private static void UpgradeToVersion24(XmlNode node, string fileName)
        {
            foreach (XmlNode managerNode in XmlUtilities.FindAllRecursivelyByType(node, "manager"))
            {
                ManagerConverter manager = new ManagerConverter();
                manager.Read(managerNode);
                List<MethodCall> methods = manager.FindMethodCalls("SoluteManager", "Add");
                foreach (MethodCall method in methods)
                {
                    if (method.Arguments.Count == 2)
                    {
                        method.Arguments.Insert(1, "SoluteManager.SoluteSetterType.Fertiliser");
                        manager.SetMethodCall(method);
                    }
                }
                manager.Write(managerNode);
            }
                
        }
    }
}
