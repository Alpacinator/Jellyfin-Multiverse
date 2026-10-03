using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.CrossAuth.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;

namespace Jellyfin.Plugin.CrossAuth.Services;

/// <summary>
/// Manages "shadow users": normal Jellyfin accounts on THIS server that represent a guest.
///
/// Why shadow users? Jellyfin already knows how to restrict libraries, track sessions and
/// revoke access for normal users. By giving each guest a locked down local account we get all of
/// that for free, instead of inventing our own permission system.
///
/// Safety measures for every shadow user:
///   - random 64 character password nobody knows, so nobody can log in through the normal login form
///   - not an administrator, cannot delete content, cannot download, cannot control other users
///   - only the allowed libraries are enabled
///   - hidden from the login screen
/// Related to: LibraryAccessService (which libraries), FederationController.Redeem (who calls us)
/// </summary>
public class ShadowUserService
{
    private const int MaxRecords = 2000; // protects the settings file from a flood of fake guests
    private readonly IUserManager _userManager;
    private readonly object _recordLock = new();

    public ShadowUserService(IUserManager userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Finds the saved record for a guest, or creates a new one. No Jellyfin account is created here,
    /// only a line in our settings (so the admin can see and configure the guest).
    /// Returns null if the record limit is reached.
    /// </summary>
    public ExternalUserRecord? GetOrCreateRecord(TrustedServer server, string homeUserId, string homeUserName)
    {
        var config = Plugin.Instance!.Configuration;

        lock (_recordLock)
        {
            var record = config.ExternalUsers.FirstOrDefault(
                u => u.PairingId == server.PairingId && u.HomeUserId == homeUserId);

            if (record != null)
            {
                if (record.HomeUserName != homeUserName)
                {
                    record.HomeUserName = homeUserName;
                    Plugin.Instance.SaveConfiguration();
                }

                return record;
            }

            if (config.ExternalUsers.Count >= MaxRecords) return null;

            record = new ExternalUserRecord
            {
                PairingId = server.PairingId,
                HomeUserId = homeUserId,
                HomeUserName = homeUserName
            };
            config.ExternalUsers.Add(record);
            Plugin.Instance.SaveConfiguration();
            return record;
        }
    }

    /// <summary>
    /// Makes sure the guest has a local Jellyfin account and that its permissions match the
    /// CURRENT library settings. Called on every sign in, so changing settings takes effect at the next sign in.
    /// </summary>
    public async Task<User> EnsureShadowUserAsync(
        TrustedServer server,
        ExternalUserRecord record,
        IReadOnlyList<Guid> allowedLibraries)
    {
        User? user = record.ShadowUserId != Guid.Empty ? _userManager.GetUserById(record.ShadowUserId) : null;

        if (user == null)
        {
            // First visit (or an admin deleted the account): create a new local account.
            var name = BuildUserName(server, record);
            if (_userManager.GetUserByName(name) != null)
            {
                // Never reuse an existing account, it could be a real user. Make the name unique instead.
                name += "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();
            }

            user = await _userManager.CreateUserAsync(name).ConfigureAwait(false);

            // Give the account an unknown password so the normal login form can never be used for it.
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            await _userManager.ChangePassword(user, password).ConfigureAwait(false);

            lock (_recordLock)
            {
                record.ShadowUserId = user.Id;
                Plugin.Instance!.SaveConfiguration();
            }
        }

        await ApplyPolicyAsync(user, allowedLibraries).ConfigureAwait(false);
        return user;
    }

    /// <summary>
    /// Writes the locked down permissions to the shadow account.
    /// Only the given libraries are enabled; everything dangerous is switched off.
    /// </summary>
    private async Task ApplyPolicyAsync(User user, IReadOnlyList<Guid> allowedLibraries)
    {
        var policy = new UserPolicy
        {
            IsAdministrator = false,
            IsHidden = true,
            IsDisabled = false,

            // Library restriction: not "all folders", only the list we computed.
            EnableAllFolders = false,
            EnabledFolders = allowedLibraries.ToArray(),

            // Dangerous or unneeded abilities are off.
            EnableContentDeletion = false,
            EnableContentDownloading = false,
            EnableRemoteControlOfOtherUsers = false,
            EnableSharedDeviceControl = false,
            EnableLiveTvManagement = false,
            EnableLiveTvAccess = false,
            EnableMediaConversion = false,
            EnablePublicSharing = false,

            // Needed so the guest can reach the server from outside.
            EnableRemoteAccess = true,

            // One device at a time per guest keeps the concurrent count honest.
            MaxActiveSessions = 1,

            // These two are required fields; copy what Jellyfin already set for the account.
            AuthenticationProviderId = user.AuthenticationProviderId,
            PasswordResetProviderId = user.PasswordResetProviderId
        };

        await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a readable local user name such as "ext-bobs-server-alice-1a2b3c4d".
    /// Only safe characters are kept. The hash part keeps names unique between different guests.
    /// </summary>
    private static string BuildUserName(TrustedServer server, ExternalUserRecord record)
    {
        string Clean(string text, int max)
        {
            var cleaned = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return cleaned.Length > max ? cleaned[..max] : cleaned;
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(server.PairingId + "|" + record.HomeUserId)))[..8]
            .ToLowerInvariant();

        return $"ext-{Clean(server.Name, 12)}-{Clean(record.HomeUserName, 16)}-{hash}";
    }
}
