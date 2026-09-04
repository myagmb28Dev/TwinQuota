namespace TwinQuota.Core;

public sealed record ProcessTreeEntry(
    int ProcessId,
    int ParentProcessId,
    string ProcessName);

public static class AntigravityProcessTree
{
    public static bool HasActiveExtensionDescendant(
        int editorProcessId,
        IReadOnlyList<ProcessTreeEntry> processes)
    {
        if (editorProcessId <= 0 || processes.Count == 0)
        {
            return false;
        }

        var childrenByParent = processes
            .GroupBy(process => process.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var pending = new Queue<int>();
        var visited = new HashSet<int> { editorProcessId };
        pending.Enqueue(editorProcessId);

        while (pending.Count > 0)
        {
            var parentId = pending.Dequeue();
            if (!childrenByParent.TryGetValue(parentId, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (!visited.Add(child.ProcessId))
                {
                    continue;
                }

                var processName = Path.GetFileNameWithoutExtension(child.ProcessName);
                if (Normalize(processName).Equals("agy", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                pending.Enqueue(child.ProcessId);
            }
        }

        return false;
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));
}
