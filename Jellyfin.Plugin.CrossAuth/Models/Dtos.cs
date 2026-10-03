namespace Jellyfin.Plugin.CrossAuth.Models;

// DTOs ("data transfer objects") are plain classes that describe the JSON messages
// sent between servers and between the browser and this plugin.
// They contain no logic, only fields.
// Related to: Api/FederationController.cs, Api/CatalogController.cs, Services/RemoteServerClient.cs

/// <summary>Sent by the home server: "show me what this user may see on your server".</summary>
public class CatalogRequest
{
    public string HomeUserId { get; set; } = string.Empty;
    public string HomeUserName { get; set; } = string.Empty;
}

/// <summary>One movie or series in a catalog.</summary>
public class CatalogItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Year { get; set; }

    /// <summary>"Movie" or "Series".</summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>Answer to CatalogRequest: the items plus how many guest slots are left.</summary>
public class CatalogResponse
{
    public string ServerName { get; set; } = string.Empty;
    public int SlotsMax { get; set; }
    public int SlotsLeft { get; set; }
    public List<CatalogItem> Items { get; set; } = new();
}

/// <summary>Sent by the home server: "this user wants to watch something here, give me a ticket".</summary>
public class ConnectRequest
{
    public string HomeUserId { get; set; } = string.Empty;
    public string HomeUserName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
}

/// <summary>Answer to ConnectRequest: a short lived one time ticket.</summary>
public class ConnectResponse
{
    public string Ticket { get; set; } = string.Empty;
}

/// <summary>Sent by the guest's browser (from the landing page) to swap the ticket for a login.</summary>
public class RedeemRequest
{
    public string Ticket { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}

/// <summary>Answer to RedeemRequest: everything the Jellyfin web client needs to be logged in.</summary>
public class RedeemResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
}

/// <summary>What the catalog page receives for ONE server (the dot colour is calculated from the slots).</summary>
public class ServerCatalogView
{
    public string PairingId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool Online { get; set; }
    public string Error { get; set; } = string.Empty;
    public int SlotsMax { get; set; }
    public int SlotsLeft { get; set; }
    public List<CatalogItem> Items { get; set; } = new();
}

/// <summary>Sent by the catalog page when the user clicks a cover.</summary>
public class ConnectInput
{
    public string PairingId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
}

/// <summary>Answer to ConnectInput: the address the browser should open.</summary>
public class ConnectResult
{
    public string Url { get; set; } = string.Empty;
}

/// <summary>Simple pair of numbers describing guest capacity.</summary>
public record SlotInfo(int Max, int Left);

/// <summary>A ticket waiting to be redeemed. Lives in memory only, for about a minute.</summary>
public class PendingTicket
{
    public string PairingId { get; set; } = string.Empty;
    public string HomeUserId { get; set; } = string.Empty;
    public string HomeUserName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
}
