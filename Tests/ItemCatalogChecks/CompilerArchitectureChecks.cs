using System;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;
using AgeOfAgi.Items.Logic.BaseModel;
using AgeOfAgi.Items.Logic.AiModel;
using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.Compiler;

internal static partial class CompilerChecks
{
    private static void CheckArchitectures()
    {
        var residual = new Rig(); residual.Install();
        residual.Place("part_enc_0", 1, 4, new byte[] { 1, 1, 1, 0, 1, 1 }, 2);
        residual.Place("part_dec_0", 4, 4, new byte[] { 1, 1, 1 }, 1);
        residual.Place("part_link", 2, 5, new byte[] { 1, 1 }, 2);
        residual.Place("part_residual", 2, 2, new byte[] { 1, 1, 1, 1, 0, 1 }, 3);
        residual.Place("part_residual", 2, 7, new byte[] { 1, 0, 1, 1, 1, 1 }, 3);
        var result = residual.Calculate();
        Assert(result.Model.Traits == AiModelTraits.Residual, "Residual needs the same actual linked pair.");
        Near(result.TrainingWork, 64, "Two active residual bridges apply once globally");
        Assert(result.MemoryGb == 3, "Round final residual memory upward.");
        CheckArchitecturePreview(residual, 80, 2, 24, 10, 20);
        residual.Base.childItem.ModifyTag(BaseModelState.MemoryMultiplierTag, tag => tag.SetFloat(2));
        CheckArchitecturePreview(residual, 80, 4, 24, 10, 20);
        Assert(residual.Calculate().MemoryGb == 5, "Actual residual memory still includes the architecture and base multipliers.");
        residual.Grid.Remove(residual.Grid.childItems.Single(item => item.identifier == CompilerCatalog.LinkId));
        Assert((residual.Calculate().Model.Traits & AiModelTraits.Residual) == 0, "Touching an unlinked pair gives no residual benefit.");

        var crossed = new Rig(); crossed.Install();
        crossed.Place("part_enc_0", 1, 4, new byte[] { 1, 1, 1, 0 }, 2);
        crossed.Place("part_enc_1", 6, 1);
        var correctDecoder = crossed.Place("part_dec_0", 4, 5, new byte[] { 1, 1 }, 1);
        var otherDecoder = crossed.Place("part_dec_1", 4, 4, new byte[] { 1, 1, 1 }, 3);
        crossed.Place("part_link", 2, 5, new byte[] { 1, 1 }, 2);
        crossed.Place("part_link", 6, 2, new byte[] { 1, 1 }, 1);
        crossed.Place("part_residual", 2, 2, new byte[] { 1, 1, 1, 1, 0, 1 }, 3);
        Assert((crossed.Calculate().Model.Traits & AiModelTraits.Residual) == 0,
            "Two separately matched endpoints are insufficient when they are not each other's selected pair.");
        otherDecoder.SetShape(new GridShapeBuilder().SetData(new byte[] { 1 }, 1).SetPosition(6, 4));
        correctDecoder.SetShape(new GridShapeBuilder().SetData(new byte[] { 1, 1, 1 }, 1).SetPosition(4, 4));
        Assert((crossed.Calculate().Model.Traits & AiModelTraits.Residual) != 0, "Contact with an actual selected pair activates residual.");
        CheckResidualPorts();

        var batch = new Rig(); batch.Install();
        batch.Place("part_batch", 2, 2); batch.Place("part_batch", 3, 2);
        batch.Place("part_enc_0", 2, 1); batch.Place("part_enc_1", 3, 1);
        batch.Place("part_enc_2", 2, 3, new byte[] { 1, 1 }, 2);
        batch.Place("part_dec_0", 0, 0);
        batch.Place("part_batch", 6, 5); batch.Place("part_enc_3", 6, 4); batch.Place("part_enc_4", 7, 5);
        result = batch.Calculate();
        Assert(result.Model.BatchSize == 3 && (result.Model.Traits & AiModelTraits.Batch) != 0,
            "Count shared encoders once and take the largest group, not the sum of groups.");
        Near(result.TrainingWork, 440 * 1.3, "Batch processors increase global work once by thirty percent");
        batch.Grid.Items.Reverse();
        Assert(result.Matches(batch.Calculate()), "Traits and maximum group must be independent of insertion order.");
        batch.Click();
        var frozen = batch.Output.childItems.Single();
        Assert(AiModelState.GetBatchSize(frozen) == 3 && AiModelState.GetTraits(frozen) == AiModelTraits.Batch,
            "Compilation freezes trait identities and batch size on the output.");
        frozen.onLoaded?.Invoke(frozen);
        Assert(AiModelState.GetBatchSize(frozen) == 3, "Loading preserves frozen batch size.");

        var separate = new Rig(); separate.Install();
        separate.Place("part_batch", 1, 1); separate.Place("part_batch", 3, 1);
        separate.Place("part_enc_0", 2, 1); separate.Place("part_enc_1", 1, 0); separate.Place("part_enc_2", 3, 0);
        separate.Place("part_dec_0", 0, 5);
        Assert(separate.Calculate().Model.BatchSize == 2, "A shared encoder cannot join separate processor groups.");
        var lone = new Rig(); lone.Install();
        lone.Place("part_batch", 1, 1); lone.Place("part_enc_0", 1, 0); lone.Place("part_dec_0", 3, 0);
        Assert(lone.Calculate().Model.Traits == AiModelTraits.None && lone.Calculate().Model.BatchSize == 1,
            "One adjacent encoder does not activate batch processing.");

        foreach (bool encoderHeavy in new[] { true, false })
        {
            var heavy = new Rig(); heavy.Install();
            for (int i = 0; i < 4; i++) heavy.Place(encoderHeavy ? "part_enc_0" : "part_dec_0", i, 0);
            heavy.Place(encoderHeavy ? "part_dec_0" : "part_enc_0", 0, 2);
            Assert(heavy.Calculate().Model.Traits == AiModelTraits.None, "Heavy architectures require regularization.");
            heavy.Place("part_regularization", 1, 2);
            result = heavy.Calculate();
            Assert(result.Model.Traits == (encoderHeavy ? AiModelTraits.HeavyEncoder : AiModelTraits.HeavyDecoder), "Correct asymmetric architecture.");
            Near(result.ComputeBonusPercent, encoderHeavy ? 40 : 60, "Multiply model bonus, not percentage-point addition");
            Assert(result.MemoryGb == (encoderHeavy ? 3 : 7), "Final memory uses exact decimal multipliers before ceiling.");
            Near(result.InitialDurability, 9, "Round duplicate penalty and regularization together once");
            CheckArchitecturePreview(heavy, 200, 5, 50, 9, 50);
        }

        var rank = new Rig(); rank.Install();
        for (int i = 0; i < 3; i++) { rank.Place("part_enc_" + i, i, 0); rank.Place("part_dec_" + i, i, 2); }
        rank.Place("part_regularization", 4, 0);
        result = rank.Calculate();
        Assert(result.Model.Traits == AiModelTraits.HighRank, "Uniqueness is checked within each family.");
        Near(result.InitialDurability, 18, "High rank and regularization multiply");
        Near(result.TrainingWork, 432, "High rank training cost");
        CheckArchitecturePreview(rank, 360, 10, 90, 12, 90);
        rank.Place("part_enc_0", 5, 5);
        Assert((rank.Calculate().Model.Traits & AiModelTraits.HighRank) == 0, "A repeated ID disables high rank.");

        var stacked = new Rig(); stacked.Install();
        for (int i = 0; i < 3; i++) { stacked.Place("part_enc_" + i, i, 0); stacked.Place("part_dec_" + i, i, 3); }
        stacked.Place("part_batch", 0, 1, new byte[] { 1, 1, 1 }, 3);
        stacked.Place("part_regularization", 4, 5);
        result = stacked.Calculate();
        Assert(result.Model.Traits == (AiModelTraits.Batch | AiModelTraits.HighRank) && result.Model.BatchSize == 3,
            "Different architecture traits coexist and remain individually visible.");
        Near(result.TrainingWork, 360 * 1.2 * 1.3, "Different architecture work effects multiply");
        Near(result.InitialDurability, 18, "Stacking training effects does not apply durability twice");
        CheckArchitecturePreview(stacked, 360 * 1.3, 10, 90, 12, 90, 3);
        stacked.Click();
        var stackedModel = stacked.Output.childItems.Single();
        Near(AiModelState.GetTrainingWork(stackedModel), 360 * 1.2 * 1.3, "Compiled work retains the hidden high-rank multiplier");
        Near(AiModelState.GetPredictedDurability(stackedModel), 18, "Compiled durability retains the hidden high-rank multiplier");
        Assert(AiModelState.GetTraits(stackedModel) == (AiModelTraits.Batch | AiModelTraits.HighRank),
            "Hiding preview traits must not remove them from the compiled model.");

        Assert(!AiModelState.ValidTraits(AiModelTraits.Batch, 1) && !AiModelState.ValidTraits((AiModelTraits)128, 1),
            "Reject malformed frozen traits rather than inventing runtime batch behavior.");

        var deep = new Rig(); deep.Install();
        for (int i = 0; i < 2; i++)
        {
            deep.Place("part_enc_" + i, 1, 1 + 3 * i);
            deep.Place("part_dec_" + i, 6, 1 + 3 * i);
            deep.Place("part_feedback", 2, 1 + 3 * i, new byte[] { 1, 1, 1, 1 }, 4);
        }
        result = deep.Calculate();
        Assert(result.Model.Traits == AiModelTraits.DeepFeedback && result.TrainingData == 25, "Deep feedback discounts only the training data budget.");
        Near(result.TrainingWork, 200 * .8 * 1.3, "Local feedback and deep feedback training multipliers coexist");
        CheckArchitecturePreview(deep, 200 * .8, 6, 50, 10, 50);
        deep.Grid.childItems.Single(item => item.identifier == "age_of_agi.part_enc_0")
            .ModifyTag(AiPartState.TrainingDataTag, tag => tag.SetInt(11));
        Assert(deep.Calculate().TrainingData == 26, "Odd data totals round upward after all multipliers.");

        var asym = new Rig(); asym.Install();
        asym.Place("part_link", 2, 2, new byte[] { 1, 1 }, 2);
        asym.Place("part_link", 2, 5, new byte[] { 1, 1 }, 2);
        asym.Place("part_enc_0", 1, 2); asym.Place("part_enc_1", 1, 5);
        asym.Place("part_dec_0", 4, 2); asym.Place("part_dec_1", 4, 5); asym.Place("part_dec_2", 6, 6);
        Assert((asym.Calculate().Model.Traits & AiModelTraits.UShape) == 0, "Spare unpaired decoders block U-shape.");
        asym.Grid.Remove(asym.Grid.childItems.Single(item => item.identifier == "age_of_agi.part_dec_2"));
        Assert(asym.Calculate().Model.Traits == AiModelTraits.UShape, "Fully linked balanced pairs activate U-shape.");
        Near(asym.Calculate().ComputeBonusPercent, 90, "Actual U-shape compute retains its multiplier");
        CheckArchitecturePreview(asym, 200, 6, 60, 10, 50);
        Console.WriteLine("PASS: residual actual pairs, batch connected groups, U-shape coverage, heavy architectures, high rank, deep feedback, rounding and frozen model traits.");
    }

    private static void CheckArchitecturePreview(Rig rig, double work, int memory, float compute, float durability, int data, int batchSize = 1)
    {
        string previousLocale = ModHelper.Locale;
        try
        {
            foreach (string locale in new[] { "en", "zh" })
            {
                ModHelper.Locale = locale;
                rig.Machine.contentWindow.stateChangeActionFunc();
                string actual = ((MachineryStack)rig.Row.GetPanel("control").content).Elements.OfType<MachineryText>().Single().Text;
                string expected = ModHelper.GetLocalized(AgeOfAgi.ModConstants.ModId, "compiler_preview", work, memory, compute, durability, 10, data) +
                    "\n\n" + AiModelState.TaskName(AiTaskTypes.Computing);
                if (batchSize > 1) expected += "\n" + ModHelper.GetLocalized(AgeOfAgi.ModConstants.ModId, "ai_stat_batch", batchSize);
                Assert(actual == expected.Replace("\\n", "\n"),
                    "Preview must show component/base values and batch only, hiding other architecture effects and names: " + actual);
            }
        }
        finally
        {
            ModHelper.Locale = previousLocale;
        }
    }

    private static void CheckResidualPorts()
    {
        var expected = new[]
        {
            (First: (X: 3, Y: 5), Second: (X: 5, Y: 5)),
            (First: (X: 5, Y: 3), Second: (X: 5, Y: 5)),
            (First: (X: 3, Y: 2), Second: (X: 5, Y: 2)),
            (First: (X: 2, Y: 3), Second: (X: 2, Y: 5))
        };
        GridShape Cell(int x, int y) => new GridShapeBuilder().SetData(new byte[] { 1 }, 1).SetPosition(x, y);
        for (int rotation = 0; rotation < 4; rotation++)
            foreach (bool flip in new[] { false, true })
            {
                var shape = new GridShapeBuilder().SetData(new byte[] { 1, 1, 1, 1, 0, 1 }, 3).SetTransform(3, 3, flip, rotation);
                Assert(CompilerResidualBridge.TryGetPorts(shape, out var first, out var second) &&
                    first == expected[rotation].First && second == expected[rotation].Second,
                    "C ports follow the opening under every rotation and flip.");
                var encoder = Cell(first.X, first.Y); var decoder = Cell(second.X, second.Y);
                Assert(CompilerResidualBridge.Connects(shape, encoder, decoder) && CompilerResidualBridge.Connects(shape, decoder, encoder),
                    "Either C port can receive the encoder or decoder.");
            }
        var normal = new GridShapeBuilder().SetData(new byte[] { 1, 1, 1, 1, 0, 1 }, 3).SetPosition(3, 3);
        foreach (var contacts in new[] { (3, 2, 5, 2), (2, 4, 6, 4), (4, 4, 5, 5), (2, 5, 6, 5) })
            Assert(!CompilerResidualBridge.Connects(normal, Cell(contacts.Item1, contacts.Item2), Cell(contacts.Item3, contacts.Item4)),
                "Back, outer sides, inner gap and diagonals never activate residual.");
        var hole = new GridShapeBuilder().SetData(new byte[] { 1, 0 }, 2).SetPosition(2, 5);
        Assert(!CompilerResidualBridge.Connects(normal, hole, Cell(5, 5)), "Require an occupied port cell, not just the basis bounding box.");
        var boundary = new GridShapeBuilder().SetData(new byte[] { 1, 1, 1, 1, 0, 1 }, 3).SetTransform(0, 0, false, 2);
        Assert(!CompilerResidualBridge.Connects(boundary, Cell(0, 1), Cell(2, 1)), "Ports outside the compiler do not borrow interior neighbors.");
        foreach (var mask in new[] { new byte[] { 1, 1, 1, 1 }, new byte[] { 1, 1, 1, 1, 1, 1 },
            new byte[] { 0, 1, 1, 1, 1, 1 }, new byte[] { 1, 1, 0, 1, 0, 1 } })
        {
            var invalid = new Rig(); invalid.Install();
            invalid.Place("part_residual", 0, 0, mask, mask.Length == 4 ? 4 : 3);
            invalid.Place("part_enc_0", 5, 5); invalid.Place("part_dec_0", 7, 5);
            Assert(!invalid.TryCalculate(out _, out string reason) && reason == "compiler_invalid_parts", "Only a complete five-cell C is a residual bridge.");
        }
    }
}
