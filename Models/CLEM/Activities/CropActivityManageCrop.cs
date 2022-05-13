using Models.Core;
using Models.CLEM.Interfaces;
using Models.CLEM.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Newtonsoft.Json;
using Models.Core.Attributes;
using System.IO;

namespace Models.CLEM.Activities
{
    /// <summary>Grow management activity</summary>
    /// <summary>This activity sets aside land for the crop(s)</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("This activity manages a crop(s) by assigning land to be used for child activities.")]
    [Version(1, 0, 1, "Beta build")]
    [Version(1, 0, 2, "Rotational cropping implemented")]
    [HelpUri(@"Content/Features/Activities/Crop/ManageCrop.htm")]
    public class CropActivityManageCrop: CLEMActivityBase, IValidatableObject, IPastureManager
    {
        [Link]
        private Clock clock = null;

        private int currentCropIndex = 0;

        /// <summary>
        /// Land type where crop is to be grown
        /// </summary>
        [Description("Land type where crop is to be grown")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourcesAvailableByName", ValuesArgs = new object[] { new Type[] { typeof(Land) } })]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Land resource type required")]
        public string LandItemNameToUse { get; set; }

        /// <summary>
        /// Area of land requested
        /// </summary>
        [Description("Area of crop")]
        [Required, GreaterThanEqualValue(0)]
        public double AreaRequested { get; set; }

        /// <summary>
        /// Use unallocated available
        /// </summary>
        [Description("Use unallocated land")]
        public bool UseAreaAvailable { get; set; }
        
        /// <summary>
        /// Area of land actually received (maybe less than requested)
        /// </summary>
        [JsonIgnore]
        public double Area { get; set; }

        /// <summary>
        /// Land item
        /// </summary>
        [JsonIgnore]
        public LandType LinkedLandItem { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public CropActivityManageCrop()
        {
            base.ModelSummaryStyle = HTMLSummaryStyle.SubActivityLevel2;
            TransactionCategory = "Crop";
        }

        /// <summary>An event handler to allow us to initialise</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            if (LandItemNameToUse != null && LandItemNameToUse != "")
            {
                // locate Land Type resource for this forage.
                LinkedLandItem = Resources.FindResourceType<Land, LandType>(this, LandItemNameToUse, OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop);

                if (UseAreaAvailable)
                    LinkedLandItem.TransactionOccurred += LinkedLandItem_TransactionOccurred;
            }

        }

        /// <summary>An event handler to allow us to make checks after resources and activities initialised.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("FinalInitialise")]
        private void OnFinalInitialise(object sender, EventArgs e)
        {
            // set and enable first crop in the list for rotational cropping.
            int i = 0;
            foreach (CropActivityManageProduct item in this.Children.OfType<CropActivityManageProduct>())
            {
                item.ActivityEnabled = (i == currentCropIndex);
                item.FirstTimeStepOfRotation = clock.StartDate.Year * 100 + clock.StartDate.Month;
                if (item.ActivityEnabled && LinkedLandItem != null)
                {
                    // get land for this crop (first crop in list)
                    // this may include a multiplier to modify the crop area planted and needed
                    AdjustLand(item);
                }
                i++;
            }

            if (Area == 0 && UseAreaAvailable)
                Summary.WriteMessage(this, $"No area of [r={LinkedLandItem.NameWithParent}] has been assigned for [a={this.NameWithParent}] at the start of the simulation.\r\nThis is because you have selected to use unallocated land and all land is used by other activities.", MessageType.Warning);
        }

        /// <summary>
        /// Method to rotate to the next crop in the list
        /// </summary>
        public void RotateCrop()
        {
            int numberCrops = this.FindAllChildren<CropActivityManageProduct>().Count();
            if (numberCrops>1)
            {
                currentCropIndex++;
                if (currentCropIndex >= numberCrops)
                    currentCropIndex = 0;

                int i = 0;
                foreach (CropActivityManageProduct item in this.FindAllChildren<CropActivityManageProduct>())
                {
                    item.ActivityEnabled = (i == currentCropIndex);
                    if (item.ActivityEnabled)
                    {
                        item.FirstTimeStepOfRotation = item.FirstTimeStepOfRotation = clock.Today.AddDays(1).Year * 100 + clock.Today.AddDays(1).Month;
                        AdjustLand(item);
                    }
                    else
                        item.FirstTimeStepOfRotation = 0;
                    i++;
                }
            }
        }

        /// <summary>
        /// Method to adjust area planted if crop has a area planted multiplier
        /// </summary>
        /// <param name="cropProduct">The crop product details to define final land area</param>
        private void AdjustLand(CropActivityManageProduct cropProduct)
        {
            // is this using available land and not yet assigned, or not using available land
            if (Area == 0 || !UseAreaAvailable)
            {
                // is the requested land different to land currently provided
                double areaneeded = UseAreaAvailable ? LinkedLandItem.AreaAvailable : (AreaRequested * cropProduct.PlantedMultiplier) - Area;
                if (areaneeded != 0)
                {
                    if(areaneeded > 0)
                    {
                        ResourceRequestList = new List<ResourceRequest> {
                            new ResourceRequest() {
                                Resource = LinkedLandItem,
                                AllowTransmutation = false,
                                Required = areaneeded,
                                ResourceType = typeof(Land),
                                ResourceTypeName = LandItemNameToUse,
                                ActivityModel = this,
                                Category = TransactionCategory,
                                FilterDetails = null,
                                RelatesToResource = cropProduct.LinkedResourceItem.Name
                            }
                        };

                        if (!UseAreaAvailable & LinkedLandItem != null)
                        {
                            CheckResources(ResourceRequestList, Guid.NewGuid());
                            TakeResources(ResourceRequestList, false);
                            //Now the Land has been allocated we have an Area 
                            //Assign the area actually got after taking it. It might be less than AreaRequested (if partial)
                            Area += ResourceRequestList.FirstOrDefault().Provided;
                        }
                        else
                            Area += areaneeded;
                    }
                    else
                    {
                        // excess land for planting can be reterned to land resource
                        // careful that this doesn't get taken by a use all available elewhere if you want it back again.
                        if (LinkedLandItem != null)
                        {
                            LinkedLandItem.Add(-areaneeded, this, cropProduct.LinkedResourceItem.Name, this.TransactionCategory);
                            Area += areaneeded;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Overrides the base class method to allow for clean up
        /// </summary>
        [EventSubscribe("Completed")]
        private void OnSimulationCompleted(object sender, EventArgs e)
        {
            if (LinkedLandItem != null && UseAreaAvailable)
                LinkedLandItem.TransactionOccurred -= LinkedLandItem_TransactionOccurred;
        }

        // Method to listen for land use transactions 
        // This allows this activity to dynamically respond when use available area is selected
        // only listens when use available is set for parent
        private void LinkedLandItem_TransactionOccurred(object sender, EventArgs e)
        {
            Area = LinkedLandItem.AreaAvailable;
        }

        /// <inheritdoc/>
        public override void DoActivity()
        {
            Status = ActivityStatus.NoTask;
            return;
        }


        /// <summary>
        /// Validate model
        /// </summary>
        /// <param name="validationContext"></param>
        /// <returns></returns>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();
            // check that this activity contains at least one CollectProduct activity
            if (this.Children.OfType<CropActivityManageProduct>().Count() == 0)
            {
                string[] memberNames = new string[] { "Collect product activity" };
                results.Add(new ValidationResult("At least one [a=CropActivityManageProduct] activity must be present under this manage crop activity", memberNames));
            }
            return results;
        }


        /// <inheritdoc/>
        public override string ModelSummary()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                htmlWriter.Write("\r\n<div class=\"activityentry\">This crop uses ");

                Land parentLand = null;
                IModel clemParent = FindAncestor<ZoneCLEM>();
                if (LandItemNameToUse != null && LandItemNameToUse != "")
                    if (clemParent != null && clemParent.Enabled)
                        parentLand = clemParent.FindInScope(LandItemNameToUse.Split('.')[0]) as Land;

                if (UseAreaAvailable)
                    htmlWriter.Write("the unallocated portion of ");
                else
                {
                    if (parentLand == null)
                        htmlWriter.Write("<span class=\"setvalue\">" + AreaRequested.ToString("0.###") + "</span> <span class=\"errorlink\">[UNITS NOT SET]</span> of ");
                    else
                        htmlWriter.Write("<span class=\"setvalue\">" + AreaRequested.ToString("0.###") + "</span> " + parentLand.UnitsOfArea + " of ");
                }
                if (LandItemNameToUse == null || LandItemNameToUse == "")
                    htmlWriter.Write("<span class=\"errorlink\">[LAND NOT SET]</span>");
                else
                    htmlWriter.Write("<span class=\"resourcelink\">" + LandItemNameToUse + "</span>");
                htmlWriter.Write("</div>");
                return htmlWriter.ToString(); 
            }
        }

        /// <inheritdoc/>
        public override string ModelSummaryInnerClosingTags()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                if (this.FindAllChildren<CropActivityManageProduct>().Count() > 0)
                    htmlWriter.Write("\r\n</div>");
                return htmlWriter.ToString(); 
            }
        }

        /// <inheritdoc/>
        public override string ModelSummaryInnerOpeningTags()
        {
            using (StringWriter htmlWriter = new StringWriter())
            {
                if (this.FindAllChildren<CropActivityManageProduct>().Count() == 0)
                {
                    htmlWriter.Write("\r\n<div class=\"errorbanner clearfix\">");
                    htmlWriter.Write("<div class=\"filtererror\">No Crop Activity Manage Product component provided</div>");
                    htmlWriter.Write("</div>");
                }
                else
                {
                    bool rotation = this.FindAllChildren<CropActivityManageProduct>().Count() > 1;
                    if (rotation)
                        htmlWriter.Write("\r\n<div class=\"croprotationlabel\">Rotating through crops</div>");
                    htmlWriter.Write("\r\n<div class=\"croprotationborder\">");
                }
                return htmlWriter.ToString(); 
            }
        } 
    }
}
