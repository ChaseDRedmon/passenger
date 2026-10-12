namespace Passenger.App.Vcs;

/// <summary>A row of the log's graph: the commit, the lane its dot is in, and the commit each lane waits for just
/// above the row and just below it (null for an empty lane), from which the lines through the row are drawn.</summary>
public sealed record GraphRow(CommitInfo Commit, int Lane, IReadOnlyList<string?> Above, IReadOnlyList<string?> Below)
{
    /// <summary>The most lanes the row's lines cross, for its width.</summary>
    public int Width => Math.Max(Lane + 1, Math.Max(Above.Count, Below.Count));
}

/// <summary>Lays out the log's graph as Git's own does: each commit takes the lane that waits for it (a new one when
/// none does), hands that lane to its first parent and opens lanes for the others, and lanes that waited for the same
/// commit join at its dot.</summary>
public static class CommitGraph
{
    public static IReadOnlyList<GraphRow> Layout(IReadOnlyList<CommitInfo> commits)
    {
        List<string?> lanes = [];
        List<GraphRow> rows = [];
        foreach (CommitInfo commit in commits)
        {
            string?[] above = [.. lanes];
            int lane = lanes.IndexOf(commit.Hash);
            if (lane < 0)
            {
                lane = lanes.IndexOf(null);
                if (lane < 0)
                {
                    lane = lanes.Count;
                    lanes.Add(null);
                }
            }
            for (int i = 0; i < lanes.Count; i++)
            {
                if (i != lane && lanes[i] == commit.Hash)
                {
                    lanes[i] = null;
                }
            }
            lanes[lane] = commit.Parents.Count > 0 ? commit.Parents[0] : null;
            foreach (string parent in commit.Parents.Skip(1))
            {
                if (lanes.Contains(parent))
                {
                    continue;
                }
                int free = lanes.IndexOf(null);
                if (free < 0)
                {
                    lanes.Add(parent);
                }
                else
                {
                    lanes[free] = parent;
                }
            }
            while (lanes.Count > 0 && lanes[^1] is null)
            {
                lanes.RemoveAt(lanes.Count - 1);
            }
            rows.Add(new GraphRow(commit, lane, above, [.. lanes]));
        }
        return rows;
    }
}
