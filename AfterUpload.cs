using System.Text;

namespace SteamCmdHelper;

/// <summary>
/// A command run in the mods folder after each successful upload (Options > Uploading > After uploading, per mods
/// folder), for anything the built-in steps don't cover: copy the build to a backup drive, post somewhere else, update
/// a website. What was uploaded is in environment variables, so the same command works for every mod. Like the build
/// step it comes from this PC's settings, never from the repo.
/// </summary>
public static class AfterUpload
{
    /// <summary>The variables a command gets, for the Options hint and the README.</summary>
    public static readonly string[] Variables =
        { "SCH_MOD_NAME", "SCH_MOD_FOLDER", "SCH_MOD_DIR", "SCH_VERSION", "SCH_WORKSHOP_ID", "SCH_WORKSHOP_URL", "SCH_CREATED", "SCH_APP_ID", "SCH_COMMIT", "SCH_NOTE_FILE" };

    /// <summary>The environment for one uploaded mod. The change note is in a file (<c>SCH_NOTE_FILE</c>), since it can be long and hold any character.</summary>
    public static Dictionary<string, string> Environment(BatchItem r, string appId, string commit, string noteFile) => new()
    {
        ["SCH_MOD_NAME"] = r.Mod.Name,
        ["SCH_MOD_FOLDER"] = r.Mod.RelPath,
        ["SCH_MOD_DIR"] = r.Mod.Dir,
        ["SCH_VERSION"] = r.Version,
        ["SCH_WORKSHOP_ID"] = r.WorkshopId ?? "",
        ["SCH_WORKSHOP_URL"] = r.WorkshopId is { } id ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id : "",
        ["SCH_CREATED"] = r.Status == BatchStatus.Created ? "1" : "0",
        ["SCH_APP_ID"] = appId,
        ["SCH_COMMIT"] = commit,
        ["SCH_NOTE_FILE"] = noteFile,
    };

    /// <summary>
    /// Runs <paramref name="command"/> once for each uploaded or created mod in <paramref name="results"/>. Returns
    /// false when it failed for any of them (the uploads themselves already went through).
    /// </summary>
    public static async Task<bool> RunAsync(string repo, string? command, IEnumerable<BatchItem> results, string appId, string commit,
        Func<ModInfo, string> noteOf, Action<string> say, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) return true;
        var ok = true;
        foreach (var r in results.Where(r => r.Status is BatchStatus.Uploaded or BatchStatus.Created))
        {
            var noteFile = Path.Combine(Path.GetTempPath(), $"steamcmd_helper_note_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(noteFile, noteOf(r.Mod), new UTF8Encoding(false), ct);
                say($"After upload ({r.Mod.Name}): {command}");
                var code = await BuildStep.RunAsync(repo, command, l => say("    " + l), ct, Environment(r, appId, commit, noteFile));
                if (code != 0) { say($"Warning: the after-upload command failed for {r.Mod.Name} (exit code {code}). The upload itself went through."); ok = false; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { say($"Warning: the after-upload command could not start: {e.Message}"); return false; }
            finally { try { File.Delete(noteFile); } catch { } }
        }
        return ok;
    }
}
