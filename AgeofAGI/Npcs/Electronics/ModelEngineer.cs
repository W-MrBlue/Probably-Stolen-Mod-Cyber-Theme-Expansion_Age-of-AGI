namespace AgeOfAgi.Npcs
{
    internal static class ModelEngineer
    {
        internal const string ClientId = ModConstants.ModId + ".model_engineer";

        internal static StoreClient CreateBuyer() => Create(StoreClient.ClientIntent.BUY);
        internal static StoreClient CreateSeller() => Create(StoreClient.ClientIntent.SELL);

        private static StoreClient Create(StoreClient.ClientIntent intent)
        {
            if (!ElectronicsMarket.Ready) return null;
            StoreClient client = StoreClientListTier.CreateStationEngineer();
            client.identifier = ClientId;
            client.displayName = ModHelper.GetLocalized(ModConstants.ModId, "model_engineer_name");
            client.clientIntent = intent;
            if (intent == StoreClient.ClientIntent.SELL)
            {
                client.isOfferWholesale = true;
                client.wholesaleDiscount = -RNG.GetRandomInt(15, 25);
            }
            client.clientBuyingTagList.Clear();
            client.clientBuyingIdList.Clear();
            client.willAcceptItem = item => ElectronicsMarket.Ready && item != null &&
                !item.IsTag("NOT_FOR_RESALE") && item.GetGameItemType().Contains("ai_models");
            ClientDialogueCatalog.Apply(client, ClientId);
            ModHelper.SetClientSource(client, ModConstants.ModId);
            return client;
        }
    }
}
