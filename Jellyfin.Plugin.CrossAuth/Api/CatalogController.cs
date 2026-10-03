using Jellyfin.Plugin.CrossAuth.Configuration;
using Jellyfin.Plugin.CrossAuth.Models;
using Jellyfin.Plugin.CrossAuth.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CrossAuth.Api;

/// <summary>
/// The endpoints used by the "Federated Library" page of OUR OWN signed in users.
///
///   GET  /CrossAuth/Catalog   asks every paired server what we may see and merges the answers
///   POST /CrossAuth/Connect   asks one server for a ticket and returns the landing page address
///
/// [Authorize] means only normal, logged in Jellyfin users can call these.
/// Related to: RemoteServerClient (talks to the other servers), FederationController (their receiving end),
///             Web/catalogPage.html (the page that calls us)
/// </summary>
[ApiController]
[Route("CrossAuth")]
[RequireSupportedServer]
[Authorize]
public class CatalogController : ControllerBase
{
    private readonly RemoteServerClient _remote;
    private readonly IUserManager _userManager;
    private readonly ILogger<CatalogController> _logger;

    public CatalogController(RemoteServerClient remote, IUserManager userManager, ILogger<CatalogController> logger)
    {
        _remote = remote;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Builds the combined catalog. All servers are asked at the same time (in parallel), and a server
    /// that is offline or slow only marks itself as offline instead of breaking the whole page.
    /// </summary>
    [HttpGet("Catalog")]
    public async Task<ActionResult<List<ServerCatalogView>>> GetCatalog(CancellationToken cancellationToken)
    {
        var identity = GetCurrentUserIdentity();
        if (identity == null) return Unauthorized();

        var servers = Plugin.Instance!.Configuration.TrustedServers
            .Where(s => s.Enabled && s.ShowInCatalog)
            .ToList();

        var tasks = servers.Select(s => LoadOneServerAsync(s, identity.Value, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>
    /// Called when the user clicks a cover. We ask the remote server for a ticket on behalf of this user
    /// and return the landing page address. The browser then opens it in a new tab.
    /// </summary>
    [HttpPost("Connect")]
    public async Task<ActionResult<ConnectResult>> Connect([FromBody] ConnectInput input, CancellationToken cancellationToken)
    {
        var identity = GetCurrentUserIdentity();
        if (identity == null) return Unauthorized();

        var server = Plugin.Instance!.Configuration.TrustedServers
            .FirstOrDefault(s => s.PairingId == input.PairingId && s.Enabled && s.ShowInCatalog);
        if (server == null) return NotFound();

        try
        {
            var response = await _remote.PostAsync<ConnectResponse>(
                server,
                "/CrossAuth/Server/Connect",
                new ConnectRequest
                {
                    HomeUserId = identity.Value.Id,
                    HomeUserName = identity.Value.Name,
                    ItemId = input.ItemId
                },
                cancellationToken).ConfigureAwait(false);

            if (response == null || string.IsNullOrEmpty(response.Ticket)) return StatusCode(502);

            // Build the landing page address on the remote server. Query values are url encoded.
            var url = server.BaseUrl.TrimEnd('/')
                + "/CrossAuth/Landing?ticket=" + Uri.EscapeDataString(response.Ticket)
                + "&itemId=" + Uri.EscapeDataString(input.ItemId ?? string.Empty);

            return new ConnectResult { Url = url };
        }
        catch (RemoteCallException ex) when ((int)ex.StatusCode == 409)
        {
            return Conflict("No free slot on that server right now.");
        }
        catch (RemoteCallException ex) when ((int)ex.StatusCode == 403)
        {
            return StatusCode(403, "You do not have access to that server.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jellyfin Multiverse: connect to {Server} failed", server.Name);
            return StatusCode(502, "Could not reach that server.");
        }
    }

    /// <summary>Asks ONE server for its catalog and converts any failure into an "offline" result.</summary>
    private async Task<ServerCatalogView> LoadOneServerAsync(
        TrustedServer server,
        (string Id, string Name) identity,
        CancellationToken cancellationToken)
    {
        var view = new ServerCatalogView
        {
            PairingId = server.PairingId,
            ServerName = server.Name,
            BaseUrl = server.BaseUrl.TrimEnd('/')
        };

        try
        {
            var response = await _remote.PostAsync<CatalogResponse>(
                server,
                "/CrossAuth/Server/Catalog",
                new CatalogRequest { HomeUserId = identity.Id, HomeUserName = identity.Name },
                cancellationToken).ConfigureAwait(false);

            if (response == null) throw new InvalidOperationException("Empty answer");

            view.Online = true;
            view.SlotsMax = response.SlotsMax;
            view.SlotsLeft = response.SlotsLeft;
            view.Items = response.Items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jellyfin Multiverse: catalog of {Server} unavailable", server.Name);
            view.Online = false;
            view.Error = "Unavailable";
        }

        return view;
    }

    /// <summary>
    /// Finds out who is calling. Jellyfin puts the user id into a claim named "Jellyfin-UserId".
    /// Guests (shadow accounts) are rejected, otherwise a guest could hop from server to server.
    /// </summary>
    private (string Id, string Name)? GetCurrentUserIdentity()
    {
        var claim = User.FindFirst("Jellyfin-UserId")?.Value;
        if (!Guid.TryParse(claim, out var userId)) return null;

        var user = _userManager.GetUserById(userId);
        if (user == null) return null;

        var isGuest = Plugin.Instance!.Configuration.ExternalUsers.Any(u => u.ShadowUserId == userId);
        if (isGuest) return null;

        return (userId.ToString("N"), user.Username);
    }
}
