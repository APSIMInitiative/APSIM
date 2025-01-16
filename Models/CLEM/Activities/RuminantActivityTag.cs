using Models.CLEM.Groupings;
using Models.CLEM.Interfaces;
using Models.CLEM.Resources;
using Models.Core;
using Models.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Models.CLEM.Activities
{
    /// <summary>Add or remove a tag to specified individual ruminants</summary>
    /// <version>1.0</version>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Add or remove a specified tag to/from the specified individuals for customised filtering")]
    [Version(1, 1, 0, "Implements event based activity control")]
    [Version(1, 0, 2, "Uses the Attribute feature of Ruminants")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantTag.htm")]

    public class RuminantActivityTag : CLEMRuminantActivityBase, IHandlesActivityCompanionModels
    {
        private int numberToDo;
        private int numberToSkip;
        private IEnumerable<Ruminant> uniqueIndividuals;
        private IEnumerable<RuminantGroup> filterGroups;

        /// <summary>
        /// Tag label
        /// </summary>
        [Description("Label of tag")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Label for tag required")]
        public string TagLabel { get; set; }

        /// <summary>
        /// Application style - add or remove tag
        /// </summary>
        [Description("Add or remove tag")]
        [System.ComponentModel.DefaultValueAttribute(TagApplicationStyle.Add)]
        public TagApplicationStyle ApplicationStyle { get; set; }

        /// <summary>
        /// constructor
        /// </summary>
        public RuminantActivityTag()
        {
            // activity is performed in ManageAnimals
            this.AllocationStyle = ResourceAllocationStyle.Manual;
        }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            // get all ui tree herd filters that relate to this activity
            this.InitialiseHerd(false, true);
            filterGroups = GetCompanionModelsByIdentifier<RuminantGroup>(true, false);
        }

        /// <inheritdoc/>
        public override LabelsForCompanionModels DefineCompanionModelLabels(string type)
        {
            switch (type)
            {
                case "RuminantGroup":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>() ,
                        measures: new List<string>()
                        );
                case "ActivityFee":
                case "LabourRequirement":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>() {
                        },
                        measures: new List<string>() {
                            "fixed",
                            "per head"
                        }
                        );
                default:
                    return new LabelsForCompanionModels();
            }
        }

        /// <inheritdoc/>
        [EventSubscribe("CLEMAnimalMark")]
        protected override void OnGetResourcesPerformActivity(object sender, EventArgs e)
        {
            ManageActivityResourcesAndTasks();
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            numberToDo = 0;
            numberToSkip = 0;
            IEnumerable<Ruminant> herd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.AllOnFarm).Where(a => a.Attributes.Exists(TagLabel) == (ApplicationStyle != TagApplicationStyle.Add));
            uniqueIndividuals = GetUniqueIndividuals<Ruminant>(filterGroups, herd);
            numberToDo = uniqueIndividuals?.Count() ?? 0;

            // provide updated measure for companion models
            foreach (var valueToSupply in valuesForCompanionModels)
            {
                int number = numberToDo;
                switch (valueToSupply.Key.unit)
                {
                    case "fixed":
                        valuesForCompanionModels[valueToSupply.Key] = 1;
                        break;
                    case "per head":
                        valuesForCompanionModels[valueToSupply.Key] = number;
                        break;
                    default:
                        if(valueToSupply.Key.type != "RuminantGroup")
                        {
                            throw new NotImplementedException(UnknownUnitsErrorText(this, valueToSupply.Key));
                        }
                        break;
                }
            }
            return null;
        }

        /// <inheritdoc/>
        protected override void AdjustResourcesForTimestep()
        {
            IEnumerable<ResourceRequest> shortfalls = MinimumShortfallProportion();
            if (shortfalls.Any())
            {
                // find shortfall by identifiers as these may have different influence on outcome
                var tagsShort = shortfalls.FirstOrDefault();
                if (tagsShort != null)
                {
                    numberToSkip = Convert.ToInt32(numberToDo * (1 - tagsShort.Available / tagsShort.Required));
                    if (numberToSkip == numberToDo)
                    {
                        Status = ActivityStatus.Warning;
                        AddStatusMessage("Resource shortfall prevented any action");
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            if(numberToDo - numberToSkip > 0)
            {
                int tagged = 0;
                foreach (Ruminant ruminant in uniqueIndividuals.SkipLast(numberToSkip).ToList())
                {
                    switch (ApplicationStyle)
                    {
                        case TagApplicationStyle.Add:
                            ruminant.Attributes.Add(TagLabel);
                            break;
                        case TagApplicationStyle.Remove:
                            ruminant.Attributes.Add(TagLabel);
                            break;
                    }
                    tagged++;
                }
                SetStatusSuccessOrPartial(tagged != numberToDo);
            }
        }

        #region descriptive summary

        /// <inheritdoc/>
        public override string ModelSummary()
        {
            return $"\r\n<div class=\"activityentry\">{CLEMModel.DisplaySummaryValueSnippet(ApplicationStyle)} the tag {CLEMModel.DisplaySummaryValueSnippet(TagLabel)} {((ApplicationStyle == TagApplicationStyle.Add)?"to":"from")} all individuals in the following groups</div>";
        }
        #endregion
    }
}
