namespace Dorksmith.Api.Configuration;

/// <summary>Resolves repository-relative paths (data/, web/) whether the app runs from the repo root,
/// from the project directory (<c>dotnet run</c>), or from a container where paths are absolute.</summary>
public static class ContentPaths
{
    public static string? Resolve(string contentRoot, string configured, string probeFile)
    {
        if (System.IO.Path.IsPathRooted(configured))
            return File.Exists(System.IO.Path.Combine(configured, probeFile)) ? configured : null;

        var dir = new DirectoryInfo(contentRoot);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, configured);
            if (File.Exists(System.IO.Path.Combine(candidate, probeFile))) return candidate;
        }
        return null;
    }
}
