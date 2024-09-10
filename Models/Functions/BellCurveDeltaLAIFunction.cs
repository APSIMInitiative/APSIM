using System;
using Models.Core;

namespace Models.Functions
{
    /// <summary>
    /// Calculates the daily change in plant leaf area (mm2/plant) assuming a Bell-shaped curve distribution of plant leaf sizes, accounting for mutliple leaves expanding in parallel.
    /// </summary>
    [Serializable]
    public class BellCurveDeltaLAIFunction : Model, IFunction
    {
        /// <summary>The largest leaf position</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction largestLeafPosition = null; // Node position where the largest leaf occurs (e.g. 10 is the 10th leaf from bottom to top)
        /// <summary>The area maximum</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction areaMax = null;             // Area of the largest leaf of a plant (m2)
        /// <summary>The breadth</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction breadth = null;
        /// <summary>The skewness</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction skewness = null;
        /// <summary>The number of leaf tips</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction leafTips = null;
        /// <summary>The number of leaf ligules (ie fully expanded leaves)</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction leafLigules = null;
        /// <summary>Leaf Apprearance Rate</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction leafAppearanceRate = null;  // (/day)
        /// <summary>Plant Population</summary>
        [Link(Type = LinkType.Child, ByName = true)] IFunction population = null;  // (/m2)


        /// <summary>Gets the value.</summary>
        public double Value(int arrayIndex = -1)
        {
            double tips = leafTips.Value(arrayIndex);
            double collars = leafLigules.Value(arrayIndex);
            
            double deltaLAI = 0;
            int lowestExpandingLeaf = Convert.ToInt32(collars + 1);
            int highestExpandingLeaf = Convert.ToInt32(tips);

            for (int l = lowestExpandingLeaf; l < highestExpandingLeaf; l++)
            {
                double leafarea = areaMax.Value(arrayIndex) * Math.Exp(breadth.Value(arrayIndex) * Math.Pow(l - largestLeafPosition.Value(arrayIndex), 2.0)
                                + skewness.Value(arrayIndex) * (Math.Pow(l - largestLeafPosition.Value(arrayIndex), 3.0)));
                deltaLAI += leafarea * leafAppearanceRate.Value(arrayIndex) * population.Value(arrayIndex);
            }
            return deltaLAI;
        }
    }
}
