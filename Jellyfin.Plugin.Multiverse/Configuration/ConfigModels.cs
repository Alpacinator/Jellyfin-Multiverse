namespace Jellyfin.Plugin.CrossAuth.Configuration;

/// <summary>
/// One paired server. Pairing works like a shared password:
/// BOTH admins enter the same PairingId and Secret on their own server.
/// The Secret never travels over the network, it is only used to sign messages.
/// Related to: Services/SignatureService.cs, Services/RemoteServerClient.cs
/// </summary>
public class TrustedServer
{
    /// <summary>Random id both servers share. Identifies which pairing a message belongs to.</summary>
    public string PairingId { get; set; } = string.Empty;

    /// <summary>Friendly name shown in the catalog (for example "Bob's server").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Address of the other server, for example https://jelly.example.com (include the base path if any).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Shared secret used to sign messages. Keep it private.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Turn this pairing off without deleting it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True: users from that server may sign in HERE. False: we only browse their library.</summary>
    public bool AllowIncoming { get; set; } = true;

    /// <summary>True: this server's media is listed in OUR federated catalog.</summary>
    public bool ShowInCatalog { get; set; } = true;

    /// <summary>
    /// Max guests from this one server at the same time. 0 means "no extra limit",
    /// only the global limit applies. The stricter of the two always wins.
    /// </summary>
    public int MaxConcurrentUsers { get; set; } = 0;

    /// <summary>If true, LibraryIds below replaces the default library list for guests of this server.</summary>
    public bool OverrideLibraries { get; set; } = false;

    /// <summary>Libraries guests of this server may see (only used when OverrideLibraries is true).</summary>
    public List<string> LibraryIds { get; set; } = new();
}

/// <summary>
/// A guest account. It remembers who the guest is on their home server and which local
/// "shadow user" represents them here. Created automatically the first time a guest shows up.
/// Related to: Services/ShadowUserService.cs
/// </summary>
public class ExternalUserRecord
{
    /// <summary>Which pairing (server) this guest comes from.</summary>
    public string PairingId { get; set; } = string.Empty;

    /// <summary>The guest's user id on their home server.</summary>
    public string HomeUserId { get; set; } = string.Empty;

    /// <summary>The guest's user name on their home server (for display only).</summary>
    public string HomeUserName { get; set; } = string.Empty;

    /// <summary>The local Jellyfin account that represents this guest. Empty until the first sign in.</summary>
    public Guid ShadowUserId { get; set; } = Guid.Empty;

    /// <summary>True: this guest is banned from this server.</summary>
    public bool Blocked { get; set; } = false;

    /// <summary>If true, LibraryIds below replaces the server and default lists for this guest.</summary>
    public bool OverrideLibraries { get; set; } = false;

    /// <summary>Libraries this guest may see (only used when OverrideLibraries is true).</summary>
    public List<string> LibraryIds { get; set; } = new();
}
