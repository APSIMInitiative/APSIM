using Models.CLEM.Activities;
using Models.CLEM.Resources;
using Models.Core;
using Models.Core.Attributes;
using System;
using System.IO;

namespace Models.CLEM.Groupings
{
    ///<summary>
    /// Contains a group of filters to identify individual in labour pool
    ///</summary> 
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(LabourActivityFeed))]
    [Description("Defines the feed value for specific individuals from the labour pool")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Filters/Groups/LabourFeedGroup.htm")]
    public class LabourFeedGroup : LabourGroup
    {
        /// <summary>
        /// Value to supply for each month
        /// </summary>
        [Description("Value to supply")]
        [GreaterThanValue(0)]
        public double Value { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LabourFeedGroup()
        {
            base.ModelSummaryStyle = HTMLSummaryStyle.SubActivity;
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                if (this.Parent.GetType() != typeof(LabourActivityFeed))
                {
                    htmlWriter.Write("<div class=\"warningbanner\">This Labour Feed Group must be placed beneath a Labour Activity Feed component</div>");
                    return htmlWriter.ToString();
                }

                LabourFeedActivityTypes ft = (this.Parent as LabourActivityFeed).FeedStyle;
                htmlWriter.Write("\r\n<div class=\"activityentry\">");
                switch (ft)
                {
                    case LabourFeedActivityTypes.SpecifiedDailyAmountPerAE:
                    case LabourFeedActivityTypes.SpecifiedDailyAmountPerIndividual:
                        htmlWriter.Write($"<span class=\"{((Value <= 0) ? "errorlink" : "setvalue")}\">{Value}</span>");
                        break;
                    default:
                        break;
                }

                ZoneCLEM zoneCLEM = FindAncestor<ZoneCLEM>();
                ResourcesHolder resHolder = zoneCLEM.FindChild<ResourcesHolder>();
                HumanFoodStoreType food = resHolder.FindResourceType<HumanFoodStore, HumanFoodStoreType>(this, (this.Parent as LabourActivityFeed).FeedTypeName, OnMissingResourceActionTypes.Ignore, OnMissingResourceActionTypes.Ignore);
                if (food != null)
                    htmlWriter.Write(" " + food.Units + " ");

                htmlWriter.Write("<span class=\"setvalue\">");
                switch (ft)
                {
                    case LabourFeedActivityTypes.SpecifiedDailyAmountPerIndividual:
                        htmlWriter.Write(" per individual per day");
                        break;
                    case LabourFeedActivityTypes.SpecifiedDailyAmountPerAE:
                        htmlWriter.Write(" per AE per day");
                        break;
                    default:
                        break;
                }
                htmlWriter.Write("</span> ");
                switch (ft)
                {
                    case LabourFeedActivityTypes.SpecifiedDailyAmountPerAE:
                    case LabourFeedActivityTypes.SpecifiedDailyAmountPerIndividual:
                        htmlWriter.Write("is fed to each individual");
                        break;
                }
                htmlWriter.Write(" that matches the following conditions:");

                htmlWriter.Write("</div>");
                return htmlWriter.ToString();
            }
        }

        #endregion
    }
}
