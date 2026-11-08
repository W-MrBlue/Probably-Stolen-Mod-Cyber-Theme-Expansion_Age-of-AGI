using AgeOfAgi.Items.Logic.DataContent;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgeOfAgi.Items.Logic.Server
{
    // A transaction snapshot. Ordering determines where newly extracted data goes first.
    internal sealed class ServerDataPool
    {
        private readonly Func<IEnumerable<GameItem>> read;
        private readonly GameItem[] containers;
        private readonly int[] amounts, capacities;
        internal long Amount => amounts.Sum(value => (long)value);
        internal long Free => capacities.Select((capacity, index) => (long)capacity - amounts[index]).Sum();
        internal bool Present => containers.Length > 0;

        internal ServerDataPool(Func<IEnumerable<GameItem>> read)
        {
            this.read = read;
            containers = Read();
            amounts = containers.Select(DataContentState.Amount).ToArray();
            capacities = containers.Select(DataContentState.Capacity).ToArray();
        }

        private GameItem[] Read() => read().Where(item => DataContentState.IsContainer(item) &&
            DataContentState.Valid(item) && GeneralHelper.IsItemOwned(item)).Distinct().ToArray();

        internal bool Matches() => containers.SequenceEqual(Read()) && containers.Select((item, index) =>
            DataContentState.Amount(item) == amounts[index] && DataContentState.Capacity(item) == capacities[index]).All(value => value);

        internal void AddTools(CraftingRecipe recipe, List<GameItem> inputs)
        {
            foreach (GameItem container in containers)
            {
                inputs.Add(container);
                recipe.AddTool(container.name, item => ReferenceEquals(item, container));
            }
        }

        internal void Consume(int amount) => Change(amount, false);
        internal void Deliver(int amount) => Change(amount, true);

        private void Change(int remaining, bool adding)
        {
            for (int index = 0; index < containers.Length && remaining > 0; index++)
            {
                int change = Math.Min(remaining, adding ? capacities[index] - amounts[index] : amounts[index]);
                if (change > 0) DataContentState.SetAmount(containers[index], amounts[index] + (adding ? change : -change));
                remaining -= change;
            }
        }
    }
}
