using System;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Functions;
using Models.PMF.Organs;
using Models.PMF.Struct;

namespace Models.PMF.Phen
{
    /// <summary>
    /// This model calculates a BBCH growth stage value based upon the current phenological growth stage within the model. 
    /// The model uses information regarding germination, emergence and leaf appearance for early growth stages (BBCH stages 0 to 39).
    /// 
    ///|BeginStage |Growth Phase     |Description                             |
    ///|-----------|-----------------|:---------------------------------------|
    ///|1          |Germinating      |BBCH = 5 x FractionThroughPhase         |
    ///|2          |Emerging         |BBCH = 5 + 5 x FractionThroughPhase     |
    ///|3          |Juvenile         |BBCH = 10 + (Leaf.AppearedCohortNo - 1) |
    ///|4          |PhotoSensitive   |BBCH = 10 + (Leaf.AppearedCohortNo - 1) |
    ///|5          |LeafAppearance   |BBCH = 30 + LeavesAppearedInPhase       |
    ///
    /// BBCHSTages 11-19 assume the dropy leaf method of measuring leaf appearance and to translate this to a model variable we assum droopy leaves are one fewer than the number of tips visiable
    /// WE assume that the begining of stem extension (BBCH 30) corresponds to the floral initiation stage in the model (Stage 5).  Scores between 31 and 39 depend on the number of nodes visiable.
    /// The model does not simulate nodes explicitly so we assume that node apparance occurs at the same rate as leaf appearance an add the number of leaves that have appeared in the LeafAppearance Phase to give an estimate of BBCH score
    /// BBCH stage 50 occurs when the tastle is just visiable.  This is assumed to occur at the same time as the appearance of the tip of the flag leaf.  This occurs toward the end of the leaf appearance phase 
    /// While still in the leaf apperance phase after flag leaf tip appearance BBCH score is calculated as:
    /// BBCH = 50 + 5 * FractionFlagLeafExpansion
    /// This assumes the tassel will be half emerged when the flaf leaf is full expanded   
    /// The model then uses simulated phenological growth stages for BBCH stages 55 to 99.
    /// 
    ///|Stage   |APSIM Name              |BBCH translation
    ///|--------|------------------------|-------------------------------------|
    ///|6.0     |FlagLeafFullyExpanded   |55 - Mid Tassel Emergence
    ///|7.0     |Flowering               |65 - Mid flowering
    ///|8.0     |StartGrainFill          |70 - Begining of Grain development
    ///|9.0     |EndGrainFill            |87 - Physiological maturity
    ///|10      |Maturity                |99 - Harvest Product
    ///
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Phenology))]
    public class BBCH : Model
    {
        /// <summary>The phenology</summary>
        [Link]
        Phenology Phenology = null;

        /// <summary>
        /// The Simple Leaf class
        /// </summary>
        [Link]
        SimpleLeaf leaf = null;

        bool StemExtensionInitialised = false;
        bool TasselVisiable = false;
        double ligulesAtStartStemExtension = 0;
        double FractionInPhaseAtBBCH50 = 0;

        /// <summary>Gets the stage.</summary>
        /// <value>The stage.</value>
        [Description("BBCH phenoligical stages")]

        public double Stage
        {
            get
            {
                double fracInCurrent = Phenology.FractionInCurrentPhase;
                double BBCH_stage = 0.0;
                double LeafTips = (leaf.FindChild("Tips") as IFunction).Value();
                double LeafLigules = (leaf.FindChild("Ligules") as IFunction).Value();
                double FinalLeafNumber = (leaf.FindChild("FinalLeafNumber") as IFunction).Value();

                if (Phenology.InPhase("Germinating"))
                    BBCH_stage = 5.0f * fracInCurrent;
                else if (Phenology.InPhase("Emerging"))
                {

                    BBCH_stage = 5.0f + 5 * fracInCurrent;
                }
                else if (Phenology.InPhase("Juvenile") || Phenology.InPhase("PhotoSensitive"))
                {
                    BBCH_stage = Math.Min(19.0, 10.0f + Math.Max(0, LeafTips - 1));
                }
                else if (Phenology.InPhase("LeafAppearance") && (LeafTips <= FinalLeafNumber))
                {
                    if (StemExtensionInitialised == false)
                    {
                        ligulesAtStartStemExtension = LeafLigules;
                        StemExtensionInitialised = true;
                    }
                    BBCH_stage = 30.0f + Math.Min(9, LeafLigules - ligulesAtStartStemExtension);
                }
                else if (Phenology.InPhase("LeafAppearance") && (LeafTips >= FinalLeafNumber))
                {
                    if (TasselVisiable == false)
                    {
                        FractionInPhaseAtBBCH50 = fracInCurrent;
                        TasselVisiable = true;
                    }
                    if (fracInCurrent == 0.0)//Falt in FractionCurrent meaning it returns a zero when phase is complete rather than a one
                        fracInCurrent = 1.0;
                    BBCH_stage = 50.0f + 5 * (fracInCurrent - FractionInPhaseAtBBCH50) / (1 - FractionInPhaseAtBBCH50);
                }
                else
                {

                    double[] BBCH_code_y = { 55, 65, 70, 87, 99.0 };
                    double[] PMF_Stage = { 6.0, 7.0, 8.0, 9.0, 10.0 };
                    bool DidInterpolate;
                    BBCH_stage = MathUtilities.LinearInterpReal(Phenology.Stage,
                                                               PMF_Stage, BBCH_code_y,
                                                               out DidInterpolate);
                }
                return BBCH_stage;
            }
        }
    }
}