using System.Collections.Concurrent;
using System.Security.Cryptography;
using Jellyfin.Plugin.CrossAuth.Models;

namespace Jellyfin.Plugin.CrossAuth.Services;

/// <summary>
/// Hands out ONE TIME login tickets.
/// A ticket is a long random string that is valid for 60 seconds and can be used exactly once.
/// It exists because we do not want to put a real Jellyfin access token into a URL:
///   1. Home server asks us (signed) for a ticket.
///   2. The user's browser opens our landing page with the ticket in the address.
///   3. The landing page swaps the ticket for a real login (see FederationController.Redeem).
/// Tickets live in memory only, so a server restart simply invalidates them.
/// </summary>
public class TicketService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, PendingTicket> _tickets = new();

    /// <summary>Creates a ticket for the given guest and returns the random ticket string.</summary>
    public string Issue(PendingTicket ticket)
    {
        PurgeExpired();
        ticket.ExpiresUtc = DateTime.UtcNow + TicketLifetime;

        // 32 random bytes = 256 bits, impossible to guess.
        var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        _tickets[id] = ticket;
        return id;
    }

    /// <summary>
    /// Exchanges a ticket string for its details and DELETES it, so it cannot be used again.
    /// Returns null if the ticket is unknown or expired.
    /// </summary>
    public PendingTicket? Redeem(string ticketId)
    {
        if (string.IsNullOrEmpty(ticketId)) return null;
        if (!_tickets.TryRemove(ticketId, out var ticket)) return null;
        return ticket.ExpiresUtc >= DateTime.UtcNow ? ticket : null;
    }

    /// <summary>Removes expired tickets so memory does not grow.</summary>
    private void PurgeExpired()
    {
        foreach (var pair in _tickets)
        {
            if (pair.Value.ExpiresUtc < DateTime.UtcNow) _tickets.TryRemove(pair.Key, out _);
        }
    }
}
