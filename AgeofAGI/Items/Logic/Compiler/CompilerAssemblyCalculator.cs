using AgeOfAgi.Items.Logic.AiParts;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Definitions;

namespace AgeOfAgi.Items.Logic.Compiler
{
    // No inventory mutations or item creation here: preview and execution share this
    // calculation and always include every placed component in the result/cost.
    internal static class CompilerAssemblyCalculator
    {
        internal static bool TryCalculate(GameGridInventory grid, int width, int height, float memoryMultiplier,
            AiPartCatalog catalog, CompilerRecipeDefinition recipe, out CompilerAssemblyResult result, out string reason)
        {
            result = null;
            reason = "compiler_invalid_parts";
            if (width <= 0 || height <= 0 || !Positive(memoryMultiplier))
            {
                return false;
            }
            var parts = new List<CompilerPartSnapshot>();
            var occupied = new HashSet<(int, int)>();
            foreach (GameItem item in grid.childItems)
            {
                if (item == null || !ReferenceEquals(item.parentInventory, grid) || !GeneralHelper.IsItemOwned(item) ||
                    item.unitCount != 1 || !AiPartState.IsPart(item) || !catalog.TryGet(item.identifier, out var definition)) return false;
                var part = new CompilerPartSnapshot(item, definition);
                if (definition.IsBasis && (!item.IsTag(AiPartState.ComputeBonusTag) || !item.IsTag(AiPartState.MemoryTag) ||
                    !item.IsTag(AiPartState.TrainingTag) || !item.IsTag(AiPartState.TrainingDataTag) ||
                    !Positive(part.Compute) || part.Memory <= 0 || !Positive(part.Training) || part.TrainingData <= 0)) return false;
                bool any = false;
                for (int y = part.Shape.minY; y < part.Shape.minY + part.Shape.globalHeight; y++)
                {
                    for (int x = part.Shape.minX; x < part.Shape.minX + part.Shape.globalWidth; x++)
                    {
                        if (!At(part.Shape, x, y))
                        {
                            continue;
                        }
                        if (x < 0 || y < 0 || x >= width || y >= height || !occupied.Add((x, y)))
                        {
                            return false;
                        }
                        any = true;
                    }
                }
                if (!any)
                {
                    return false;
                }
                if ((definition.Role == "feedback" && !IsEndpointShape(part.Shape, AiPartState.FeedbackLength)) ||
                    (definition.Role == "link" && !IsEndpointShape(part.Shape, 2)) ||
                    (definition.Role == "residual" && !CompilerResidualBridge.TryGetPorts(part.Shape, out _, out _)))
                {
                    return false;
                }
                parts.Add(part);
            }
            // Sorting by position makes equal-score pairing independent of insertion
            // order and stable after native save reconstruction.
            parts = parts.OrderBy(part => part.Shape.minY).ThenBy(part => part.Shape.minX)
                .ThenBy(part => part.Item.identifier, StringComparer.Ordinal).ToList();
            var encoders = parts.Where(part => part.Definition.Role == "encoder").ToArray();
            var decoders = parts.Where(part => part.Definition.Role == "decoder").ToArray();
            if (encoders.Length == 0 || decoders.Length == 0)
            {
                reason = "compiler_missing_pair";
                return false;
            }
            string[] taskTypes = parts.Where(part => part.Definition.Role == "task_head")
                .Select(part => part.Definition.TaskType).ToArray();
            if (taskTypes.Length > 1)
            {
                reason = "compiler_conflicting_heads";
                return false;
            }

            int regularizers = parts.Count(part => part.Definition.Role == "regularization");
            if (regularizers > 1)
            {
                reason = "compiler_multiple_regularizers";
                return false;
            }
            bool[,] linkEdges = BuildEdges(parts, encoders, decoders, "link");
            bool[,] feedbackEdges = BuildEdges(parts, encoders, decoders, "feedback");
            var linkPairs = Match(encoders, decoders, linkEdges);
            var feedbackPairs = Match(encoders, decoders, feedbackEdges);
            var linked = new HashSet<CompilerPartSnapshot>(linkPairs.SelectMany(pair => new[] { pair.Encoder, pair.Decoder }));
            var feedback = new HashSet<CompilerPartSnapshot>(feedbackPairs.SelectMany(pair => new[] { pair.Encoder, pair.Decoder }));
            double compute = 0;
            double training = 0;
            long memory = 0;
            long trainingData = 0;
            var shapeCounts = new Dictionary<(string, int), int>();
            int duplicates = 0;
            foreach (CompilerPartSnapshot part in parts.Where(part => part.Definition.IsBasis))
            {
                compute += part.Compute * (linked.Contains(part) ? (double)recipe.LinkComputeMultiplier : 1);
                training += part.Training * (feedback.Contains(part) ? (double)recipe.FeedbackTrainingRetention : 1);
                // Feedback reduces computation only. Data is an independent saved attribute.
                trainingData += part.TrainingData;
                memory += part.Memory;
                var key = (part.Definition.Role, part.Definition.ShapeId);
                shapeCounts.TryGetValue(key, out int count);
                if (count > 0)
                {
                    duplicates++;
                }
                shapeCounts[key] = count + 1;
            }
            CompilerArchitecture architecture;
            decimal previewMemory, finalMemory, finalData;
            try
            {
                architecture = CompilerArchitecture.Calculate(parts, encoders, decoders, linkPairs, feedbackPairs, regularizers == 1, recipe);
                previewMemory = Math.Ceiling(memory * (decimal)memoryMultiplier);
                finalMemory = Math.Ceiling(memory * architecture.MemoryMultiplier * (decimal)memoryMultiplier);
                finalData = Math.Ceiling(trainingData * architecture.DataMultiplier);
            }
            catch (OverflowException) { return false; }
            // Preview reveals component/base effects and batch processing only.
            // Keep the unrounded component durability for the final model calculation.
            double componentDurability = recipe.InitialDurability * Math.Pow(recipe.DuplicateDurabilityRetention, duplicates) *
                (regularizers == 1 ? recipe.RegularizationMultiplier : 1);
            AiModelTraits previewTraits = architecture.Traits & AiModelTraits.Batch;
            double previewTraining = training * (previewTraits != AiModelTraits.None ? recipe.BatchTrainingMultiplier : 1);
            float previewDurability = AiModelState.RoundDurability(componentDurability);
            string taskType = taskTypes.FirstOrDefault() ?? AiTaskTypes.Computing;
            var preview = new CompilerAssemblyPreview(previewTraining, previewMemory, compute,
                previewDurability, trainingData, previewTraits, architecture.BatchSize);
            compute *= architecture.ComputeMultiplier;
            training *= architecture.TrainingMultiplier;
            float durability = AiModelState.RoundDurability(componentDurability * architecture.DurabilityMultiplier);
            if (finalMemory > int.MaxValue || finalData > int.MaxValue || finalMemory < 1 || finalData < 1 ||
                !Positive((float)compute) || !Positive((float)training) || !Positive(durability))
            {
                return false;
            }
            result = new CompilerAssemblyResult(parts.AsReadOnly(), taskType,
                (float)training, (int)finalMemory, (float)compute, durability, linkPairs.Count, feedbackPairs.Count, (int)finalData,
                preview, architecture.Traits, architecture.BatchSize);
            reason = null;
            return true;
        }

        private static bool Positive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);

        private static bool At(GridShape shape, int x, int y)
        {
            return x >= shape.minX && y >= shape.minY && x < shape.minX + shape.globalWidth &&
                y < shape.minY + shape.globalHeight && shape.Get(x, y) == 1;
        }

        internal static bool Adjacent(GridShape first, GridShape second)
        {
            // Native node adjacency translates the actual occupied mask by one cell
            // along each cardinal direction. Bounding-box contact alone is insufficient.
            for (int y = first.minY; y < first.minY + first.globalHeight; y++)
                for (int x = first.minX; x < first.minX + first.globalWidth; x++)
                    if (At(first, x, y) && (At(second, x - 1, y) || At(second, x + 1, y) ||
                        At(second, x, y - 1) || At(second, x, y + 1))) return true;
            return false;
        }

        private static bool IsEndpointShape(GridShape shape, int length)
        {
            if (!((shape.globalWidth == length && shape.globalHeight == 1) ||
                (shape.globalWidth == 1 && shape.globalHeight == length))) return false;
            for (int y = 0; y < shape.globalHeight; y++)
                for (int x = 0; x < shape.globalWidth; x++)
                    if (!At(shape, shape.minX + x, shape.minY + y))
                    {
                        return false;
                    }
            return true;
        }

        private static bool EndpointPair(GridShape connector, GridShape encoder, GridShape decoder)
        {
            int x = connector.minX;
            int y = connector.minY;
            bool horizontal = connector.globalHeight == 1;
            int firstX = horizontal ? x - 1 : x;
            int firstY = horizontal ? y : y - 1;
            int lastX = horizontal ? x + connector.globalWidth : x;
            int lastY = horizontal ? y : y + connector.globalHeight;
            return (At(encoder, firstX, firstY) && At(decoder, lastX, lastY)) ||
                (At(decoder, firstX, firstY) && At(encoder, lastX, lastY));
        }

        private static bool[,] BuildEdges(List<CompilerPartSnapshot> parts, CompilerPartSnapshot[] encoders,
            CompilerPartSnapshot[] decoders, string role)
        {
            var edges = new bool[encoders.Length, decoders.Length];
            foreach (var function in parts.Where(part => part.Definition.Role == role))
                for (int e = 0; e < encoders.Length; e++)
                    for (int d = 0; d < decoders.Length; d++)
                        edges[e, d] |= EndpointPair(function.Shape, encoders[e].Shape, decoders[d].Shape);
            return edges;
        }

        // Maximum-weight bipartite matching. Residual paths can reassign an earlier
        // pair when connectors overlap, maximizing total base compute rather than
        // letting the first enumerated connector claim the best components.
        private static List<CompilerPair> Match(CompilerPartSnapshot[] encoders,
            CompilerPartSnapshot[] decoders, bool[,] edges)
        {
            int source = encoders.Length + decoders.Length;
            int sink = source + 1;
            int nodes = sink + 1;
            var capacity = new int[nodes, nodes];
            var cost = new double[nodes, nodes];
            for (int e = 0; e < encoders.Length; e++) capacity[source, e] = 1;
            for (int d = 0; d < decoders.Length; d++) capacity[encoders.Length + d, sink] = 1;
            for (int e = 0; e < encoders.Length; e++)
                for (int d = 0; d < decoders.Length; d++)
                {
                    if (!edges[e, d])
                    {
                        continue;
                    }
                    int decoder = encoders.Length + d;
                    capacity[e, decoder] = 1;
                    cost[e, decoder] = -((double)encoders[e].Compute + decoders[d].Compute);
                    cost[decoder, e] = -cost[e, decoder];
                }
            while (true)
            {
                var distance = Enumerable.Repeat(double.PositiveInfinity, nodes).ToArray();
                var previous = Enumerable.Repeat(-1, nodes).ToArray();
                distance[source] = 0;
                for (int iteration = 0; iteration < nodes - 1; iteration++)
                {
                    bool changed = false;
                    for (int from = 0; from < nodes; from++)
                        for (int to = 0; to < nodes; to++)
                        {
                            double next = distance[from] + cost[from, to];
                            if (capacity[from, to] > 0 && next < distance[to] - 0.0000001)
                            {
                                distance[to] = next;
                                previous[to] = from;
                                changed = true;
                            }
                        }
                    if (!changed)
                    {
                        break;
                    }
                }
                if (previous[sink] < 0 || distance[sink] >= 0)
                {
                    break;
                }
                for (int to = sink; to != source; to = previous[to])
                {
                    int from = previous[to];
                    capacity[from, to]--;
                    capacity[to, from]++;
                }
            }
            var matched = new List<CompilerPair>();
            for (int e = 0; e < encoders.Length; e++)
                for (int d = 0; d < decoders.Length; d++)
                    if (edges[e, d] && capacity[e, encoders.Length + d] == 0)
                    {
                        matched.Add(new CompilerPair(encoders[e], decoders[d]));
                    }
            return matched;
        }
    }
}
