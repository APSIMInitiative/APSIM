using APSIM.Shared.Documentation.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using APSIM.Documentation.Models;
using APSIM.Interop.Documentation;
using APSIM.Shared.Utilities;
using Models;
using System.Text;
using Models.Core;
using System.Diagnostics;

namespace APSIM.Documentation
{
    class Program
    {
        private static IEnumerable<string> cols = new string[] { "Name", "Documentation files" };
        private static readonly string apsimx = PathUtilities.GetAbsolutePath("%root%", null);
        private static readonly string underReview = Path.Combine(apsimx, "Tests", "UnderReview");
        private static readonly string validation = Path.Combine(apsimx, "Tests", "Validation");
        private static readonly string resources = Path.Combine(apsimx, "Models", "Resources");
        private static readonly string examples = Path.Combine(apsimx, "Examples");

        private const string agpScience = "https://apsimdev.apsim.info/ApsimX/Documents/AgPastureScience.pdf";
        private const string microClimateScience = "https://www.apsim.info/wp-content/uploads/2019/09/Micromet.pdf";
        private const string grazPlan = "https://grazplan.csiro.au/wp-content/uploads/2007/08/TechPaperMay12.pdf";
        private const string swim = "https://apsimdev.apsim.info/ApsimX/Documents/SWIMv21UserManual.pdf";

        private static PdfOptions options = PdfOptions.Default;

        static int Main(string[] args)
        {
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                // Get autodocs config - ie which models to document.
                IEnumerable<IDocumentationTable> tables = new[]
                {
                    GetAutodocsConfig(),
                    GetTutorialsTable(),
                    GetClemTable(),
                };
                StringBuilder html = new StringBuilder();
                html.AppendLine("<html>");
                html.AppendLine("  <head>");
                html.AppendLine("    <meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\">");
                html.AppendLine("    <style>");
                string css = ReflectionUtilities.GetResourceAsString("APSIM.Documentation.index.css");
                css = css.Replace(Environment.NewLine, " ");
                html.AppendLine(css);
                html.AppendLine("    </style>");

                html.AppendLine("  </head>");
                html.AppendLine("  <body>");

                // Create new working directory, into which all docs will be generated.
                string outputPath;
                if (args.Length > 0)
                    outputPath = args[0];
                else
                    outputPath = Path.Combine(Path.GetTempPath(), $"autodocs-{Guid.NewGuid()}");

                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                // Build each table of documents.
                foreach (IDocumentationTable table in tables)
                {
                    // Generate autodocs.
                    table.BuildDocuments(outputPath);

                    // todo - stream
                    string markup = table.BuildHtmlDocument();
                    html.AppendLine(markup);
                }

                // Built index.html file.
                html.AppendLine("  </body>");
                html.AppendLine("</html>");
                string index = Path.Combine(outputPath, "index.html");
                File.WriteAllText(index, html.ToString());

                Console.WriteLine($"Successfully generated files at {outputPath}. Elapsed time: {stopwatch.Elapsed.TotalSeconds} seconds.");
            }
            catch (Exception err)
            {
                Console.Error.WriteLine(err);
                return 1;
            }
            return 0;
        }

        private static IDocumentationTable GetTutorialsTable()
        {
            IEnumerable<IDocumentationRow> rows = new[]
            {
                StandardTutorialRow("Climate Controller", "ClimateController"),
                LifecycleRow(),
                StandardTutorialRow("Manager", "Manager"),
                StandardTutorialRow("Parameter sensitivity (Morris)", "Sensitivity_MorrisMethod"),
                StandardTutorialRow("Parameter sensitivity (SOBOL)", "Sensitivity_SobolMethod"),
                StandardTutorialRow("Parameter sensitivity (Factorial ANOVA)", "Sensitivity_FactorialANOVA"),
                StandardTutorialRow("Predicted/Observed data handling", "PredictedObserved"),
                StandardTutorialRow("Report", "Report")
            };
            return new DocumentationTable("Tutorials", cols, rows);
        }

        private static IDocumentationTable GetClemTable()
        {
            IEnumerable<IDocumentationRow> rows = new[] { ClemDocsRow() };
            return new DocumentationTable("CLEM", cols, rows);
        }

        private static IDocumentationTable GetAutodocsConfig()
        {
            IEnumerable<IDocumentationRow> rows = new[]
            {
                AgPastureDocsRow("AGPRyegrass (AgPasture)", "AGPRyegrass.json", "AgPasture.apsimx", "AgpRyegrass.pdf", true),
                AgPastureDocsRow("AGPWhiteClover (AgPasture)", "AGPWhiteClover.json", "AgPasture.apsimx", "AgpWhiteClover.pdf", false),
                // todo: agroforestry
                StandardPmfPlantRow("Barley"),
                StandardPmfPlantRow("Canola", new ExternalDocument("Video", "https://www.youtube.com/watch?v=kz3w5nOtdqM")),
                StandardPmfPlantRow("Chicory"),
                StandardPmfPlantRow("Chickpea"),
                CustomModelRow("Clock", "Clock"),
                StandardPmfPlantRow("Eucalyptus"),
                StandardPmfPlantRow("FodderBeet"),
                StandardPmfPlantRow("Gliricidia"),
                StandardPmfPlantRow("Maize"),
                ModelWithNoResourceRow("MicroClimate", extraCells: new[] { new DocumentationCell(new ExternalDocument("Science Documentation", microClimateScience)) }),
                StandardPmfPlantRow("Mungbean", new ExternalDocument("Video", "https://www.youtube.com/watch?v=nyDZkT1JTXw")),
                StandardPmfPlantRow("Nutrient"),
                StandardPmfPlantRow("Oats"),
                StandardPmfPlantRow("OilPalm"),
                StandardPmfPlantRow("Peanut"),
                StandardPmfPlantRow("Pinus"),
                StandardPmfPlantRow("PlantainForage"),
                StandardPmfPlantRow("Potato"),
                StandardPmfPlantRow("RedClover"),
                StandardPmfPlantRow("SCRUM"),
                StandardPmfPlantRow("Slurp"),
                ModelWithNoResourceRow("SoilArbitrator"),
                ModelWithNoResourceRow("SoilTemperature"),
                SoilWaterDocs(),
                StandardPmfPlantRow("Sorghum"),
                StandardPmfPlantRow("Soybean"),
                SugarcaneRow(),
                StockRow(),
                ModelWithNoResourceRow("SWIM", extraCells: new[] { new DocumentationCell(new ExternalDocument("SWIM Technical Documentation (1996)", swim)) }),
                StandardPmfPlantRow("Wheat"),
                StandardPmfPlantRow("WhiteClover"),
            };
            return new DocumentationTable($"Model Documentation for version {Simulations.ApsimVersion}", cols, rows);
        }

        private static IDocumentationRow AgPastureDocsRow(string name, string resourceFile, string validationFile, string outFile, bool documentSpeciesTable)
        {
            string speciesFile = Path.Combine(validation, "AgPasture", "SpeciesTable.apsimx");
            IDocumentationFile speciesParams = new DocsFromFile("Species table", speciesFile, "SpeciesTable.pdf", options);
            IDocumentationFile scienceDocs = new ExternalDocument("Science Documentation", agpScience);

            List<IDocumentationFile> files = new List<IDocumentationFile>();
	        files.Add(scienceDocs);
	        if (documentSpeciesTable)
		        files.Add(speciesParams);
            IDocumentationCell detailsCells = new DocumentationCell(files);
            return StandardDocsRow(name, resourceFile, validationFile, outFile, detailsCells.ToEnumerable());
        }

        private static IDocumentationRow LifecycleRow()
        {
            string apsimxFile = Path.Combine(examples, "Tutorials", "Lifecycle", "lifecycle.apsimx");
            string outFile = "lifecycle.pdf";
            return CustomDocsRow("Life cycle", "Tutorial", new[] { apsimxFile }, outFile);
        }

        private static IDocumentationRow StandardTutorialRow(string rowName, string fileName)
        {
            string inputFile = Path.Combine(examples, "Tutorials", $"{fileName}.apsimx");
            IDocumentationFile file = new DocsFromFile("Tutorial", inputFile, $"{fileName}.pdf", options);
            IDocumentationCell cell = new DocumentationCell(file);
            return new DocumentationRow(rowName, cell.ToEnumerable());
        }

        private static IDocumentationRow ClockRow()
        {
            string apsimxFile = Path.Combine(examples, "Tutorials", "clock.apsimx");
            string outFile = "clock.pdf";
            return CustomModelRow("Tutorial", outFile);
        }

        private static IDocumentationRow ClemDocsRow()
        {
            string clem = Path.Combine(examples, "CLEM");
            string croppingFile = Path.Combine(clem, "CLEM_Example_Cropping.apsimx");
            string grazingFile = Path.Combine(clem, "CLEM_Example_Grazing.apsimx");
            IDocumentationFile scienceDocs = new ExternalDocument("Science Documentation", "https://www.apsim.info/clem");
            IDocumentationFile croppingExample = new DocsFromFile("Cropping example", croppingFile, "CLEM_Example_Cropping.pdf", options);
            IDocumentationFile grazingExample = new DocsFromFile("Grazing example", grazingFile, "CLEM_Example_Grazing.pdf", options);
            IEnumerable<IDocumentationCell> cells = new IDocumentationCell[3]
            {
                new DocumentationCell(new[] { scienceDocs }),
                new DocumentationCell(new[] { croppingExample }),
                new DocumentationCell(new[] { grazingExample })
            };
            return new DocumentationRow("CLEM", cells);
        }

        private static IDocumentationRow SugarcaneRow()
        {
            IDocumentationFile cane = new DocsFromModel<Sugarcane>("Sugarcane.pdf", options);
            IDocumentationCell cell = new DocumentationCell(new[] { cane });
            IDocumentationFile paramsFile = new ParamsDocsFromModel<Sugarcane>("Sugarcane-params.pdf", options);
            IDocumentationCell paramsCell = new DocumentationCell(paramsFile);
            return new DocumentationRow("Sugarcane", new[] { cell, paramsCell });
        }

        private static IDocumentationRow StockRow()
        {
            string validationFile = Path.Combine(validation, "Stock", "Stock.apsimx");

            IDocumentationFile paramsDoc = new ParamsDocsFromFile(validationFile, $"Stock-parameters.pdf", options, "[Supplement]");
            IDocumentationCell paramsCell = new DocumentationCell(paramsDoc);

            IDocumentationFile grazPlanDoc = new ExternalDocument("GRAZPLAN Animal Biology Model", grazPlan);
            IDocumentationCell detailsCell = new DocumentationCell(grazPlanDoc);

            return CustomDocsRow("Stock", "Description & validation", validationFile.ToEnumerable(), "Stock.pdf", new [] { paramsCell, detailsCell });
        }

        private static IDocumentationRow SoilWaterDocs()
        {
            // This is almost like StandardPmfModelRow() except that the resource
            // file is WaterBalance.json rather than SoilWater.json.
            string modelName = "SoilWater";
            string model = Path.Combine(resources, "WaterBalance.json");
            string validationFile = Path.Combine(validation, modelName, $"{modelName}.apsimx");
            IEnumerable<string> inputs = new string[2] { model, validationFile };

            IDocumentationFile parameters = new ParamsDocsFromFile(validationFile, $"SoilWater-parameters.pdf", options);
            IDocumentationCell paramsCell = new DocumentationCell(parameters);
            return CustomDocsRow("SoilWater", "Description & validation", inputs, $"{modelName}.pdf", paramsCell.ToEnumerable());
        }

        private static IDocumentationRow ModelWithNoResourceRow(string modelName, bool isUnderReview = false, IEnumerable<IDocumentationCell> extraCells = null)
        {
            Console.WriteLine($"Creating documentation for {modelName}");
            string validationFile;
            string displayName = modelName;
            if (isUnderReview)
            {
                displayName += " (under review)";
                validationFile = Path.Combine(underReview, modelName, $"{modelName}.apsimx");
            }
            else
                validationFile = Path.Combine(validation, modelName, $"{modelName}.apsimx");
            IEnumerable<string> inputs = new string[1] { validationFile };
            
            IDocumentationFile paramsDocs = new ParamsDocsFromFile(validationFile, $"{modelName}-params.pdf", options, path:modelName);
            IDocumentationCell paramsCell = new DocumentationCell(paramsDocs);
            extraCells = extraCells == null ? paramsCell.ToEnumerable() : extraCells.Prepend(paramsCell);
           
            return CustomDocsRow(displayName, "Description & validation", inputs, $"{modelName}.pdf", extraCells);
        }

        private static IDocumentationRow CreateUnderReviewPlantRow(string modelName)
        {
            Console.WriteLine($"Creating documentation for {modelName}");
            string validationFile = Path.Combine(underReview, modelName, $"{modelName}.apsimx");
            string modelPath = $"[Replacements].{modelName}";
            IDocumentationFile file = new DocsFromModelPath(validationFile, modelPath, $"{modelName}.pdf", options, true);
            IDocumentationCell cell = new DocumentationCell(new[] { file });
            IDocumentationFile parameters = new ParamsDocsFromFile(validationFile, $"{modelName}-parameters.pdf", options, modelPath);
            IDocumentationCell paramsCell = new DocumentationCell(parameters);
            return new DocumentationRow(modelName, new[] { cell, paramsCell });
        }

        private static IDocumentationRow StandardPmfPlantRow(string modelName, ExternalDocument extraFile)
        {
            return StandardDocsRow($"{modelName}", $"{modelName}.json", $"{modelName}.apsimx", $"{modelName}.pdf", new IDocumentationCell[] { new DocumentationCell(extraFile) });
        }

        private static IDocumentationRow StandardPmfPlantRow(string modelName, IEnumerable<IDocumentationCell> extraFiles = null)
        {
            return StandardDocsRow($"{modelName}", $"{modelName}.json", $"{modelName}.apsimx", $"{modelName}.pdf", extraFiles);
        }

        private static IDocumentationRow StandardDocsRow(string name, string modelResourceFile, string validationFile, string outFile, IEnumerable<IDocumentationCell> extraCells = null)
        {
            Console.WriteLine($"Creating documentation for {name}");
            string model = Path.Combine(resources, modelResourceFile);
            string validation = Path.Combine(Program.validation, Path.GetFileNameWithoutExtension(validationFile), validationFile);
            IEnumerable<string> files = new string[1] { validation };

            string paramsFileName = $"{Path.GetFileNameWithoutExtension(outFile)}-parameters.pdf";

            IDocumentationFile autodoc = new DocsFromFile("Description & validation", files, outFile, options);
            IDocumentationFile param = new ParamsDocsFromFile(model, paramsFileName, options);

            List<IDocumentationCell> cells = new List<IDocumentationCell>();
            cells.Add(new DocumentationCell(autodoc));
            cells.Add(new DocumentationCell(param));
            if (extraCells != null)
                cells.AddRange(extraCells);

            return new DocumentationRow(name, cells);
        }

        private static IDocumentationRow CustomDocsRow(string name, string subName, IEnumerable<string> inputs, string output, IEnumerable<IDocumentationCell> extraCells = null)
        {
            List<IDocumentationCell> cells = new List<IDocumentationCell>();
            cells.Add(new DocumentationCell(new DocsFromFile(subName, inputs, output, options)));
            if (extraCells != null)
                cells.AddRange(extraCells);
            return new DocumentationRow(name, cells);
        }

        private static IDocumentationRow CustomModelRow(string name, string output, IEnumerable<IDocumentationCell> extraCells = null)
        {
            List<IDocumentationCell> cells = new List<IDocumentationCell>();
            cells.Add(new DocumentationCell(new DocsFromModel<Clock>($"{output}.pdf", options)));
            if (extraCells != null)
                cells.AddRange(extraCells);
            return new DocumentationRow(name, cells);
        }
    }
}
