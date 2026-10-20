using System.Reflection;
using System.Text.Json;
using Steamworks;
using Steamworks.Data;

namespace SteamCmdHelper;

/// <summary>
/// Applies an <see cref="ExtrasRequest"/> through the Steam client, in a child process started by
/// <see cref="WorkshopExtras.ApplyOnSteamAsync"/> ("SteamCmdHelper.exe --steam-page-extras request.json"). Like the
/// tag update, the process tells Steam it is the game. Facepunch.Steamworks keeps Steam's required-items calls to
/// itself, so they are reached through its internal ISteamUGC wrapper.
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
        if (req == null || !uint.TryParse(req.AppId, out var appId) || !ulong.TryParse(req.ItemId, out var itemId))
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
        var id = new PublishedFileId { Value = itemId };
        var query = Steamworks.Ugc.Query.All.WithFileId(id).WithChildren(true);
        var page = await query.GetPageAsync(1);
        if (page is not { } pg) return ExtrasResult.Fail($"Steam could not find Workshop item {itemId}.");
        var result = new ExtrasResult { Ok = true };
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
            return result;
        }
        finally { pg.Dispose(); }
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
        var ok = (bool)ugc.GetType().GetMethod("GetQueryUGCChildren", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(ugc, new object[] { handle, 0u, ids, count })!;
        if (!ok) throw new InvalidOperationException("Steam did not list the item's required items.");
        return ids.Select(i => i.Value).ToArray();
    }

    /// <summary>Calls an ISteamUGC method that returns a CallResult, waits for Steam's answer and reads one field of it.</summary>
    static async Task<T> Ugc<T>(string method, string field, params object[] args)
    {
        var ugc = Internal;
        var call = ugc.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(ugc, args)!;
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
