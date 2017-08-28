﻿// -----------------------------------------------------------------------
// <copyright file="Converter.cs" company="CSIRO">
// TODO: Update copyright text.
// </copyright>
// -----------------------------------------------------------------------

namespace Models.Core
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
    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public class APSIMFileConverter
    {
        /// <summary>Gets the lastest .apsimx file format version.</summary>
        public static int LastestVersion { get { return 11; } }

        /// <summary>Converts to file to the latest version.</summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>Returns true if something was changed.</returns>
        public static bool ConvertToLatestVersion(string fileName)
        {
            // Load the file.
            XmlDocument doc = new XmlDocument();
            doc.Load(fileName);

            // Apply converter.
            bool changed = ConvertToLatestVersion(doc.DocumentElement);

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
        /// <returns>Returns true if something was changed.</returns>
        public static bool ConvertToLatestVersion(XmlNode rootNode)
        {
            string fileVersionString = XmlUtilities.Attribute(rootNode, "Version");
            int fileVersion = 0;
            if (fileVersionString != string.Empty)
                fileVersion = Convert.ToInt32(fileVersionString);

            // Update the xml if not at the latest version.
            bool changed = false;
            while (fileVersion < LastestVersion)
            {
                changed = true;

                // Find the method to call to upgrade the file by one version.
                int toVersion = fileVersion + 1;
                MethodInfo method = typeof(APSIMFileConverter).GetMethod("UpgradeToVersion" + toVersion, BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                    throw new Exception("Cannot find converter to go to version " + toVersion);

                // Found converter method so call it.
                method.Invoke(null, new object[] { rootNode });

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
        private static void UpgradeToVersion1(XmlNode node)
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
        private static void UpgradeToVersion2(XmlNode node)
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
        private static void UpgradeToVersion3(XmlNode node)
        {
            foreach (XmlNode zoneNode in XmlUtilities.FindAllRecursivelyByType(node, "Zone"))
            {
                string areaString = XmlUtilities.Value(zoneNode, "Area");

                try
                {
                    double area = Convert.ToDouble(areaString);
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
        private static void UpgradeToVersion4(XmlNode node)
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
        private static void UpgradeToVersion5(XmlNode node)
        {
            foreach (XmlNode soilNode in XmlUtilities.FindAllRecursivelyByType(node, "Soil"))
                XmlUtilities.EnsureNodeExists(soilNode, "CERESSoilTemperature");
        }

        /// <summary>
        /// Upgrades to version 6. Make sure all KLModifier, KNO3, KNH4 nodes have value
        /// XProperty values.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        private static void UpgradeToVersion6(XmlNode node)
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
        private static void UpgradeToVersion7(XmlNode node)
        {
            foreach (XmlNode manager in XmlUtilities.FindAllRecursivelyByType(node, "manager"))
                APSIMFileConverterUtilities.SearchReplaceManagerCodeUsingRegEx(manager, @"([\[\]\.\w]+\.ESW)", "MathUtilities.Sum($1)", "using APSIM.Shared.Utilities;");
            foreach (XmlNode report in XmlUtilities.FindAllRecursivelyByType(node, "report"))
                APSIMFileConverterUtilities.SearchReplaceReportCode(report, @"([\[\]\.\w]+\.ESW)", "sum($1)");
        }

        /// <summary>
        /// Upgrades to version 8. Create ApexStandard node.
        /// </summary>
        /// <param name="node">The node to upgrade.</param>
        private static void UpgradeToVersion8(XmlNode node)
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
        private static void UpgradeToVersion9(XmlNode node)
        {
            foreach (XmlNode root in XmlUtilities.FindAllRecursivelyByType(node, "Root"))
            {
                XmlNode partitionFraction = APSIMFileConverterUtilities.FindPMFNode(root, "PartitionFraction");
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
        private static void UpgradeToVersion10(XmlNode node)
        {
            List<XmlNode> organs = XmlUtilities.FindAllRecursivelyByType(node, "GenericOrgan");
            organs.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "SimpleLeaf"));
            organs.AddRange(XmlUtilities.FindAllRecursivelyByType(node, "Nodule"));

            foreach (XmlNode organ in organs)
            {
                APSIMFileConverterUtilities.AddConstantFuntionIfNotExists(organ, "NRetranslocationFactor", "0.0");
                APSIMFileConverterUtilities.AddConstantFuntionIfNotExists(organ, "NitrogenDemandSwitch", "1.0");
                APSIMFileConverterUtilities.AddConstantFuntionIfNotExists(organ, "DMReallocationFactor", "0.0");
                APSIMFileConverterUtilities.AddConstantFuntionIfNotExists(organ, "DMRetranslocationFactor", "0.0");
                APSIMFileConverterUtilities.AddVariableReferenceFuntionIfNotExists(organ, "CriticalNConc", "[" + organ.FirstChild.InnerText + "].MinimumNConc.Value()");
            }
        }

        /// <summary>
        /// Rename NonStructural to Storage in Biomass organs
        /// </summary>
        /// <param name="node">The node to modifiy</param>
        private static void UpgradeToVersion11(XmlNode node)
        {
            APSIMFileConverterUtilities.RenameVariable(node, ".NonStructural", ".Storage");
            APSIMFileConverterUtilities.RenameVariable(node, ".NonStructuralDemand", ".StorageDemand");
            APSIMFileConverterUtilities.RenameVariable(node, ".TotalNonStructuralDemand", ".TotalStorageDemand");
            APSIMFileConverterUtilities.RenameVariable(node, ".NonStructuralAllocation", ".StorageAllocation");
            APSIMFileConverterUtilities.RenameVariable(node, ".NonStructuralFraction", ".StorageFraction");
            APSIMFileConverterUtilities.RenameVariable(node, ".NonStructuralWt", ".StorageWt");
            APSIMFileConverterUtilities.RenameVariable(node, ".NonStructuralN", ".StorageN");
            APSIMFileConverterUtilities.RenameVariable(node, ".NonStructuralNConc", ".StorageNConc");
            APSIMFileConverterUtilities.RenameVariable(node, "NonStructuralFraction", "StorageFraction");
            APSIMFileConverterUtilities.RenameVariable(node, "LeafStartNonStructuralNReallocationSupply", "LeafStartStorageFractionNReallocationSupply");
            APSIMFileConverterUtilities.RenameVariable(node, "LeafStartNonStructuralNRetranslocationSupply", "LeafStartStorageNRetranslocationSupply");
            APSIMFileConverterUtilities.RenameVariable(node, "LeafStartNonStructuralDMReallocationSupply", "LeafStartStorageDMReallocationSupply");
            APSIMFileConverterUtilities.RenameVariable(node, "NonStructuralDMDemand", "StorageDMDemand");
            APSIMFileConverterUtilities.RenameVariable(node, "NonStructuralNDemand", "StorageNDemand");

            // renames
            APSIMFileConverterUtilities.RenameNode(node, "NonStructuralNReallocated", "StorageNReallocated");
            APSIMFileConverterUtilities.RenameNode(node, "NonStructuralWtReallocated", "StorageWtReallocated");
            APSIMFileConverterUtilities.RenameNode(node, "NonStructuralNRetrasnlocated", "StorageNRetrasnlocated");
        }
    }
}
