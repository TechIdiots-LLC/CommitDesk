using CommitDesk.Core.Hosting;

namespace CommitDesk.ViewModels;

/// <summary>The server types a user can choose for an account or sign-in when the app can't tell by itself.</summary>
public static class ServerTypes
{
    static readonly (HostingKind Kind, string Name)[] All =
    [
        (HostingKind.GitLab, "GitLab"),
        (HostingKind.GitHub, "GitHub Enterprise"),
        (HostingKind.Unknown, "Other git server"),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. All.Select(t => t.Name)];

    public static int IndexOf(HostingKind kind) => Array.FindIndex(All, t => t.Kind == kind) is var i and >= 0 ? i : All.Length - 1;

    public static HostingKind KindAt(int index) => index >= 0 && index < All.Length ? All[index].Kind : HostingKind.Unknown;
}
