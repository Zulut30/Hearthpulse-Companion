namespace Hearthpulse.Core.Pool;

/// <summary>
/// The states of the own hero statistics for the render check of both apps (<c>--render-ui</c>, group <c>bgown</c>),
/// so the two hosts draw the same set. Not used by the running app.
/// </summary>
public static class OwnBgHeroesScenes
{
    /// <summary>
    /// Each state by the name of its frame. The frames with rows (<c>ok</c>, <c>small</c>, <c>old-copy</c>) need the
    /// pool answers <c>bg-heroes-render.json</c> and <c>bg-heroes-render-small.json</c> in
    /// <paramref name="fixtureFolder"/>; without them only the empty states are returned.
    /// </summary>
    public static IReadOnlyList<(string Name, OwnBgHeroesResult Result)> All(string? fixtureFolder, DateTime now)
    {
        var scenes = new List<(string, OwnBgHeroesResult)>();
        if (Read(fixtureFolder, "bg-heroes-render.json") is { } ok)
        {
            scenes.Add(("ok", OwnBgHeroesResult.FromJson(ok, now) ?? OwnBgHeroesResult.Of(OwnBgHeroesState.Offline)));
            scenes.Add(("old-copy", OwnBgHeroesResult.FromJson(ok, now.AddDays(-2), fromOldCopy: true) ?? OwnBgHeroesResult.Of(OwnBgHeroesState.Offline)));
        }

        if (Read(fixtureFolder, "bg-heroes-render-small.json") is { } small)
        {
            scenes.Add(("small", OwnBgHeroesResult.FromJson(small, now) ?? OwnBgHeroesResult.Of(OwnBgHeroesState.Offline)));
        }

        scenes.Add(("not-published", OwnBgHeroesResult.Of(OwnBgHeroesState.NotPublished)));
        scenes.Add(("rebuilding", OwnBgHeroesResult.Of(OwnBgHeroesState.Rebuilding)));
        scenes.Add(("offline", OwnBgHeroesResult.Of(OwnBgHeroesState.Offline)));
        return scenes;
    }

    private static string? Read(string? folder, string name)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        try
        {
            var file = Path.Combine(folder, name);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
