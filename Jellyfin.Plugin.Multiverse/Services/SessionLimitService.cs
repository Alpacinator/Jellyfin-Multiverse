using Jellyfin.Plugin.CrossAuth.Configuration;
using Jellyfin.Plugin.CrossAuth.Models;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.CrossAuth.Services;

/// <summary>
/// Enforces the "how many guests at once" limits and reports how many slots are left.
///
/// A guest counts as ACTIVE if their shadow account had any session activity in the last
/// few minutes. Two limits exist and the stricter one wins:
///   - the global limit (all servers together)  : PluginConfiguration.MaxConcurrentExternalUsers
///   - the per server limit                     : TrustedServer.MaxConcurrentUsers (0 = none)
/// A guest who is already active never needs a new slot (so reconnecting is always possible).
/// Related to: FederationController (blocks sign in when full, reports slots for the coloured dots)
/// </summary>
public class SessionLimitService
{
    private const int ActiveWindowMinutes = 5;
    private readonly ISessionManager _sessionManager;

    public SessionLimitService(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Counts distinct active guests. Pass a pairingId to count only guests of that server,
    /// or null to count guests of all servers.
    /// </summary>
    public int CountActiveUsers(string? pairingId)
    {
        var config = Plugin.Instance!.Configuration;
        var cutoff = DateTime.UtcNow.AddMinutes(-ActiveWindowMinutes);

        // All Jellyfin user ids that did something recently.
        var activeUserIds = _sessionManager.Sessions
            .Where(s => s.LastActivityDate > cutoff)
            .Select(s => s.UserId)
            .ToHashSet();

        // Keep only those that are guest (shadow) accounts, optionally of one server.
        return config.ExternalUsers.Count(u =>
            u.ShadowUserId != Guid.Empty
            && (pairingId == null || u.PairingId == pairingId)
            && activeUserIds.Contains(u.ShadowUserId));
    }

    /// <summary>
    /// Returns capacity for one server: the effective maximum and how many slots remain.
    /// This is what the coloured dot on the covers is based on.
    /// </summary>
    public SlotInfo GetSlots(TrustedServer server)
    {
        var config = Plugin.Instance!.Configuration;

        var globalMax = Math.Max(0, config.MaxConcurrentExternalUsers);
        var globalLeft = Math.Max(0, globalMax - CountActiveUsers(null));

        // No per server limit: only the global limit matters.
        if (server.MaxConcurrentUsers <= 0) return new SlotInfo(globalMax, globalLeft);

        var serverMax = server.MaxConcurrentUsers;
        var serverLeft = Math.Max(0, serverMax - CountActiveUsers(server.PairingId));

        // The stricter limit wins.
        return serverLeft < globalLeft ? new SlotInfo(serverMax, serverLeft) : new SlotInfo(globalMax, globalLeft);
    }

    /// <summary>
    /// True if this guest may start a session now: either they are already active,
    /// or at least one slot is free.
    /// </summary>
    public bool CanStart(TrustedServer server, Guid shadowUserId)
    {
        if (shadowUserId != Guid.Empty && IsActive(shadowUserId)) return true;
        return GetSlots(server).Left > 0;
    }

    /// <summary>True if the given Jellyfin user had recent session activity.</summary>
    private bool IsActive(Guid userId)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-ActiveWindowMinutes);
        return _sessionManager.Sessions.Any(s => s.UserId == userId && s.LastActivityDate > cutoff);
    }
}
