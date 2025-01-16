using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using APSIM.Interop.Graphing.CustomSeries;
using APSIM.Interop.Graphing.Extensions;
using APSIM.Shared.Documentation.Extensions;
using APSIM.Shared.Graphing;
using APSIM.Shared.Utilities;
using ApsimNG.Graphing;
using Gtk;
using MathNet.Numerics.Statistics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.GtkSharp;
using OxyPlot.Series;
using UserInterface.EventArguments;
using UserInterface.Interfaces;
using Utility;
using LegendPlacement = OxyPlot.Legends.LegendPlacement;
using OxyLegendOrientation = OxyPlot.Legends.LegendOrientation;
using OxyLegendPosition = OxyPlot.Legends.LegendPosition;

namespace UserInterface.Views
{
    /// <summary>
    /// A view that contains a graph and click zones for the user to allow
    /// editing various parts of the graph.
    /// </summary>
    public class GraphView : ViewBase, IGraphView
    {
        private double fontSize = 14;

        /// <summary>
        /// Overall font size for the graph.
        /// </summary>
        public double FontSize
        {
            get
            {
                return fontSize;
            }
            set
            {
                fontSize = value;
                if (plot1 != null && plot1.Model != null)
                {
                    plot1.Model.DefaultFontSize = value;
                    plot1.Model.SetLegendFontSize(value);

                    foreach (OxyPlot.Annotations.Annotation annotation in this.plot1.Model.Annotations)
                        if (annotation is OxyPlot.Annotations.TextAnnotation textAnnotation)
                            textAnnotation.FontSize = value;
                }
            }
        }

        private MarkerSize markerSize;

        /// <summary>
        /// Marker size.
        /// </summary>
        public MarkerSize MarkerSize
        {
            get
            {
                return markerSize;
            }
            set
            {
                markerSize = value;
                double numericValue = GetMarkerSizeNumericValue(value);
                if (plot1 != null && plot1.Model != null)
                    foreach (var series in plot1.Model.Series.OfType<Utility.LineSeriesWithTracker>())
                        series.MarkerSize = numericValue;
            }
        }

        /// <summary>
        /// Overall font to use.
        /// </summary>
        private const string Font = "Calibri Light";

        /// <summary>
        /// Margin to use
        /// </summary>
        private const int TopMargin = 75;

        /// <summary>The smallest date used on any axis.</summary>
        private DateTime smallestDate = DateTime.MaxValue;

        /// <summary>The largest date used on any axis</summary>
        private DateTime largestDate = DateTime.MinValue;

        private bool inRightClick = false;

        private OxyPlot.GtkSharp.PlotView plot1;
        private Box vbox1 = null;
        private Expander expander1 = null;
        private Box vbox2 = null;
        private Label captionLabel = null;
        private EventBox captionEventBox = null;
        private Label label2 = null;
        private Menu popup = new Menu();
        private List<string> unselectedSeriesNames = new List<string>();
        private List<string> UnselectedSeriesNames { get { return unselectedSeriesNames; } set { unselectedSeriesNames = value; } }

        /// <summary>Default constructor.</summary>
        public GraphView() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphView" /> class.
        /// </summary>
        public GraphView(ViewBase owner) : base(owner)
        {
            Builder builder = BuilderFromResource("ApsimNG.Resources.Glade.GraphView.glade");
            vbox1 = (Box)builder.GetObject("vbox1");
            expander1 = (Expander)builder.GetObject("expander1");
            vbox2 = (Box)builder.GetObject("vbox2");
            captionLabel = (Label)builder.GetObject("captionLabel");
            captionEventBox = (EventBox)builder.GetObject("captionEventBox");
            label2 = (Label)builder.GetObject("label2");
            Initialise(owner, vbox1);
        }

   
        protected override void Initialise(ViewBase ownerView, GLib.Object gtkControl)
        {
            this.owner = ownerView;
            vbox1 = gtkControl as Box;
            mainWidget = vbox1;

            plot1 = new PlotView();
            plot1.Model = new PlotModel();
            if (vbox1.Window != null)
                plot1.SetSizeRequest(vbox1.Window.Width, vbox1.Window.Height);
            if (vbox2 == null)
                vbox2 = vbox1;
            vbox2.PackStart(plot1, true, true, 0);

            smallestDate = DateTime.MaxValue;
            largestDate = DateTime.MinValue;
            this.LeftRightPadding = 40;
            if (expander1 != null)
            {
                expander1.Visible = false;
                captionEventBox.Visible = true;
            }
#pragma warning disable CS0618
            // todo : need to refacto this to use PlotController,
            // as the "old" way of doing things is now considered obsolete.
            plot1.Model.MouseDown += OnChartClick;
            plot1.Model.MouseUp += OnChartMouseUp;
            plot1.Model.MouseMove += OnChartMouseMove;
            plot1.Model.MouseLeave += OnChartMouseMove;
            plot1.Model.Updated += OnModelUpdated;
#pragma warning restore CS0618
            popup.AttachToWidget(plot1, null);

            if (captionLabel != null)
            {
                captionLabel.Text = null;
                captionEventBox.ButtonPressEvent += OnCaptionLabelDoubleClick;
            }

            Color foregroundColor = Color.Gray;
            if (!Configuration.Settings.ThemeRestartRequired)
                foregroundColor = Configuration.Settings.DarkTheme ? Color.White : Color.Black;
            else foregroundColor = Configuration.Settings.DarkTheme ? Color.Black : Color.White;
            ForegroundColour = Colour.ToOxy(foregroundColor);

            Color backgroundColor = Color.Gray;
            if (!Configuration.Settings.ThemeRestartRequired)
                backgroundColor = Configuration.Settings.DarkTheme ? Color.FromArgb(255,48,48,48) : Color.White;
            else backgroundColor = Configuration.Settings.DarkTheme ? Color.White : Color.FromArgb(255,48,48,48);
            BackColor = Colour.ToOxy(backgroundColor);

            mainWidget.Destroyed += _mainWidget_Destroyed;

            // Not sure why but Oxyplot fonts are not scaled correctly on .net core on high DPI screens.
            // On my Surface Pro screen I'm using a 150% scaling which makes the fonts on graphs tiny.
            // I notice that the GTK3 ScaleFactor has a value of 80% in this situation. If the screen
            // scaling is 125% or 100% then ScaleFactor is 1.0. It doesn't seem consistent though.
            // For now I'll just scale all fonts by 2.0. Works on my various screens. Will need some testing.
            var font = Pango.FontDescription.FromString(Utility.Configuration.Settings.FontName);
            fontSize = font.SizeIsAbsolute ? font.Size : Convert.ToInt32(font.Size / Pango.Scale.PangoScale) * 2;
        }

        private void OnModelUpdated(object sender, EventArgs e)
        {
            if ((sender as PlotModel).Series.Count > 0)
            {
                if ((sender as PlotModel).Legends.Count() > 0 && (sender as PlotModel).Series.First() is INameableSeries)
                {
                    // Get the series in the changed model.
                    // Get the series with false 'isVisible' property.
                    List<string> reselectedSeriesNames = new();
                    List<string> newUnselectedSeriesNames = new();
                    foreach (OxyPlot.Series.Series series in (sender as PlotModel).Series)
                    {
                        if (series is INameableSeries seriesNameable) 
                        {
                            bool seriesNamePreviouslyUnselected = false;
                            List<string> matchingNames = new();

                            if (UnselectedSeriesNames.Any())
                                matchingNames = UnselectedSeriesNames.Where(seriesname => seriesname.Equals(seriesNameable.Name)).ToList();
                            if (matchingNames.Any())
                                seriesNamePreviouslyUnselected = true;
                            if ((series as OxyPlot.Series.Series).IsVisible == false && (series as OxyPlot.Series.Series).Title != null)
                                newUnselectedSeriesNames.Add(seriesNameable.Name);
                            else if ((series as OxyPlot.Series.Series).IsVisible == true && 
                                    (series as OxyPlot.Series.Series).Title != null && 
                                    seriesNamePreviouslyUnselected)
                                reselectedSeriesNames.Add(seriesNameable.Name);
                        }
                        
                    }
                    UnselectedSeriesNames = newUnselectedSeriesNames;
                    _ = Enum.TryParse((sender as PlotModel).Legends.First().LegendPosition.ToString(), out LegendPosition legendPosition);
                    _ = Enum.TryParse((sender as PlotModel).Legends.First().LegendOrientation.ToString(), out LegendOrientation legendOrientation);
                    // Reformat the legend without the matching unselectedSeries.
                    FormatLegend(legendPosition, legendOrientation, newUnselectedSeriesNames, reselectedSeriesNames);
                }
            }
        }

        private void _mainWidget_Destroyed(object sender, EventArgs e)
        {
            try
            {
#pragma warning disable CS0618
                plot1.Model.MouseDown -= OnChartClick;
                plot1.Model.MouseUp -= OnChartMouseUp;
                plot1.Model.MouseMove -= OnChartMouseMove;
                plot1.Model.MouseLeave -= OnChartMouseMove;
                plot1.Model.Updated -= OnModelUpdated;
#pragma warning restore CS0618
                if (captionEventBox != null)
                    captionEventBox.ButtonPressEvent -= OnCaptionLabelDoubleClick;
                popup.Clear();
                popup.Dispose();
                Clear();
                plot1.Dispose();
                mainWidget.Destroyed -= _mainWidget_Destroyed;
                owner = null;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// Invoked when the user clicks on the plot area (the area inside the axes)
        /// </summary>
        public event EventHandler OnPlotClick;

        /// <summary>
        /// Invoked when the user clicks on an axis.
        /// </summary>
        public event ClickAxisDelegate OnAxisClick;

        /// <summary>
        /// Invoked when the user clicks on a legend.
        /// </summary>
        public event EventHandler<LegendClickArgs> OnLegendClick;

        /// <summary>
        /// Invoked when the user clicks on the graph title.
        /// </summary>
        public event EventHandler OnTitleClick;

        /// <summary>
        /// Invoked when the user clicks on the graph caption.
        /// </summary>
        public event EventHandler OnCaptionClick;

        /// <summary>
        /// Invoked when the user clicks on the annotation.
        /// </summary>
        public event EventHandler OnAnnotationClick;

        /// <summary>Invoked when the user single clicks on the graph</summary>
        public event EventHandler SingleClick;

        /// <summary>
        /// Left margin in pixels.
        /// </summary>
        public int LeftRightPadding { get; set; }

        /// <summary>
        /// Disable scrolling via mouse wheel on the graph.
        /// </summary>
        public void DisableScrolling()
        {
            plot1.ActualController.UnbindMouseWheel();
        }

        /// <summary>
        /// Controls the background colour of the graph.
        /// </summary>
        public OxyColor BackColor
        {
            get
            {
                if (plot1 == null || plot1.Model == null)
                    return OxyColors.White;
                return this.plot1.Model.Background;
            }
            set
            {
                if (plot1 != null && plot1.Model != null)
                    this.plot1.Model.Background = value;
            }
        }

        /// <summary>
        /// Controls the foreground colour of the graph.
        /// </summary>
        public OxyColor ForegroundColour
        {
            get
            {
                if (plot1 == null || plot1.Model == null)
                    return OxyColors.Black; // Fallback to black
                return this.plot1.Model.TextColor;
            }
            set
            {
                if (plot1 != null && plot1.Model != null)
                    this.plot1.Model.TextColor = value;
            }
        }

        public int Width
        {
            get
            {

                plot1.GetPreferredWidth(out int minWidth, out int preferredWidth);

                return plot1.Allocation.Width > 1 ? plot1.Allocation.Width : preferredWidth;
            }
            set
            {
                plot1.WidthRequest = value;
            }
        }

        public int Height
        {
            get
            {

                plot1.GetPreferredHeight(out int minHeight, out int preferredHeight);

                return plot1.Allocation.Height > 1 ? plot1.Allocation.Height : preferredHeight;
            }
            set
            {
                plot1.HeightRequest = value;
            }
        }

        /// <summary>Set the preferred width as a fraction of the parent window.</summary>
        /// <param name="fraction">Fraction of the parent window for the graph to occupy.</param>
        public void SetPreferredWidth(double fraction)
        {
            plot1?.SetSizeRequest(Convert.ToInt32(vbox1.Window.EffectiveParent.Width * fraction), 100);
        }

        /// <summary>Gets or sets a value indicating if the legend is visible.</summary>
        public bool IsLegendVisible
        {
            get { return this.plot1.Model.IsLegendVisible; }
            set { this.plot1.Model.IsLegendVisible = value; }
        }

        /// <summary>
        /// Iff set to true, the legend will appear inside the graph boundaries.
        /// </summary>
        public bool LegendInsideGraph
        {
            get
            {
                return plot1.Model.GetLegendPlacement() == LegendPlacement.Inside;
            }
            set
            {
                plot1.Model.SetLegendPlacement(value ? LegendPlacement.Inside : LegendPlacement.Outside);
            }
        }

        /// <summary>
        /// Clear the graph of everything.
        /// </summary>
        public void Clear()
        {
            this.plot1.Model.Series.Clear();
            this.plot1.Model.Axes.Clear();
            this.plot1.Model.Annotations.Clear();
            // modLMC - 11/05/2016 - Need to clear the chart title as well
            this.FormatTitle("");
        }

        /// <summary>
        /// Update the graph data sources; this causes the axes minima and maxima to be calculated
        /// </summary>
        public void UpdateView()
        {
            IPlotModel theModel = this.plot1.Model as IPlotModel;
            if (theModel != null)
                theModel.Update(true);
        }

        /// <summary>
        /// Refresh the graph.
        /// </summary>
        public void Refresh()
        {
            this.plot1.Model.DefaultFontSize = FontSize;
            this.plot1.Model.PlotAreaBorderThickness = new OxyThickness(0.0);
            this.plot1.Model.SetLegendBorder(OxyColors.Transparent);
            this.plot1.Model.SetLegendBackground(OxyColors.Transparent);

            if (this.LeftRightPadding != 0)
                this.plot1.Model.Padding = new OxyThickness(10, 10, this.LeftRightPadding, 10);

            this.plot1.Model.SetLegendFontSize(FontSize);

            foreach (OxyPlot.Annotations.Annotation annotation in this.plot1.Model.Annotations)
            {
                if (annotation is OxyPlot.Annotations.TextAnnotation)
                {
                    OxyPlot.Annotations.TextAnnotation textAnnotation = annotation as OxyPlot.Annotations.TextAnnotation;
                    if (textAnnotation != null)
                        textAnnotation.FontSize = FontSize;
                }
            }

            this.plot1.Model.InvalidatePlot(true);
            plot1.ShowAll();
        }

        /// <summary>
        ///  Draw a line and markers series with the specified arguments.
        /// </summary>
        /// <param name="title">The series title</param>
        /// <param name="x">The x values for the series</param>
        /// <param name="y">The y values for the series</param>
        /// <param name="xFieldName">The name of the x variable.</param>
        /// <param name="yFieldName">The name of the y variable.</param>
        /// <param name="xError">The error values for the x series</param>
        /// <param name="yError">The error values for the y series</param>
        /// <param name="xAxisType">The axis type the x values are related to</param>
        /// <param name="yAxisType">The axis type the y values are related to</param>
        /// <param name="colour">The series color</param>
        /// <param name="lineType">The type of series line</param>
        /// <param name="markerType">The type of series markers</param>
        /// <param name="lineThickness">The line thickness</param>
        /// <param name="markerSize">The size of the marker</param>
        /// <param name="markerModifier">Multiplier on marker size.</param>
        /// <param name="showOnLegend">Show in legend?</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawLineAndMarkers(
             string title,
             IEnumerable x,
             IEnumerable y,
             string xFieldName,
             string yFieldName,
             IEnumerable xError,
             IEnumerable yError,
             APSIM.Shared.Graphing.AxisPosition xAxisType,
             APSIM.Shared.Graphing.AxisPosition yAxisType,
             Color colour,
             LineType lineType,
             APSIM.Shared.Graphing.MarkerType markerType,
             APSIM.Shared.Graphing.LineThickness lineThickness,
             APSIM.Shared.Graphing.MarkerSize markerSize,
             double markerModifier,
             bool showOnLegend)
        {
            Utility.LineSeriesWithTracker series = null;
            if (x != null && y != null)
            {
                series = new Utility.LineSeriesWithTracker(title);
                if (x.Count() > 0)
                    series.XType = x.Cast<object>().ToArray()[0].GetType();
                else
                    series.XType = null;

                if (y.Count() > 0)
                    series.YType = y.Cast<object>().ToArray()[0].GetType();
                else
                    series.YType = null;

                if (showOnLegend)
                {
                    if (String.IsNullOrEmpty(title))
                        series.Title = "null";
                    else
                        series.Title = title;
                }
                else
                    series.ToolTip = title;

                if (colour.ToArgb() == Color.Empty.ToArgb())
                    colour = Utility.Configuration.Settings.DarkTheme ? Color.White : Color.Black;
                else if (colour.R == BackColor.R && colour.G == BackColor.G && colour.B == BackColor.B)
                    colour = Utility.Colour.FromOxy(ForegroundColour);
                series.Color = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);

                series.ItemsSource = this.PopulateDataPointSeries(x, y, xAxisType, yAxisType);

                series.XAxisKey = xAxisType.ToString();
                series.YAxisKey = yAxisType.ToString();

                series.XFieldName = xFieldName;
                series.YFieldName = yFieldName;

                series.CanTrackerInterpolatePoints = false;

                bool filled = false;
                string oxyMarkerName = markerType.ToString();
                if (oxyMarkerName.StartsWith("Filled"))
                {
                    oxyMarkerName = oxyMarkerName.Remove(0, 6);
                    filled = true;
                }

                // Line type.
                LineStyle oxyLineType;
                if (Enum.TryParse<LineStyle>(lineType.ToString(), out oxyLineType))
                {
                    series.LineStyle = oxyLineType;
                    if (series.LineStyle == LineStyle.None)
                        series.Color = OxyColors.Transparent;
                }

                // Line thickness
                if (lineThickness == APSIM.Shared.Graphing.LineThickness.Thin)
                    series.StrokeThickness = 0.5;

                // Marker type.
                OxyPlot.MarkerType type;
                if (Enum.TryParse<OxyPlot.MarkerType>(oxyMarkerName, out type))
                {
                    series.MarkerType = type;
                }

                //MarkerSize = markerSize;
                series.MarkerSize = GetMarkerSizeNumericValue(markerSize) * markerModifier;

                series.MarkerStroke = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
                if (filled)
                {
                    series.MarkerFill = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
                    series.MarkerStroke = OxyColors.White;
                }

                this.plot1.Model.Series.Add(series);
                if (xError != null || yError != null)
                {
                    NamedScatterErrorSeries errorSeries = new(series.Name)
                    {
                        Title = series.Title,
                        ItemsSource = this.PopulateErrorPointSeries(x, y, xError, yError, xAxisType, yAxisType),
                        XAxisKey = xAxisType.ToString(),
                        YAxisKey = yAxisType.ToString(),
                        ErrorBarColor = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B),
                    };
                    this.plot1.Model.Series.Add(errorSeries);
                }
            }

        }

        private double GetMarkerSizeNumericValue(MarkerSize markerSize)
        {
            if (markerSize == MarkerSize.Large)
                return 9.0;

            if (markerSize == MarkerSize.Normal)
                return 7.0;

            if (markerSize == MarkerSize.Small)
                return 5.0;

            if (markerSize == MarkerSize.VerySmall)
                return 3.0;

            throw new NotImplementedException($"No supported marker size translation for {markerSize}");
        }

        /// <summary>
        /// Draw a bar series with the specified arguments.
        /// </summary>
        /// <param name="title">The series title</param>
        /// <param name="x">The x values for the series</param>
        /// <param name="y">The y values for the series</param>
        /// <param name="xAxisType">The axis type the x values are related to</param>
        /// <param name="yAxisType">The axis type the y values are related to</param>
        /// <param name="colour">The series color</param>
        /// <param name="showOnLegend">Show this series in the legend?</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawBar(
            string title,
            IEnumerable x,
            IEnumerable y,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType,
            Color colour,
            bool showOnLegend)
        {
            if (x != null && y != null)
            {
                var series = new ColumnXYSeries();
                if (showOnLegend)
                {
                    if (String.IsNullOrEmpty(title))
                        series.Title = "null";
                    else
                        series.Title = title;
                }
                series.FillColor = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
                series.StrokeColor = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
                series.ItemsSource = this.PopulateDataPointSeries(x, y, xAxisType, yAxisType);
                series.XAxisKey = xAxisType.ToString();
                series.YAxisKey = yAxisType.ToString();


                // By default, clicking on a datapoint (a bar) of a bar graph
                // will create a pop-up showing the x/y values at the beginning
                // and end of the bar. We override this here, so that it only
                // shows the x/y pair at the end of the bar. Perhaps we should
                // accept the tracker string as an argument to this function?
                series.TrackerFormatString = "{0}\n{1}: {2}\n{3}: {4}";
                this.plot1.Model.Series.Add(series);
            }
        }

        /// <summary>
        /// Draw an  area series with the specified arguments. A filled polygon is
        /// drawn with the x1, y1, x2, y2 coordinates.
        /// </summary>
        /// <param name="title">The series title</param>
        /// <param name="x1">The x1 values for the series</param>
        /// <param name="y1">The y1 values for the series</param>
        /// <param name="x2">The x2 values for the series</param>
        /// <param name="y2">The y2 values for the series</param>
        /// <param name="xAxisType">The axis type the x values are related to</param>
        /// <param name="yAxisType">The axis type the y values are related to</param>
        /// <param name="colour">The series color</param>
        /// <param name="showOnLegend">Show this series in the legend?</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawRegion(
            string title,
            IEnumerable x1,
            IEnumerable y1,
            IEnumerable x2,
            IEnumerable y2,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType,
            Color colour,
            bool showOnLegend)
        {
            AreaSeriesWithTracker series = new AreaSeriesWithTracker();
            series.TooltipTitle = title;
            if (x1.Count() > 0)
                series.XType = x1.Cast<object>().ToArray()[0].GetType();
            else
                series.XType = null;

            if (y1.Count() > 0)
                series.YType = y1.Cast<object>().ToArray()[0].GetType();
            else
                series.YType = null;

            series.Color = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
            series.Fill = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
            List<DataPoint> points = this.PopulateDataPointSeries(x1, y1, xAxisType, yAxisType);
            List<DataPoint> points2 = this.PopulateDataPointSeries(x2, y2, xAxisType, yAxisType);

            if (showOnLegend)
            {
                if (String.IsNullOrEmpty(title))
                    series.Title = "null";
                else
                    series.Title = title;
            }
            if (points != null && points2 != null)
            {
                foreach (DataPoint point in points)
                {
                    series.Points.Add(point);
                }

                foreach (DataPoint point in points2)
                {
                    series.Points2.Add(point);
                }
            }
            series.CanTrackerInterpolatePoints = false;

            this.plot1.Model.Series.Add(series);
        }

        /// <summary>
        /// Checks that the given data is equidistant. Shows a warning
        /// message if this is not true.
        /// </summary>
        /// <param name="x">Data to be tested.</param>
        private void EnsureMonotonic(double[] x)
        {
            double diff = x[1] - x[0];
            for (int i = 1; i < x.Length; i++)
            {
                double newDiff = x[i] - x[i - 1];
                if (!MathUtilities.FloatsAreEqual(diff, newDiff))
                    MasterView.ShowMessage($"WARNING: x data is not monotonic at index {i}; x = [..., {x[i - 2]}, {x[i - 1]}, {x[i]}, ...]", Models.Core.MessageType.Warning, withButton: false);
            }
        }

        /// <summary>
        /// Draw an area series with the specified arguments. Similar to a
        /// line series, but the area under the curve will be filled with colour.
        /// </summary>
        /// <param name="title">The series title</param>
        /// <param name="x">The x values for the series</param>
        /// <param name="y">The y values for the series</param>
        /// <param name="xAxisType">The axis type the x values are related to</param>
        /// <param name="yAxisType">The axis type the y values are related to</param>
        /// <param name="colour">The series color</param>
        /// <param name="showOnLegend">Show this series in the legend?</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawArea(
            string title,
            IEnumerable x,
            IEnumerable y,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType,
            Color colour,
            bool showOnLegend)
        {
            // Just use a region series (colours area between two curves), and use y = 0 for the second curve.
            List<double> y2 = new List<double>();
            y2.AddRange(Enumerable.Repeat(0d, ((ICollection)y).Count));

            DrawRegion(title, x, y2, x, y, xAxisType, yAxisType, colour, showOnLegend);
        }

        /// <summary>
        /// Draw a stacked area series with the specified arguments.Similar to
        /// an area series except that the area between this curve and the
        /// previous curve (or y = 0 if this is first) will be filled with
        /// colour. Currently this only works if y-data is numeric.
        /// </summary>
        /// <param name="title">The series title</param>
        /// <param name="x">The x values for the series</param>
        /// <param name="y">The y values for the series</param>
        /// <param name="xAxisType">The axis type the x values are related to</param>
        /// <param name="yAxisType">The axis type the y values are related to</param>
        /// <param name="colour">The series color</param>
        /// <param name="showOnLegend">Show this series in the legend?</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawStackedArea(
            string title,
            object[] x,
            double[] y,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType,
            Color colour,
            bool showOnLegend)
        {
            if (this.plot1.Model.Series.Count < 1 || plot1.Model.Series.OfType<OxyPlot.Series.LineSeries>().Count() < 1)
            {
                // This is the first series to be added to the chart. Just use
                // a region series (colours area between two curves), and use
                // y = 0 for the second curve.
                List<double> y0 = new List<double>();
                y0.AddRange(Enumerable.Repeat(0d, y.Length));
                DrawRegion(title, x, y, x, y0, xAxisType, yAxisType, colour, showOnLegend);
                return;
            }

            // Get x/y data from previous series
            var previous = plot1.Model.Series.OfType<OxyPlot.Series.LineSeries>().Last();

            // This will work if the previous series was an area series.
            double[] x1 = previous.Points.Select(p => p.X).ToArray();
            double[] y1 = previous.Points.Select(p => p.Y).ToArray();

            // This will work if the previous series was a line/scatter series.
            if (x1 == null || x1.Length < 1)
                x1 = previous.ItemsSource.Cast<DataPoint>().Select(p => p.X).ToArray();

            if (y1 == null || y1.Length < 1)
                y1 = previous.ItemsSource.Cast<DataPoint>().Select(p => p.Y).ToArray();

            if (x1 == null || x1.Length < 1 || y1 == null || y1.Length < 1)
                return;

            // Now, for each datapoint in the previous series, we need
            // to add its y-value onto the corresponding data point in
            // the new series so that this area series appears to sit
            // on top of the previous series.
            List<double> y2 = new List<double>();

            Type xType = x[0].GetType();
            bool xIsFloatingPoint = xType == typeof(double) || xType == typeof(float);

            for (int i = 0; i < x1.Length; i++)
            {
                double xVal = x1[i]; // x-value in the previous series

                // The previous series might not have exactly the same set of x
                // values as the new series. First we check if the new series
                // contains this x value. If it does not, we do a linear interp
                // to find an appropriate y-value.
                int index = -1;
                if (xIsFloatingPoint)
                    index = MathUtilities.SafeIndexOf(x.Cast<double>().ToList(), xVal);
                else if (xType == typeof(DateTime))
                    index = Array.IndexOf(x, DateTimeAxis.ToDateTime(xVal));
                else
                    index = i; // Array.IndexOf(x, xVal); // this is unlikely to work

                double yVal = y1[i];
                if (index >= 0)
                    yVal += y[i];
                else if (xIsFloatingPoint)
                    yVal += MathUtilities.LinearInterpReal(xVal, x.Cast<double>().ToArray(), y, out bool didInterp);
                y2.Add(yVal);
            }

            DrawRegion(title, x1, y2, x1, y1, xAxisType, yAxisType, colour, showOnLegend);

            // If the X data is not monotonic, the area will not be
            // filled with colour. In this case, show a warning to the
            // user so they know why their area series is not working.
            AreaSeries series = plot1.Model.Series.OfType<AreaSeries>().LastOrDefault();
            if (series != null)
            {
                EnsureMonotonic(series.Points.Select(p => p.X).ToArray());
                EnsureMonotonic(series.Points2.Select(p => p.X).ToArray());
            }
        }

        /// <summary>
        /// Draw a box-and-whisker plot.
        /// colour.
        /// </summary>
        /// <param name="title">The series title</param>
        /// <param name="x">The x values for the series</param>
        /// <param name="y">The y values for the series</param>
        /// <param name="xAxisType">The axis type the x values are related to</param>
        /// <param name="yAxisType">The axis type the y values are related to</param>
        /// <param name="colour">The series color</param>
        /// <param name="showOnLegend">Show this series in the legend?</param>
        /// <param name="lineType">Type of line to be used.</param>
        /// <param name="markerType">Type of marker to be used.</param>
        /// <param name="lineThickness">Line thickness.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawBoxPLot(
            string title,
            object[] x,
            double[] y,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType,
            Color colour,
            bool showOnLegend,
            LineType lineType,
            APSIM.Shared.Graphing.MarkerType markerType,
            APSIM.Shared.Graphing.LineThickness lineThickness)
        {
            if (x?.Length > 0 && y?.Length > 0)
            {
                BoxPlotSeries series = new BoxPlotSeries();
                series.Items = GetBoxPlotItems(x, y, xAxisType, yAxisType);
                if (showOnLegend)
                {
                    if (String.IsNullOrEmpty(title))
                        series.Title = "null";
                    else
                        series.Title = title;
                }


                // Line style
                if (Enum.TryParse(lineType.ToString(), out LineStyle oxyLineType))
                {
                    series.LineStyle = oxyLineType;
                    if (series.LineStyle == LineStyle.None)
                        series.Fill = OxyColors.Transparent;
                    series.Stroke = OxyColors.Transparent;
                }

                // Min/max lines = marker type
                string marker = markerType.ToString();
                if (marker.StartsWith("Filled"))
                    marker = marker.Remove(0, 6);

                if (Enum.TryParse(marker, out OxyPlot.MarkerType oxyMarkerType))
                    series.OutlierType = oxyMarkerType;

                // Line thickness
                if (lineThickness == APSIM.Shared.Graphing.LineThickness.Thin)
                {
                    double thickness = 0.5;
                    series.StrokeThickness = thickness;
                    series.MeanThickness = thickness;
                    series.MedianThickness = thickness;
                }

                // Colour
                if (colour.ToArgb() == Color.Empty.ToArgb())
                    colour = Utility.Configuration.Settings.DarkTheme ? Color.White : Color.Black;
                else if (colour.R == BackColor.R && colour.G == BackColor.G && colour.B == BackColor.B)
                    colour = Utility.Colour.FromOxy(ForegroundColour);

                OxyColor oxyColour = Utility.Colour.ToOxy(colour);
                series.Fill = oxyColour;
                series.Stroke = oxyColour;

                series.XAxisKey = xAxisType.ToString();
                series.YAxisKey = yAxisType.ToString();

                double width = 0.5;
                series.BoxWidth = width;
                series.WhiskerWidth = width;

                plot1.Model.Series.Add(series);

                OxyPlot.Axes.Axis xAxis = GetAxis(xAxisType);

                //xAxis.Minimum = 0 - width;
                //xAxis.Maximum = plot1.Model.Series.OfType<BoxPlotSeries>().Count() - 1 + width;
            }
        }

        private List<BoxPlotItem> GetBoxPlotItems(object[] x, double[] data,
                                                  APSIM.Shared.Graphing.AxisPosition xAxisType,
                                                  APSIM.Shared.Graphing.AxisPosition yAxisType)
        {
            data = data.Where(d => !double.IsNaN(d)).ToArray();
            double[] fiveNumberSummary = data.FiveNumberSummary();
            double min = fiveNumberSummary[0];
            double lowerQuartile = fiveNumberSummary[1];
            double median = fiveNumberSummary[2];
            double upperQuartile = fiveNumberSummary[3];
            double max = fiveNumberSummary[4];

            double xValue = plot1.Model.Series.OfType<BoxPlotSeries>().Count();
            if (x[0] is double)
            {
                xValue = (double)x[0];
                EnsureAxisExists(xAxisType, typeof(double));
            }
            else
            {
                EnsureAxisExists(xAxisType, typeof(string));
                CategoryAxis axis = GetAxis(xAxisType) as CategoryAxis;
                if (axis != null)
                {
                    var xLabel = x[0].ToString();
                    int index = axis.Labels.IndexOf(xLabel);
                    if (index == -1)
                    {
                        axis.Labels.Add(xLabel);
                        index = axis.Labels.IndexOf(xLabel);
                    }
                    xValue = index;
                }
            }

            EnsureAxisExists(yAxisType, typeof(double));

            return new List<BoxPlotItem>() { new BoxPlotItem(xValue, min, lowerQuartile, median, upperQuartile, max) };
        }

        /// <summary>
        /// Draw text on the graph at the specified coordinates.
        /// </summary>
        /// <param name="text">The text to put on the graph</param>
        /// <param name="x">The x position in graph coordinates</param>
        /// <param name="y">The y position in graph coordinates</param>
        /// <param name="leftAlign">Left align the text?</param>
        /// <param name="topAlign">Top align the text?</param>
        /// <param name="textRotation">Text rotation</param>
        /// <param name="xAxisType">The axis type the x value relates to</param>
        /// <param name="yAxisType">The axis type the y value are relates to</param>
        /// <param name="colour">The color of the text</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawText(
            string text,
            object x,
            object y,
            bool leftAlign,
            bool topAlign,
            double textRotation,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType,
            Color colour)
        {
            OxyPlot.Annotations.TextAnnotation annotation = new OxyPlot.Annotations.TextAnnotation();
            annotation.Text = text;
            if (leftAlign)
                annotation.TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left;
            else
                annotation.TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right;
            if (topAlign)
                annotation.TextVerticalAlignment = VerticalAlignment.Top;
            else
                annotation.TextVerticalAlignment = VerticalAlignment.Bottom;
            annotation.Stroke = BackColor;
            annotation.Font = Font;
            annotation.TextRotation = textRotation;

            double xPosition = 0.0;
            if (x is DateTime)
                xPosition = DateTimeAxis.ToDouble(x);
            else
                xPosition = Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
            double yPosition = 0.0;
            if ((double)y == double.MinValue)
            {
                yPosition = AxisMinimum(yAxisType);
                annotation.TextVerticalAlignment = VerticalAlignment.Bottom;
            }
            else if ((double)y == double.MaxValue)
                yPosition = AxisMaximum(yAxisType);
            else
                yPosition = (double)y;
            annotation.TextPosition = new DataPoint(xPosition, yPosition);

            if (colour == Color.Empty)
                annotation.TextColor = ForegroundColour;
            else if (colour.R == BackColor.R && colour.G == BackColor.G && colour.B == BackColor.B)
                // We never want text to be the same as the background colour.
                annotation.TextColor = ForegroundColour;
            else
                annotation.TextColor = Utility.Colour.ToOxy(colour);

            this.plot1.Model.Annotations.Add(annotation);
        }
        /// <summary>
        /// Draw line on the graph at the specified coordinates.
        /// </summary>
        /// <param name="x1">The x1 position in graph coordinates</param>
        /// <param name="y1">The y1 position in graph coordinates</param>
        /// <param name="x2">The x2 position in graph coordinates</param>
        /// <param name="y2">The y2 position in graph coordinates</param>
        /// <param name="type">Line type</param>
        /// <param name="textRotation">Text rotation</param>
        /// <param name="thickness">Line thickness</param>
        /// <param name="colour">The color of the text</param>
        /// <param name="inFrontOfSeries">Show annotation in front of series?</param>
        /// <param name="toolTip">Annotation tool tip.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        public void DrawLine(
            object x1,
            object y1,
            object x2,
            object y2,
            LineType type,
            APSIM.Shared.Graphing.LineThickness thickness,
            Color colour,
            bool inFrontOfSeries,
            string toolTip)
        {
            double x1Position = 0.0;
            if (x1 is DateTime)
                x1Position = DateTimeAxis.ToDouble(x1);
            else
                x1Position = Convert.ToDouble(x1, System.Globalization.CultureInfo.InvariantCulture);
            double y1Position = 0.0;
            if ((double)y1 == double.MinValue)
                y1Position = AxisMinimum(APSIM.Shared.Graphing.AxisPosition.Left);
            else if ((double)y1 == double.MaxValue)
                y1Position = AxisMaximum(APSIM.Shared.Graphing.AxisPosition.Left);
            else
                y1Position = (double)y1;
            double x2Position = 0.0;
            if (x2 is DateTime)
                x2Position = DateTimeAxis.ToDouble(x2);
            else
                x2Position = Convert.ToDouble(x2, System.Globalization.CultureInfo.InvariantCulture);
            double y2Position = 0.0;
            if ((double)y2 == double.MinValue)
                y2Position = AxisMinimum(APSIM.Shared.Graphing.AxisPosition.Left);
            else if ((double)y2 == double.MaxValue)
                y2Position = AxisMaximum(APSIM.Shared.Graphing.AxisPosition.Left);
            else
                y2Position = (double)y2;

            OxyPlot.Annotations.Annotation annotation;

            if (x1Position == x2Position)
            {
                var lineAnnotation = new OxyPlot.Annotations.LineAnnotation();
                lineAnnotation.X = x1Position;
                lineAnnotation.Y = y1Position;
                lineAnnotation.MinimumX = x1Position;
                lineAnnotation.MinimumY = y1Position;
                lineAnnotation.MaximumX = x2Position;
                lineAnnotation.MaximumY = y2Position;
                lineAnnotation.Type = LineAnnotationType.Vertical;
                lineAnnotation.Color = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
                if (thickness == APSIM.Shared.Graphing.LineThickness.Thin)
                    lineAnnotation.StrokeThickness = 0.5;
                annotation = lineAnnotation;
            }
            else
            {
                var rectangleAnnotation = new RectangleAnnotation();
                rectangleAnnotation.MinimumX = x1Position;
                rectangleAnnotation.MinimumY = y1Position;
                rectangleAnnotation.MaximumX = x2Position;
                rectangleAnnotation.MaximumY = y2Position;
                rectangleAnnotation.Fill = OxyColor.FromArgb(colour.A, colour.R, colour.G, colour.B);
                annotation = rectangleAnnotation;
            }
            if (inFrontOfSeries)
                annotation.Layer = AnnotationLayer.AboveSeries;
            else
                annotation.Layer = AnnotationLayer.BelowSeries;
            annotation.ToolTip = toolTip;
            this.plot1.Model.Annotations.Add(annotation);
        }

        /// <summary>
        /// Format the specified axis.
        /// </summary>
        /// <param name="axisType">The axis type to format</param>
        /// <param name="title">The axis title. If null then a default axis title will be shown</param>
        /// <param name="inverted">Invert the axis?</param>
        /// <param name="minimum">Minimum axis scale</param>
        /// <param name="maximum">Maximum axis scale</param>
        /// <param name="interval">Axis scale interval</param>
        /// <param name="crossAtZero">Axis crosses at zero?</param>
        /// <param name="labelOnOneLine">Show Axis Label on one line</param>
        public void FormatAxis(
            APSIM.Shared.Graphing.AxisPosition axisType,
            string title,
            bool inverted,
            double minimum,
            double maximum,
            double interval,
            bool crossAtZero,
            bool labelOnOneLine)
        {
            OxyPlot.Axes.Axis oxyAxis = this.GetAxis(axisType);

            if (oxyAxis != null)
            {
                oxyAxis.AxislineColor = this.ForegroundColour;
                oxyAxis.ExtraGridlineColor = this.ForegroundColour;
                oxyAxis.MajorGridlineColor = this.ForegroundColour;
                oxyAxis.MinorGridlineColor = this.ForegroundColour;
                oxyAxis.TicklineColor = this.ForegroundColour;
                oxyAxis.MinorTicklineColor = this.ForegroundColour;
                oxyAxis.TitleColor = this.ForegroundColour;
                oxyAxis.TextColor = this.ForegroundColour;

                oxyAxis.Title = title.Trim();
                oxyAxis.MinorTickSize = 0;
                oxyAxis.AxislineStyle = LineStyle.Solid;
                oxyAxis.AxisTitleDistance = 10;
                oxyAxis.PositionAtZeroCrossing = crossAtZero;

                if (labelOnOneLine)
                {
                    string newline = Environment.NewLine;
                    oxyAxis.Title = oxyAxis.Title.Replace(newline, ", ");
                }

                if (inverted)
                {
                    oxyAxis.StartPosition = 1;
                    oxyAxis.EndPosition = 0;
                }
                else
                {
                    oxyAxis.StartPosition = 0;
                    oxyAxis.EndPosition = 1;
                }

                double min = minimum;
                if (!double.IsNaN(minimum))
                    oxyAxis.Minimum = minimum;
                else
                    min = AxisMinimum(axisType);

                double max = maximum;
                if (!double.IsNaN(maximum))
                    oxyAxis.Maximum = maximum;
                else
                    max = AxisMaximum(axisType);

                if (max <= min)
                    max = min + 1;

                oxyAxis.AbsoluteMinimum = min;
                oxyAxis.AbsoluteMaximum = max;

                if (oxyAxis is DateTimeAxis dateOxyAxis)
                {
                    if (!double.IsNaN(interval))
                    {
                        FormatAxisGivenInterval(interval, dateOxyAxis);
                    }
                    else
                    {
                        FormatAxisWithoutInterval(dateOxyAxis);
                    }
                }
                if (oxyAxis is LinearAxis && oxyAxis is not DateTimeAxis && (oxyAxis.ActualStringFormat == null || !oxyAxis.ActualStringFormat.Contains("yyyy")))
                {
                    FormatLinearAxis(oxyAxis);
                }
            }
        }

        private static void FormatLinearAxis(OxyPlot.Axes.Axis oxyAxis)
        {
            // We want the axis labels to always have a leading 0 when displaying decimal places.
            // e.g. we want 0.5 rather than .5

            // Use the current culture to format the string.
            string st = oxyAxis.ActualMajorStep.ToString(CultureInfo.InvariantCulture);

            // count the number of decimal places in the above string.
            int pos = st.IndexOfAny(".,".ToCharArray());
            if (pos != -1)
            {
                int numDecimalPlaces = st.Length - pos - 1;
                oxyAxis.StringFormat = "F" + numDecimalPlaces.ToString();
            }
        }

        private static void FormatAxisGivenInterval(double interval, DateTimeAxis dateOxyAxis)
        {
            double FIVE_YEARS_IN_DAYS = 1825;
            if (interval < 30)
            {
                dateOxyAxis.MinorIntervalType = DateTimeIntervalType.Days;
                dateOxyAxis.StringFormat = "dd/MM/yyyy";
                dateOxyAxis.MajorTickSize = 1;
                dateOxyAxis.FontSize = 12;
            }
            else if (interval >= 30 && interval <= FIVE_YEARS_IN_DAYS)
            {
                dateOxyAxis.MinorIntervalType = DateTimeIntervalType.Months;
                dateOxyAxis.StringFormat = "MM-yyyy";
                dateOxyAxis.MajorTickSize = 3;
                dateOxyAxis.FontSize = 12;

            }
            else if (interval > FIVE_YEARS_IN_DAYS)
            {
                dateOxyAxis.MinorIntervalType = DateTimeIntervalType.Years;
                dateOxyAxis.StringFormat = "yyyy";
            }
            else dateOxyAxis.StringFormat = "dd/MM/yyyy";
        }

        private void FormatAxisWithoutInterval(DateTimeAxis dateOxyAxis)
        {
            int numDays = (largestDate - smallestDate).Days;
            if (numDays < 100)
            {
                dateOxyAxis.IntervalType = DateTimeIntervalType.Days;
                dateOxyAxis.StringFormat = "dd/MM/yyyy";
                dateOxyAxis.FontSize = 12;
            }
            else if (numDays <= 366)
            {
                dateOxyAxis.IntervalType = DateTimeIntervalType.Months;
                dateOxyAxis.StringFormat = "dd-MMM";
                dateOxyAxis.FontSize = 12;

            }
            else if (numDays <= 720)
            {
                dateOxyAxis.IntervalType = DateTimeIntervalType.Months;
                dateOxyAxis.StringFormat = "MMM-yyyy";
                dateOxyAxis.FontSize = 12;

            }
            else
            {
                dateOxyAxis.IntervalType = DateTimeIntervalType.Years;
                dateOxyAxis.StringFormat = "yyyy";
            }
        }

        /// <summary>
        /// Format the legend.
        /// </summary>
        /// <param name="legendPositionType">Position of the legend</param>
        /// <param name="orientation">Orientation of items in the legend.</param>
        /// <param name="namesOfSeriesToRemove">Names of Series to remove.</param>
        /// <param name="reselectedSeriesNames">Names of series to reenable.</param>
        public void FormatLegend(LegendPosition legendPositionType, LegendOrientation orientation, List<string> namesOfSeriesToRemove=null, List<string> reselectedSeriesNames=null)
        {
            if (!plot1.Model.Legends.Any())
                plot1.Model.Legends.Add(new OxyPlot.Legends.Legend());
            if (Enum.TryParse(legendPositionType.ToString(), out OxyLegendPosition oxyLegendPosition))
            {
                this.plot1.Model.SetLegendFont(Font);
                this.plot1.Model.SetLegendFontSize(FontSize);
                this.plot1.Model.SetLegendPosition(oxyLegendPosition);
                if (Enum.TryParse(orientation.ToString(), out OxyLegendOrientation legendOrientation))
                    plot1.Model.SetLegendOrientation(legendOrientation);
            }

            this.plot1.Model.SetLegendSymbolLength(30);
            // Sort the list of series by this view's line or marker type.

            foreach (var series in plot1.Model.Series)
            {
                if (series is INameableSeries)
                {
                    // Reenable legend items if previously unselected but reselected.
                    if (reselectedSeriesNames != null)
                        if (reselectedSeriesNames.Contains((series as INameableSeries).Name))
                        {
                            if (series is NamedScatterErrorSeries == false)
                            {
                                series.Title = (series as INameableSeries).Name;
                            }
                            series.IsVisible = true;
                        }

                    // Remove series that match list of names to remove.
                    if (namesOfSeriesToRemove != null)
                        foreach (var nameToRemove in namesOfSeriesToRemove)
                            if ((series as INameableSeries).Name == nameToRemove)
                                series.IsVisible = false;
                }
                // Tidy up duplicate names.
                var matchingSeries = FindMatchingSeries(series);
                if (matchingSeries != null)
                {
                    // Make it so it doesn't show in legend.
                    matchingSeries.Title = null;
                }
            }
        }


        /// <summary>
        /// Format the title.
        /// </summary>
        /// <param name="text">Text of the title</param>
        public void FormatTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                plot1.Model.Title = null;
            else
            {
                this.plot1.Model.Title = text;
                this.plot1.Model.TitleFont = Font;
                this.plot1.Model.TitleFontSize = 30;
                this.plot1.Model.TitleFontWeight = OxyPlot.FontWeights.Bold;
            }
        }

        /// <summary>
        /// Format the footer.
        /// </summary>
        /// <param name="text">The text for the footer</param>
        /// <param name="italics">Italics?</param>
        public void FormatCaption(string text, bool italics)
        {
            if (text != null && text != string.Empty)
            {
                captionLabel.Text = text;
                if (italics)
                    text = "<i>" + text + "</i>";
                captionLabel.Markup = text;
            }
            else
            {
                captionLabel.Text = "          ";
            }

        }

        /// <summary>
        /// Show the specified editor.
        /// </summary>
        /// <param name="editorObj">The editor to show</param>
        /// <param name="expanderLabel">Text to be displayed in the editor.</param>
        public void ShowEditorPanel(object editorObj, string expanderLabel)
        {
            Widget editor = editorObj as Widget;
            if (editor != null)
            {
                expander1.Foreach(delegate (Widget widget)
                {
                    if (widget != label2)
                    {
                        expander1.Remove(widget);
                        widget.Dispose();
                    }
                });
                expander1.Add(editor);
                expander1.Visible = true;
                expander1.Expanded = true;
                label2.Text = expanderLabel;
            }
        }

        /// <summary>
        /// Export the graph to the specified 'bitmap'
        /// </summary>
        /// <param name="bitmap">Bitmap to write to</param>
        /// <param name="r">Desired image size.</param>
        /// <param name="legendOutside">Put legend outside of graph?</param>
        public void Export(out Gdk.Pixbuf bitmap, Rectangle r, bool legendOutside)
        {
            MemoryStream stream = new MemoryStream();
            PngExporter pngExporter = new PngExporter();
            pngExporter.Width = r.Width;
            pngExporter.Height = r.Height;
            pngExporter.Export(plot1.Model, stream);
            bitmap = new Gdk.Pixbuf(stream);
        }

        /// <summary>
        /// Export the graph to the clipboard
        /// </summary>
        public void ExportToClipboard()
        {
            string fileName = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), ".png");
            (int, int) size = Configuration.Settings.GetGraphSize();
            int width = size.Item1;
            int height = size.Item2;
            PngExporter.Export(plot1.Model, fileName, width, height, new Cairo.SolidPattern(new Cairo.Color(BackColor.R / 255.0, BackColor.G / 255.0, BackColor.B / 255.0, 1), false));
            Clipboard cb = MainWidget.GetClipboard(Gdk.Selection.Clipboard);
            cb.Image = new Gdk.Pixbuf(fileName);
        }

        /// <summary>
        /// Add an action (on context menu) on the memo.
        /// </summary>
        /// <param name="menuText">Menu item text</param>
        /// <param name="ticked">Menu ticked?</param>
        /// <param name="onClick">Event handler for menu item click</param>
        public void AddContextAction(string menuText, System.EventHandler onClick)
        {
            MenuItem item = new MenuItem(menuText);
            item.Activated += onClick;
            popup.Append(item);
            popup.ShowAll();
        }

        /// <summary>
        /// Add an action (on context menu) on the series grid.
        /// </summary>
        /// <param name="menuItemText">The text of the menu item</param>
        /// <param name="onClick">The event handler to call when menu is selected</param>
        /// <param name="active">Should the context item be active?</param>
        public void AddContextOption(string menuItemText, System.EventHandler onClick, bool active)
        {
            CheckMenuItem item = null;
            foreach (Widget w in popup)
            {
                CheckMenuItem oldItem = w as CheckMenuItem;
                if (oldItem != null)
                {
                    AccelLabel itemText = oldItem.Child as AccelLabel;
                    if (itemText.Text == menuItemText)
                    {
                        item = oldItem;
                        _ = item.DetachHandler("activate");
                    }
                }
            }
            if (item == null)
            {
                item = new CheckMenuItem(menuItemText);
                item.DrawAsRadio = false;
                popup.Append(item);
                popup.ShowAll();
            }
            // Be sure to set the Active property before attaching the Activated event, since
            // the event handler will call this function again when Active is changed.
            // This can lead to infinite recursion. This is also why we deactivate the handler
            // (done above) when the item is already found in the menu
            item.Active = active;
            item.Activated += onClick;
        }

        /// <summary>
        /// Find a graph series that has the same title as the specified series.
        /// </summary>
        /// <param name="series">The series to match.</param>
        /// <returns>The series or null if not found.</returns>
        private OxyPlot.Series.Series FindMatchingSeries(OxyPlot.Series.Series series)
        {
            foreach (var s in plot1.Model.Series)
            {
                if (s != series && s.Title == series.Title && series is NamedScatterErrorSeries == false)
                    return s;

            }
            return null;
        }


        /// <summary>
        /// Populate the specified DataPointSeries with data from the data table.
        /// </summary>
        /// <param name="x">The x values</param>
        /// <param name="y">The y values</param>
        /// <param name="xAxisType">The x axis the data is associated with</param>
        /// <param name="yAxisType">The y axis the data is associated with</param>
        /// <returns>A list of 'DataPoint' objects ready to be plotted</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        private List<DataPoint> PopulateDataPointSeries(
            IEnumerable x,
            IEnumerable y,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType)
        {
            List<DataPoint> points = new List<DataPoint>();
            if (x != null && y != null && ((ICollection)x).Count > 0 && ((ICollection)y).Count > 0)
            {
                List<double[]> arrays = GetDataPointValues(new List<IEnumerator>() {x.GetEnumerator(), y.GetEnumerator()}, 
                                                            new List<APSIM.Shared.Graphing.AxisPosition>() {xAxisType, yAxisType});
                double[] xValues = arrays[0];
                double[] yValues = arrays[1];

                // Create data points
                for (int i = 0; i < Math.Min(xValues.Length, yValues.Length); i++)
                    if (!double.IsNaN(xValues[i]) && !double.IsNaN(yValues[i]))
                        points.Add(new DataPoint(xValues[i], yValues[i]));

                return points;
            }
            else
                return null;
        }

        /// <summary>
        /// Populate the specified DataPointSeries with data from the data table.
        /// </summary>
        /// <param name="x">The x values</param>
        /// <param name="y">The y values</param>
        /// <param name="xError">The error size values for the x-axis.</param>
        /// <param name="yError">The error size values</param>
        /// <param name="xAxisType">The x axis the data is associated with</param>
        /// <param name="yAxisType">The y axis the data is associated with</param>
        /// <returns>A list of 'DataPoint' objects ready to be plotted</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation", Justification = "Reviewed.")]
        private List<ScatterErrorPoint> PopulateErrorPointSeries(
            IEnumerable x,
            IEnumerable y,
            IEnumerable xError,
            IEnumerable yError,
            APSIM.Shared.Graphing.AxisPosition xAxisType,
            APSIM.Shared.Graphing.AxisPosition yAxisType)
        {
            List<ScatterErrorPoint> points = new List<ScatterErrorPoint>();
            if (x != null && y != null && (yError != null || xError != null))
            {
                List<double[]> arrays = GetDataPointValues(new List<IEnumerator>() {x.GetEnumerator(), y.GetEnumerator(), xError?.GetEnumerator(), yError?.GetEnumerator()}, 
                                                            new List<APSIM.Shared.Graphing.AxisPosition>() {xAxisType, yAxisType, xAxisType, yAxisType});
                double[] xValues = arrays[0];
                double[] yValues = arrays[1];
                double[] xErrorValues = arrays[2];
                double[] yErrorValues = arrays[3];

                if (xValues.Length == yValues.Length)
                {
                    if (xValues.Length == xErrorValues.Length && xErrorValues.Length == yErrorValues.Length)
                    {
                        // We have error data for both x and y series.
                        for (int i = 0; i < xValues.Length; i++)
                            if (!double.IsNaN(xValues[i]) && !double.IsNaN(yValues[i]) && !double.IsNaN(yErrorValues[i]) && !double.IsNaN(xErrorValues[i]))
                                points.Add(new ScatterErrorPoint(xValues[i], yValues[i], xErrorValues[i], yErrorValues[i], 0));

                        return points;
                    }
                    else if (xValues.Length == xErrorValues.Length)
                    {
                        // We have error data for the x series.
                        for (int i = 0; i < xValues.Length; i++)
                            if (!double.IsNaN(xValues[i]) && !double.IsNaN(yValues[i]) && !double.IsNaN(xErrorValues[i]))
                                points.Add(new ScatterErrorPoint(xValues[i], yValues[i], xErrorValues[i], 0, 0));

                        return points;
                    }
                    else if (yValues.Length == yErrorValues.Length)
                    {
                        // We have error data for the y series.
                        for (int i = 0; i < xValues.Length; i++)
                            if (!double.IsNaN(xValues[i]) && !double.IsNaN(yValues[i]) && !double.IsNaN(yErrorValues[i]))
                                points.Add(new ScatterErrorPoint(xValues[i], yValues[i], 0, yErrorValues[i], 0));

                        return points;
                    }
                }
            }
            return null;
        }

        private List<double[]> GetDataPointValues(List<IEnumerator> enumerators, List<APSIM.Shared.Graphing.AxisPosition> axisTypes)
        {
            //NOTE: This function only looks at the first element of each enumerator to get the type, 
            //      this could lead to mistakes if there is a mix of types in a column of data.
            List<List<double>> output = new List<List<double>>();
            List<int> indiciesToRemove = new List<int>();

            for(int i = 0; i < enumerators.Count; i++)
            {
                IEnumerator enumerator = enumerators[i];
                APSIM.Shared.Graphing.AxisPosition axisType = axisTypes[i];
                List<double> values = new List<double>();
                int index = 0;
                bool hasValues = true;
                if (enumerator == null || !enumerator.MoveNext()) //moves to first value, returns false if can't
                    hasValues = false;
                    
                if (hasValues)
                {
                    bool isDate = false;
                    bool isDouble = false;
                    bool isString = false;
                    if (enumerator.Current.GetType() == typeof(DateTime)) {
                        isDate = true;
                    }
                    if (enumerator.Current.GetType() == typeof(double) || enumerator.Current.GetType() == typeof(float))
                    {
                        isDouble = true;
                    }
                    if (!isDate && !isDouble)
                    {
                        //check if double stored as a string
                        if (double.TryParse(enumerator.Current.ToString(), out double parseOut)) 
                            isDouble = true;

                        isString = true;
                    }
                    enumerator.Reset();//reset poition so the while loops work in the next section
                    if (isDate)
                    {
                        this.EnsureAxisExists(axisType, typeof(DateTime));
                        DateTime defaultDate = new DateTime();
                        smallestDate = DateTime.MaxValue;
                        largestDate = DateTime.MinValue;
                        while (enumerator.MoveNext())
                        {
                            DateTime date = Convert.ToDateTime(enumerator.Current, CultureInfo.InvariantCulture);
                            if (date != defaultDate)
                            {
                                values.Add(DateTimeAxis.ToDouble(date));
                                if (date < smallestDate)
                                    smallestDate = date;
                                if (date > largestDate)
                                    largestDate = date;
                            }
                            else 
                            {
                                MasterView.ShowMessage($"An empty datetime cell was found and excluded from the graph.", Models.Core.MessageType.Warning, overwrite: false);
                                values.Add(0); //leave a 0 in this entry so that the indexs line up still for later.
                                indiciesToRemove.Add(index);
                            }
                            index += 1;
                        }
                    }
                    else if (isDouble)
                    {
                        this.EnsureAxisExists(axisType, typeof(double));
                        while (enumerator.MoveNext())
                        {
                            double value = 0;
                            if (isString)
                                double.TryParse(enumerator.Current.ToString(), out value);
                            else
                                value = Convert.ToDouble(enumerator.Current, CultureInfo.InvariantCulture);

                            values.Add(value);
                            index += 1;
                        }
                    }
                    else if (isString)
                    {
                        this.EnsureAxisExists(axisType, typeof(string));
                        CategoryAxis axis = GetAxis(axisType) as CategoryAxis;
                        if (axis != null)
                        {
                            while (enumerator.MoveNext())
                            {
                                int axisIndex = axis.Labels.IndexOf(enumerator.Current.ToString());
                                if (axisIndex == -1) {
                                    axis.Labels.Add(enumerator.Current.ToString());
                                    axisIndex = axis.Labels.Count - 1;
                                }
                                
                                values.Add(axisIndex);
                                index += 1;
                            }
                        }
                    }
                }
                output.Add(values);
            }

            int length = output[0].Count;
            for (int i = 0; i < output.Count; i++)
            {
                if (output[i].Count > 0 && output[i].Count != length) //we need to check if more than 0 so that empty axis are skipped (like null error axis)
                    throw new Exception("XY point pairs are misaligned. Array of X values and array of Y values have different lengths.");

                for (int j = output[i].Count-1; j >= 0; j--)
                {
                    if (indiciesToRemove.Contains(j))
                        output[i].RemoveAt(j);
                }
            }

            List<double[]> outputArrays = new List<double[]>();
            for (int i = 0; i < output.Count; i++)
            {
                outputArrays.Add(output[i].ToArray());
            }

            return outputArrays;
        }

        /// <summary>
        /// Ensure the specified X exists. Uses the 'DataType' property of the DataColumn
        /// to determine the type of axis.
        /// </summary>
        /// <param name="axisType">The axis type to check</param>
        /// <param name="dataType">The data type of the axis</param>
        private void EnsureAxisExists(APSIM.Shared.Graphing.AxisPosition axisType, Type dataType)
        {
            // Make sure we have an x axis at the correct position.
            if (this.GetAxis(axisType) == null)
            {
                OxyPlot.Axes.AxisPosition position = this.AxisTypeToPosition(axisType);
                OxyPlot.Axes.Axis axisToAdd;
                if (dataType == typeof(DateTime))
                {
                    axisToAdd = new DateTimeAxis();
                }
                else if (dataType == typeof(double))
                {
                    axisToAdd = new LinearAxis();
                }
                else
                {
                    axisToAdd = new CategoryAxis();
                }

                axisToAdd.Position = position;
                axisToAdd.Key = axisType.ToString();
                this.plot1.Model.Axes.Add(axisToAdd);
            }
        }

        /// <summary>
        /// Return an axis that has the specified AxisType. Returns null if not found.
        /// </summary>
        /// <param name="axisType">The axis type to retrieve </param>
        /// <returns>The axis</returns>
        public OxyPlot.Axes.Axis GetAxis(APSIM.Shared.Graphing.AxisPosition axisType)
        {
            int i = this.GetAxisIndex(axisType);
            if (i == -1)
                return null;
            else
                return this.plot1.Model.Axes[i];
        }

        /// <summary>
        /// Return an axis that has the specified AxisType. Returns null if not found.
        /// </summary>
        /// <param name="axisType">The axis type to retrieve </param>
        /// <returns>The axis</returns>
        private int GetAxisIndex(APSIM.Shared.Graphing.AxisPosition axisType)
        {
            OxyPlot.Axes.AxisPosition position = this.AxisTypeToPosition(axisType);
            for (int i = 0; i < this.plot1.Model.Axes.Count; i++)
            {
                if (this.plot1.Model.Axes[i].Position == position)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Convert the Axis.AxisType into an OxyPlot.AxisPosition.
        /// </summary>
        /// <param name="type">The axis type</param>
        /// <returns>The position of the axis.</returns>
        private OxyPlot.Axes.AxisPosition AxisTypeToPosition(APSIM.Shared.Graphing.AxisPosition type)
        {
            if (type == APSIM.Shared.Graphing.AxisPosition.Bottom)
            {
                return OxyPlot.Axes.AxisPosition.Bottom;
            }
            else if (type == APSIM.Shared.Graphing.AxisPosition.Left)
            {
                return OxyPlot.Axes.AxisPosition.Left;
            }
            else if (type == APSIM.Shared.Graphing.AxisPosition.Top)
            {
                return OxyPlot.Axes.AxisPosition.Top;
            }

            return OxyPlot.Axes.AxisPosition.Right;
        }

        /// <summary>
        /// Convert the OxyPlot.AxisPosition into an Axis.AxisType.
        /// </summary>
        /// <param name="type">The axis type</param>
        /// <returns>The position of the axis.</returns>
        private APSIM.Shared.Graphing.AxisPosition AxisPositionToType(OxyPlot.Axes.AxisPosition type)
        {
            if (type == OxyPlot.Axes.AxisPosition.Bottom)
                return APSIM.Shared.Graphing.AxisPosition.Bottom;
            else if (type == OxyPlot.Axes.AxisPosition.Left)
                return APSIM.Shared.Graphing.AxisPosition.Left;
            else if (type == OxyPlot.Axes.AxisPosition.Top)
                return APSIM.Shared.Graphing.AxisPosition.Top;

            return APSIM.Shared.Graphing.AxisPosition.Right;
        }

        /// <summary>
        /// User has double clicked somewhere on a graph.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void OnMouseDoubleClick(object sender, OxyMouseDownEventArgs e)
        {
            try
            {
                OnPlotClick?.Invoke(this, EventArgs.Empty);
                OnAxisClick?.Invoke(APSIM.Shared.Graphing.AxisPosition.Bottom);
                OnLegendClick?.Invoke(this, new LegendClickArgs());
                OnTitleClick?.Invoke(this, EventArgs.Empty);
                OnAnnotationClick?.Invoke(this, EventArgs.Empty);
                Point location = new Point((int)e.Position.X, (int)e.Position.Y);
                Cairo.Rectangle plotRect = this.plot1.Model.PlotArea.ToRect(false);
                Rectangle plotArea = new Rectangle((int)plotRect.X, (int)plotRect.Y, (int)plotRect.Width, (int)plotRect.Height);

                IEnumerable<Cairo.Rectangle> legends;

                legends = plot1.Model.Legends.Select(l => l.LegendArea.ToRect(true));

                foreach (Cairo.Rectangle legendRect in legends)
                {
                    Rectangle legendArea = new Rectangle((int)legendRect.X, (int)legendRect.Y, (int)legendRect.Width, (int)legendRect.Height);
                    if (legendArea.Contains(location))
                    {
                        int y = Convert.ToInt32(location.Y - legendRect.Y, CultureInfo.InvariantCulture);
                        int itemHeight = Convert.ToInt32(legendRect.Height, CultureInfo.InvariantCulture) / this.plot1.Model.Series.Count;
                        int seriesIndex = y / itemHeight;
                        if (this.OnLegendClick != null)
                        {
                            LegendClickArgs args = new LegendClickArgs();
                            args.SeriesIndex = seriesIndex;
                            args.ControlKeyPressed = e.IsControlDown;
                            this.OnLegendClick.Invoke(sender, args);
                            return;
                        }
                    }
                }
                if (plotArea.Contains(location))
                {
                    bool userClickedOnAnnotation = false;
                    foreach (var annotation in this.plot1.Model.Annotations)
                    {
                        var result = annotation.HitTest(new HitTestArguments(new ScreenPoint(location.X, location.Y), 10.0));
                        if (result != null)
                        {
                            userClickedOnAnnotation = true;
                            OnAnnotationClick?.Invoke(this, new EventArgs());
                        }
                    }

                    if (!userClickedOnAnnotation && this.OnPlotClick != null)
                        this.OnPlotClick.Invoke(sender, e);
                }
                else
                {
                    Rectangle leftAxisArea = new Rectangle(0, plotArea.Y, plotArea.X, plotArea.Height);
                    Rectangle titleArea = new Rectangle(plotArea.X, 0, plotArea.Width, plotArea.Y);
                    Rectangle topAxisArea = new Rectangle(plotArea.X, 0, plotArea.Width, 0);

                    if (this.GetAxis(APSIM.Shared.Graphing.AxisPosition.Top) != null)
                    {
                        titleArea = new Rectangle(plotArea.X, 0, plotArea.Width, plotArea.Y / 2);
                        topAxisArea = new Rectangle(plotArea.X, plotArea.Y / 2, plotArea.Width, plotArea.Y / 2);
                    }

                    Rectangle rightAxisArea = new Rectangle(plotArea.Right, plotArea.Top, MainWidget.Allocation.Width - plotArea.Right, plotArea.Height);
                    Rectangle bottomAxisArea = new Rectangle(plotArea.Left, plotArea.Bottom, plotArea.Width, MainWidget.Allocation.Height - plotArea.Bottom);
                    if (titleArea.Contains(location))
                    {
                        if (this.OnTitleClick != null)
                        {
                            this.OnTitleClick(sender, e);
                        }
                    }

                    if (this.OnAxisClick != null)
                    {
                        if (leftAxisArea.Contains(location) && GetAxis(APSIM.Shared.Graphing.AxisPosition.Left) != null)
                        {
                            this.OnAxisClick.Invoke(APSIM.Shared.Graphing.AxisPosition.Left);
                        }
                        else if (topAxisArea.Contains(location) && GetAxis(APSIM.Shared.Graphing.AxisPosition.Top) != null)
                        {
                            this.OnAxisClick.Invoke(APSIM.Shared.Graphing.AxisPosition.Top);
                        }
                        else if (rightAxisArea.Contains(location) && GetAxis(APSIM.Shared.Graphing.AxisPosition.Right) != null)
                        {
                            this.OnAxisClick.Invoke(APSIM.Shared.Graphing.AxisPosition.Right);
                        }
                        else if (bottomAxisArea.Contains(location) && GetAxis(APSIM.Shared.Graphing.AxisPosition.Bottom) != null)
                        {
                            this.OnAxisClick.Invoke(APSIM.Shared.Graphing.AxisPosition.Bottom);
                        }
                    }
                }
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>
        /// User has clicked the caption
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void OnCaptionLabelDoubleClick(object sender, ButtonPressEventArgs e)
        {
            try
            {
                if (e.Event.Type == Gdk.EventType.TwoButtonPress && e.Event.Button == 1 && OnCaptionClick != null)
                    OnCaptionClick.Invoke(this, e);
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        public IEnumerable<APSIM.Shared.Graphing.Axis> Axes
        {
            get
            {
                return plot1.Model.Axes.Select(axis => new APSIM.Shared.Graphing.Axis(
                    axis.Title,
                    AxisPositionToType(axis.Position),
                    MathUtilities.FloatsAreEqual(axis.StartPosition, 1),
                    axis.PositionAtZeroCrossing,
                    axis.ActualMinimum,
                    axis.ActualMaximum,
                    axis.ActualMajorStep
                ));
            }
        }

        public void SetAxisMax(double value, APSIM.Shared.Graphing.AxisPosition axisType)
        {
            var axis = GetAxis(axisType);
            if (axis != null) axis.Maximum = value;
        }

        public void SetAxisMin(double value, APSIM.Shared.Graphing.AxisPosition axisType)
        {
            var axis = GetAxis(axisType);
            if (axis != null) axis.Minimum = value;
        }

        /// <summary>
        /// Gets the maximum scale of the specified axis.
        /// </summary>
        public double AxisMaximum(APSIM.Shared.Graphing.AxisPosition axisType)
        {
            OxyPlot.Axes.Axis axis = GetAxis(axisType);
            if (axis != null)
            {
                return AxisMaximum(axis);
            }
            else
                return double.NaN;
        }

        private double AxisMaximum(OxyPlot.Axes.Axis axis)
        {
            if (double.IsNaN(axis.Maximum))
                return axis.ActualMaximum;
            else
                return axis.Maximum;
        }

        /// <summary>
        /// Gets the minimum scale of the specified axis.
        /// </summary>
        public double AxisMinimum(APSIM.Shared.Graphing.AxisPosition axisType)
        {
            OxyPlot.Axes.Axis axis = GetAxis(axisType);

            if (axis != null)
            {
                if (double.IsNaN(axis.Minimum))
                    return axis.ActualMinimum;
                else
                    return axis.Minimum;
            }
            else
                return double.NaN;
        }

        /// <summary>
        /// Gets the interval (major step) of the specified axis.
        /// </summary>
        public string AxisTitle(APSIM.Shared.Graphing.AxisPosition axisType)
        {
            OxyPlot.Axes.Axis axis = GetAxis(axisType);

            if (axis != null)
                return axis.Title;

            return string.Empty;
        }

        /// <summary>
        /// Gets the interval (major step) of the specified axis.
        /// </summary>
        public double AxisMajorStep(APSIM.Shared.Graphing.AxisPosition axisType)
        {
            OxyPlot.Axes.Axis axis = GetAxis(axisType);

            if (axis != null)
            {
                return axis.IntervalLength;
            }
            else
                return double.NaN;
        }

        /// <summary>Gets the series names.</summary>
        /// <returns></returns>
        public string[] GetSeriesNames()
        {
            List<string> names = new List<string>();
            foreach (OxyPlot.Series.Series series in this.plot1.Model.Series)
            {
                names.Add(series.Title);
            }
            return names.ToArray();
        }

        /// <summary>Sets the margins.</summary>
        public void SetMargins(int margin)
        {
            this.plot1.Model.Padding = new OxyThickness(margin, margin, margin, margin);
        }

        /// <summary>Graph has been clicked.</summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnChartClick(object sender, OxyMouseDownEventArgs e)
        {
            try
            {
                e.Handled = false;

                inRightClick = e.ChangedButton == OxyMouseButton.Right;
                if (e.ChangedButton == OxyMouseButton.Left) // Left clicks only
                {
                    if (e.ClickCount == 1 && SingleClick != null)
                        SingleClick.Invoke(this, e);
                    else if (e.ClickCount == 2)
                        OnMouseDoubleClick(sender, e);
                }

                // Annotation tool tips.
                if (e.HitTestResult != null && e.HitTestResult.Element is OxyPlot.Annotations.Annotation)
                    plot1.TooltipText = (e.HitTestResult.Element as OxyPlot.Annotations.Annotation).ToolTip;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>Mouse up event on chart. If in a right click, display the popup menu.</summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnChartMouseUp(object sender, OxyMouseEventArgs e)
        {
            try
            {
                e.Handled = false;
                if (inRightClick)
                    popup.Popup();
                inRightClick = false;
                plot1.TooltipText = null;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        /// <summary>Mouse has moved on the chart.
        /// If the user was just dragging the chart, we won't want to 
        /// display the popup menu when the mouse is released</summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnChartMouseMove(object sender, OxyMouseEventArgs e)
        {
            try
            {
                e.Handled = false;
                inRightClick = false;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }

        public void ShowControls(bool visible)
        {
            try
            {
                captionEventBox.Visible = visible;
                expander1.Visible = visible && expander1.Expanded;
            }
            catch (Exception err)
            {
                ShowError(err);
            }
        }
    }
}
