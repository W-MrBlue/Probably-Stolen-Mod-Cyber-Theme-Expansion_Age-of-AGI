using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Logic.ComplexData;
using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Npcs;
using System;
using System.Collections.Generic;
using System.Linq;

internal static class DataBuyerChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        var loaded = Program.PrepareLoadedMod(items);
        var mod = new AgeOfAgiMod(); mod.Init(loaded.Manifest);
        PlayerStore.Instance = null;
        PlayerStore.ForbidLazyCreation = true;
        mod.OnEnable(); mod.OnEnable();
        ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
        Assert(DataBuyers.CreateEnterprise() == null, "The enterprise buyer waits for native player and scene setup.");
        PlayerStore.ForbidLazyCreation = false;
        PlayerStore.Instance = new PlayerStore { Attractiveness = 400 };
        EmporiumEntry.Instance = new EmporiumEntry();
        StoreStation.Instance.dayCounter = 1;
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations[locale] = Program.ReadLocale(locale);
        try
        {
            ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
            Assert(TypeHelper.MAJOR_TYPES_LIST.Count(type => type == DataBuyers.MainType) == 1 &&
                StoreClientListDict.storeClientDict.Count == 13 && StoreClientList.generalPool.entries.Count == 3,
                "One enterprise data buyer replaces the three tiered data engineers without adding a random buyer pool.");
            StoreClient client = DataBuyers.CreateEnterprise();
            Assert(client.identifier == DataBuyers.EnterpriseId && client.clientIntent == StoreClient.ClientIntent.BUY &&
                client.clientFaction == "FACTION_UPPER_LEVEL" && client.GetBudget() == StoreClientListTier.CreateHospitalAdmin().GetBudget() &&
                client.clientBuyingTagList.Count == 0 && client.clientBuyingIdList.Count == 0,
                "The enterprise service uses the native upper medical budget and predicate-only container purchasing.");
            foreach (string id in new[] { "age_of_agi.data_container", "age_of_agi.hypo_data_container" })
            {
                GameItem container = DirectoryMaster.Item(id);
                DataContentState.SetAmount(container, 0);
                Assert(client.willAcceptItem(container), "Empty containers remain saleable at shell value.");
                DataContentState.SetAmount(container, 1);
                Assert(client.willAcceptItem(container), "Partially filled containers are accepted.");
                container.EnableTag("NOT_FOR_RESALE");
                Assert(!client.willAcceptItem(container), "Native resale restrictions remain authoritative.");
                container.DisableTag("NOT_FOR_RESALE");
                container.ModifyTag(DataContentState.AmountTag, tag => tag.SetInt(DataContentState.Capacity(container) + 1));
                Assert(!client.willAcceptItem(container), "Malformed container contents are rejected.");
                container.Destroy();
            }
            Assert(!client.willAcceptItem(null) && !client.willAcceptItem(DirectoryMaster.Item("age_of_agi.cpu0")) &&
                !client.willAcceptItem(DirectoryMaster.Item("age_of_agi.dc_common_data_disk")) &&
                !client.willAcceptItem(DirectoryMaster.Item(ComplexDataState.ItemId)),
                "The enterprise buyer rejects hardware, raw carriers and genetic containers.");

            client.OverrideBudget(19);
            client.mainDialogue.startAction();
            var lines = new List<string>();
            for (Dialogue line = client.mainDialogue; line != null; line = line.nextDialogue) lines.Add(line.plainText);
            Assert(lines.Last().Contains("19") && EmporiumEntry.Instance.frontInvinvElement.childItems.Count == 0,
                "The buyer reports its current budget and never offers empty containers for sale.");

            string selected = "native";
            Assert(DataShowcasePatch.Prefix("WATER", ref selected) && selected == "native" &&
                !DataShowcasePatch.Prefix(DataBuyers.MainType, ref selected) && selected == null,
                "An empty container showcase produces no buyer while native showcase branches remain untouched.");
            GameItem displayed = DirectoryMaster.Item("age_of_agi.data_container");
            DataContentState.SetAmount(displayed, 0);
            EmporiumEntry.Instance.showcaseElement.UncheckedAccept(displayed);
            Assert(!DataShowcasePatch.Prefix(DataBuyers.MainType, ref selected) && selected == DataBuyers.EnterpriseId,
                "Any valid displayed container routes to the single enterprise buyer without value tiers.");
            Assert(DataBuyers.QueueDebug(DataBuyers.EnterpriseId) &&
                PlayerStore.Instance.storeClientManager.clientStack.Last().identifier == DataBuyers.EnterpriseId,
                "The merged buyer supports one F9 preview action.");
            Assert(!DataBuyers.QueueDebug("age_of_agi.data_engineer"), "Retired data-engineer IDs have no debug or runtime route.");
        }
        finally
        {
            mod.OnDisable();
            Assert(!TypeHelper.MAJOR_TYPES_LIST.Contains(DataBuyers.MainType) &&
                !StoreClientListDict.storeClientDict.ContainsKey(DataBuyers.EnterpriseId),
                "Disable removes the enterprise factory and owned major type.");
            PlayerStore.Instance = null;
            ModHelper.Translations.Clear(); ModHelper.Locale = "en";
        }
        Console.WriteLine("PASS: single enterprise container buyer, no container resale offer, live budget dialogue, showcase routing and owned cleanup.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
