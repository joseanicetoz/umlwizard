namespace UmlWizard.Core.Rendering;

/// <summary>
/// The layered ("Sugiyama") layout the diagram layouts share. Nodes go in layers that follow the edges, each node after
/// the nodes it has edges from, so flows read from top to bottom (or from left to right). An edge that spans several
/// layers gets a virtual node in each layer it crosses; the nodes of each layer are ordered by the average position of
/// their neighbours to keep edges from crossing, and centred on them to keep edges straight.
/// </summary>
internal static class LayeredLayout
{
    public const double LayerGap = 70;
    public const double NodeGap = 50;
    private const double VirtualWidth = 12;
    private const double VirtualGap = 40;
    private const double Clearance = 16;
    private const double PartGap = 60;
    private const double MinimumRowWidth = 1000;

    /// <summary>How many times wider clearing the lines through nodes may make a layout; past that, it is not done.</summary>
    public const double MaxClearingGrowth = 2;

    /// <summary>
    /// Places the nodes relative to (0, 0).
    /// </summary>
    /// <param name="ids">The nodes, in diagram order (which breaks ties).</param>
    /// <param name="size">The size of each node.</param>
    /// <param name="edges">Edges between the nodes; the source goes in an earlier layer than the target.</param>
    /// <param name="leftToRight">Layers run from left to right, not from top to bottom.</param>
    /// <param name="rowEach">Every node gets a row (or column) of its own along the layers, e.g. the use cases of a
    /// system, so that the lines from outside reach every one of them without crossing another.</param>
    /// <param name="drawnOnly">Edges that are drawn but do not decide the layers (e.g. associations between classes that
    /// generalizations already place); they still count when ordering the nodes of a layer and clearing lines.</param>
    /// <param name="ports">Where along a node an edge to another node leaves it, from -0.5 (its left or top end) to 0.5
    /// (its right or bottom end); 0, its middle, when not given. A container's edges leave it from the nodes inside it.</param>
    /// <param name="gaps">The space two nodes need between them when they are side by side in a layer, e.g. for the
    /// label of the edge between them; at least <see cref="NodeGap"/>. Looked up in either order.</param>
    /// <param name="maxClearingGrowth">How many times wider moving nodes off the lines that would cross them may make the
    /// layout; when it would grow more, the lines are left to cross nodes.</param>
    /// <returns>The top-left corner of every node, and the layers.</returns>
    public static (Dictionary<string, (double X, double Y)> Positions, List<List<string>> Layers) Place(
        IReadOnlyList<string> ids, IReadOnlyDictionary<string, (double Width, double Height)> size,
        IReadOnlyList<(string Source, string Target)> edges, bool leftToRight, bool rowEach = false,
        IReadOnlyList<(string Source, string Target)>? drawnOnly = null,
        IReadOnlyDictionary<(string Node, string Other), double>? ports = null,
        IReadOnlyDictionary<(string, string), double>? gaps = null, double maxClearingGrowth = MaxClearingGrowth)
    {
        var positions = new Dictionary<string, (double X, double Y)>();
        if (ids.Count == 0)
        {
            return (positions, []);
        }

        var layers = Layers(ids, edges);
        double Along((double Width, double Height) s) => leftToRight ? s.Height : s.Width;
        double Across((double Width, double Height) s) => leftToRight ? s.Width : s.Height;

        // An edge that spans several layers gets a virtual node in each layer it crosses, so that the layers keep a
        // channel free for it: edges are drawn straight, and would otherwise cross the nodes in between.
        var layerOf = new Dictionary<string, int>();
        var rows = new List<List<string>>();
        for (var i = 0; i < layers.Count; i++)
        {
            rows.Add([.. layers[i]]);
            foreach (var id in layers[i])
            {
                layerOf[id] = i;
            }
        }

        var extent = ids.ToDictionary(id => id, id => Along(size[id]));
        var links = new List<(string Upper, string Lower)>();
        var longEdges = new List<(string, string)>();
        var portOf = new Dictionary<(string Node, string Neighbour), double>();
        var virtualNodes = 0;
        foreach (var (source, target) in edges.Concat(drawnOnly ?? []))
        {
            var (from, to) = layerOf[source] <= layerOf[target] ? (source, target) : (target, source);
            if (layerOf[from] == layerOf[to])
            {
                continue;
            }

            if (layerOf[to] - layerOf[from] > 1)
            {
                longEdges.Add((from, to));
            }

            var previous = from;
            for (var layer = layerOf[from] + 1; layer < layerOf[to]; layer++)
            {
                var id = "\0" + virtualNodes++;
                rows[layer].Add(id);
                extent[id] = VirtualWidth;
                links.Add((previous, id));
                previous = id;
            }

            links.Add((previous, to));

            // The ports of the edge, on the links that leave its two ends.
            var first = links[^(layerOf[to] - layerOf[from])].Lower;
            if (ports?.TryGetValue((from, to), out var fromPort) == true)
            {
                portOf.TryAdd((from, first), fromPort);
            }

            if (ports?.TryGetValue((to, from), out var toPort) == true)
            {
                portOf.TryAdd((to, previous), toPort);
            }
        }

        double Port(string node, string neighbour) => portOf.TryGetValue((node, neighbour), out var port) ? port : 0;
        Order(rows, links, Port);
        double Gap(string a, string b) =>
            gaps is not null && (gaps.TryGetValue((a, b), out var gap) || gaps.TryGetValue((b, a), out gap)) ? Math.Max(NodeGap, gap) : NodeGap;
        var center = Coordinates(rows, links, extent, Port, Gap);

        if (rowEach)
        {
            var along = 0.0;
            foreach (var id in ids.OrderBy(id => center[id]).ThenBy(id => layerOf[id]))
            {
                center[id] = along + extent[id] / 2;
                along += extent[id] + NodeGap / 2;
            }

            foreach (var id in center.Keys.Where(id => id[0] == '\0').ToList())
            {
                center.Remove(id);
            }
        }

        // The rows (or columns) one after the other; each node centred in its layer's thickness.
        var thickness = layers.Select(layer => layer.Max(id => Across(size[id]))).ToArray();
        var depth = new double[layers.Count];
        for (var i = 1; i < layers.Count; i++)
        {
            depth[i] = depth[i - 1] + thickness[i - 1] + LayerGap;
        }

        if (!rowEach)
        {
            // Each line cleared pushes nodes apart. On large, densely connected diagrams the pushes add up to a layout
            // many times wider than it needs to be, nearly all of it empty: there, lines are left to cross nodes instead.
            var cleared = new Dictionary<string, double>(center);
            ClearLongEdges(layers, layerOf, cleared, extent, id => Across(size[id]), depth, thickness, longEdges);
            double Width(Dictionary<string, double> at) => ids.Max(id => at[id] + extent[id] / 2) - ids.Min(id => at[id] - extent[id] / 2);
            if (Width(cleared) <= Width(center) * maxClearingGrowth)
            {
                center = cleared;
            }
        }

        var start = ids.Min(id => center[id] - extent[id] / 2);
        for (var i = 0; i < layers.Count; i++)
        {
            foreach (var id in layers[i])
            {
                var along = center[id] - extent[id] / 2 - start;
                var offset = depth[i] + (thickness[i] - Across(size[id])) / 2;
                positions[id] = leftToRight ? (offset, along) : (along, offset);
            }
        }

        return (positions, layers);
    }

    /// <summary>
    /// Edges are drawn as straight lines, not through the channels kept for them: when a line spanning several layers
    /// would run through a node, opens a gap along the line on that node's side, moving every node on that side of the
    /// line (in every layer) away from it. Each layer keeps its order, nothing comes to overlap, and the nodes that move
    /// together keep the lines between them.
    /// </summary>
    private static void ClearLongEdges(
        List<List<string>> layers, Dictionary<string, int> layerOf, Dictionary<string, double> center, Dictionary<string, double> extent,
        Func<string, double> across, double[] depth, double[] thickness, List<(string From, string To)> longEdges)
    {
        for (var round = 0; round < 10; round++)
        {
            var moved = false;
            foreach (var (from, to) in longEdges)
            {
                var (x1, y1) = (center[from], depth[layerOf[from]] + thickness[layerOf[from]] / 2);
                var (x2, y2) = (center[to], depth[layerOf[to]] + thickness[layerOf[to]] / 2);
                double At(double y) => x1 + (x2 - x1) * (y - y1) / (y2 - y1);

                for (var layer = layerOf[from] + 1; layer < layerOf[to]; layer++)
                {
                    foreach (var id in layers[layer].OrderBy(id => center[id]).ToList())
                    {
                        var top = depth[layer] + (thickness[layer] - across(id)) / 2;
                        var (lineLeft, lineRight) = (Math.Min(At(top), At(top + across(id))), Math.Max(At(top), At(top + across(id))));
                        var (left, right) = (center[id] - extent[id] / 2, center[id] + extent[id] / 2);
                        if (lineRight + Clearance <= left || lineLeft - Clearance >= right)
                        {
                            continue;
                        }

                        // Where the line cuts each layer; beyond its ends, where its end is.
                        double Cut(int k) => k <= layerOf[from] ? x1 : k >= layerOf[to] ? x2 : At(depth[k] + thickness[k] / 2);
                        var toLeft = center[id] < (lineLeft + lineRight) / 2;
                        var shift = toLeft ? -(right + Clearance - lineLeft) : lineRight + Clearance - left;
                        for (var k = 0; k < layers.Count; k++)
                        {
                            var cut = Cut(k);
                            foreach (var other in layers[k].Where(o => o != from && o != to && (toLeft ? center[o] < cut : center[o] > cut)))
                            {
                                center[other] += shift;
                            }
                        }

                        moved = true;
                    }
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The groups of nodes that edges connect, each in diagram order: the largest first, then the nodes without edges.
    /// </summary>
    public static List<List<string>> ConnectedParts(IReadOnlyList<string> ids, IReadOnlyList<(string Source, string Target)> edges)
    {
        var neighbours = edges.SelectMany(e => new[] { (e.Source, e.Target), (e.Target, e.Source) }).ToLookup(p => p.Item1, p => p.Item2);
        var order = ids.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
        var part = new Dictionary<string, int>();
        var parts = new List<List<string>>();
        foreach (var id in ids)
        {
            if (part.ContainsKey(id))
            {
                continue;
            }

            var members = new List<string>();
            var pending = new Stack<string>([id]);
            part[id] = parts.Count;
            while (pending.TryPop(out var current))
            {
                members.Add(current);
                foreach (var next in neighbours[current].Where(n => !part.ContainsKey(n)))
                {
                    part[next] = parts.Count;
                    pending.Push(next);
                }
            }

            parts.Add([.. members.OrderBy(m => order[m])]);
        }

        // Stable: parts of the same size keep diagram order.
        return [.. parts.OrderByDescending(p => p.Count)];
    }

    /// <summary>
    /// Packs separately laid out parts (each placed relative to (0, 0)) in rows, left to right, wrapping at a width that
    /// keeps the whole roughly twice as wide as it is tall, and at least as wide as the widest part.
    /// </summary>
    public static Dictionary<string, (double X, double Y)> Pack(
        IReadOnlyList<Dictionary<string, (double X, double Y)>> parts, IReadOnlyDictionary<string, (double Width, double Height)> size)
    {
        var bounds = parts.Select(p => (
            Width: p.Max(n => n.Value.X + size[n.Key].Width),
            Height: p.Max(n => n.Value.Y + size[n.Key].Height))).ToList();
        var area = bounds.Sum(b => (b.Width + PartGap) * (b.Height + PartGap));
        var rowWidth = Math.Max(bounds.Max(b => b.Width), Math.Max(MinimumRowWidth, Math.Sqrt(area * 2)));

        var result = new Dictionary<string, (double X, double Y)>();
        var (x, y, rowHeight) = (0.0, 0.0, 0.0);
        for (var i = 0; i < parts.Count; i++)
        {
            if (x > 0 && x + bounds[i].Width > rowWidth)
            {
                (x, y, rowHeight) = (0, y + rowHeight + PartGap, 0);
            }

            foreach (var (id, position) in parts[i])
            {
                result[id] = (x + position.X, y + position.Y);
            }

            x += bounds[i].Width + PartGap;
            rowHeight = Math.Max(rowHeight, bounds[i].Height);
        }

        return result;
    }

    /// <summary>
    /// The layer of each node: one after the latest node it has an edge from, ignoring the edges that close a cycle
    /// (found by a depth-first search in diagram order), so loops do not push nodes further and further.
    /// </summary>
    private static List<List<string>> Layers(IReadOnlyList<string> ids, IReadOnlyList<(string Source, string Target)> edges)
    {
        var outgoing = edges.ToLookup(e => e.Source, e => e.Target);
        var state = new Dictionary<string, int>();
        var forward = new HashSet<(string, string)>();
        var order = new List<string>();

        // Start from the nodes nothing leads to (e.g. an initial state), then the rest in diagram order.
        var incoming = edges.Select(e => e.Target).ToHashSet();
        foreach (var id in ids.Where(id => !incoming.Contains(id)).Concat(ids))
        {
            Visit(id);
        }

        void Visit(string id)
        {
            if (state.ContainsKey(id))
            {
                return;
            }

            state[id] = 1;
            foreach (var target in outgoing[id])
            {
                if (!state.TryGetValue(target, out var s))
                {
                    forward.Add((id, target));
                    Visit(target);
                }
                else if (s == 2)
                {
                    forward.Add((id, target));
                }
            }

            state[id] = 2;
            order.Add(id);
        }

        var layer = ids.ToDictionary(id => id, _ => 0);
        order.Reverse();
        foreach (var id in order)
        {
            foreach (var target in outgoing[id].Where(t => forward.Contains((id, t))))
            {
                layer[target] = Math.Max(layer[target], layer[id] + 1);
            }
        }

        return [.. ids.GroupBy(id => layer[id]).OrderBy(g => g.Key).Select(g => g.ToList())];
    }

    /// <summary>
    /// Orders the nodes of each layer to keep edges from crossing: by the average position of their neighbours, sweeping
    /// down and up, then swapping neighbours where that saves crossings; the order with the fewest crossings wins.
    /// </summary>
    private static void Order(List<List<string>> rows, List<(string Upper, string Lower)> links, Func<string, string, double> port)
    {
        var above = links.ToLookup(l => l.Lower, l => l.Upper);
        var below = links.ToLookup(l => l.Upper, l => l.Lower);
        var best = rows.Select(r => r.ToList()).ToList();
        var fewest = Crossings(rows, below, port);
        for (var sweep = 0; sweep < 12 && fewest > 0; sweep++)
        {
            Sweep(rows, above, below, port, down: sweep % 2 == 0);
            Transpose(rows, above, below, port);
            var crossings = Crossings(rows, below, port);
            if (crossings < fewest)
            {
                (best, fewest) = (rows.Select(r => r.ToList()).ToList(), crossings);
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i] = best[i];
        }
    }

    /// <summary>Where an edge leaves a node, in positions within its layer: within the node's own place.</summary>
    private static double Offset(double port) => port * 0.9;

    /// <summary>The number of edges between neighbouring layers that cross.</summary>
    private static int Crossings(List<List<string>> rows, ILookup<string, string> below, Func<string, string, double> port)
    {
        var count = 0;
        for (var i = 0; i + 1 < rows.Count; i++)
        {
            var index = rows[i + 1].Select((id, j) => (id, j)).ToDictionary(p => p.id, p => p.j);
            var edges = rows[i].SelectMany((id, j) => below[id].Where(index.ContainsKey).Select(n => (Upper: j + Offset(port(id, n)), Lower: index[n] + Offset(port(n, id))))).ToList();
            for (var a = 0; a < edges.Count; a++)
            {
                for (var b = a + 1; b < edges.Count; b++)
                {
                    if ((edges[a].Upper - edges[b].Upper) * (edges[a].Lower - edges[b].Lower) < 0)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    /// <summary>Swaps neighbouring nodes of a layer while that makes fewer of their edges cross.</summary>
    private static void Transpose(List<List<string>> rows, ILookup<string, string> above, ILookup<string, string> below, Func<string, string, double> port)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var improved = false;
            for (var i = 0; i < rows.Count; i++)
            {
                var up = i > 0 ? rows[i - 1].Select((id, j) => (id, j)).ToDictionary(p => p.id, p => p.j) : [];
                var down = i + 1 < rows.Count ? rows[i + 1].Select((id, j) => (id, j)).ToDictionary(p => p.id, p => p.j) : [];
                for (var j = 0; j + 1 < rows[i].Count; j++)
                {
                    var (u, v) = (rows[i][j], rows[i][j + 1]);
                    if (Between(v, u) < Between(u, v))
                    {
                        (rows[i][j], rows[i][j + 1]) = (v, u);
                        improved = true;
                    }
                }

                // The crossings between the edges of left and right, with left to the left of right.
                int Between(string left, string right) =>
                    Count(above[left].Where(up.ContainsKey).Select(n => up[n] + Offset(port(n, left))), above[right].Where(up.ContainsKey).Select(n => up[n] + Offset(port(n, right)))) +
                    Count(below[left].Where(down.ContainsKey).Select(n => down[n] + Offset(port(n, left))), below[right].Where(down.ContainsKey).Select(n => down[n] + Offset(port(n, right))));

                static int Count(IEnumerable<double> left, IEnumerable<double> right) =>
                    left.Sum(l => right.Count(r => r < l));
            }

            if (!improved)
            {
                return;
            }
        }
    }

    /// <summary>Orders each layer by the average position of the nodes' neighbours in the layer before it.</summary>
    private static void Sweep(List<List<string>> rows, ILookup<string, string> above, ILookup<string, string> below, Func<string, string, double> port, bool down)
    {
        for (var k = 1; k < rows.Count; k++)
        {
            var i = down ? k : rows.Count - 1 - k;
            var index = rows[down ? i - 1 : i + 1].Select((id, j) => (id, j)).ToDictionary(p => p.id, p => (double)p.j);
            var keys = rows[i].Select((id, j) =>
            {
                var positions = (down ? above[id] : below[id]).Where(index.ContainsKey).Select(n => index[n] + Offset(port(n, id))).ToList();
                return (Id: id, Key: positions.Count > 0 ? positions.Average() : j);
            }).ToList();
            rows[i] = [.. keys.OrderBy(k2 => k2.Key).Select(k2 => k2.Id)];
        }
    }

    /// <summary>
    /// The centre of each node (and virtual node) along its layer: each one as near as it can be to the average of its
    /// neighbours in the layers above and below, without overlapping the nodes beside it or changing their order. A few
    /// rounds make edges as straight as they can be, and keep long edges in straight channels.
    /// </summary>
    private static Dictionary<string, double> Coordinates(
        List<List<string>> rows, List<(string Upper, string Lower)> links, Dictionary<string, double> extent, Func<string, string, double> port,
        Func<string, string, double> gap)
    {
        var neighbours = links.SelectMany(l => new[] { (l.Upper, l.Lower), (l.Lower, l.Upper) }).ToLookup(p => p.Item1, p => p.Item2);
        var center = new Dictionary<string, double>();
        foreach (var row in rows)
        {
            var x = 0.0;
            for (var i = 0; i < row.Count; i++)
            {
                x += i == 0 ? extent[row[i]] / 2 : Separation(row[i - 1], row[i]);
                center[row[i]] = x;
            }
        }

        for (var round = 0; round < 12; round++)
        {
            var down = round % 2 == 0;
            for (var k = 0; k < rows.Count; k++)
            {
                var row = rows[down ? k : rows.Count - 1 - k];
                var desired = row.Select(id => neighbours[id].Any() ? neighbours[id].Average(n => center[n] + port(n, id) * extent[n]) : center[id]).ToArray();

                // As near to the desired place as the order allows: from the left, from the right, then between the two.
                var left = new double[row.Count];
                var right = new double[row.Count];
                for (var i = 0; i < row.Count; i++)
                {
                    left[i] = i == 0 ? desired[i] : Math.Max(desired[i], left[i - 1] + Separation(row[i - 1], row[i]));
                }

                for (var i = row.Count - 1; i >= 0; i--)
                {
                    right[i] = i == row.Count - 1 ? desired[i] : Math.Min(desired[i], right[i + 1] - Separation(row[i], row[i + 1]));
                }

                for (var i = 0; i < row.Count; i++)
                {
                    var x = (left[i] + right[i]) / 2;
                    center[row[i]] = i == 0 ? x : Math.Max(x, center[row[i - 1]] + Separation(row[i - 1], row[i]));
                }
            }
        }

        return center;

        // Nodes keep a gap between them, wider where an edge between them needs room for its label; a channel for a long
        // edge needs less.
        double Separation(string a, string b) =>
            extent[a] / 2 + extent[b] / 2 + (a[0] == '\0' || b[0] == '\0' ? VirtualGap : gap(a, b));
    }
}
