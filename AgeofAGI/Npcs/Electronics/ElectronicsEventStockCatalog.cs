using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgeOfAgi.Npcs
{
    internal sealed class ElectronicsEventStockCatalog
    {
        internal const string CatalogPath = "Data/electronics_event_stock.csv";
        private readonly Dictionary<string, List<MarketOffer>> offers;

        private ElectronicsEventStockCatalog(Dictionary<string, List<MarketOffer>> offers)
        {
            this.offers = offers;
        }

        internal static ElectronicsEventStockCatalog Load(IReadOnlyList<ItemDefinition> items) =>
            Parse(items, CatalogCsvReader.ReadModFile(CatalogPath), CatalogPath);

        internal static ElectronicsEventStockCatalog Parse(IReadOnlyList<ItemDefinition> items, string csv, string path)
        {
            var definitions = items.ToDictionary(item => item.ItemId, StringComparer.Ordinal);
            var allowedSellers = new HashSet<string>(ElectronicsEventClients.SellerIds, StringComparer.Ordinal);
            var result = new Dictionary<string, List<MarketOffer>>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in CatalogCsvReader.ReadRows(csv, path,
                new[] { "seller", "item_id", "count", "data_fraction" }))
            {
                string seller = row.ReadString("seller");
                string itemId = row.ReadString("item_id");
                int count = row.ReadInt("count");
                double fraction = row.ReadDouble("data_fraction");
                if (!allowedSellers.Contains(seller)) throw row.Invalid("Unknown event seller: " + seller);
                if (!definitions.TryGetValue(itemId, out var definition)) throw row.Invalid("Unknown event stock item: " + itemId);
                if (count <= 0) throw row.Invalid("Event stock count must be positive.");
                if (fraction < -1 || fraction > 1) throw row.Invalid("data_fraction must be -1 or a value from 0 to 1.");
                bool dataItem = definition.Kind == "data_carrier" || definition.Kind == "data_container";
                if (!dataItem && fraction >= 0) throw row.Invalid("Only data carriers and containers accept a data fraction.");
                if (!seen.Add(seller + ":" + itemId)) throw row.Invalid("Duplicate event stock item: " + seller + "/" + itemId);
                if (!result.TryGetValue(seller, out var sellerOffers))
                    result.Add(seller, sellerOffers = new List<MarketOffer>());
                for (int index = 0; index < count; index++)
                {
                    sellerOffers.Add(new MarketOffer
                    {
                        Id = itemId,
                        State = fraction < 0 ? "factory" : "fraction",
                        Fraction = fraction
                    });
                }
            }
            foreach (string seller in allowedSellers)
                if (!result.ContainsKey(seller)) throw new InvalidOperationException(path + ": missing event seller " + seller + ".");
            return new ElectronicsEventStockCatalog(result);
        }

        internal List<MarketOffer> GetOffers(string seller)
        {
            return offers[seller].Select(offer => new MarketOffer
            {
                Id = offer.Id,
                State = offer.State,
                Fraction = offer.Fraction
            }).ToList();
        }
    }
}
