using Models.Core;
using Models.CLEM.Resources;
using Models.CLEM.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using Models.CLEM.Groupings;
using Models.Core.Attributes;
using APSIM.Shared.Utilities;

namespace Models.CLEM.Activities
{
    /// <summary>Ruminant herd cost </summary>
    /// <summary>This activity will arrange payment of a herd expense such as vet fees</summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(CLEMActivityBase))]
    [ValidParent(ParentType = typeof(ActivitiesHolder))]
    [ValidParent(ParentType = typeof(ActivityFolder))]
    [Description("Arrange payment of a ruminant herd expense with specified style")]
    [Version(1, 1, 0, "Implements event based activity control")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Activities/Ruminant/RuminantHerdCost.htm")]
    public class RuminantActivityHerdCost : CLEMRuminantActivityBase, IHandlesActivityCompanionModels
    {
        private int numberToDo;
        private int numberToSkip;
        private double amountToDo;
        private double amountToSkip;
        private IEnumerable<Ruminant> uniqueIndividuals;
        private IEnumerable<RuminantGroup> filterGroups;

        /// <summary>
        /// Constructor
        /// </summary>
        public RuminantActivityHerdCost()
        {
            this.SetDefaults();
        }

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("CLEMInitialiseActivity")]
        private void OnCLEMInitialiseActivity(object sender, EventArgs e)
        {
            this.InitialiseHerd(false, true);
            filterGroups = GetCompanionModelsByIdentifier<RuminantGroup>(false, true);
        }

        /// <inheritdoc/>
        public override LabelsForCompanionModels DefineCompanionModelLabels(string type)
        {
            switch (type)
            {
                case "RuminantGroup":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>(),
                        measures: new List<string>()
                        );
                case "ActivityFee":
                case "LabourRequirement":
                    return new LabelsForCompanionModels(
                        identifiers: new List<string>() {
                        },
                        measures: new List<string>() {
                            "fixed",
                            "per head",
                            "per AE"
                        }
                        );
                default:
                    return new LabelsForCompanionModels();
            }
        }

        /// <inheritdoc/>
        public override List<ResourceRequest> RequestResourcesForTimestep(double argument = 0)
        {
            numberToDo = 0;
            numberToSkip = 0;
            IEnumerable<Ruminant> herd = GetIndividuals<Ruminant>(GetRuminantHerdSelectionStyle.NotMarkedForSale);
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
                    case "per AE":
                        amountToDo = uniqueIndividuals.Sum(a => a.AdultEquivalent);
                        valuesForCompanionModels[valueToSupply.Key] = amountToDo;
                        break;
                    default:
                        throw new NotImplementedException(UnknownUnitsErrorText(this, valueToSupply.Key));
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
                var shorts = shortfalls.Where(a => a.CompanionModelDetails.unit == "per head").FirstOrDefault();
                if (shorts != null)
                    numberToSkip = Convert.ToInt32(numberToDo * (1 - shorts.Available / shorts.Required));

                var amountShort = shortfalls.Where(a => a.CompanionModelDetails.unit == "per AE").FirstOrDefault();
                if (amountShort != null)
                    amountToSkip = Convert.ToInt32(amountToDo * (1 - amountShort.Available / amountShort.Required));
            }
        }

        /// <inheritdoc/>
        public override void PerformTasksForTimestep(double argument = 0)
        {
            if (numberToDo - numberToSkip > 0)
            {
                SetStatusSuccessOrPartial(MathUtilities.IsPositive(numberToSkip + amountToSkip));
            }
        }

    }
}
