using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.CrossAuth.Services;

/// <summary>
/// SECURITY CORE. Proves that a message really came from a paired server and was not changed.
///
/// How it works, in plain words:
///   - The sender builds a text from: time + random nonce + request path + hash of the body.
///   - It signs that text with HMAC-SHA256 using the shared secret and sends the signature.
///   - The receiver rebuilds the same text and signs it with its own copy of the secret.
///   - If both signatures match, the sender knows the secret, so it is the paired server.
///
/// Extra protections:
///   - Time check: messages older than 60 seconds are rejected.
///   - Nonce check: each random nonce can be used only once, so a captured message cannot be replayed.
///   - Constant time comparison: prevents timing attacks when comparing signatures.
/// Related to: RemoteServerClient (signs outgoing), FederationController (verifies incoming)
/// </summary>
public class SignatureService
{
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(60);

    // Remembers nonces we have already accepted (nonce -> time we saw it).
    private readonly ConcurrentDictionary<string, DateTime> _seenNonces = new();

    /// <summary>
    /// Creates the signature for an outgoing message.
    /// Used by: RemoteServerClient.PostAsync, and internally by Verify.
    /// </summary>
    public string Sign(string secret, string path, long timestamp, string nonce, string body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var message = $"{timestamp}\n{nonce}\n{path}\n{bodyHash}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    /// <summary>
    /// Checks the signature of an incoming message. Returns true only if the message is
    /// fresh, never seen before, and signed with the right secret.
    /// Used by: FederationController.ReadAndVerifyAsync
    /// </summary>
    public bool Verify(string secret, string path, long timestamp, string nonce, string body, string signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature)) return false;
        if (string.IsNullOrEmpty(nonce) || nonce.Length > 100) return false;

        // 1. Is the message recent enough?
        try
        {
            var sentAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            if ((DateTimeOffset.UtcNow - sentAt).Duration() > MaxClockSkew) return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        // 2. Does the signature match? (constant time compare)
        var expected = Sign(secret, path, timestamp, nonce, body);
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
        if (!matches) return false;

        // 3. Has this nonce been used before? We only remember nonces of VALID messages,
        //    so attackers cannot fill our memory with garbage.
        PurgeOldNonces();
        return _seenNonces.TryAdd(nonce, DateTime.UtcNow);
    }

    /// <summary>Forgets nonces older than the allowed clock skew (they can no longer be replayed anyway).</summary>
    private void PurgeOldNonces()
    {
        var cutoff = DateTime.UtcNow - (MaxClockSkew * 2);
        foreach (var pair in _seenNonces)
        {
            if (pair.Value < cutoff) _seenNonces.TryRemove(pair.Key, out _);
        }
    }
}
