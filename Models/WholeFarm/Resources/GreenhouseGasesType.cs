﻿using Models.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Models.WholeFarm.Resources
{
	///<summary>
	/// Store for emission type
	///</summary> 
	[Serializable]
	[ViewName("UserInterface.Views.GridView")]
	[PresenterName("UserInterface.Presenters.PropertyPresenter")]
	[ValidParent(ParentType = typeof(ResourcesHolder))]
	public class GreenhouseGasesType : WFModel, IResourceWithTransactionType, IResourceType
	{
		/// <summary>
		/// Starting amount
		/// </summary>
		[Description("Starting amount")]
		public double StartingAmount { get; set; }

		private double amount;
		/// <summary>
		/// Current amount of this resource
		/// </summary>
		public double Amount { get { return amount; } }

		/// <summary>
		/// Global warming potential
		/// </summary>
		[Description("Global warming potential")]
		public double GlobalWarmingPotential { get; set; }

		/// <summary>
		/// CO2 equivalents
		/// </summary>
		[Description("CO2 equivalents")]
		public double CO2Equivalents { get { return Amount * GlobalWarmingPotential; } }


		/// <summary>An event handler to allow us to initialise ourselves.</summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
		[EventSubscribe("WFInitialiseResource")]
		private void OnWFInitialiseResource(object sender, EventArgs e)
		{
			Initialise();
		}

		/// <summary>
		/// Initialise resource type
		/// </summary>
		public void Initialise()
		{
			this.amount = 0;
			if (StartingAmount > 0)
			{
				Add(StartingAmount, this.Name, "Starting value");
			}
		}

		#region transactions

		/// <summary>
		/// Back account transaction occured
		/// </summary>
		public event EventHandler TransactionOccurred;

		/// <summary>
		/// Transcation occurred 
		/// </summary>
		/// <param name="e"></param>
		protected virtual void OnTransactionOccurred(EventArgs e)
		{
			if (TransactionOccurred != null)
				TransactionOccurred(this, e);
		}

		/// <summary>
		/// Last transaction received
		/// </summary>
		[XmlIgnore]
		public ResourceTransaction LastTransaction { get; set; }

		/// <summary>
		/// Add money to account
		/// </summary>
		/// <param name="ResourceAmount"></param>
		/// <param name="ActivityName"></param>
		/// <param name="Reason"></param>
		public void Add(object ResourceAmount, string ActivityName, string Reason)
		{
			if (ResourceAmount.GetType().ToString() != "System.Double")
			{
				throw new Exception(String.Format("ResourceAmount object of type {0} is not supported Add method in {1}", ResourceAmount.GetType().ToString(), this.Name));
			}
			double addAmount = (double)ResourceAmount;
			if (addAmount > 0)
			{
				amount += addAmount;

				ResourceTransaction details = new ResourceTransaction();
				details.Credit = addAmount;
				details.CreditStandardised = addAmount * GlobalWarmingPotential;
				details.Activity = ActivityName;
				details.Reason = Reason;
				details.ResourceType = this.Name;
				LastTransaction = details;
				TransactionEventArgs te = new TransactionEventArgs() { Transaction = details };
				OnTransactionOccurred(te);
			}
		}

		/// <summary>
		/// Remove money (object) from account
		/// </summary>
		/// <param name="RemoveRequest"></param>
		public void Remove(object RemoveRequest)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Remove from finance type store
		/// </summary>
		/// <param name="Request">Resource request class with details.</param>
		public void Remove(ResourceRequest Request)
		{
			if (Request.Required == 0) return;
			// avoid taking too much
			double amountRemoved = Request.Required;
			amountRemoved = Math.Min(this.Amount, amountRemoved);
			this.amount -= amountRemoved;

			Request.Provided = amountRemoved;
			ResourceTransaction details = new ResourceTransaction();
			details.ResourceType = this.Name;
			details.Debit = amountRemoved * -1;
			details.DebitStandardised = amountRemoved * -1 * GlobalWarmingPotential;
			details.Activity = Request.ActivityModel.Name;
			details.Reason = Request.Reason;
			LastTransaction = details;
			TransactionEventArgs te = new TransactionEventArgs() { Transaction = details };
			OnTransactionOccurred(te);
		}

		/// <summary>
		/// Set the amount in an account.
		/// </summary>
		/// <param name="NewAmount"></param>
		public void Set(double NewAmount)
		{
			amount = NewAmount;
		}

		#endregion
	}
}
