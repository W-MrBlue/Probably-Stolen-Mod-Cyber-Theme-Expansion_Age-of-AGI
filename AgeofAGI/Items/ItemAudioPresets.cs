using System;
using System.Collections.Generic;

namespace AgeOfAgi.Items
{
    internal static class ItemAudioPresets
    {
        // Resolve only while binding factories. Helpers invoke the captured native method.
        private static readonly Dictionary<string, Action<GameItem>> initializers =
            new Dictionary<string, Action<GameItem>>(StringComparer.Ordinal)
            {
                ["none"] = item => { },
                ["default"] = global::AudioHelper.SetToDefault,
                ["battery"] = global::AudioHelper.InitBattery,
                ["module"] = global::AudioHelper.InitModule,
                ["small_module"] = global::AudioHelper.InitSmallModule,
                ["small_machine"] = global::AudioHelper.InitSmallMachine,
                ["metal_machine"] = global::AudioHelper.InitMetalMachine,
                ["tiny_item"] = global::AudioHelper.InitTinyItem,
                ["tiny_metal"] = global::AudioHelper.InitTinyMetal,
                ["paper_sheet"] = global::AudioHelper.InitPaperSheet,
                ["plastic_card"] = global::AudioHelper.InitPlasticCard,
                ["plastic_container"] = global::AudioHelper.InitPlasticContainer,
                ["solid_metal"] = global::AudioHelper.InitSolidMetal,
                ["glass"] = global::AudioHelper.InitGlass,
                ["book"] = global::AudioHelper.InitBook,
                ["cassette"] = global::AudioHelper.InitCassette,
                ["scanner"] = global::AudioHelper.InitScanner,
                ["scrap_metal"] = global::AudioHelper.InitScrapMetal,
                ["carton_box"] = global::AudioHelper.InitCartonBox,
                ["carton_card"] = global::AudioHelper.InitCartonCard,
                ["dossier"] = global::AudioHelper.InitDossier
            };

        internal static Action<GameItem> Resolve(string preset)
        {
            if (preset == null || !initializers.TryGetValue(preset, out var initialize))
            {
                throw new ArgumentException("Unknown audio_preset: '" + preset + "'.", nameof(preset));
            }

            return initialize;
        }
    }
}
