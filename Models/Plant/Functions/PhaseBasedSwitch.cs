// ----------------------------------------------------------------------
// <copyright file="PhaseBasedSwitch.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
//-----------------------------------------------------------------------
namespace Models.PMF.Functions
{
    using Models.Core;
    using Models.PMF.Phen;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// # [Name]
    /// Returns a value of 1 if phenology is between start and end phases and otherwise a value of 0.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    public class PhaseBasedSwitch : BaseFunction, ICustomDocumentation
    {
        /// <summary>The value being returned</summary>
        private double returnValue = 0;

        //Fixme.  This can be removed an phase lookup returnig a constant of 1 if in phase.

        /// <summary>The phenology</summary>
        [Link]
        private Phenology phenologyModel = null;

        /// <summary>The start</summary>
        [Description("Start")]
        public string Start { get; set; }

        /// <summary>The end</summary>
        [Description("End")]
        public string End { get; set; }

        /// <summary>Gets the value.</summary>
        /// <value>The value.</value>
        /// <exception cref="System.Exception">
        /// Phase start name not set: + Name
        /// or
        /// Phase end name not set: + Name
        /// </exception>
        public override double[] Values()
        {
            if (Start == "")
                throw new Exception("Phase start name not set:" + Name);
            if (End == "")
                throw new Exception("Phase end name not set:" + Name);

            if (phenologyModel.Between(Start, End))
                returnValue = 1.0;
            else
                returnValue = 0.0;

            return new double[] { returnValue };
        }

        /// <summary>Writes documentation for this function by adding to the list of documentation tags.</summary>
        /// <param name="tags">The list of tags to add to.</param>
        /// <param name="headingLevel">The level (e.g. H2) of the headings.</param>
        /// <param name="indent">The level of indentation 1, 2, 3 etc.</param>
        public void Document(List<AutoDocumentation.ITag> tags, int headingLevel, int indent)
        {
            if (IncludeInDocumentation)
            {
                if (!(Parent is IFunction) && headingLevel > 0)
                    tags.Add(new AutoDocumentation.Heading(Name, headingLevel));

                tags.Add(new AutoDocumentation.Paragraph("A value of 1 is returned if phenology is between " + Start + " and " + End + " phases, otherwise a value of 0 is returned.", indent));

                // write memos.
                foreach (IModel memo in Apsim.Children(this, typeof(Memo)))
                    AutoDocumentation.DocumentModel(memo, tags, -1, indent);
            }
        }
    }
}


