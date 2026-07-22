using System;
using System.Collections.Generic;

namespace AetherLove.Changelog;

public sealed record ChangelogEntry(
    Version Version,
    DateOnly ReleaseDate,
    IReadOnlyList<string> NewFeatures,
    IReadOnlyList<string> BugFixes,
    IReadOnlyList<string> Important)
{
    public string VersionString => $"{Version.Major}.{Version.Minor}.{Version.Build}";
}
