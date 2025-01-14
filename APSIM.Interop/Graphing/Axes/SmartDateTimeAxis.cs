using OxyPlot.Axes;
using System;

namespace APSIM.Interop.Graphing.Axes
{
    /// <summary>
    /// This class extends OxyPlot's DateTimeAxis, with "better" formatting
    /// of axis tick labels and intervals.
    /// </summary>
    /// <remarks>
    /// Technically we could achieve the same result here without extending
    /// the DateTimeAxis class. However, doing so gives us convenient access
    /// to the min and max values being displayed on the axis.
    /// </remarks>
    public class SmartDateTimeAxis : DateTimeAxis
    {
        protected override double CalculateActualInterval(double availableSize, double maxIntervalSize, double minIntervalCount, double maxIntervalCount)
        {
            DateTime min = ToDateTime(ActualMinimum, DefaultPrecision);
            DateTime max = ToDateTime(ActualMaximum, DefaultPrecision);

            (DateTimeIntervalType interval, string format) = CalculateInterval(min, max);
            IntervalType = interval;
            // MinorIntervalType = (DateTimeIntervalType)Math.Max(0, (int)interval - 1);
            if (!string.IsNullOrEmpty(format))
                this.StringFormat = format;

            return base.CalculateActualInterval(availableSize, maxIntervalSize, minIntervalCount, maxIntervalCount);
        }

        /// <summary>
        /// This method provides a better axis tick resolution algorithm for date time axes.
        /// Returns an appropriate interval and format string for the given date range.
        /// </summary>
        /// <param name="min">The earliest date displayed on the axis.</param>
        /// <param name="max">The latest date displayed on the axis.</param>
        private static (DateTimeIntervalType, string) CalculateInterval(DateTime min, DateTime max)
        {
            int numDays = (max - min).Days;
            if (numDays < 100)
                return (DateTimeIntervalType.Days, null);
            else if (numDays <= 366)
                return (DateTimeIntervalType.Months, "dd-MMM");
            else if (numDays <= 720)
                return (DateTimeIntervalType.Months, "MMM-yyyy");
            else
                return (DateTimeIntervalType.Years, "yyyy");
        }
    }
}
