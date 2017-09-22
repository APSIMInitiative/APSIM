﻿using Models.Core;
using Models.WholeFarm.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Models.WholeFarm;
using Models.WholeFarm.Groupings;
using System.ComponentModel.DataAnnotations;

namespace Models.WholeFarm.Activities
{
	/// <summary>Ruminant herd cost </summary>
	/// <summary>This activity will arrange payment of a herd expense such as vet fees</summary>
	[Serializable]
	[ViewName("UserInterface.Views.GridView")]
	[PresenterName("UserInterface.Presenters.PropertyPresenter")]
	[ValidParent(ParentType = typeof(WFActivityBase))]
	[ValidParent(ParentType = typeof(ActivitiesHolder))]
	[ValidParent(ParentType = typeof(ActivityFolder))]
	public class RuminantActivityHerdCost : WFActivityBase
	{
		/// <summary>
		/// Get the Clock.
		/// </summary>
		[XmlIgnore]
		[Link]
		Clock Clock = null;
		[Link]
		private ResourcesHolder Resources = null;

		[XmlIgnore]
		[Link]
		ISummary Summary = null;

		/// <summary>
		/// The payment interval (in months, 1 monthly, 12 annual)
		/// </summary>
		[System.ComponentModel.DefaultValueAttribute(12)]
		[Description("The payment interval (in months, 1 monthly, 12 annual)")]
        [Required, Range(1, int.MaxValue, ErrorMessage = "Value must be a greter than or equal to 1")]
        public int PaymentInterval { get; set; }

		/// <summary>
		/// First month to pay overhead
		/// </summary>
		[System.ComponentModel.DefaultValueAttribute(6)]
		[Description("First month to pay expense (1-12)")]
        [Required, Range(1, 12, ErrorMessage = "Value must represent a month from 1 (Jan) to 12 (Dec)")]
        public int MonthDue { get; set; }

		/// <summary>
		/// Amount payable
		/// </summary>
		[Description("Amount payable")]
        [Required, Range(0, double.MaxValue, ErrorMessage = "Value must be a greter than or equal to 0")]
        public double Amount { get; set; }

		/// <summary>
		/// Payment style
		/// </summary>
		[System.ComponentModel.DefaultValueAttribute(AnimalPaymentStyleType.perHead)]
		[Description("Payment style")]
        [Required]
        public AnimalPaymentStyleType PaymentStyle { get; set; }

		/// <summary>
		/// name of account to use
		/// </summary>
		[Description("Name of account to use")]
        [Required]
        public string AccountName { get; set; }

		/// <summary>
		/// Month this overhead is next due.
		/// </summary>
		[XmlIgnore]
		public DateTime NextDueDate { get; set; }

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
        [EventSubscribe("StartOfSimulation")]
        private void OnStartOfSimulation(object sender, EventArgs e)
        {
            if (MonthDue >= Clock.StartDate.Month)
			{
				NextDueDate = new DateTime(Clock.StartDate.Year, MonthDue, Clock.StartDate.Day);
			}
			else
			{
				NextDueDate = new DateTime(Clock.StartDate.Year, MonthDue, Clock.StartDate.Day);
				while (Clock.StartDate > NextDueDate)
				{
					NextDueDate = NextDueDate.AddMonths(PaymentInterval);
				}
			}
		}

		/// <summary>
		/// Method to determine resources required for this activity in the current month
		/// </summary>
		/// <returns>List of required resource requests</returns>
		public override List<ResourceRequest> GetResourcesNeededForActivity()
		{
			ResourceRequestList = new List<ResourceRequest>();

			if (this.NextDueDate.Year == Clock.Today.Year & this.NextDueDate.Month == Clock.Today.Month)
			{
				double amountNeeded = 0;
				List<Ruminant> herd = new List<Ruminant>();
				switch (PaymentStyle)
				{
					case AnimalPaymentStyleType.Fixed:
						amountNeeded = Amount;
						break;
					case AnimalPaymentStyleType.perHead:
						herd = Resources.RuminantHerd().Herd;
						// check for Ruminant filter group
						if(Apsim.Children(this, typeof(RuminantFilterGroup)).Count() > 0)
						{
							herd = herd.Filter(Apsim.Children(this, typeof(RuminantFilterGroup)).FirstOrDefault() as RuminantFilterGroup);
						}
						amountNeeded = Amount*herd.Count();
						break;
					case AnimalPaymentStyleType.perAE:
						herd = Resources.RuminantHerd().Herd;
						// check for Ruminant filter group
						if (Apsim.Children(this, typeof(RuminantFilterGroup)).Count() > 0)
						{
							herd = herd.Filter(Apsim.Children(this, typeof(RuminantFilterGroup)).FirstOrDefault() as RuminantFilterGroup);
						}
						amountNeeded = Amount * herd.Sum(a => a.AdultEquivalent);
						break;
					default:
						throw new Exception(String.Format("Unknown Payment style {0} in {1}",PaymentStyle, this.Name));
				}

				if (amountNeeded == 0) return ResourceRequestList;

				// determine breed
				string BreedName = "Multiple breeds";
				List<string> breeds = herd.Select(a => a.Breed).Distinct().ToList();
				if(breeds.Count==1)
				{
					BreedName = breeds[0];
				}

				ResourceRequestList.Add(new ResourceRequest()
				{
					AllowTransmutation = false,
					Required = amountNeeded,
					ResourceType = typeof(Finance),
					ResourceTypeName = this.AccountName,
					ActivityModel = this,
					Reason = BreedName
				}
				);
			}
			return ResourceRequestList;
		}

		/// <summary>
		/// Method used to perform activity if it can occur as soon as resources are available.
		/// </summary>
		public override void DoActivity()
		{
			// if occurred
			if (this.NextDueDate.Year == Clock.Today.Year & this.NextDueDate.Month == Clock.Today.Month)
			{
				ResourceRequest thisRequest = ResourceRequestList.FirstOrDefault();
				if (thisRequest != null)
				{
					// update next due date
					this.NextDueDate = this.NextDueDate.AddMonths(this.PaymentInterval);
				}
			}
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
