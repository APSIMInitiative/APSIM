﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Models.Core;
using System.Xml.Serialization;
using Models.Interfaces;
using APSIM.Shared.Utilities;
using Models.Soils.Arbitrator;
using Models.Zones;

namespace Models.Agroforestry
{
    /// <summary>
    /// A simple agroforestry model
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentModels = new Type[] { typeof(Simulation), typeof(Zone) })]
    public class ForestrySystem : Zone
    {
        /// <summary>
        /// Return the area of the zone.
        /// </summary>
        [XmlIgnore]
        public override double Area
        {
            get
            {
                double A = 0;
                foreach (Zone Z in Apsim.Children(this, typeof(Zone)))
                    A += Z.Area;
                return A;
            }
            set
            {
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [XmlIgnore]
        public TreeProxy tree = null;

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            //find the tree
            tree = Apsim.Child(this, typeof(TreeProxy)) as TreeProxy;
        }

        /// <summary>
        /// Passthrough for child nodes that need information from the tree.
        /// Saves having to query the simulation for the node location all the time.
        /// </summary>
        /// <param name="z">The zone.</param>
        /// <returns></returns>
        public double GetDistanceFromTrees(Zone z)
        {
            return tree.GetDistanceFromTrees(z);
        }
    }
}
