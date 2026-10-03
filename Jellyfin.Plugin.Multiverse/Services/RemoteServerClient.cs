using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.CrossAuth.Configuration;

namespace Jellyfin.Plugin.CrossAuth.Services;

/// <summary>
/// Thrown when another server answers with an error status (for example 409 = no free slot).
/// </summary>
public class RemoteCallException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public RemoteCallException(HttpStatusCode statusCode)
        : base($"Remote server answered {(int)statusCode}")
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// The OUTGOING side of server to server talk. Whenever we need something from a paired server
/// (its catalog, or a login ticket) this class builds the request, signs it, and sends it.
/// Related to: SignatureService (signing), CatalogController (the caller),
///             FederationController (the receiving end on the other server)
/// </summary>
public class RemoteServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SignatureService _signatures;

    public RemoteServerClient(IHttpClientFactory httpClientFactory, SignatureService signatures)
    {
        _httpClientFactory = httpClientFactory;
        _signatures = signatures;
    }

    /// <summary>
    /// Sends a signed POST to a paired server and returns the answer.
    /// "path" is the part after the server address, for example "/CrossAuth/Server/Catalog".
    /// The path is part of what we sign, so a message for one endpoint cannot be reused on another.
    /// </summary>
    public async Task<TResponse?> PostAsync<TResponse>(
        TrustedServer server,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        EnsureAddressIsAllowed(server);

        var body = JsonSerializer.Serialize(payload);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var signature = _signatures.Sign(server.Secret, path, timestamp, nonce, body);

        using var request = new HttpRequestMessage(HttpMethod.Post, server.BaseUrl.TrimEnd('/') + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CrossAuth-Pairing", server.PairingId);
        request.Headers.Add("X-CrossAuth-Timestamp", timestamp.ToString());
        request.Headers.Add("X-CrossAuth-Nonce", nonce);
        request.Headers.Add("X-CrossAuth-Signature", signature);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10); // a slow or dead server must not freeze the catalog

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new RemoteCallException(response.StatusCode);

        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses addresses that are not valid, and plain http unless the admin explicitly allowed it.
    /// </summary>
    private static void EnsureAddressIsAllowed(TrustedServer server)
    {
        if (!Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The server address is not a valid URL.");
        }

        var isHttps = uri.Scheme == Uri.UriSchemeHttps;
        var isHttp = uri.Scheme == Uri.UriSchemeHttp;
        if (!isHttps && !(isHttp && Plugin.Instance!.Configuration.AllowInsecureHttp))
        {
            throw new InvalidOperationException("Only https addresses are allowed (see the insecure http setting).");
        }
    }
}
