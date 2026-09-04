using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class AntigravityProcessTreeTests
{
    [Fact]
    public void DetectsAgyOnlyWhenItDescendsFromTheEditorWindowProcess()
    {
        ProcessTreeEntry[] processes =
        [
            new(10, 1, "Code.exe"),
            new(11, 10, "Code.exe"),
            new(12, 11, "agy.exe")
        ];

        Assert.True(AntigravityProcessTree.HasActiveExtensionDescendant(10, processes));
        Assert.False(AntigravityProcessTree.HasActiveExtensionDescendant(99, processes));
    }

    [Fact]
    public void RejectsAnUnrelatedAgyProcessAndGenericLanguageServers()
    {
        ProcessTreeEntry[] processes =
        [
            new(10, 1, "Code.exe"),
            new(11, 10, "language_server.exe"),
            new(30, 1, "agy.exe")
        ];

        Assert.False(AntigravityProcessTree.HasActiveExtensionDescendant(10, processes));
    }
}
