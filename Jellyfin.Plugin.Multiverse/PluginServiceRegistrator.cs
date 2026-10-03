using Jellyfin.Plugin.CrossAuth.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CrossAuth;

/// <summary>
/// Registers our helper classes ("services") with Jellyfin so they can be handed
/// automatically to any controller or service that asks for them in its constructor.
/// This is called "dependency injection". One instance of each is shared (AddSingleton).
/// Related to: everything in the Services folder.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>Jellyfin calls this once at startup.</summary>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SignatureService>();      // signs and verifies server to server messages
        serviceCollection.AddSingleton<TicketService>();         // one time login tickets
        serviceCollection.AddSingleton<LibraryAccessService>();  // decides which libraries a guest may see
        serviceCollection.AddSingleton<SessionLimitService>();   // counts guests and enforces the limits
        serviceCollection.AddSingleton<ShadowUserService>();     // creates the local "guest" accounts
        serviceCollection.AddSingleton<RemoteServerClient>();    // sends signed requests to other servers
    }
}
