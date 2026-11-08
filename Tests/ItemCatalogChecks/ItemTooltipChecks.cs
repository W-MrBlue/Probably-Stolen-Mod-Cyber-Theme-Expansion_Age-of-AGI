using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using AgeOfAgi;
using AgeOfAgi.Items;
using HarmonyLib;

internal static class ItemTooltipChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> definitions)
    {
        CheckNativeLayout();
        LoadedMod loaded = Program.PrepareLoadedMod(definitions);
        var lifecycle = new AgeOfAgiMod();
        lifecycle.Init(loaded.Manifest);
        lifecycle.OnEnable();
        lifecycle.OnEnable();
        var zero = new GameItemElement { Id = "age_of_agi.part_dec_0" };
        ModHelper.SetSource(zero, ModConstants.ModId, "Age of AGI");
        try
        {
            foreach (string locale in new[] { "en", "zh" })
            {
                ModHelper.Locale = locale;
                var builder = new RichTextBuilder();
                ModHook.RaiseCreateTooltipEarly(builder, zero);
                Assert(builder.Lines.Count == 1 && builder.Lines[0].Text ==
                    LocHelper.GetLocalizedMechanic("mech_tooltip_est_value", 0L, 0L),
                    "Zero values must display exactly one localized native price row.");
                Assert(builder.Lines[0].Color == RenderHandler.ColorPalette.LightTurquoise,
                    "Zero prices must use the native price color.");
            }

            zero.SetValue(100);
            Assert(PriceRows(zero) == 0, "Nonzero prices already have a native row and must not be duplicated.");
            zero.CurrentValueOverride = 0;
            zero.BaseValueOverride = 100;
            var reduced = new RichTextBuilder();
            ModHook.RaiseCreateTooltipEarly(reduced, zero);
            Assert(reduced.Lines.Single().Text.Contains("100"), "Use live base value when modifiers reduce the estimate to zero.");
            Assert(zero.unitValue == 100 && zero.SetValueCalls == 1, "Rendering must not change prices.");
            zero.SetValue(0);
            zero.CurrentValueOverride = 50;
            Assert(PriceRows(zero) == 0, "A zero CSV price with valuable contents must retain its native nonzero price row.");
            zero.CurrentValueOverride = null;
            zero.BaseValueOverride = null;

            var foreign = new GameItemElement { Id = "age_of_agi.foreign" };
            ModHelper.SetSource(foreign, "another_mod", "Other Mod");
            var native = new GameItemElement { Id = "native.item" };
            Assert(PriceRows(foreign) == 0 && PriceRows(native) == 0 && PriceRows(null) == 0,
                "Price rendering must only affect this mod's items.");
            ItemTooltipHelper.CreateZeroValueTooltip(null, zero);

            Action<GameItemElement, RichTextBuilder> renderHeader = CompileHeader();
            Assert(Render(renderHeader, zero) == "Title\nPrice",
                "Items without types must collapse the category area and header spacer.");
            zero.SetGameItemType("codeparts");
            Assert(Render(renderHeader, zero).Contains("[native:codeparts]\n<size=14px> </size>Price"),
                "Typed items must keep native categories and header spacing.");
            Assert(Render(renderHeader, foreign) == "Title\n<size=14px> </size>Price" &&
                Render(renderHeader, native) == "Title\n<size=14px> </size>Price",
                "Native and foreign item layouts must be unchanged.");
            zero.GetGameItemType().Clear();

            loaded.IsEnabled = false;
            Assert(PriceRows(zero) == 0 && Render(renderHeader, zero).Contains("<size=14px>"),
                "Disabled mods must pass through both price and layout handling.");
            loaded.IsEnabled = true;
            lifecycle.OnDisable();
            Assert(PriceRows(zero) == 0, "OnDisable must remove the early tooltip subscription.");
            lifecycle.OnEnable();
            Assert(PriceRows(zero) == 1, "Re-enabling must restore exactly one zero-price subscription.");
            ModLoader.Mods.Clear();
            Assert(PriceRows(zero) == 0 && Render(renderHeader, zero).Contains("<size=14px>"),
                "Patches must safely pass through before loader registration.");
        }
        finally
        {
            lifecycle.OnDisable();
            ModHelper.Locale = "en";
        }
    }

    private static int PriceRows(GameItem item)
    {
        var builder = new RichTextBuilder();
        ModHook.RaiseCreateTooltipEarly(builder, item);
        return builder.Lines.Count;
    }

    private static string Render(Action<GameItemElement, RichTextBuilder> renderHeader, GameItemElement item)
    {
        var builder = new RichTextBuilder();
        builder.AddLine("Title");
        renderHeader(item, builder);
        builder.AddLine("Price");
        return builder.Text;
    }

    private static Action<GameItemElement, RichTextBuilder> CompileHeader()
    {
        // Execute the transformed native header call sequence, not just its predicates.
        var original = new[]
        {
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CommonTooltipHelper), "CreateItemTypeTooltip")),
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)14),
            new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(RichTextBuilder), "AddEmptyLine")),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ret)
        };
        var method = new DynamicMethod("PatchedHeader", typeof(void),
            new[] { typeof(GameItemElement), typeof(RichTextBuilder) }, typeof(ItemTooltipChecks).Module, true);
        ILGenerator il = method.GetILGenerator();
        foreach (CodeInstruction instruction in ItemTooltipLayoutPatch.Transpiler(original))
        {
            if (instruction.operand is MethodInfo target)
            {
                il.Emit(instruction.opcode, target);
            }
            else if (instruction.operand is sbyte number)
            {
                il.Emit(instruction.opcode, number);
            }
            else
            {
                il.Emit(instruction.opcode);
            }
        }

        foreach (CodeInstruction[] unsupported in new[] { Array.Empty<CodeInstruction>(), original.Take(3).ToArray() })
        {
            bool rejected = false;
            try { ItemTooltipLayoutPatch.Transpiler(unsupported); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Unexpected native layouts must be rejected rather than partially patched.");
        }

        return (Action<GameItemElement, RichTextBuilder>)method.CreateDelegate(typeof(Action<GameItemElement, RichTextBuilder>));
    }

    private static void CheckNativeLayout()
    {
        // Inspect the installed DLL without loading Unity into this test process.
        string assembly = Program.GameAssemblyPath;
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinition type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(candidate => metadata.GetString(candidate.Name) == "GameItemElement");
        MethodDefinition method = type.GetMethods().Select(metadata.GetMethodDefinition)
            .Single(candidate => metadata.GetString(candidate.Name) == "GetTooltipBasic");
        byte[] bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null))
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        var calls = new List<string>();
        for (int offset = 0; offset < bytes.Length;)
        {
            ushort value = bytes[offset++];
            if (value == 0xFE) value = (ushort)(0xFE00 | bytes[offset++]);
            OpCode opcode = opcodes[value];
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                EntityHandle handle = MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, offset));
                string name = handle.Kind == HandleKind.MethodDefinition
                    ? metadata.GetString(metadata.GetMethodDefinition((MethodDefinitionHandle)handle).Name)
                    : handle.Kind == HandleKind.MemberReference
                        ? metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle)handle).Name) : "";
                calls.Add(name);
            }

            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }

        Assert(calls.Count(name => name == "CreateItemTypeTooltip") == 1 && calls.Count(name => name == "AddEmptyLine") == 1,
            "Installed GameItemElement.GetTooltipBasic no longer matches the two targeted header calls.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
