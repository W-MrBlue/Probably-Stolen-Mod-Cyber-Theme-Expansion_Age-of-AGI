using AgeOfAgi.Items.Logic.DataContent;
using System;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Gpu;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsStock
    {
        internal static GameItem Create(MarketOffer offer, bool dishonest = false)
        {
            GameItem item = DirectoryMaster.Item(offer.Id);
            if (item == null) throw new InvalidOperationException("Electronic merchandise is not registered: " + offer.Id);
            try
            {
                if (offer.State != "factory")
                    DataContentState.SetAmount(item, (int)Math.Round(DataContentState.Capacity(item) * offer.Fraction,
                        MidpointRounding.AwayFromZero));
                if (dishonest) GpuLabels.ApplyDishonestClaims(item, new Random(Guid.NewGuid().GetHashCode()));
                return item;
            }
            catch { item.Destroy(); throw; }
        }

        internal static double RollRawFraction(Random random)
        {
            double fraction;
            do
            {
                double normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
                fraction = 0.75 + 0.1 * normal;
            } while (fraction < 0.5 || fraction > 1);
            return fraction;
        }

        // A failed placement consumes the draw; never replace it with a smaller item.
        internal static bool TryDeliver(MarketOffer offer, bool stolen = false, bool dishonest = false, bool matchActualLabels = false)
        {
            if (!PlayerStore.HasInstance || EmporiumEntry.Instance == null) return false;
            GameItem item = ElectronicsStock.Create(offer, dishonest);
            if (matchActualLabels && GpuRuntime.IsGpu(item)) GpuLabels.MatchActualLabels(item);
            bool fits;
            try
            {
                fits = GraphUtils.CanAccept(EmporiumEntry.Instance.frontInvinvElement, item);
            }
            catch { item.Destroy(); throw; }
            if (!fits)
            {
                item.Destroy();
                return false;
            }
            // Native delivery owns cleanup if a final placement/faction check fails, and preserves stolen-item guards.
            return PlayerStore.Instance.TryAddDirectSellingItemToTable(item, isStolen: stolen);
        }

    }

    internal sealed class MarketOffer
    {
        internal string Id, State;
        internal double Fraction;
    }
}
