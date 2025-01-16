using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using APSIM.Shared.Documentation.Extensions;
using APSIM.Shared.Graphing;
using APSIM.Shared.Utilities;
using UserInterface.EventArguments;
using Models;
using Models.Core;
using Models.Storage;
using UserInterface.Views;
using UserInterface.Interfaces;
using Configuration = Utility.Configuration;

namespace UserInterface.Presenters
{


    /// <summary>
    /// A presenter for a graph.
    /// </summary>
    public class GraphPresenter : IPresenter, IExportable
    {
        /// <summary>
        /// The storage object
        /// </summary>
        [Link]
        private IDataStore storage = null;

        /// <summary>The graph view</summary>
        private IGraphView graphView;

        /// <summary>The graph</summary>
        private Graph graph;

        /// <summary>The explorer presenter</summary>
        private ExplorerPresenter explorerPresenter;

        /// <summary>The series definitions to show on graph.</summary>
        public IEnumerable<SeriesDefinition> SeriesDefinitions { get; set; } = new List<SeriesDefinition>();

        /// <summary>Attach the model to the view.</summary>
        /// <param name="model">The model.</param>
        /// <param name="view">The view.</param>
        /// <param name="explorerPresenter">The explorer presenter.</param>
        public void Attach(object model, object view, ExplorerPresenter explorerPresenter)
        {
            Attach(model, view, explorerPresenter, null);
        }

        /// <summary>Attach the model to the view.</summary>
        /// <param name="model">The model.</param>
        /// <param name="view">The view.</param>
        /// <param name="explorerPresenter">The explorer presenter.</param>
        /// <param name="cache">Cached definitions to be used.</param>
        public void Attach(object model, object view, ExplorerPresenter explorerPresenter, List<SeriesDefinition> cache)
        {
            this.graph = model as Graph;
            this.graphView = view as GraphView;
            this.explorerPresenter = explorerPresenter;

            graphView.OnAxisClick += OnAxisClick;
            graphView.OnLegendClick += OnLegendClick;
            graphView.OnCaptionClick += OnCaptionClick;
            graphView.OnAnnotationClick += OnAnnotationClick;
            explorerPresenter.CommandHistory.ModelChanged += OnGraphModelChanged;
            this.graphView.AddContextAction("Copy graph to clipboard", CopyGraphToClipboard);

            if (cache == null)
                DrawGraph();
            else
            {
                SeriesDefinitions = cache;
                DrawGraph(cache);
            }
        }

        /// <summary>Detach the model from the view.</summary>
        public void Detach()
        {
            explorerPresenter.CommandHistory.ModelChanged -= OnGraphModelChanged;
            if (CurrentPresenter != null)
            {
                CurrentPresenter.Detach();
            }

            graphView.OnAxisClick -= OnAxisClick;
            graphView.OnLegendClick -= OnLegendClick;
            graphView.OnCaptionClick -= OnCaptionClick;
            graphView.OnAnnotationClick -= OnAnnotationClick;
        }

        public void DrawGraph()
        {
            if (graph.Parent is GraphPanel)
                return;

            graphView.Clear();
            if (storage == null)
                storage = graph.FindInScope<IDataStore>();

            // Get a list of series definitions.
            try
            {
                var page = new GraphPage();
                page.Graphs.Add(graph);
                SeriesDefinitions = page.GetAllSeriesDefinitions(graph, storage?.Reader, SimulationFilter)[0].SeriesDefinitions;
            }
            catch (SQLiteException e)
            {
                explorerPresenter.MainPresenter.ShowError(new Exception("Error obtaining data from database: ", e));
            }
            catch (FirebirdException e)
            {
                explorerPresenter.MainPresenter.ShowError(new Exception("Error obtaining data from database: ", e));
            }

            DrawGraph(SeriesDefinitions);
        }

        /// <summary>Draw the graph on the screen.</summary>
        public void DrawGraph(IEnumerable<SeriesDefinition> definitions)
        {
            explorerPresenter.MainPresenter.ClearStatusPanel();
            graphView.Clear();
            if (storage == null)
                storage = graph.FindInScope<IDataStore>();
            if (graph != null && graph.Series != null)
            {
                if (!definitions.Any() && Configuration.Settings.EnableGraphDebuggingMessages)
                    explorerPresenter.MainPresenter.ShowMessage($"{this.graph.Name}: No data matches the properties and filters set for this graph", Simulation.MessageType.Warning, false);

                foreach (SeriesDefinition definition in definitions)
                {
                    DrawOnView(definition);
                }

                // Update axis maxima and minima
                graphView.UpdateView();

                //check if the axes are too small, update if so
                AdjustAxesboundaries(definitions);

                int pointsOutsideAxis = 0;
                int pointsInsideAxis = 0;
                foreach (SeriesDefinition definition in definitions)
                {
                    string seriesName = graph.Name;
                    if (definition.Series != null)
                        seriesName = graph.Name + " (" + definition.Series.Name + ")";

                    double xMin = graphView.AxisMinimum(definition.XAxis);
                    double xMax = graphView.AxisMaximum(definition.XAxis);
                    int xNaNCount = 0;
                    int yNaNCount = 0;
                    int bothNaNCount = 0;
                    double yMin = graphView.AxisMinimum(definition.YAxis);
                    double yMax = graphView.AxisMaximum(definition.YAxis);

                    List<double> valuesX = GetSeriesAsDoublesList(definition.X);
                    List<double> valuesY = GetSeriesAsDoublesList(definition.Y);

                    PerformPointErrorChecks(ref pointsOutsideAxis, ref pointsInsideAxis, xMin, xMax, ref xNaNCount, ref yNaNCount, ref bothNaNCount, yMin, yMax, valuesX, valuesY);
                    if (Configuration.Settings.EnableGraphDebuggingMessages && xNaNCount == valuesX.Count || yNaNCount == valuesY.Count || bothNaNCount == valuesX.Count)
                    {
                        DisplayNaNWarnings(seriesName, xNaNCount, yNaNCount, bothNaNCount);
                    }
                }

                if (pointsOutsideAxis > 0 && pointsInsideAxis == 0 && Configuration.Settings.EnableGraphDebuggingMessages)
                {
                    explorerPresenter.MainPresenter.ShowMessage($"{this.graph.Name}: No points are visible with current axis values.", Simulation.MessageType.Warning, false);
                }
                else if (pointsOutsideAxis > 0 && Configuration.Settings.EnableGraphDebuggingMessages)
                {
                    explorerPresenter.MainPresenter.ShowMessage($"{this.graph.Name}: {pointsOutsideAxis} points are outside of the provided graph axis. Adjust the minimums and maximums for the axis, or clear them to have them autocalculate and show everything.", Simulation.MessageType.Warning, false);
                }

                // Get a list of series annotations.
                DrawOnView(graph.GetAnnotationsToGraph());

                // Format the legend.
                graphView.FormatLegend(graph.LegendPosition, graph.LegendOrientation);

                // Format the title
                graphView.FormatTitle(graph.Name);

                // Format the footer
                if (string.IsNullOrEmpty(graph.Caption))
                {
                    graphView.FormatCaption("Double click to add a caption", true);
                }
                else
                {
                    graphView.FormatCaption(graph.Caption, false);
                }

                // Remove series titles out of the graph disabled series list when
                // they are no longer valid i.e. not on the graph.
                if (graph.DisabledSeries == null)
                    graph.DisabledSeries = new List<string>();
                IEnumerable<string> validSeriesTitles = definitions.Select(s => s.Title);
                List<string> seriesTitlesToKeep = new List<string>(validSeriesTitles.Intersect(this.graph.DisabledSeries));
                this.graph.DisabledSeries.Clear();
                this.graph.DisabledSeries.AddRange(seriesTitlesToKeep);
                graphView.LegendInsideGraph = !graph.LegendOutsideGraph;

                graphView.Refresh();
            }
        }

        private void DisplayNaNWarnings(string seriesName, int xNaNCount, int yNaNCount, int bothNaNCount)
        {
            explorerPresenter.MainPresenter.ShowMessage($"{seriesName}: NaN Values found in points. These may be empty cells in the datastore.", Simulation.MessageType.Information, false);
            if (xNaNCount > 0)
                explorerPresenter.MainPresenter.ShowMessage($"{seriesName}: {xNaNCount} points where X is NaN, but Y is valid.", Simulation.MessageType.Information, false);
            if (yNaNCount > 0)
                explorerPresenter.MainPresenter.ShowMessage($"{seriesName}: {yNaNCount} points where Y is NaN, but X is valid.", Simulation.MessageType.Information, false);
            if (bothNaNCount > 0)
                explorerPresenter.MainPresenter.ShowMessage($"{seriesName}: {bothNaNCount} points where both values are NaN.", Simulation.MessageType.Information, false);
        }

        private static void PerformPointErrorChecks(ref int pointsOutsideAxis, ref int pointsInsideAxis, double xMin, double xMax, ref int xNaNCount, ref int yNaNCount, ref int bothNaNCount, double yMin, double yMax, List<double> valuesX, List<double> valuesY)
        {
            for (int i = 0; i < valuesX.Count; i++)
            {
                bool isOutside = false;
                double x = valuesX[i];
                double y = valuesY[i];
                if (double.IsNaN(x) && !double.IsNaN(y))
                {
                    xNaNCount += 1;
                }
                else if (!double.IsNaN(x) && double.IsNaN(y))
                {
                    yNaNCount += 1;
                }
                else if (double.IsNaN(x) && double.IsNaN(y))
                {
                    bothNaNCount += 1;
                }
                else
                {
                    if (!double.IsNaN(xMin) && x < xMin)
                        isOutside = true;
                    if (!double.IsNaN(xMax) && x > xMax)
                        isOutside = true;
                    if (!double.IsNaN(yMin) && y < yMin)
                        isOutside = true;
                    if (!double.IsNaN(yMax) && y > yMax)
                        isOutside = true;

                    if (isOutside)
                        pointsOutsideAxis += 1;
                    else
                        pointsInsideAxis += 1;
                }
            }
        }

        private static List<double> GetSeriesAsDoublesList(IEnumerable definitionAxis)
        {
            List<double> axisDoubles = new();
            foreach (var axisValue in definitionAxis)
            {
                double axisDouble = 0;
                if (axisValue is DateTime)
                    axisDouble = ((DateTime)axisValue).ToOADate();
                else if (axisValue is string)
                    axisDouble = 0; //string axis are handled elsewhere, so just set this to 0
                else
                    axisDouble = Convert.ToDouble(axisValue);

                axisDoubles.Add(axisDouble);
            }
            return axisDoubles;
        }

        private void AdjustAxesboundaries(IEnumerable<SeriesDefinition> definitions)
        {
            const double tolerance = 0.00001;
            double xAxisLargestErrorValue = 0.0;
            double yAxisLargestErrorValue = 0.0;
            foreach (Axis axis in graph.Axis)
            {
                double minimum = graphView.AxisMinimum(axis.Position);
                double maximum = graphView.AxisMaximum(axis.Position);
                if (axis.Maximum - axis.Minimum < tolerance)
                {
                    axis.Minimum -= tolerance / 2;
                    axis.Maximum += tolerance / 2;
                }
                // Add space for error bars if they exist for the axes
                // Bottom axis (x)
                if (axis.Position == AxisPosition.Bottom)
                {
                    // x is usually at the bottom.
                    foreach (SeriesDefinition seriesDef in definitions)
                    {
                        if (seriesDef.XError != null)
                        {
                            double largestErrorValue = MathUtilities.Max(seriesDef.XError);
                            if (largestErrorValue > xAxisLargestErrorValue)
                                xAxisLargestErrorValue = largestErrorValue;
                        }
                    }
                    // Add error values to min and max.
                    if (xAxisLargestErrorValue != 0)
                    {
                        axis.Minimum = minimum - (xAxisLargestErrorValue / 2);
                        axis.Maximum = maximum + (xAxisLargestErrorValue / 2);
                    }

                }
                // Left axis (y)
                if (axis.Position == AxisPosition.Left)
                {
                    // x is usually at the bottom.
                    foreach (SeriesDefinition seriesDef in definitions)
                    {
                        if (seriesDef.YError != null)
                        {
                            double largestErrorValue = MathUtilities.Max(seriesDef.YError);
                            if (largestErrorValue > yAxisLargestErrorValue)
                                yAxisLargestErrorValue = largestErrorValue;
                        }
                    }
                    // Add values to min and max.
                    if (yAxisLargestErrorValue != 0)
                    {
                        axis.Minimum = minimum - (yAxisLargestErrorValue / 2);
                        axis.Maximum = maximum + (yAxisLargestErrorValue / 2);
                    }
                }
                FormatAxis(axis);
            }
        }

        /// <summary>Export the contents of this graph to the specified file.</summary>
        /// <param name="folder">The folder.</param>
        /// <returns>The file name</returns>
        public string ExportToPNG(string folder)
        {
            // The rectange numbers below are optimised for generation of PDF document
            // on a computer that has its display settings at 100%.
            System.Drawing.Rectangle r = new System.Drawing.Rectangle(0, 0, 600, 450);
            Gdk.Pixbuf img;
            graphView.Export(out img, r, true);

            string path = graph.FullPath.Replace(".Simulations.", string.Empty);
            string fileName = Path.Combine(folder, path + ".png");
            img.Save(fileName, "png");

            return fileName;
        }

        /// <summary>Gets the series names.</summary>
        /// <returns>A list of series names.</returns>
        public string[] GetSeriesNames()
        {
            return SeriesDefinitions.Select(s => s.Title).ToArray();
        }

        public List<string> SimulationFilter { get; set; }

        /// <summary>The current presenter</summary>
        public IPresenter CurrentPresenter { get; set; }

        /// <summary>
        /// Iff set to true, the legend will appear inside the graph boundaries.
        /// </summary>
        public bool LegendInsideGraph
        {
            get
            {
                return graphView.LegendInsideGraph;
            }
            set
            {
                graphView.LegendInsideGraph = value;
            }
        }

        /// <summary>Draws the specified series definition on the view.</summary>
        /// <param name="definition">The definition.</param>
        private void DrawOnView(SeriesDefinition definition)
        {
            if (graph.DisabledSeries == null ||
                !graph.DisabledSeries.Contains(definition.Title))
            {
                try
                {
                    System.Drawing.Color colour = definition.Colour;

                    // Create the series and populate it with data.
                    if (definition.Type == SeriesType.Bar)
                    {
                        graphView.DrawBar(
                                          definition.Title,
                                          definition.X,
                                          definition.Y,
                                          definition.XAxis,
                                          definition.YAxis,
                                          colour,
                                          definition.ShowInLegend);
                    }
                    else if (definition.Type == SeriesType.Scatter)
                    {
                        graphView.DrawLineAndMarkers(
                                                    definition.Title,
                                                    definition.X,
                                                    definition.Y,
                                                    definition.XFieldName,
                                                    definition.YFieldName,
                                                    definition.XError,
                                                    definition.YError,
                                                    definition.XAxis,
                                                    definition.YAxis,
                                                    colour,
                                                    definition.Line,
                                                    definition.Marker,
                                                    definition.LineThickness,
                                                    definition.MarkerSize,
                                                    definition.MarkerModifier,
                                                    definition.ShowInLegend);
                    }
                    else if (definition.Type == SeriesType.Region)
                    {
                        graphView.DrawRegion(
                                            definition.Title,
                                            definition.X,
                                            definition.Y,
                                            definition.X2,
                                            definition.Y2,
                                            definition.XAxis,
                                            definition.YAxis,
                                            colour,
                                            definition.ShowInLegend);
                    }
                    else if (definition.Type == SeriesType.Area)
                    {
                        graphView.DrawArea(
                            definition.Title,
                            definition.X,
                            definition.Y,
                            definition.XAxis,
                            definition.YAxis,
                            colour,
                            definition.ShowInLegend);
                    }
                    else if (definition.Type == SeriesType.StackedArea)
                    {
                        graphView.DrawStackedArea(
                            definition.Title,
                            definition.X.Cast<object>().ToArray(),
                            definition.Y.Cast<double>().ToArray(),
                            definition.XAxis,
                            definition.YAxis,
                            colour,
                            definition.ShowInLegend);
                    }
                    else if (definition.Type == SeriesType.Box)
                    {
                        graphView.DrawBoxPLot(definition.Title,
                            definition.X.Cast<object>().ToArray(),
                            definition.Y.Cast<double>().ToArray(),
                            definition.XAxis,
                            definition.YAxis,
                            definition.Colour,
                            definition.ShowInLegend,
                            definition.Line,
                            definition.Marker,
                            definition.LineThickness);
                    }
                }
                catch (Exception err)
                {
                    throw new Exception($"Unable to draw graph {graph.FullPath}", err);
                }
            }
        }

        /// <summary>Draws the specified series definition on the view.</summary>
        /// <param name="annotations">The list of annotations</param>
        private void DrawOnView(IEnumerable<IAnnotation> annotations)
        {
            var range = graphView.AxisMaximum(AxisPosition.Bottom) - graphView.AxisMinimum(AxisPosition.Bottom);

            double minimumX = graphView.AxisMinimum(AxisPosition.Bottom) + range * 0.03;
            double maximumX = graphView.AxisMaximum(AxisPosition.Bottom);
            double minimumY = graphView.AxisMinimum(AxisPosition.Left);
            double maximumY = graphView.AxisMaximum(AxisPosition.Left);
            double lowestAxisScale = Math.Min(minimumX, minimumY);
            double largestAxisScale = Math.Max(maximumX, maximumY);
            for (int i = 0; i < annotations.Count(); i++)
            {
                IAnnotation annotation = annotations.ElementAt(i);
                if (annotation is TextAnnotation textAnnotation)
                {
                    double interval = (maximumY - lowestAxisScale) / 8; // fit 8 annotations on graph.

                    object x, y;
                    bool leftAlign = textAnnotation.leftAlign;
                    bool topAlign = textAnnotation.topAlign;
                    if (textAnnotation.Name != null && textAnnotation.Name.StartsWith("Regression"))
                    {
                        if (graph.AnnotationLocation == AnnotationPosition.TopLeft)
                        {
                            x = minimumX;
                            y = maximumY - i * interval;
                        }
                        else if (graph.AnnotationLocation == AnnotationPosition.TopRight)
                        {
                            x = minimumX + range * 0.95;
                            y = maximumY - i * interval;
                            leftAlign = false;
                        }
                        else if (graph.AnnotationLocation == AnnotationPosition.BottomRight)
                        {
                            x = minimumX + range * 0.95;
                            y = minimumY + i * interval;
                            leftAlign = false;
                            topAlign = false;
                        }
                        else
                        {
                            x = minimumX;
                            y = minimumY + i * interval;
                            topAlign = false;
                        }

                        //y = largestAxisScale - (i * interval);
                    }
                    else
                    {
                        x = textAnnotation.x;
                        y = textAnnotation.y;
                    }

                    graphView.DrawText(textAnnotation.text,
                                        x,
                                        y,
                                        leftAlign,
                                        topAlign,
                                        textAnnotation.textRotation,
                                        AxisPosition.Bottom,
                                        AxisPosition.Left,
                                        textAnnotation.colour);
                }
                else if (annotation is LineAnnotation lineAnnotation)
                {
                    graphView.DrawLine(
                                        lineAnnotation.x1,
                                        lineAnnotation.y1,
                                        lineAnnotation.x2,
                                        lineAnnotation.y2,
                                        lineAnnotation.type,
                                        lineAnnotation.thickness,
                                        lineAnnotation.colour,
                                        lineAnnotation.InFrontOfSeries,
                                        lineAnnotation.ToolTip);
                }
                else
                    throw new Exception($"Unknown annotation type {annotation.GetType()}");
            }
        }


        /// <summary>Format the specified axis.</summary>
        /// <param name="axis">The axis to format</param>
        private void FormatAxis(APSIM.Shared.Graphing.Axis axis)
        {
            string title = axis.Title;
            if (axis.Title == null || axis.Title == string.Empty)
            {
                // Work out a default title by going through all series and getting the
                // X or Y field name depending on whether 'axis' is an x axis or a y axis.
                HashSet<string> titles = new HashSet<string>();
                HashSet<string> variableNames = new HashSet<string>();
                foreach (SeriesDefinition definition in SeriesDefinitions)
                {
                    if (definition.X != null && definition.XAxis == axis.Position && definition.XFieldName != null)
                    {
                        IEnumerator enumerator = definition.X.GetEnumerator();
                        // if (enumerator.MoveNext())
                        //     axis.DateTimeAxis = enumerator.Current.GetType() == typeof(DateTime);
                        string xName = definition.XFieldName;
                        if (!variableNames.Contains(xName))
                        {
                            variableNames.Add(xName);
                            if (definition.XFieldUnits != null)
                                xName = xName + " (" + definition.XFieldUnits + ")";

                            titles.Add(xName);
                        }
                    }

                    if (definition.Y != null && definition.YAxis == axis.Position && definition.YFieldName != null)
                    {
                        IEnumerator enumerator = definition.Y.GetEnumerator();
                        // if (enumerator.MoveNext())
                        //     axis.DateTimeAxis = enumerator.Current.GetType() == typeof(DateTime);
                        string yName = definition.YFieldName;
                        if (!variableNames.Contains(yName))
                        {
                            variableNames.Add(yName);
                            if (definition.YFieldUnits != null)
                                yName = yName + " (" + definition.YFieldUnits + ")";

                            titles.Add(yName);
                        }
                    }
                }

                // Create a default title by appending all 'names' together.
                if (axis.LabelOnOneLine)
                    title = StringUtilities.BuildString(titles.ToArray(), ", ");
                else
                    title = StringUtilities.BuildString(titles.ToArray(), Environment.NewLine);
            }

            graphView.FormatAxis(axis.Position, title, axis.Inverted, axis.Minimum ?? double.NaN, axis.Maximum ?? double.NaN, axis.Interval ?? double.NaN, axis.CrossesAtZero, axis.LabelOnOneLine);
        }

        /// <summary>The graph model has changed.</summary>
        /// <param name="model">The model.</param>
        private void OnGraphModelChanged(object model)
        {
            if (model == graph || graph.FindAllDescendants().Contains(model) || graph.Axis.Contains(model))
                DrawGraph();
        }

        /// <summary>User has clicked an axis.</summary>
        /// <param name="axisType">Type of the axis.</param>
        private void OnAxisClick(AxisPosition axisType)
        {
            if (CurrentPresenter != null)
            {
                CurrentPresenter.Detach();
            }

            bool isXAxis = true;
            if (axisType == AxisPosition.Left || axisType == AxisPosition.Right)
                isXAxis = false;

            bool isDateAxis = false;
            foreach (SeriesDefinition definition in SeriesDefinitions)
            {
                
                if (isXAxis)
                {
                    foreach (var x in definition.X)
                        if (x is DateTime)
                            isDateAxis = true;
                } 
                else
                {
                    foreach (var y in definition.Y)
                        if (y is DateTime)
                            isDateAxis = true;
                }
            }

            AxisPresenter axisPresenter = new AxisPresenter();
            axisPresenter.SetAsDateAxis(isDateAxis);
            CurrentPresenter = axisPresenter;
            AxisView a = new AxisView(graphView as GraphView);
            string dimension = (axisType == AxisPosition.Left || axisType == AxisPosition.Right) ? "Y" : "X";
            graphView.ShowEditorPanel(a.MainWidget, dimension + "-Axis options");
            axisPresenter.Attach(GetAxis(axisType), a, explorerPresenter);
        }

        /// <summary>User has clicked a footer.</summary>
        /// <param name="sender">Sender of event</param>
        /// <param name="e">Event arguments</param>
        private void OnCaptionClick(object sender, EventArgs e)
        {
            if (CurrentPresenter != null)
            {
                CurrentPresenter.Detach();
            }

            TitlePresenter titlePresenter = new TitlePresenter();
            CurrentPresenter = titlePresenter;
            titlePresenter.ShowCaption = true;

            TitleView t = new TitleView(graphView as GraphView);
            graphView.ShowEditorPanel(t.MainWidget, "Title options");
            titlePresenter.Attach(graph, t, explorerPresenter);
        }

        /// <summary>Get an axis</summary>
        /// <param name="position">Type of the axis.</param>
        /// <returns>Return the axis</returns>
        /// <exception cref="System.Exception">Cannot find axis with type:  + axisType.ToString()</exception>
        private object GetAxis(AxisPosition position)
        {
            foreach (Axis a in graph.Axis)
                if (a.Position == position)
                    return a;

            throw new Exception("Cannot find axis with type: " + position.ToString());
        }

        /// <summary>The axis has changed</summary>
        /// <param name="axis">The axis.</param>
        private void OnAxisChanged(Axis axis)
        {
            DrawGraph();
        }

        /// <summary>User has clicked the legend.</summary>
        /// <param name="sender">Sender of event</param>
        /// <param name="e">Event arguments</param>
        private void OnLegendClick(object sender, LegendClickArgs e)
        {
            if (CurrentPresenter != null)
            {
                CurrentPresenter.Detach();
            }

            LegendPresenter presenter = new LegendPresenter(this);
            CurrentPresenter = presenter;

            LegendView view = new LegendView(graphView as GraphView);
            graphView.ShowEditorPanel(view.MainWidget, "Legend options");
            presenter.Attach(graph, view, explorerPresenter);
        }

        /// <summary>
        /// User has clicked on graph annotation.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAnnotationClick(object sender, EventArgs e)
        {
            if (CurrentPresenter != null)
                CurrentPresenter.Detach();

            AnnotationPresenter presenter = new AnnotationPresenter();
            CurrentPresenter = presenter;

            //LegendView view = new LegendView(graphView as GraphView);
            var view = new ViewBase(graphView as ViewBase, "ApsimNG.Resources.Glade.AnnotationView.glade");
            graphView.ShowEditorPanel(view.MainWidget, "Stats / Equation options");
            presenter.Attach(graph, view, explorerPresenter);
        }

        /// <summary>User has hovered over a point on the graph.</summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void OnHoverOverPoint(object sender, EventArguments.HoverPointArgs e)
        {
            // Find the correct series.
            foreach (SeriesDefinition definition in SeriesDefinitions)
            {
                if (definition.Title == e.SeriesName)
                {
                    e.HoverText = GetSimulationNameForPoint(e.X, e.Y);
                    if (e.HoverText == null)
                    {
                        e.HoverText = e.SeriesName;
                    }

                    return;
                }
            }
        }

        /// <summary>User has clicked "copy graph" menu item.</summary>
        /// <param name="sender">Sender of event</param>
        /// <param name="e">Event arguments</param>
        private void CopyGraphToClipboard(object sender, EventArgs e)
        {
            graphView.ExportToClipboard();
        }

        /// <summary>
        /// Look for the row in data that has the specified x and y. 
        /// </summary>
        /// <param name="x">The x coordinate</param>
        /// <param name="y">The y coordinate</param>
        /// <returns>The simulation name of the row</returns>
        private string GetSimulationNameForPoint(double x, double y)
        {
            foreach (SeriesDefinition definition in SeriesDefinitions)
            {
                if (definition.SimulationNamesForEachPoint != null)
                {
                    IEnumerator xEnum = definition.X.GetEnumerator();
                    IEnumerator yEnum = definition.Y.GetEnumerator();
                    IEnumerator simNameEnum = definition.SimulationNamesForEachPoint.GetEnumerator();

                    while (xEnum.MoveNext() && yEnum.MoveNext() && simNameEnum.MoveNext())
                    {
                        object rowX = xEnum.Current;
                        object rowY = yEnum.Current;

                        if (rowX is double && rowY is double &&
                            MathUtilities.FloatsAreEqual(x, (double)rowX) &&
                            MathUtilities.FloatsAreEqual(y, (double)rowY))
                        {
                            object simulationName = simNameEnum.Current;
                            if (simulationName != null)
                            {
                                return simulationName.ToString();
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}
