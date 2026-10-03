# Jellyfin Multiverse

Lets users of paired Jellyfin servers sign in to each other's servers, with a cap on
concurrent guests and library restrictions at three levels (default, per server, per guest).
Includes a Federated Library page with a coloured dot on each cover showing free guest slots.

## Requirements

**Jellyfin 12.1 or newer.** The plugin is built for .NET 10 against Jellyfin 12.1 and its
`targetAbi` is 12.1.0.0, which is a minimum version: older servers are not offered the plugin and
Jellyfin refuses to load it. If the dll does load on an older 12.x server (for example copied by hand),
the plugin disables itself and shows "Please update Jellyfin to at least 12.1" on its pages and in
the log. Do NOT copy this dll into a Jellyfin 10.11 server: Jellyfin 12 plugins can make older servers
clear their plugin folders.

## Build

1. Install the .NET 10 SDK.
2. In this folder run: `dotnet publish -c Release`
3. Copy `bin/Release/net10.0/publish/Jellyfin.Plugin.Multiverse.dll` and `meta.json` into
   `<jellyfin data>/plugins/Multiverse_1.1.0.0/` and restart Jellyfin.

To change the required version later, edit three places: `MinimumJellyfinVersion` in Plugin.cs,
`targetAbi` in meta.json, and the Jellyfin package versions in the .csproj.

## Pairing two servers (A and B)

1. On A: Dashboard > Plugins > Jellyfin Multiverse > Add server. Enter B's name and address,
   click Generate, and save.
2. On B: add A the same way, but paste the SAME Pairing ID and Secret from A.
3. Choose limits and libraries on each side. Each server controls what ITS guests may see.
4. Open the Federated Library page:
   `/web/#/configurationpage?name=CrossAuthCatalog`

## Code map

| File | Purpose |
|------|---------|
| Plugin.cs | Entry point, registers the two web pages, checks the Jellyfin version |
| PluginServiceRegistrator.cs | Registers the services below |
| Configuration/ | The saved settings (servers, guests, limits) |
| Models/Dtos.cs | The JSON messages |
| Services/SignatureService.cs | Signs and verifies server to server messages (security core) |
| Services/TicketService.cs | One time, 60 second login tickets |
| Services/LibraryAccessService.cs | Default / server / guest library rules |
| Services/SessionLimitService.cs | Concurrent guest limits and free slot counts |
| Services/ShadowUserService.cs | Creates locked down local accounts for guests |
| Services/RemoteServerClient.cs | Sends signed requests to other servers |
| Api/FederationController.cs | What other servers and guest browsers call on this server |
| Api/CatalogController.cs | What our own users' catalog page calls |
| Api/MinimumVersionAttribute.cs | Blocks endpoints with an update message on too old servers |
| Api/StatusController.cs | Lets the web pages ask whether the server is new enough |
| Web/*.html | Admin page, catalog page, landing page |

## How a guest sign in works

1. Alice (server A) clicks a cover on the Federated Library page.
2. Server A sends a SIGNED request to server B: "Alice wants in".
3. B checks the signature, the block list, the libraries and the free slots, then returns a one time ticket.
4. Alice's browser opens B's landing page with the ticket. The page swaps it for a real session of a
   locked down shadow account on B and opens the item.

## Security notes

- Messages are signed with HMAC-SHA256, expire after 60 seconds, and cannot be replayed.
- The secret is never sent. Use https. Plain http is refused unless you tick the unsafe option.
- Guests use random-password shadow accounts: not admin, no deleting, no downloading, one device, only allowed libraries.
- Only pair servers you trust. A paired server vouches for who its users are.
- Cover images are loaded straight from the other server and Jellyfin serves images without login.
- Signing in through the landing page replaces any login that browser already has for that server address.
