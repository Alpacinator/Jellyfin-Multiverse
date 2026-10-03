using Jellyfin.Plugin.CrossAuth.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CrossAuth;

/// <summary>
/// ENTRY POINT of the plugin. Jellyfin finds this class when it loads the dll.
/// It does four things:
///   1. Gives the plugin a name and a unique id.
///   2. Stores and loads the settings (PluginConfiguration) automatically.
///   3. Registers the two web pages: the admin settings page and the federated catalog page.
///   4. Checks that the Jellyfin server is new enough (see MinimumJellyfinVersion below).
/// Related to: Configuration/PluginConfiguration.cs, Web/configPage.html, Web/catalogPage.html,
///             Api/MinimumVersionAttribute.cs, Api/StatusController.cs
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The oldest Jellyfin version this plugin supports. To change the requirement later,
    /// edit this number, the targetAbi in meta.json, and the package versions in the .csproj.
    /// </summary>
    public static readonly Version MinimumJellyfinVersion = new(12, 1, 0);

    /// <summary>Jellyfin calls this when the server starts. The base class loads the saved settings for us.
    /// We keep a static "Instance" so any other class can reach the settings via Plugin.Instance.Configuration.</summary>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IServerApplicationHost applicationHost,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Compare only major.minor.build so labels such as "12.1.0.0" and "12.1.0" behave the same.
        var running = applicationHost.ApplicationVersion;
        ServerVersionText = $"{running.Major}.{running.Minor}.{Math.Max(running.Build, 0)}";
        IsServerSupported = new Version(running.Major, running.Minor, Math.Max(running.Build, 0)) >= MinimumJellyfinVersion;

        if (!IsServerSupported)
        {
            logger.LogError("Jellyfin Multiverse: {Message}", UnsupportedMessage);
        }
    }

    /// <summary>Static shortcut to the running plugin (used by all services to read settings).</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>True when the running Jellyfin is at least MinimumJellyfinVersion.</summary>
    public static bool IsServerSupported { get; private set; } = true;

    /// <summary>The version of the Jellyfin server we are running in, for messages.</summary>
    public static string ServerVersionText { get; private set; } = string.Empty;

    /// <summary>The message shown to admins and callers when the server is too old.</summary>
    public static string UnsupportedMessage =>
        $"Jellyfin Multiverse needs Jellyfin {MinimumJellyfinVersion.Major}.{MinimumJellyfinVersion.Minor} or newer. "
        + $"This server runs Jellyfin {ServerVersionText}. Please update Jellyfin to at least "
        + $"{MinimumJellyfinVersion.Major}.{MinimumJellyfinVersion.Minor}.";

    /// <summary>Name shown in the Jellyfin dashboard plugin list.</summary>
    public override string Name => "Jellyfin Multiverse";

    /// <summary>Unique id of this plugin. Never change it after release, Jellyfin uses it to find saved settings.</summary>
    public override Guid Id => Guid.Parse("b7c1e5a2-3d4f-4a89-9c61-2f8e7d5a1b34");

    /// <summary>Short description shown in the plugin catalog.</summary>
    public override string Description =>
        "Lets users of other trusted Jellyfin servers sign in here, with limits and library restrictions. Requires Jellyfin 12.1 or newer.";

    /// <summary>
    /// Tells Jellyfin which html pages belong to this plugin.
    /// The html files are embedded in the dll (see the .csproj file).
    /// "Name" is what appears in the page address: /web/#/configurationpage?name=CrossAuthCatalog
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;

        return new[]
        {
            // Admin settings page (servers, limits, libraries).
            new PluginPageInfo
            {
                Name = "CrossAuthConfig",
                EmbeddedResourcePath = $"{ns}.Web.configPage.html"
            },

            // The combined view of all media on all connected servers.
            new PluginPageInfo
            {
                Name = "CrossAuthCatalog",
                DisplayName = "Federated Library",
                EmbeddedResourcePath = $"{ns}.Web.catalogPage.html",
                EnableInMainMenu = true,
                MenuIcon = "cloud"
            }
        };
    }
}
