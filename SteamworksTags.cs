using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;

namespace SteamCmdHelper;

/// <summary>
/// Sets one Workshop item's tags through the Steam client, in a child process started by
/// <see cref="WorkshopTags.UpdateOnSteamAsync"/> ("SteamCmdHelper.exe --steam-set-tags appid id tag..."). The process
/// tells Steam it is the game (the same way the game's own uploader works), reads the item's current tags, and sends
/// only a tag change: files, title, description and visibility stay as they are.
/// </summary>
static class SteamworksTags
{
    public static int RunChild(string[] args)
    {
        var line = Run(args);
        Console.Out.WriteLine(WorkshopTags.ChildLine(line));
        Console.Out.Flush();
        return line.Ok ? 0 : 1;
    }

    static TagResult Run(string[] args)
    {
        var wanted = args.Skip(2).ToArray();
        if (args.Length < 3 || !uint.TryParse(args[0], out var appId) || !ulong.TryParse(args[1], out var itemId))
            return new TagResult(false, false, wanted, "usage: --steam-set-tags <app id> <workshop id> <tag>...");
        try { LoadSteamApi(); }
        catch (Exception e) { return new TagResult(false, false, wanted, "Could not load Valve's Steamworks library: " + e.Message); }
        try
        {
            try { SteamClient.Init(appId, asyncCallbacks: true); }
            catch (Exception)
            {
                return new TagResult(false, false, wanted, "Could not reach the Steam client. Open Steam and sign in with the account that owns this mod "
                    + "(and owns the game), then try again.");
            }
            return SetAsync(itemId, wanted).GetAwaiter().GetResult();
        }
        catch (Exception e) { return new TagResult(false, false, wanted, e.Message); }
        finally
        {
            try { if (SteamClient.IsValid) SteamClient.Shutdown(); } catch { }
        }
    }

    static async Task<TagResult> SetAsync(ulong itemId, string[] wanted)
    {
        var item = await SteamUGC.QueryFileAsync(new PublishedFileId { Value = itemId });
        if (item is not { } it || it.Result != Result.OK)
            return new TagResult(false, false, wanted, $"Steam could not find Workshop item {itemId} ({item?.Result.ToString() ?? "no answer"}).");
        if (it.Owner.Id.Value != SteamClient.SteamId.Value)
            return new TagResult(false, false, wanted, $"Steam is signed in as {SteamClient.Name}, but item {itemId} belongs to another account. "
                + "Sign in to Steam with the account that uploaded it.");

        var tags = WorkshopTags.Merge(it.Tags ?? Array.Empty<string>(), wanted);
        if (WorkshopTags.Same(tags, it.Tags ?? Array.Empty<string>())) return new TagResult(true, false, tags, "");

        var edit = new Steamworks.Ugc.Editor(it.Id);
        foreach (var t in tags) edit = edit.WithTag(t);
        var r = await edit.SubmitAsync();
        if (r.NeedsWorkshopAgreement)
            return new TagResult(false, false, tags, "Steam wants the Workshop legal agreement accepted first (open the item on Steam).");
        return r.Success ? new TagResult(true, true, tags, "") : new TagResult(false, false, tags, $"Steam refused the tag change ({r.Result}).");
    }

    /// <summary>
    /// Writes the embedded steam_api64.dll next to the settings (once per version of it) and points the Steamworks
    /// wrapper at it, so no build has to ship it as a loose file.
    /// </summary>
    internal static void LoadSteamApi()
    {
        using var res = typeof(SteamworksTags).Assembly.GetManifestResourceStream("SteamCmdHelper.steam_api64.dll")
            ?? throw new FileNotFoundException("steam_api64.dll is not built into this copy.");
        var dir = Path.Combine(AppSettings.Folder, "steamworks", res.Length.ToString());
        var dll = Path.Combine(dir, "steam_api64.dll");
        if (!File.Exists(dll) || new FileInfo(dll).Length != res.Length)
        {
            Directory.CreateDirectory(dir);
            var tmp = dll + "." + Environment.ProcessId + ".tmp";
            using (var f = File.Create(tmp)) res.CopyTo(f);
            File.Move(tmp, dll, overwrite: true);
        }
        var handle = NativeLibrary.Load(dll);
        NativeLibrary.SetDllImportResolver(typeof(SteamClient).Assembly, (name, _, _) =>
            name.StartsWith("steam_api", StringComparison.OrdinalIgnoreCase) ? handle : IntPtr.Zero);
    }
}
