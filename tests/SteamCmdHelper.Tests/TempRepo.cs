namespace SteamCmdHelper.Tests;

/// <summary>A throwaway mods repo in the temp folder, deleted on dispose. It is not a git repo.</summary>
sealed class TempRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "steamcmdhelper_tests_" + Guid.NewGuid().ToString("N"));

    public TempRepo() => Directory.CreateDirectory(Root);

    /// <summary>Writes a file at a forward-slash path relative to the repo root.</summary>
    public string Write(string rel, string content)
    {
        var path = Path.Combine(Root, ProjectConfig.Norm(rel));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A mod that passes every check with the RimWorld defaults. Pass null to leave a file out.</summary>
    public string AddMod(
        string folder,
        string? version = "1.2.3",
        string? changelog = "1.2.3\n- Fixed the thing that was broken.",
        string? workshopId = "123456789",
        bool preview = true,
        bool built = true)
    {
        var versionEl = version == null ? "" : $"<modVersion>{version}</modVersion>";
        Write($"{folder}/About/About.xml",
            $"<?xml version=\"1.0\"?><ModMetaData><name>{folder} Display</name>{versionEl}<description>A test mod.</description></ModMetaData>");
        if (changelog != null) Write($"{folder}/About/changelog.txt", changelog);
        if (workshopId != null) Write($"{folder}/About/PublishedFileId.txt", workshopId);
        if (preview) Write($"{folder}/About/Preview.png", "png");
        if (built) Write($"{folder}/Assemblies/{folder}.dll", "dll");
        return Path.Combine(Root, folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}
