﻿using Models.Core;
using Models.WholeFarm.Groupings;
using Models.WholeFarm.Resources;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models.WholeFarm.Activities
{
	/// <summary>Activity to undertake milking of particular herd</summary>
	[Serializable]
	[ViewName("UserInterface.Views.GridView")]
	[PresenterName("UserInterface.Presenters.PropertyPresenter")]
	[ValidParent(ParentType = typeof(WFActivityBase))]
	[ValidParent(ParentType = typeof(ActivitiesHolder))]
	[ValidParent(ParentType = typeof(ActivityFolder))]
	public class RuminantActivityMilking: WFActivityBase
	{
		[Link]
		private ResourcesHolder Resources = null;

		/// <summary>
		/// Herd to milk
		/// </summary>
		[Description("Name of herd to milk")]
        [Required]
        public string HerdName { get; set; }

		/// <summary>
		/// Labour settings
		/// </summary>
		private List<LabourFilterGroupSpecified> labour { get; set; }

		private HumanFoodStoreType milkStore;

        /// <summary>An event handler to allow us to initialise ourselves.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("StartOfSimulation")]
        private void OnStartOfSimulation(object sender, EventArgs e)
        {
            // get labour specifications
            labour = Apsim.Children(this, typeof(LabourFilterGroupSpecified)).Cast<LabourFilterGroupSpecified>().ToList(); //  this.Children.Where(a => a.GetType() == typeof(LabourFilterGroupSpecified)).Cast<LabourFilterGroupSpecified>().ToList();
			if (labour == null) labour = new List<LabourFilterGroupSpecified>();

			// find milk store
			milkStore = Resources.GetResourceItem(this, typeof(HumanFoodStore), "Milk", OnMissingResourceActionTypes.ReportErrorAndStop, OnMissingResourceActionTypes.ReportErrorAndStop) as HumanFoodStoreType;
		}

		/// <summary>An event handler to allow us to initialise herd pricing.</summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
		[EventSubscribe("WFInitialiseActivity")]
		private void OnWFInitialiseActivity(object sender, EventArgs e)
		{
		}

		/// <summary>An event handler to call for all herd management activities</summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
		[EventSubscribe("WFMilking")]
		private void OnWFMilking(object sender, EventArgs e)
		{
			// take all milk
			List<RuminantFemale> herd = Resources.RuminantHerd().Herd.Where(a => a.HerdName == HerdName & a.Gender == Sex.Female).Cast<RuminantFemale>().Where(a => a.IsLactating == true & a.SucklingOffspring.Count() == 0).ToList();
			double milkTotal = herd.Sum(a => a.MilkAmount);
			if (milkTotal > 0)
			{
				// set these females to state milking perfomred so they switch to the non-suckling milk production curves.
				herd.Select(a => a.MilkingPerformed == true);

				// only provide what labour would allow
				// calculate labour limit
				double labourLimit = 1;
				double labourNeeded = ResourceRequestList.Where(a => a.ResourceType == typeof(Labour)).Sum(a => a.Required);
				double labourProvided = ResourceRequestList.Where(a => a.ResourceType == typeof(Labour)).Sum(a => a.Provided);
				if (labourNeeded > 0)
				{
					labourLimit = labourProvided / labourNeeded;
				}

				milkStore.Add(milkTotal * labourLimit, this.Name, HerdName);
			}
		}

		/// <summary>
		/// Method to determine resources required for this activity in the current month
		/// </summary>
		/// <returns>List of required resource requests</returns>
		public override List<ResourceRequest> GetResourcesNeededForActivity()
		{
			ResourceRequestList = null;

			List<RuminantFemale> herd = Resources.RuminantHerd().Herd.Where(a => a.HerdName == HerdName & a.Gender == Sex.Female).Cast<RuminantFemale>().Where(a => a.IsLactating == true & a.SucklingOffspring.Count() == 0).ToList();
			int head = herd.Count();
			if (head > 0)
			{
				// for each labour item specified
				foreach (var item in labour)
				{
					double daysNeeded = 0;
					switch (item.UnitType)
					{
						case LabourUnitType.Fixed:
							daysNeeded = item.LabourPerUnit;
							break;
						case LabourUnitType.perHead:
							daysNeeded = Math.Ceiling(head / item.UnitSize) * item.LabourPerUnit;
							break;
						default:
							throw new Exception(String.Format("LabourUnitType {0} is not supported for {1} in {2}", item.UnitType, item.Name, this.Name));
					}
					if (daysNeeded > 0)
					{
						if (ResourceRequestList == null) ResourceRequestList = new List<ResourceRequest>();
						ResourceRequestList.Add(new ResourceRequest()
						{
							AllowTransmutation = false,
							Required = daysNeeded,
							ResourceType = typeof(Labour),
							ResourceTypeName = "",
							ActivityModel = this,
							Reason = "Milking",
							FilterDetails = new List<object>() { item }
						}
						);
					}
				}

			}
			return ResourceRequestList;
		}

		/// <summary>
		/// Method used to perform activity if it can occur as soon as resources are available.
		/// </summary>
		public override void DoActivity()
		{
			return;
		}

		/// <summary>
		/// Method to determine resources required for initialisation of this activity
		/// </summary>
		/// <returns></returns>
		public override List<ResourceRequest> GetResourcesNeededForinitialisation()
		{
			return null;
		}

		/// <summary>
		/// Method used to perform initialisation of this activity.
		/// This will honour ReportErrorAndStop action but will otherwise be preformed regardless of resources available
		/// It is the responsibility of this activity to determine resources provided.
		/// </summary>
		public override void DoInitialisation()
		{
			return;
		}

		/// <summary>
		/// Resource shortfall event handler
		/// </summary>
		public override event EventHandler ResourceShortfallOccurred;

		/// <summary>
		/// Shortfall occurred 
		/// </summary>
		/// <param name="e"></param>
		protected override void OnShortfallOccurred(EventArgs e)
		{
			if (ResourceShortfallOccurred != null)
				ResourceShortfallOccurred(this, e);
		}

		/// <summary>
		/// Resource shortfall occured event handler
		/// </summary>
		public override event EventHandler ActivityPerformed;

		/// <summary>
		/// Shortfall occurred 
		/// </summary>
		/// <param name="e"></param>
		protected override void OnActivityPerformed(EventArgs e)
		{
			if (ActivityPerformed != null)
				ActivityPerformed(this, e);
		}


	}
}
