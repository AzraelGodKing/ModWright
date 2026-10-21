using System.Reflection;
using System.Text.Json;
using Steamworks;
using Steamworks.Data;
using SteamCmdHelper.DropPod;

namespace SteamCmdHelper;

/// <summary>
/// Applies a <see cref="CollectionRequest"/> through the Steam client, in a child process started by
/// <see cref="SteamCollections.ApplyOnSteamAsync"/> ("SteamCmdHelper.exe --steam-collection request.json"). Like the
/// tag update, the process tells Steam it is the game. A collection is a Workshop item of the "collection" type whose
/// children are its items, so it is created with CreateItem and filled with AddDependency / RemoveDependency.
/// </summary>
static class SteamworksCollections
{
    /// <summary>Steam's k_EWorkshopFileTypeCollection.</summary>
    const int CollectionType = 2;

    public static int RunChild(string[] args)
    {
        var r = Run(args);
        Console.Out.WriteLine(SteamCollections.ChildLine(r));
        Console.Out.Flush();
        return r.Ok ? 0 : 1;
    }

    static CollectionResult Run(string[] args)
    {
        CollectionRequest? req;
        try { req = args.Length == 1 ? JsonSerializer.Deserialize<CollectionRequest>(File.ReadAllText(args[0])) : null; }
        catch (Exception e) { return CollectionResult.Fail("Could not read the request: " + e.Message); }
        var collectionId = 0UL;
        if (req == null || !uint.TryParse(req.AppId, out var appId) || (req.CollectionId != null && !ulong.TryParse(req.CollectionId, out collectionId)))
            return CollectionResult.Fail("usage: --steam-collection <request.json>");
        try { SteamworksTags.LoadSteamApi(); }
        catch (Exception e) { return CollectionResult.Fail("Could not load Valve's Steamworks library: " + e.Message); }
        try
        {
            try { SteamClient.Init(appId, asyncCallbacks: true); }
            catch (Exception)
            {
                return CollectionResult.Fail("Could not reach the Steam client. Open Steam and sign in with an account that owns the game, then try again.");
            }
            return ApplyAsync(req, appId, collectionId).GetAwaiter().GetResult();
        }
        catch (Exception e) { return CollectionResult.Fail(e.InnerException?.Message ?? e.Message); }
        finally
        {
            try { if (SteamClient.IsValid) SteamClient.Shutdown(); } catch { }
        }
    }

    static async Task<CollectionResult> ApplyAsync(CollectionRequest req, uint appId, ulong collectionId)
    {
        var result = new CollectionResult { Ok = true };
        var app = new AppId { Value = appId };
        List<ulong> current;
        if (collectionId == 0)
        {
            var fileType = Enum.ToObject(SteamworksExtras.Method("CreateItem").GetParameters()[1].ParameterType, CollectionType);
            var created = await SteamworksExtras.UgcAnswer("CreateItem", app, fileType);
            var r = (Result)Field(created, "Result");
            if ((bool)Field(created, "UserNeedsToAcceptWorkshopLegalAgreement"))
                return CollectionResult.Fail("Steam wants the Workshop legal agreement accepted first: open the game's Workshop page on Steam and accept it.");
            if (r != Result.OK) return CollectionResult.Fail($"Steam refused to create the collection ({r}).");
            collectionId = ((PublishedFileId)Field(created, "PublishedFileId")).Value;
            result.CollectionId = collectionId.ToString();
            result.Created = true;
            current = new();
        }
        else
        {
            result.CollectionId = collectionId.ToString();
            var page = await Steamworks.Ugc.Query.All.WithFileId(new PublishedFileId { Value = collectionId }).WithChildren(true).GetPageAsync(1);
            if (page is not { } pg) return Partial(result, $"Steam could not find collection {collectionId}.");
            using (pg)
            {
                var item = pg.Entries.FirstOrDefault();
                if (item.Result != Result.OK) return Partial(result, $"Steam could not find collection {collectionId} ({item.Result}).");
                if (item.Owner.Id.Value != SteamClient.SteamId.Value)
                    return Partial(result, $"Steam is signed in as {SteamClient.Name}, but collection {collectionId} belongs to another account.");
                var details = typeof(Steamworks.Ugc.Item).GetField("details", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(item)!;
                if (Convert.ToInt32(details.GetType().GetField("FileType")!.GetValue(details)) != CollectionType)
                    return Partial(result, $"Workshop item {collectionId} is not a collection, so it was left alone.");
                if (item.ConsumerApp.Value != appId)
                    return Partial(result, $"Collection {collectionId} is for another game.");
                current = SteamworksExtras.Children(pg, item).ToList();
            }
        }

        if (result.Created || req.UpdatePage)
        {
            var handle = SteamworksExtras.Call("StartItemUpdate", app, new PublishedFileId { Value = collectionId });
            SteamworksExtras.Call("SetItemTitle", handle, req.Title);
            SteamworksExtras.Call("SetItemDescription", handle, req.Description);
            var visibility = Enum.ToObject(SteamworksExtras.Method("SetItemVisibility").GetParameters()[1].ParameterType, (int)req.Visibility);
            SteamworksExtras.Call("SetItemVisibility", handle, visibility);
            var r = await SteamworksExtras.Ugc<Result>("SubmitItemUpdate", "Result", handle, null!);
            if (r != Result.OK) return Partial(result, $"Steam refused the collection's title and description ({r}).");
        }

        var (add, remove) = SteamCollections.Plan(current.Select(c => c.ToString()), req.Items);
        var parent = new PublishedFileId { Value = collectionId };
        foreach (var child in remove)
        {
            var r = await SteamworksExtras.Ugc<Result>("RemoveDependency", "Result", parent, new PublishedFileId { Value = ulong.Parse(child) });
            if (r != Result.OK) return Partial(result, $"Steam refused to take item {child} out of the collection ({r}).");
            result.Removed++;
        }
        foreach (var child in add)
        {
            var r = await SteamworksExtras.Ugc<Result>("AddDependency", "Result", parent, new PublishedFileId { Value = ulong.Parse(child) });
            if (r != Result.OK) return Partial(result, $"Steam refused to add item {child} to the collection ({r}); it may be private or removed.");
            result.Added++;
        }
        return result;
    }

    static object Field(object o, string name) => o.GetType().GetField(name)!.GetValue(o)!;

    static CollectionResult Partial(CollectionResult r, string error)
    {
        r.Ok = false;
        r.Error = error;
        return r;
    }
}
