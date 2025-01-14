namespace APSIM.Interop.Graphing.CustomSeries
{
    using System;
    using System.Globalization;
    using OxyPlot;
    using OxyPlot.Axes;
    using OxyPlot.Series;

    /// <summary>
    /// A column series for graphing that doesn't need a category axis.
    /// </summary>
    /// <remarks>
    /// Note that the meaning of the tracker format string has been changed.
    /// 0: Series title
    /// 1: X axis title
    /// 2: X value of the column
    /// 3: Y axis title
    /// 4: Y value of the column
    /// </remarks>
    public class ColumnXYSeries : RectangleBarSeries
    {
        /// <summary>Updates the data.</summary>
        protected override void UpdateData()
        {
            this.MovePointsToItems();
        }

        /// <summary>
        /// Renders the Series on the specified rendering context.
        /// </summary>
        /// <param name="rc">
        /// The rendering context.
        /// </param>
        /// <param name="model">
        /// The model.
        /// </param>
        public override void Render(IRenderContext rc)
        {
            this.MovePointsToItems();

            // Let the base class draw the rectanges.
            base.Render(rc);
        }

        /// <summary>
        /// Create a series of RectangleBarItem objects in Items based on Points.
        /// </summary>
        private void MovePointsToItems()
        {
            if (this.ItemsSource != null && this.XAxis != null)
            {
                // double halfBarWidth = this.XAxis.ActualMajorStep * 0.4;
                double halfBarWidth = 0.4;
                if (this.XAxis.ActualStringFormat != null && this.XAxis.ActualStringFormat.Contains("yyyy"))
                {
                    DateTime d1 = DateTimeAxis.ToDateTime(this.XAxis.ActualMinimum, DateTimeAxis.DefaultPrecision);
                    DateTime d2 = DateTimeAxis.ToDateTime(this.XAxis.ActualMinimum + 1, DateTimeAxis.DefaultPrecision);
                    halfBarWidth = (d2 - d1).Days / 2.0;
                }

                this.Items.Clear();
                foreach (DataPoint p in this.ItemsSource)
                {
                    double x0 = p.X - halfBarWidth;
                    double x1 = p.X + halfBarWidth;
                    this.Items.Add(new RectangleBarItem(x0, 0.0, x1, p.Y));
                }
            }
        }

        /// <summary>Gets the point in the dataset that is nearest the specified point.</summary>
        /// <param name="point">The point.</param>
        /// <param name="interpolate">Specifies whether to interpolate or not.</param>
        public override TrackerHitResult GetNearestPoint(ScreenPoint point, bool interpolate)
        {
            var result = base.GetNearestPoint(point, false);
            if (result?.Item is DataPoint item)
            {
                object xValue = item.X;
                // Try and use the label for the xvalue if it's a category axis.
                // Should we do this for the y axis too, if it's a category axis?
                if (result.XAxis is CategoryAxis category && int.TryParse(xValue?.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out int x))
                {
                    if (category.ActualLabels.Count > x)
                        xValue = category.ActualLabels[x];
                    else if (category.Labels.Count > x)
                        xValue = category.Labels[x];
                }
                result.Text = string.Format(CultureInfo.CurrentCulture,
                                            TrackerFormatString,
                                            result.Series.Title,
                                            result.XAxis.Title,
                                            xValue,
                                            result.YAxis.Title,
                                            item.Y);
            }
            else if (result?.Item is RectangleBarItem barItem)
            {
                double xValue = (barItem.X0 + barItem.X1) / 2;
                string xLabel = xValue.ToString();
                if (result.XAxis is DateTimeAxis dateAxis)
                    xLabel = dateAxis.FormatValue(xValue);
                result.Text = string.Format(CultureInfo.CurrentCulture,
                                            TrackerFormatString,
                                            result.Series.Title,
                                            result.XAxis.Title,
                                            xLabel,
                                            result.YAxis.Title,
                                            barItem.Y1);
            }
            return result;
        }
    }
}
