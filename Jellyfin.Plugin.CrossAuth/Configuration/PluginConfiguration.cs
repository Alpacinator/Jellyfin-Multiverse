using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CrossAuth.Configuration;

/// <summary>
/// All settings of the plugin. Jellyfin saves this object as an xml file and loads it again on restart.
/// The admin page (Web/configPage.html) reads and writes exactly these properties.
///
/// HOW LIBRARY PERMISSIONS ARE DECIDED (most specific wins):
///   1. The guest has their own list (ExternalUserRecord.OverrideLibraries is true)
///   2. Otherwise the paired server has its own list (TrustedServer.OverrideLibraries is true)
///   3. Otherwise the DefaultLibraryIds below are used
/// Related to: Services/LibraryAccessService.cs
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Master switch. When false, this server refuses all incoming guests.</summary>
    public bool EnableFederation { get; set; } = true;

    /// <summary>
    /// Maximum number of different guests (from all servers together) who may be active at the same time.
    /// 0 means nobody is allowed.
    /// </summary>
    public int MaxConcurrentExternalUsers { get; set; } = 3;

    /// <summary>Libraries every guest may see unless a more specific list applies. Stored as "N" format ids.</summary>
    public List<string> DefaultLibraryIds { get; set; } = new();

    /// <summary>
    /// Plain http sends secrets and tickets unencrypted. Leave false unless you are testing on a private network.
    /// </summary>
    public bool AllowInsecureHttp { get; set; } = false;

    /// <summary>The other servers we are paired with.</summary>
    public List<TrustedServer> TrustedServers { get; set; } = new();

    /// <summary>Guests we have seen so far (one entry per guest per server). Created automatically.</summary>
    public List<ExternalUserRecord> ExternalUsers { get; set; } = new();
}
