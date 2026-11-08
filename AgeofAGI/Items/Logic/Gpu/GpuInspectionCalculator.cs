using System;
using System.Globalization;
using System.Collections;
using System.Linq;
using AgeOfAgi.Npcs;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfAgi.Items.Logic.Gpu
{
    internal static class GpuInspectionCalculator
    {
        private static GameObject calculatorRoot;
        private static readonly TMP_InputField[] inputs = new TMP_InputField[4];
        private static TextMeshProUGUI computeText, powerText, statusText;
        private static Button matchButton;
        internal const string QuestionedTag = "AGE_OF_AGI_GPU_QUESTIONED";
        private static bool presentingResult;
        private static ToggleGroup questionGroup;
        private static readonly ToggleGroup[] originalGroups = new ToggleGroup[4];
        private static bool typing;
        private static bool confrontTitleWasActive;
        private static InspectionUIManager owner;
        private static GameItem item;
        private static StoreClient clientAtOpen;
        private static readonly string[] countKeys = { "gpu_calculator_modules_missing", "gpu_calculator_memory_missing",
            "gpu_calculator_memory_wrong", "gpu_calculator_memory_slow" };
        private static readonly string[] counts = new string[4];
        private static readonly System.Reflection.FieldInfo confrontItemField = AccessTools.Field(typeof(InspectionUIManager), "pendingConfrontItemId");
        private static readonly System.Reflection.FieldInfo confrontTypeField = AccessTools.Field(typeof(InspectionUIManager), "pendingConfrontType");

        internal static void Show(InspectionUIManager manager, GameItem target)
        {
            Close();
            if (!GpuRuntime.IsGpu(target) || GpuInspectionState.Read(target) == null) return;
            owner = manager;
            confrontTitleWasActive = manager.confrontTitleTMP.gameObject.activeSelf;
            item = target;
            clientAtOpen = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() : null;
            ConfigureConfront(manager);
            for (int i = 0; i < counts.Length; i++) counts[i] = "0";
            BuildNativeCalculator(manager);
            Refresh();
        }

        private static void BuildNativeCalculator(InspectionUIManager manager)
        {
            TMP_InputField template = manager.apprisalServiceInputField;
            Button[] buttons = manager.appraisalPanel.GetComponentsInChildren<Button>(true);
            Button minus = buttons.First(button => NativeStep(button) < 0);
            Button plus = buttons.First(button => NativeStep(button) > 0);
            // The price panel's amount font may contain digits only. Use a native
            // language-aware label for all descriptive text instead.
            TextMeshProUGUI label = manager.confrontTitleTMP;
            var canvas = manager.confrontPanel.GetComponentInParent<Canvas>();
            Transform host = manager.magnifyingGlass.transform.parent;
            calculatorRoot = UnityEngine.Object.Instantiate(manager.appraisalPanel, host, false);
            calculatorRoot.name = "AgeOfAgiGpuCalculator";
            calculatorRoot.SetActive(false);
            calculatorRoot.transform.SetSiblingIndex(manager.magnifyingGlass.transform.GetSiblingIndex());
            foreach (var ownedCanvas in calculatorRoot.GetComponentsInChildren<Canvas>(true)) ownedCanvas.overrideSorting = false;
            ConfigureLocalization(calculatorRoot);
            // Preserve native frame art while replacing its original price controls.
            foreach (var original in calculatorRoot.GetComponentsInChildren<TMP_InputField>(true)) original.gameObject.SetActive(false);
            foreach (var original in calculatorRoot.GetComponentsInChildren<Button>(true)) original.gameObject.SetActive(false);
            foreach (var original in calculatorRoot.GetComponentsInChildren<TextMeshProUGUI>(true)) original.gameObject.SetActive(false);
            var rootRect = (RectTransform)calculatorRoot.transform;
            const float width = 350;
            const float height = 244;
            Place(rootRect, 0, 0, width, height);
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0, 1);
            Canvas.ForceUpdateCanvases();
            var sourceRect = (RectTransform)manager.confrontPanel.transform;
            var parent = (RectTransform)rootRect.parent;
            var corners = new Vector3[4];
            var viewport = canvas != null ? (RectTransform)canvas.rootCanvas.transform : parent;
            viewport.GetWorldCorners(corners);
            Vector3 minimum = parent.InverseTransformPoint(corners[0]);
            Vector3 maximum = parent.InverseTransformPoint(corners[2]);
            sourceRect.GetWorldCorners(corners);
            Vector3 topLeft = parent.InverseTransformPoint(corners[1]);
            Vector3 topRight = parent.InverseTransformPoint(corners[2]);
            float x = topLeft.x - width - 12;
            if (x < minimum.x) x = topRight.x + 12;
            x = Math.Max(minimum.x, Math.Min(x, maximum.x - width));
            float y = Math.Max(minimum.y + height, Math.Min(topLeft.y, maximum.y));
            rootRect.localPosition = new Vector3(x, y, 0);
            for (int i = 0; i < counts.Length; i++)
            {
                int index = i;
                float rowY = 12 + i * 34;
                NewText(label, countKeys[i] + "_label", Text(countKeys[i]), 12, rowY, width - 140, 28);
                NewButton(minus, countKeys[i] + "_minus", "-", () => Step(index, -1), width - 124, rowY, 28, 28);
                var fieldNode = UnityEngine.Object.Instantiate(template.gameObject, calculatorRoot.transform, false);
                fieldNode.name = countKeys[i];
                ConfigureLocalization(fieldNode);
                Place((RectTransform)fieldNode.transform, width - 92, rowY, 48, 28);
                TMP_InputField field = fieldNode.GetComponent<TMP_InputField>();
                field.onValueChanged = new TMP_InputField.OnChangeEvent();
                field.onEndEdit = new TMP_InputField.SubmitEvent();
                field.onSubmit = new TMP_InputField.SubmitEvent();
                field.onSelect = new TMP_InputField.SelectionEvent();
                field.onDeselect = new TMP_InputField.SelectionEvent();
                field.onSelect.AddListener(_ => { StoreUIManager.Instance.StartTyping(); typing = true; });
                field.onDeselect.AddListener(_ => StopTyping());
                field.onEndEdit.AddListener(_ => StopTyping());
                field.onSubmit.AddListener(_ => StopTyping());
                field.contentType = TMP_InputField.ContentType.IntegerNumber;
                field.SetTextWithoutNotify("0");
                field.onValueChanged.AddListener(value => { counts[index] = value; Refresh(); });
                fieldNode.SetActive(true);
                inputs[i] = field;
                NewButton(plus, countKeys[i] + "_plus", "+", () => Step(index, 1), width - 40, rowY, 28, 28);
            }
            computeText = NewText(label, "compute", "", 12, 154, width - 24, 26);
            powerText = NewText(label, "power", "", 12, 182, width - 24, 26);
            statusText = NewText(label, "status", "", 12, 210, width - 108, 26);
            matchButton = NewButton(plus, "match", Text("gpu_calculator_match"), MatchLabels, width - 84, height - 34, 72, 26);
            foreach (var caption in matchButton.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (caption.transform != matchButton.transform)
                    Place((RectTransform)caption.transform, 0, 0, 72, 26);
                caption.alignment = TextAlignmentOptions.Center;
            }
            calculatorRoot.SetActive(true);
        }

        private static int NativeStep(Button button)
        {
            // Native price buttons serialize their signed step as an integer UnityEvent argument.
            object group = AccessTools.Field(typeof(UnityEngine.Events.UnityEventBase), "m_PersistentCalls").GetValue(button.onClick);
            var calls = (IEnumerable)AccessTools.Field(group.GetType(), "m_Calls").GetValue(group);
            foreach (object call in calls)
            {
                if ((string)AccessTools.Field(call.GetType(), "m_MethodName").GetValue(call) != nameof(InspectionUIManager.ChangeAppraisalServicePrice)) continue;
                object arguments = AccessTools.Field(call.GetType(), "m_Arguments").GetValue(call);
                return (int)AccessTools.Field(arguments.GetType(), "m_IntArgument").GetValue(arguments);
            }
            return 0;
        }

        private static void ConfigureLocalization(GameObject node)
        {
            foreach (var behaviour in node.GetComponentsInChildren<Behaviour>(true))
            {
                // Keep native locale-dependent fonts. Only the mod supplies strings;
                // native localization must not replace them with price-panel text.
                if (behaviour is TMPLocalizer localizer)
                {
                    localizer.ChangeFontOnly = true;
                    localizer.fontRole = FontLoader.FontRole.Noto_Sans;
                    localizer.enabled = true;
                }
                else if (behaviour.GetType().Name == "LocalizeStringEvent") behaviour.enabled = false;
            }
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            var layout = rect.gameObject.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
            var fitter = rect.gameObject.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.localScale = Vector3.one;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static TextMeshProUGUI NewText(TextMeshProUGUI template, string name, string value, float x, float y, float width, float height)
        {
            var node = UnityEngine.Object.Instantiate(template.gameObject, calculatorRoot.transform, false);
            node.name = name;
            ConfigureLocalization(node);
            Place((RectTransform)node.transform, x, y, width, height);
            var text = node.GetComponent<TextMeshProUGUI>();
            var localizer = node.GetComponent<TMPLocalizer>();
            if (localizer == null)
            {
                localizer = node.AddComponent<TMPLocalizer>();
                localizer.ChangeFontOnly = true;
                localizer.fontRole = FontLoader.FontRole.Noto_Sans;
            }
            ConfigureLocalization(node);
            text.text = value;
            text.enabled = true;
            text.font = template.font;
            if (text.font != null) text.fontSharedMaterial = text.font.material;
            text.enableAutoSizing = false;
            text.fontSize = name == "status" ? 12 : 16;
            text.margin = Vector4.zero;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableWordWrapping = true;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            node.SetActive(true);
            return text;
        }

        private static Button NewButton(Button template, string name, string caption, Action callback, float x, float y, float width, float height)
        {
            var node = UnityEngine.Object.Instantiate(template.gameObject, calculatorRoot.transform, false);
            node.name = name;
            ConfigureLocalization(node);
            Place((RectTransform)node.transform, x, y, width, height);
            var button = node.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => callback());
            foreach (var text in node.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = caption;
                text.font = owner.confrontTitleTMP.font;
                if (text.font != null) text.fontSharedMaterial = text.font.material;
                text.enableAutoSizing = false;
                text.fontSize = 16;
                text.margin = Vector4.zero;
            }
            node.SetActive(true);
            return button;
        }
        private static void ConfigureConfront(InspectionUIManager manager)
        {
            Toggle[] toggles = { manager.toggle1, manager.toggle2, manager.toggle3, manager.toggle4, manager.toggle5 };
            string[] keys = { "gpu_question_goldfinger", "gpu_question_compute", "gpu_question_power", "gpu_question_supply" };
            questionGroup = manager.confrontPanel.AddComponent<ToggleGroup>();
            questionGroup.allowSwitchOff = true;
            for (int i = 0; i < toggles.Length; i++)
            {
                toggles[i].isOn = false;
                if (i < keys.Length)
                {
                    originalGroups[i] = toggles[i].group;
                    toggles[i].group = questionGroup;
                    toggles[i].GetComponentsInChildren<TextMeshProUGUI>(true).First().text = Text(keys[i]);
                }
            }
            manager.confrontTitleTMP.gameObject.SetActive(false);
            RefreshConfront(manager);
        }

        internal static int AvailableQuestions(GameItem target)
        {
            if (!GpuRuntime.IsGpu(target)) return 0;
            int completed = target.GetTagReadonly(QuestionedTag).GetInt() & 15;
            if (target.IsTag(GpuLabels.GoldfingerExposedTag)) completed |= 1;
            if (target.FindItemFeatureByID(GpuLabels.GoldfingerFeature)?.IsExposed() == true) completed |= 1;
            if (target.FindItemFeatureByID(GpuLabels.ComputeFeature)?.IsExposed() == true) completed |= 2;
            if (target.FindItemFeatureByID(GpuLabels.PowerFeature)?.IsExposed() == true) completed |= 4;
            if (target.FindItemFeatureByID(GpuLabels.SupplyFeature)?.IsExposed() == true) completed |= 8;
            return 15 & ~completed;
        }

        internal static void RefreshConfront(InspectionUIManager manager)
        {
            if (manager != owner || item == null || !ReferenceEquals(manager.currentInspectingItem, item)) return;
            RefreshMatchButton();
            int available = AvailableQuestions(item);
            Toggle[] toggles = { manager.toggle1, manager.toggle2, manager.toggle3, manager.toggle4, manager.toggle5 };
            for (int i = 0; i < toggles.Length; i++)
            {
                bool visible = i < 4 && (available & (1 << i)) != 0;
                if (!visible) toggles[i].isOn = false;
                if (toggles[i].gameObject.activeSelf != visible) toggles[i].gameObject.SetActive(visible);
            }
            bool canConfront = CanConfront(item) && ReferenceEquals(PlayerStore.Instance.currentClientInstance.GetClientBlueprint(), clientAtOpen);
            if (manager.confrontPanel.activeSelf != canConfront) manager.confrontPanel.SetActive(canConfront);
        }
        private static void Step(int index, int delta)
        {
            if (calculatorRoot == null || item == null) return;
            var state = GpuInspectionState.Read(item);
            if (state == null) return;
            int maximum = state.Parts.Count(part => part.Slot.StartsWith(index == 0 ? "power_module_" : "memory_", StringComparison.Ordinal));
            int.TryParse(counts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int current);
            counts[index] = Math.Max(0L, Math.Min(maximum, (long)current + delta)).ToString(CultureInfo.InvariantCulture);
            inputs[index].SetTextWithoutNotify(counts[index]);
            Refresh();
        }

        private static bool Read(out GpuPerformance result)
        {
            result = default;
            return int.TryParse(counts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int modules) &&
                int.TryParse(counts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int missing) &&
                int.TryParse(counts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int wrong) &&
                int.TryParse(counts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int slow) &&
                GpuPerformanceCalculator.TryCalculateInspection(GpuInspectionState.Read(item), modules, missing, wrong, slow, out result);
        }

        private static void Refresh()
        {
            if (calculatorRoot == null || item == null) return;
            bool valid = Read(out var result);
            computeText.text = valid ? Text("gpu_calculator_compute", result.ComputePower) : Text("gpu_calculator_compute_invalid");
            powerText.text = valid ? Text("gpu_calculator_power", result.PowerPerCycle) : Text("gpu_calculator_power_invalid");
            statusText.text = valid ? "" : Text("gpu_calculator_invalid");
            RefreshMatchButton();
        }

        private static void RefreshMatchButton()
        {
            if (matchButton == null) return;
            bool owned = item != null && GeneralHelper.IsItemOwned(item);
            matchButton.gameObject.SetActive(owned);
            matchButton.interactable = owned && Read(out _);
        }

        private static void MatchLabels()
        {
            if (owner == null || !ReferenceEquals(owner.currentInspectingItem, item) ||
                !GeneralHelper.IsItemOwned(item) || !Read(out var calculated)) return;
            GpuLabels.SetClaim(item, GpuLabels.ComputeFeature, calculated.ComputePower);
            GpuLabels.SetClaim(item, GpuLabels.PowerFeature, calculated.PowerPerCycle);
            StopTyping();
            NegociationUIManager.Instance.RefreshUI();
            Refresh();
        }

        internal static bool CanConfront(GameItem target) => target != null && !GeneralHelper.IsItemOwned(target) && PlayerStore.HasInstance &&
            PlayerStore.Instance.currentClientInstance != null && !InspectionUIManager.IsConfrontLocked() &&
            (EmporiumEntry.Instance?.frontInvinvElement?.childItems.Contains(target) == true ||
             EmporiumEntry.Instance?.backInvinvElement?.childItems.Contains(target) == true);

        private static int Choice(bool goldfinger, bool compute, bool power, bool supply) =>
            (goldfinger ? 1 : 0) | (compute ? 2 : 0) | (power ? 4 : 0) | (supply ? 8 : 0);

        private static bool IsSingleChoice(int choice) => choice != 0 && (choice & (choice - 1)) == 0;

        private static bool IsCorrect(GameItem target, int choice)
        {
            if (!GpuRuntime.TryGetRealPerformance(target, out var actual)) return false;
            var state = GpuInspectionState.Read(target);
            GpuLabels.Ensure(target);
            string id = FeatureForChoice(choice);
            double value = choice == 1 ? GpuPerformanceCalculator.HasMissingGoldfinger(state) ? 0 : 1 :
                choice == 2 ? actual.ComputePower : choice == 4 ? actual.PowerPerCycle :
                GpuPerformanceCalculator.HasPowerInterface(state) ? 1 : 0;
            ItemFeature feature = target.FindItemFeatureByID(id);
            return feature != null && !feature.IsExposed() &&
                Math.Abs(GpuLabels.ReadValue(feature.GetCurrentItemCondition(), value) - value) > 0.000001;
        }

        private static bool IsBeneficialCorrection(GameItem target, int choice)
        {
            string id = FeatureForChoice(choice);
            ItemFeature feature = target.FindItemFeatureByID(id);
            if (feature == null || feature.IsExposed()) return false;
            double actual = GpuLabels.ReadValue(feature.realCondition, 0);
            double claimed = GpuLabels.ReadValue(feature.GetCurrentItemCondition(), actual);
            return choice == 4 ? actual < claimed - 0.000001 : actual > claimed + 0.000001;
        }

        private static string FeatureForChoice(int choice) => choice == 1 ? GpuLabels.GoldfingerFeature :
            choice == 2 ? GpuLabels.ComputeFeature : choice == 4 ? GpuLabels.PowerFeature : GpuLabels.SupplyFeature;

        internal static bool TryConfront(GameItem target, bool goldfinger, bool compute, bool power, bool supply)
        {
            int choice = Choice(goldfinger, compute, power, supply);
            if (!CanConfront(target) || !IsSingleChoice(choice) || (AvailableQuestions(target) & choice) == 0 ||
                !GpuRuntime.TryGetRealPerformance(target, out _)) return false;
            bool correct = IsCorrect(target, choice);
            // Failure consumes the selected question too, without exposing its claim.
            int completed = target.GetTagReadonly(QuestionedTag).GetInt() & 15;
            target.ModifyTag(QuestionedTag, tag => tag.Enable().SetInt(completed | choice));
            if (!correct) return false;
            bool beneficial = IsBeneficialCorrection(target, choice);
            if (choice == 1) target.EnableTag(GpuLabels.GoldfingerExposedTag);
            ItemFeature feature = target.FindItemFeatureByID(FeatureForChoice(choice));
            feature.SetConditions(feature.realCondition, feature.realCondition);
            feature.SetFeatureDiscovered(true);
            feature.SetExposed(true);
            // Match the native reward for pointing out a supposed counterfeit is genuine.
            if (beneficial) StoreReputation.ModCurrentClientReputation(2);
            else
            {
                string seller = PlayerStore.Instance.currentClientInstance.GetClientBlueprint().identifier;
                if (seller == ElectronicsMarket.DishonestSellerId || seller == ElectronicsSalvageSellers.DishonestScavengerId)
                    GpuLabels.AddConfrontDiscount(target, choice);
            }
            NegociationUIManager.Instance.RefreshUI();
            return true;
        }

        internal static void Submit(InspectionUIManager manager)
        {
            if (presentingResult || manager != owner || !CanConfront(item) || !ReferenceEquals(manager.currentInspectingItem, item) ||
                !ReferenceEquals(PlayerStore.Instance.currentClientInstance.GetClientBlueprint(), clientAtOpen)) return;
            bool goldfinger = manager.toggle1.isOn, compute = manager.toggle2.isOn, power = manager.toggle3.isOn, supply = manager.toggle4.isOn;
            int choice = Choice(goldfinger, compute, power, supply);
            if (!IsSingleChoice(choice) || (AvailableQuestions(item) & choice) == 0) return;
            GameItem target = item;
            StoreClient seller = clientAtOpen;
            bool correct = IsCorrect(target, choice);
            bool beneficial = correct && IsBeneficialCorrection(target, choice);
            var dialogue = new Dialogue { priority = 6 };
            Dialogue last = ClientDialogueText.SetText(dialogue, seller.displayName,
                Text(beneficial ? "gpu_calculator_confront_thanks" : correct ? "gpu_calculator_confront_success" : "gpu_calculator_confront_failed"));
            bool settled = false, success = false, finished = false;
            dialogue.SetStartAction(() =>
            {
                if (settled) return;
                settled = true;
                if (owner != manager || !ReferenceEquals(item, target) || !CanConfront(target) ||
                    (AvailableQuestions(target) & choice) == 0 ||
                    !ReferenceEquals(manager.currentInspectingItem, target) ||
                    !ReferenceEquals(PlayerStore.Instance.currentClientInstance.GetClientBlueprint(), seller)) return;
                success = TryConfront(target, goldfinger, compute, power, supply);
                confrontItemField.SetValue(manager, target.identifier);
                confrontTypeField.SetValue(manager, "confront");
                if (success)
                {
                    manager.OnConfrontSuccess();
                }
                else manager.OnConfrontFailed();
                RefreshConfront(manager);
            });
            last.SetEndAction(() =>
            {
                if (finished) return;
                finished = true;
                if (success) EmporiumAudioPlayer.Instance.PlayBeatVictory();
                if (owner != manager || !ReferenceEquals(item, target)) return;
                presentingResult = false;
                RefreshConfront(manager);
            });
            StopTyping();
            presentingResult = true;
            if (!DialogUIManager.Instance.StartDialogue(dialogue)) presentingResult = false;
        }
        internal static void Close()
        {
            presentingResult = false;
            if (questionGroup != null)
            {
                if (owner != null)
                {
                    Toggle[] toggles = { owner.toggle1, owner.toggle2, owner.toggle3, owner.toggle4 };
                    for (int i = 0; i < toggles.Length; i++)
                    {
                        toggles[i].isOn = false;
                        toggles[i].group = originalGroups[i];
                        originalGroups[i] = null;
                    }
                }
                UnityEngine.Object.Destroy(questionGroup);
                questionGroup = null;
            }
            CloseCalculator();
            if (owner != null) owner.confrontTitleTMP.gameObject.SetActive(confrontTitleWasActive);
            if (owner != null && GpuRuntime.IsGpu(owner.currentInspectingItem)) owner.confrontPanel.SetActive(false);
            owner = null; item = null; clientAtOpen = null;
        }

        private static void CloseCalculator()
        {
            StopTyping();
            if (calculatorRoot != null)
            {
                calculatorRoot.SetActive(false);
                UnityEngine.Object.Destroy(calculatorRoot);
            }
            calculatorRoot = null;
            matchButton = null;
            Array.Clear(inputs, 0, inputs.Length);
            computeText = powerText = statusText = null;
        }

        private static void StopTyping()
        {
            if (!typing) return;
            typing = false;
            StoreUIManager.Instance?.StopTyping();
        }

        private static string Text(string key, params object[] args) => ModHelper.GetLocalized(ModConstants.ModId, key, args);
    }

    [HarmonyPatch(typeof(InspectionUIManager), "Update")]
    internal static class GpuInspectionAvailabilityPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(InspectionUIManager __instance) => GpuInspectionCalculator.RefreshConfront(__instance);
    }
    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OnConfront))]
    internal static class GpuInspectionConfrontPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(InspectionUIManager __instance)
        {
            if (!GpuRuntime.IsGpu(__instance.currentInspectingItem) ||
                !ModLoader.Mods.Any(mod => mod.Manifest.ID == ModConstants.ModId && mod.IsEnabled)) return true;
            GpuInspectionCalculator.Submit(__instance);
            return false;
        }
    }
}
