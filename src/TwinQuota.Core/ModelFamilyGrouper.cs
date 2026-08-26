using System.Text.RegularExpressions;

namespace TwinQuota.Core;

public sealed record ModelFamily(
    string DisplayName,
    string Provider,
    IReadOnlyList<string> Priorities,
    IReadOnlyList<ModelAvailability> Models);

public static partial class ModelFamilyGrouper
{
    private static readonly string[] PriorityOrder = ["Low", "Medium", "High"];

    public static IReadOnlyList<ModelFamily> Group(IReadOnlyList<ModelAvailability> models) => models
        .GroupBy(GetFamilyKey, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var groupedModels = group
                .OrderBy(model => PriorityIndex(GetPriority(model.DisplayName)))
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var priorities = groupedModels
                .Select(model => GetPriority(model.DisplayName))
                .Where(priority => priority is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(PriorityIndex)
                .ToArray();

            return new ModelFamily(
                GetFamilyName(groupedModels[0].DisplayName),
                groupedModels[0].Provider,
                priorities,
                groupedModels);
        })
        .OrderBy(family => family.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string GetFamilyKey(ModelAvailability model) =>
        $"{model.Provider}\u001f{GetFamilyName(model.DisplayName)}";

    private static string GetFamilyName(string displayName) =>
        PrioritySuffixRegex().Replace(displayName, string.Empty).TrimEnd();

    private static string? GetPriority(string displayName)
    {
        var match = PrioritySuffixRegex().Match(displayName);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value;
        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private static int PriorityIndex(string? priority) =>
        Array.FindIndex(PriorityOrder, item => item.Equals(priority, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0
            ? index
            : PriorityOrder.Length;

    [GeneratedRegex(@"\s*\((low|medium|high)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrioritySuffixRegex();
}
