using System.Reflection;
using System.Text.Json;
using Steamworks;
using Steamworks.Data;

namespace SteamCmdHelper;

/// <summary>
/// Applies an <see cref="ExtrasRequest"/> through the Steam client, in a child process started by
/// <see cref="WorkshopExtras.ApplyOnSteamAsync"/> ("SteamCmdHelper.exe --steam-page-extras request.json"). Like the
/// tag update, the process tells Steam it is the game. Facepunch.Steamworks keeps Steam's required-items and gallery
/// calls to itself, so they are reached through its internal ISteamUGC wrapper.
/// </summary>
static class SteamworksExtras
{
    public static int RunChild(string[] args)
    {
        var r = Run(args);
        Console.Out.WriteLine(WorkshopExtras.ChildLine(r));
        Console.Out.Flush();
        return r.Ok ? 0 : 1;
    }

    static ExtrasResult Run(string[] args)
    {
        ExtrasRequest? req;
        try { req = args.Length == 1 ? JsonSerializer.Deserialize<ExtrasRequest>(File.ReadAllText(args[0])) : null; }
        catch (Exception e) { return ExtrasResult.Fail("Could not read the request: " + e.Message); }
        var itemId = 0UL;
        var forItem = req != null && (req.Required != null || req.Gallery != null || req.Languages != null);
        if (req == null || !uint.TryParse(req.AppId, out var appId) || (forItem && !ulong.TryParse(req.ItemId, out itemId)))
            return ExtrasResult.Fail("usage: --steam-page-extras <request.json>");
        try { SteamworksTags.LoadSteamApi(); }
        catch (Exception e) { return ExtrasResult.Fail("Could not load Valve's Steamworks library: " + e.Message); }
        try
        {
            try { SteamClient.Init(appId, asyncCallbacks: true); }
            catch (Exception)
            {
                return ExtrasResult.Fail("Could not reach the Steam client. Open Steam and sign in with the account that owns this mod (and owns the game), then try again.");
            }
            return ApplyAsync(req, appId, itemId).GetAwaiter().GetResult();
        }
        catch (Exception e) { return ExtrasResult.Fail(e.InnerException?.Message ?? e.Message); }
        finally
        {
            try { if (SteamClient.IsValid) SteamClient.Shutdown(); } catch { }
        }
    }

    static async Task<ExtrasResult> ApplyAsync(ExtrasRequest req, uint appId, ulong itemId)
    {
        var result = new ExtrasResult { Ok = true };
        if (req.CountComments is { Length: > 0 } countIds) result.Comments = await CommentsAsync(countIds);
        if (req.Required == null && req.Gallery == null && req.Languages == null) return result;

        var id = new PublishedFileId { Value = itemId };
        var query = Steamworks.Ugc.Query.All.WithFileId(id).WithChildren(req.Required != null).WithAdditionalPreviews(req.Gallery != null);
        var page = await query.GetPageAsync(1);
        if (page is not { } pg) return ExtrasResult.Fail($"Steam could not find Workshop item {itemId}.");
        List<PreviewOnSteam> previews;
        try
        {
            var item = pg.Entries.FirstOrDefault();
            if (item.Result != Result.OK) return ExtrasResult.Fail($"Steam could not find Workshop item {itemId} ({item.Result}).");
            if (item.Owner.Id.Value != SteamClient.SteamId.Value)
                return ExtrasResult.Fail($"Steam is signed in as {SteamClient.Name}, but item {itemId} belongs to another account. Sign in to Steam with the account that uploaded it.");

            if (req.Required != null)
            {
                var current = Children(pg, item);
                var (add, remove) = WorkshopExtras.RequiredPlan(current.Select(c => c.ToString()), req.Required, req.Managed);
                foreach (var child in add)
                {
                    var r = await Ugc<Result>("AddDependency", "Result", id, new PublishedFileId { Value = ulong.Parse(child) });
                    if (r != Result.OK) return Partial(result, $"Steam refused to add required item {child} ({r}).");
                    result.Done.Add("added required item " + child);
                }
                foreach (var child in remove)
                {
                    var r = await Ugc<Result>("RemoveDependency", "Result", id, new PublishedFileId { Value = ulong.Parse(child) });
                    if (r != Result.OK) return Partial(result, $"Steam refused to take off required item {child} ({r}).");
                    result.Done.Add("took off required item " + child + " (the mod no longer lists it)");
                }
                result.RequiredNow = req.Required;
            }
            previews = req.Gallery != null ? Previews(pg) : new();
        }
        finally { pg.Dispose(); }

        if (req.Gallery != null)
        {
            var (remove, replace, addImages, addVideos) = WorkshopExtras.GalleryPlan(previews, req.Gallery);
            // Two updates: replacing and adding leave the indexes alone; removing (highest first) goes on its own.
            if (replace.Count + addImages.Count + addVideos.Count > 0)
            {
                var handle = Call("StartItemUpdate", new AppId { Value = appId }, id);
                foreach (var (index, path) in replace) Call("UpdateItemPreviewFile", handle, index, path);
                foreach (var path in addImages) Call("AddItemPreviewFile", handle, path, Enum.ToObject(PreviewType, 0));
                foreach (var video in addVideos) Call("AddItemPreviewVideo", handle, video);
                var r = await Ugc<Result>("SubmitItemUpdate", "Result", handle, null!);
                if (r != Result.OK) return Partial(result, $"Steam refused the gallery images ({r}).");
                foreach (var (_, path) in replace) result.Done.Add("replaced gallery image " + Path.GetFileName(path));
                foreach (var path in addImages) result.Done.Add("added gallery image " + Path.GetFileName(path));
                foreach (var video in addVideos) result.Done.Add("added video " + video);
            }
            if (remove.Count > 0)
            {
                var handle = Call("StartItemUpdate", new AppId { Value = appId }, id);
                foreach (var index in remove) Call("RemoveItemPreview", handle, index);
                var r = await Ugc<Result>("SubmitItemUpdate", "Result", handle, null!);
                if (r != Result.OK) return Partial(result, $"Steam refused to remove old gallery entries ({r}).");
                result.Done.Add($"removed {remove.Count} gallery entr{(remove.Count == 1 ? "y" : "ies")} the folder no longer has");
            }
        }

        foreach (var lang in req.Languages ?? new())
        {
            var edit = new Steamworks.Ugc.Editor(id).InLanguage(lang.Language).WithDescription(lang.Description);
            if (lang.Title != null) edit = edit.WithTitle(lang.Title);
            var r = await edit.SubmitAsync();
            if (!r.Success) return Partial(result, $"Steam refused the {lang.Language} page ({r.Result}).");
            result.Done.Add($"sent the {lang.Language} {(lang.Title != null ? "title and description" : "description")}");
        }
        result.PageSent = req.Gallery != null || req.Languages != null;
        return result;
    }

    static Type PreviewType => typeof(SteamClient).Assembly.GetType("Steamworks.ItemPreviewType")!;

    /// <summary>The item's extra previews as Steam lists them (images by original file name, videos by id).</summary>
    static List<PreviewOnSteam> Previews(Steamworks.Ugc.ResultPage page)
    {
        var handle = typeof(Steamworks.Ugc.ResultPage).GetField("Handle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        var ugc = Internal;
        var count = (uint)Method("GetQueryUGCNumAdditionalPreviews").Invoke(ugc, new object[] { handle, 0u })!;
        var list = new List<PreviewOnSteam>();
        var get = Method("GetQueryUGCAdditionalPreview");
        for (var i = 0u; i < count; i++)
        {
            var args = new object?[] { handle, 0u, i, null, null, Enum.ToObject(PreviewType, 0) };
            if (!(bool)get.Invoke(ugc, args)!) continue;
            var type = Convert.ToInt32(args[5]);
            var url = args[3] as string ?? "";
            var name = args[4] as string ?? "";
            if (type == 0) list.Add(new PreviewOnSteam(i, false, Path.GetFileName(name.Replace('\\', '/'))));
            else if (type == 1) list.Add(new PreviewOnSteam(i, true, url));
            // Sketchfab models and environment maps are left as they are.
        }
        return list;
    }

    static MethodInfo Method(string name) => Internal.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
        .First(m => m.Name == name && !m.Name.StartsWith('_'));

    /// <summary>Calls an ISteamUGC method that answers at once.</summary>
    static object Call(string name, params object[] args) => Method(name).Invoke(Internal, args)!;

    /// <summary>The comment count of each item, read in pages of 50 (Steam's page size).</summary>
    static async Task<Dictionary<string, long>> CommentsAsync(string[] ids)
    {
        var counts = new Dictionary<string, long>();
        foreach (var chunk in ids.Where(i => ulong.TryParse(i, out _)).Distinct().Chunk(50))
        {
            var files = chunk.Select(i => new PublishedFileId { Value = ulong.Parse(i) }).ToArray();
            var page = await Steamworks.Ugc.Query.All.WithFileId(files).GetPageAsync(1);
            if (page is not { } pg) continue;
            using (pg)
                foreach (var item in pg.Entries)
                    if (item.Result == Result.OK) counts[item.Id.Value.ToString()] = (long)item.NumComments;
        }
        return counts;
    }

    static ExtrasResult Partial(ExtrasResult r, string error)
    {
        r.Ok = false;
        r.Error = error;
        return r;
    }

    // ------------------------------------------------------------ Facepunch internals

    static object Internal => typeof(SteamUGC).GetProperty("Internal", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? throw new InvalidOperationException("This Steamworks library has no Workshop interface to call.");

    /// <summary>The item's required items, read from the query's handle.</summary>
    static ulong[] Children(Steamworks.Ugc.ResultPage page, Steamworks.Ugc.Item item)
    {
        var details = typeof(Steamworks.Ugc.Item).GetField("details", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(item)!;
        var count = (uint)details.GetType().GetField("NumChildren")!.GetValue(details)!;
        if (count == 0) return Array.Empty<ulong>();
        var handle = typeof(Steamworks.Ugc.ResultPage).GetField("Handle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        var ids = new PublishedFileId[count];
        var ugc = Internal;
        var ok = (bool)Method("GetQueryUGCChildren").Invoke(ugc, new object[] { handle, 0u, ids, count })!;
        if (!ok) throw new InvalidOperationException("Steam did not list the item's required items.");
        return ids.Select(i => i.Value).ToArray();
    }

    /// <summary>Calls an ISteamUGC method that returns a CallResult, waits for Steam's answer and reads one field of it.</summary>
    static async Task<T> Ugc<T>(string method, string field, params object[] args)
    {
        var ugc = Internal;
        var call = Method(method).Invoke(ugc, args)!;
        var type = call.GetType();
        var done = type.GetProperty("IsCompleted")!;
        var until = DateTime.UtcNow.AddMinutes(1);
        while (!(bool)done.GetValue(call)!)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"Steam did not answer {method} within a minute.");
            await Task.Delay(50);
        }
        var answer = type.GetMethod("GetResult")!.Invoke(call, null) ?? throw new InvalidOperationException($"Steam gave no answer to {method}.");
        return (T)answer.GetType().GetField(field)!.GetValue(answer)!;
    }
}
