using System.Collections.Generic;
using System.Linq;
using APSIM.Shared.Documentation;
using APSIM.Interop.Mapping;
using Models.Core;
using Models;

namespace APSIM.Documentation.Models.Types
{

    /// <summary>
    /// Documentation for Map
    /// </summary>
    public class DocMap : DocGeneric
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DocMap" /> class.
        /// </summary>
        public DocMap(IModel model): base(model) {}

        /// <summary>
        /// Document the model
        /// </summary>
        public override IEnumerable<ITag> Document(List<ITag> tags = null, int headingLevel = 0, int indent = 0)
        {
            List<ITag> newTags = base.Document(tags, headingLevel, indent).ToList();

            Map map = model as Map;
            newTags.Add(new Section(model.Name, new MapTag(map.Center, map.Zoom, map.GetCoordinates())));
            return newTags;
        }        
    }
}
