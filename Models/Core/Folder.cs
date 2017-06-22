﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Models.Factorial;
using Models.PMF.Interfaces;
using Models.Graph;

namespace Models.Core
{
    /// <summary>
    /// A folder model
    /// </summary>
    [ViewName("UserInterface.Views.FolderView")]
    [PresenterName("UserInterface.Presenters.FolderPresenter")]
    [ScopedModel]
    [Serializable]
    [ValidParent(ParentType = typeof(Simulation))]
    [ValidParent(ParentType = typeof(Zone))]
    [ValidParent(ParentType = typeof(Folder))]
    [ValidParent(ParentType = typeof(Simulations))]
    [ValidParent(ParentType = typeof(Experiment))]
    [ValidParent(ParentType = typeof(IOrgan))]
    public class Folder : Model
    {
        /// <summary>Show page of graphs?</summary>
        public bool ShowPageOfGraphs { get; set; }

        /// <summary>Constructor</summary>
        public Folder()
        {
            ShowPageOfGraphs = true;
        }
        /// <summary>Writes documentation for this function by adding to the list of documentation tags.</summary>
        /// <param name="tags">The list of tags to add to.</param>
        /// <param name="headingLevel">The level (e.g. H2) of the headings.</param>
        /// <param name="indent">The level of indentation 1, 2, 3 etc.</param>
        public override void Document(List<AutoDocumentation.ITag> tags, int headingLevel, int indent)
        {
            // add a heading.
            tags.Add(new AutoDocumentation.Heading(Name, headingLevel));

            if (ShowPageOfGraphs)
            {
                foreach (Memo memo in Apsim.Children(this, typeof(Memo)))
                    memo.Document(tags, headingLevel, indent);

                int pageNumber = 1;
                int i = 0;
                List<IModel> children = Apsim.Children(this, typeof(Graph.Graph));
                while (i < children.Count)
                {
                    GraphPage page = new GraphPage();
                    page.name = Name + pageNumber;
                    for (int j = i; j < i + 6 && j < children.Count; j++)
                        page.graphs.Add(children[j] as Graph.Graph);
                    tags.Add(page);
                    i += 6;
                    pageNumber++;
                }
            }
        }

    }
}
