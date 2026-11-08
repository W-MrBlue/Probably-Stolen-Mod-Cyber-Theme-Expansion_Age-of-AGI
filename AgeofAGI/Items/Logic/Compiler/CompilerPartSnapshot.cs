using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.Compiler
{
    internal sealed class CompilerPartSnapshot
    {
        internal GameItem Item { get; }
        internal AiPartDefinition Definition { get; }
        internal GridShape Shape { get; }
        internal float Compute { get; }
        internal int Memory { get; }
        internal float Training { get; }
        internal int TrainingData { get; }

        internal CompilerPartSnapshot(GameItem item, AiPartDefinition definition)
        {
            Item = item;
            Definition = definition;
            Shape = item.modifiedShape.Clone();
            Compute = definition.IsBasis ? AiPartState.GetComputeBonus(item) : 0;
            Memory = definition.IsBasis ? AiPartState.GetMemoryGb(item) : 0;
            Training = definition.IsBasis ? AiPartState.GetTrainingWork(item) : 0;
            TrainingData = definition.IsBasis ? AiPartState.GetTrainingData(item) : 0;
        }

        internal bool Matches(CompilerPartSnapshot other)
        {
            return ReferenceEquals(Item, other.Item) && Compute == other.Compute && Memory == other.Memory &&
                Training == other.Training && TrainingData == other.TrainingData && Shape.minX == other.Shape.minX && Shape.minY == other.Shape.minY &&
                Shape.orientation == other.Shape.orientation && Shape.flipped == other.Shape.flipped &&
                ItemShapeHelper.SameCells(Shape, other.Shape);
        }
    }
}
