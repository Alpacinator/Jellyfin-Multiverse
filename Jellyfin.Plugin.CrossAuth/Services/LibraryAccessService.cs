using Jellyfin.Plugin.CrossAuth.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.CrossAuth.Services;

/// <summary>
/// Decides which libraries a guest may see.
/// Order of priority (most specific wins):
///   1. the guest's own list      (ExternalUserRecord.OverrideLibraries)
///   2. the paired server's list  (TrustedServer.OverrideLibraries)
///   3. the default list          (PluginConfiguration.DefaultLibraryIds)
/// The result is always intersected with the libraries that really exist, so a deleted library
/// can never accidentally grant access to something else.
/// Related to: ShadowUserService (applies the result as a Jellyfin policy), FederationController (filters the catalog)
/// </summary>
public class LibraryAccessService
{
    private readonly ILibraryManager _libraryManager;

    public LibraryAccessService(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>Returns the ids of every library that exists on this server right now.</summary>
    public HashSet<Guid> GetAllLibraryIds()
    {
        var result = new HashSet<Guid>();
        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            if (Guid.TryParse(folder.ItemId, out var id)) result.Add(id);
        }

        return result;
    }

    /// <summary>
    /// Works out the final list of libraries for one guest from one server.
    /// Used by: FederationController (catalog + redeem).
    /// </summary>
    public IReadOnlyList<Guid> ResolveFor(TrustedServer server, ExternalUserRecord user)
    {
        var config = Plugin.Instance!.Configuration;

        List<string> chosen;
        if (user.OverrideLibraries) chosen = user.LibraryIds;           // level 1
        else if (server.OverrideLibraries) chosen = server.LibraryIds;  // level 2
        else chosen = config.DefaultLibraryIds;                         // level 3

        var existing = GetAllLibraryIds();
        var result = new List<Guid>();
        foreach (var text in chosen)
        {
            if (Guid.TryParse(text, out var id) && existing.Contains(id)) result.Add(id);
        }

        return result;
    }
}
