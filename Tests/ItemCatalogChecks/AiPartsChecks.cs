using AgeOfAgi.Items.Logic.AiParts;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;

internal static class AiPartsChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        Assert(definitions.Any(item => item.Kind == AiPartState.PartsKind) &&
            definitions.Any(item => item.Kind == AiPartState.ChipKind), "Fixture exercises both components and source chips.");
        Assert(AiPartState.HandlesKind("ai_parts") && AiPartState.HandlesKind("ai_parts_chip") &&
            !AiPartState.HandlesKind("gpu") && !AiPartState.HandlesKind(null), "Incorrect family dispatch.");
        Assert(!AiPartState.IsPart(null), "Queries must be null-safe.");

        ItemDefinition part = definitions.First(item => item.Kind == AiPartState.PartsKind);
        var mismatch = new GameItem { Id = "other.item" };
        ExpectArgument(() => AiPartsHelper.Init(mismatch, part, AudioHelper.InitModule));
        Assert(!mismatch.IsTag(AiPartState.FamilyTag), "A mismatched definition must not mutate the item.");
        ExpectArgument(() => AiPartsHelper.Init(null, part, AudioHelper.InitModule));
        ExpectArgument(() => AiPartsHelper.Init(mismatch, null, AudioHelper.InitModule));

        LoadedMod mod = Program.PrepareLoadedMod(definitions);
        var registration = new ItemRegistration(mod.Manifest);
        var directory = new ModItemDirectory();
        registration.Register(directory, definitions);
        int reads = ModHelper.FileReads;
        try
        {
            foreach (ItemDefinition definition in definitions)
            {
                GameItem item = directory.Factories[definition.ItemId]();
                bool isPart = definition.Kind == AiPartState.PartsKind;
                bool isChip = definition.Kind == AiPartState.ChipKind;
                Assert(item.IsTag(AiPartState.FamilyTag) == (isPart || isChip) &&
                    AiPartState.IsPart(item) == isPart && item.IsTag(AiPartState.ChipTag) == isChip,
                    "Family/subtype tags leaked or were missing: " + definition.ItemId);
                if (isPart || isChip)
                {
                    Assert(item.unitValue == definition.BaseValue && item.SetValueCalls == 1,
                        "Family initialization must preserve common CSV prices.");
                    if (definition.ItemId == "age_of_agi.part_feedback")
                        Assert(item.modifiedShape.globalWidth == 4 && item.modifiedShape.globalHeight == 1 &&
                            ItemShapeHelper.SameCells(item.modifiedShape, mod.Sprites[definition.SpriteKey].Shape),
                            "Feedback keeps its native 4x1 artwork footprint.");
                    else
                        Assert(ItemShapeHelper.SameCells(item.modifiedShape, mod.Sprites[definition.SpriteKey].Shape),
                            "Family initialization changed a component/chip's sprite-derived shape.");
                    Assert(isChip ? item.GetGameItemType().Count == 0 : item.GetGameItemType().SequenceEqual(new[] { "codeparts" }),
                        "Components need codeparts; source chips need no displayed type.");
                }
            }
            Assert(ModHelper.FileReads == reads, "This family has no extra configuration to reread during creation.");
        }
        finally { registration.Unregister(); }
    }

    private static void ExpectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Invalid AI-parts initialization was accepted.");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
