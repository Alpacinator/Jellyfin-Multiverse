using System.Reflection;
using System.Text;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.CrossAuth.Configuration;
using Jellyfin.Plugin.CrossAuth.Models;
using Jellyfin.Plugin.CrossAuth.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CrossAuth.Api;

/// <summary>
/// The INCOMING side of server to server talk: what OTHER servers (and the guest's browser) call on THIS server.
///
/// Endpoints:
///   POST /CrossAuth/Server/Catalog   signed   -> list of movies and series a guest may see, plus free slots
///   POST /CrossAuth/Server/Connect   signed   -> checks rules and hands out a one time ticket
///   GET  /CrossAuth/Landing          browser  -> small page that redeems the ticket
///   POST /CrossAuth/Redeem           browser  -> swaps the ticket for a real Jellyfin login
///
/// These endpoints are [AllowAnonymous] on purpose: the caller has no Jellyfin account here.
/// Instead the "Server/..." ones are protected by the signature, and Redeem by the one time ticket.
/// Related to: SignatureService, TicketService, ShadowUserService, SessionLimitService, LibraryAccessService
/// </summary>
[ApiController]
[Route("CrossAuth")]
[RequireSupportedServer]
public class FederationController : ControllerBase
{
    private const int MaxCatalogItems = 500;
    private const int MaxBodyBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Only one sign in is processed at a time so two guests cannot grab the last slot together.
    private static readonly SemaphoreSlim RedeemLock = new(1, 1);

    private readonly SignatureService _signatures;
    private readonly TicketService _tickets;
    private readonly ShadowUserService _shadowUsers;
    private readonly LibraryAccessService _libraries;
    private readonly SessionLimitService _limits;
    private readonly ILibraryManager _libraryManager;
    private readonly ISessionManager _sessionManager;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<FederationController> _logger;

    public FederationController(
        SignatureService signatures,
        TicketService tickets,
        ShadowUserService shadowUsers,
        LibraryAccessService libraries,
        SessionLimitService limits,
        ILibraryManager libraryManager,
        ISessionManager sessionManager,
        IServerApplicationHost appHost,
        ILogger<FederationController> logger)
    {
        _signatures = signatures;
        _tickets = tickets;
        _shadowUsers = shadowUsers;
        _libraries = libraries;
        _limits = limits;
        _libraryManager = libraryManager;
        _sessionManager = sessionManager;
        _appHost = appHost;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // SIGNED SERVER TO SERVER ENDPOINTS
    // ------------------------------------------------------------------

    /// <summary>
    /// Another server asks: "what may this user of mine see on your server?"
    /// We answer with the allowed movies and series and how many guest slots are free.
    /// </summary>
    [HttpPost("Server/Catalog")]
    [AllowAnonymous]
    public async Task<ActionResult<CatalogResponse>> GetCatalog()
    {
        var (server, body) = await ReadAndVerifyAsync().ConfigureAwait(false);
        if (server == null) return Unauthorized();

        var request = Deserialize<CatalogRequest>(body);
        if (!IsValidUser(request?.HomeUserId, request?.HomeUserName)) return BadRequest();

        var record = _shadowUsers.GetOrCreateRecord(server, request!.HomeUserId, request.HomeUserName);
        var slots = _limits.GetSlots(server);
        var response = new CatalogResponse
        {
            ServerName = _appHost.FriendlyName,
            SlotsMax = slots.Max,
            SlotsLeft = slots.Left
        };

        // Blocked or unknown guests simply get an empty list.
        if (record == null || record.Blocked) return response;

        var allowed = _libraries.ResolveFor(server, record);
        if (allowed.Count == 0) return response;

        // Ask Jellyfin for movies and series, but only inside the allowed libraries.
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            AncestorIds = allowed.ToArray(),
            Recursive = true,
            Limit = MaxCatalogItems,
            OrderBy = new[] { (ItemSortBy.SortName, SortOrder.Ascending) }
        });

        response.Items = items.Select(item => new CatalogItem
        {
            Id = item.Id,
            Name = item.Name,
            Year = item.ProductionYear,
            Type = item is Series ? "Series" : "Movie"
        }).ToList();

        return response;
    }

    /// <summary>
    /// Another server asks: "this user wants to watch here, can they?"
    /// We check every rule (blocked, libraries, free slot) and, if all is fine, return a one time ticket.
    /// The user is NOT signed in yet. That happens in Redeem.
    /// </summary>
    [HttpPost("Server/Connect")]
    [AllowAnonymous]
    public async Task<ActionResult<ConnectResponse>> Connect()
    {
        var (server, body) = await ReadAndVerifyAsync().ConfigureAwait(false);
        if (server == null) return Unauthorized();

        var request = Deserialize<ConnectRequest>(body);
        if (!IsValidUser(request?.HomeUserId, request?.HomeUserName)) return BadRequest();
        if (!string.IsNullOrEmpty(request!.ItemId) && !Guid.TryParse(request.ItemId, out _)) return BadRequest();

        var record = _shadowUsers.GetOrCreateRecord(server, request.HomeUserId, request.HomeUserName);
        if (record == null || record.Blocked) return StatusCode(StatusCodes.Status403Forbidden);

        if (_libraries.ResolveFor(server, record).Count == 0) return StatusCode(StatusCodes.Status403Forbidden);
        if (!_limits.CanStart(server, record.ShadowUserId)) return StatusCode(StatusCodes.Status409Conflict);

        var ticket = _tickets.Issue(new PendingTicket
        {
            PairingId = server.PairingId,
            HomeUserId = request.HomeUserId,
            HomeUserName = request.HomeUserName,
            ItemId = request.ItemId
        });

        return new ConnectResponse { Ticket = ticket };
    }

    // ------------------------------------------------------------------
    // BROWSER ENDPOINTS (protected by the one time ticket)
    // ------------------------------------------------------------------

    /// <summary>
    /// Serves the small landing page (Web/landing.html). The page reads the ticket from its own
    /// address, so the server never inserts user supplied text into the html (no injection risk).
    /// </summary>
    [HttpGet("Landing")]
    [AllowAnonymous]
    public ActionResult Landing()
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Jellyfin.Plugin.CrossAuth.Web.landing.html");
        if (stream == null) return NotFound();

        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer"; // keep the ticket out of referrer headers
        return File(stream, "text/html");
    }

    /// <summary>
    /// The landing page calls this with its ticket. If the ticket is valid and a slot is free,
    /// we create or update the shadow account and start a real Jellyfin session for it.
    /// </summary>
    [HttpPost("Redeem")]
    [AllowAnonymous]
    public async Task<ActionResult<RedeemResponse>> Redeem([FromBody] RedeemRequest request)
    {
        var pending = _tickets.Redeem(request.Ticket);
        if (pending == null) return Unauthorized();

        var config = Plugin.Instance!.Configuration;
        var server = config.TrustedServers.FirstOrDefault(
            s => s.PairingId == pending.PairingId && s.Enabled && s.AllowIncoming);
        if (!config.EnableFederation || server == null) return Unauthorized();

        await RedeemLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var record = _shadowUsers.GetOrCreateRecord(server, pending.HomeUserId, pending.HomeUserName);
            if (record == null || record.Blocked) return StatusCode(StatusCodes.Status403Forbidden);

            // Re-check the limit now. It is the authoritative check because time has passed since Connect.
            if (!_limits.CanStart(server, record.ShadowUserId)) return StatusCode(StatusCodes.Status409Conflict);

            var allowed = _libraries.ResolveFor(server, record);
            var user = await _shadowUsers.EnsureShadowUserAsync(server, record, allowed).ConfigureAwait(false);

            var result = await _sessionManager.AuthenticateDirect(new AuthenticationRequest
            {
                UserId = user.Id,
                Username = user.Username,
                App = "Jellyfin Multiverse",
                AppVersion = "1.0",
                DeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? Guid.NewGuid().ToString("N") : request.DeviceId,
                DeviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? "Federated browser" : request.DeviceName,
                RemoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString()
            }).ConfigureAwait(false);

            _logger.LogInformation("Jellyfin Multiverse: guest {Guest} from {Server} signed in", pending.HomeUserName, server.Name);

            return new RedeemResponse
            {
                AccessToken = result.AccessToken,
                UserId = user.Id.ToString("N"),
                ServerId = _appHost.SystemId,
                ServerName = _appHost.FriendlyName
            };
        }
        finally
        {
            RedeemLock.Release();
        }
    }

    // ------------------------------------------------------------------
    // HELPERS
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads the request body and checks the signature headers.
    /// Returns the matching paired server, or null if ANYTHING is wrong. We never say which check
    /// failed, so an attacker learns nothing.
    /// </summary>
    private async Task<(TrustedServer? Server, string Body)> ReadAndVerifyAsync()
    {
        if (Request.ContentLength > MaxBodyBytes) return (null, string.Empty);

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (body.Length > MaxBodyBytes) return (null, body);

        var config = Plugin.Instance!.Configuration;
        if (!config.EnableFederation) return (null, body);

        var pairingId = Request.Headers["X-CrossAuth-Pairing"].ToString();
        var nonce = Request.Headers["X-CrossAuth-Nonce"].ToString();
        var signature = Request.Headers["X-CrossAuth-Signature"].ToString();
        if (!long.TryParse(Request.Headers["X-CrossAuth-Timestamp"].ToString(), out var timestamp)) return (null, body);

        var server = config.TrustedServers.FirstOrDefault(
            s => s.Enabled && s.AllowIncoming && s.PairingId == pairingId);
        if (server == null) return (null, body);

        // Request.Path does not include Jellyfin's optional base url, matching what the sender signed.
        var ok = _signatures.Verify(server.Secret, Request.Path.Value ?? string.Empty, timestamp, nonce, body, signature);
        return ok ? (server, body) : (null, body);
    }

    /// <summary>Turns a json text into an object, returning null instead of throwing on bad input.</summary>
    private static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Basic sanity check of the guest identity sent by the other server.</summary>
    private static bool IsValidUser(string? id, string? name)
    {
        return !string.IsNullOrWhiteSpace(id) && id.Length <= 64
            && !string.IsNullOrWhiteSpace(name) && name.Length <= 64;
    }
}
