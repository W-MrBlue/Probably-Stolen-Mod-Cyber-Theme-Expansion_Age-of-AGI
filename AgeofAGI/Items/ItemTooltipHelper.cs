using AgeOfAgi.Items.Logic.Compiler;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace AgeOfAgi.Items
{
    internal static class ItemTooltipHelper
    {
        private static bool HandlesItem(GameItem item)
        {
            return item != null && item.IsTag("MOD_ID_TAG") &&
                item.GetTagReadonly("MOD_ID_TAG").GetString() == ModConstants.ModId &&
                ModLoader.Mods.Exists(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled);
        }

        internal static void CreateZeroValueTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || !HandlesItem(item))
            {
                return;
            }

            // Match GetTooltipBasic's native visibility test, including contained items
            // and current value modifiers. Nonzero values already have a native row.
            long value = item.GetCurrentValue(useRetailMarkup: false, withChild: true,
                includeEvents: true, forceMarkup: false, extraMarketMod: 0L);
            if (value == 0L)
            {
                builder.AddLine(LocHelper.GetLocalizedMechanic("mech_tooltip_est_value", value, item.GetBaseValue()),
                    ignoreEmpty: true, color: RenderHandler.ColorPalette.LightTurquoise);
            }
        }

        internal static void CreateItemTypeTooltip(RichTextBuilder builder, GameItem item)
        {
            if (HandlesItem(item) && item.GetGameItemType().Count == 0)
            {
                return;
            }

            CommonTooltipHelper.CreateItemTypeTooltip(builder, item);
        }

        internal static RichTextBuilder AddHeaderSpacing(RichTextBuilder builder, int heightPixels, GameItem item)
        {
            // The native builder defers line breaks. Skipping an empty category alone
            // cannot remove the spacer that GetTooltipBasic adds after the header.
            if (HandlesItem(item) && item.GetGameItemType().Count == 0)
            {
                return builder;
            }

            return builder.AddEmptyLine(heightPixels);
        }

        internal static string GetLocalizedMechanic(string key, object[] arguments, GameItem item)
        {
            if (key == "mech_tooltip_locked" && HandlesItem(item) && item.IsTag(Logic.Compiler.CompilerState.BaseLockedTag))
                return ModHelper.GetLocalized(ModConstants.ModId, "compiler_base_locked");
            return LocHelper.GetLocalizedMechanic(key, arguments);
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "GetTooltipBasic")]
    internal static class ItemTooltipLayoutPatch
    {
        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            var nativeTypes = AccessTools.Method(typeof(CommonTooltipHelper), nameof(CommonTooltipHelper.CreateItemTypeTooltip));
            var nativeSpacing = AccessTools.Method(typeof(RichTextBuilder), nameof(RichTextBuilder.AddEmptyLine));
            var nativeMechanic = AccessTools.Method(typeof(LocHelper), nameof(LocHelper.GetLocalizedMechanic));
            int typeCalls = 0;
            int spacingCalls = 0;
            foreach (CodeInstruction original in instructions)
            {
                var instruction = new CodeInstruction(original);
                if (instruction.Calls(nativeTypes))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ItemTooltipHelper), nameof(ItemTooltipHelper.CreateItemTypeTooltip));
                    typeCalls++;
                }
                else if (instruction.Calls(nativeSpacing) || instruction.Calls(nativeMechanic))
                {
                    bool spacing = instruction.Calls(nativeSpacing);
                    // Supply the current item to scope native tooltip adjustments.
                    var loadItem = new CodeInstruction(OpCodes.Ldarg_0);
                    loadItem.MoveLabelsFrom(instruction);
                    result.Add(loadItem);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = spacing
                        ? AccessTools.Method(typeof(ItemTooltipHelper), nameof(ItemTooltipHelper.AddHeaderSpacing))
                        : AccessTools.Method(typeof(ItemTooltipHelper), nameof(ItemTooltipHelper.GetLocalizedMechanic));
                    if (spacing) spacingCalls++;
                }

                result.Add(instruction);
            }

            if (typeCalls != 1 || spacingCalls != 1)
            {
                throw new InvalidOperationException("Unsupported GetTooltipBasic layout: expected one category call and one header spacer.");
            }

            return result;
        }
    }
}
