using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CrossAuth.Api;

/// <summary>
/// Tiny endpoint the web pages call first: "is this Jellyfin new enough?"
/// If not, the pages show the update message instead of the normal screen.
/// It has no version filter on purpose, so it can always answer.
/// Related to: Plugin.cs, Web/configPage.html, Web/catalogPage.html
/// </summary>
[ApiController]
[Route("CrossAuth")]
public class StatusController : ControllerBase
{
    /// <summary>GET /CrossAuth/Status. Reveals nothing secret (Jellyfin shows its version publicly anyway).</summary>
    [HttpGet("Status")]
    [AllowAnonymous]
    public ActionResult<object> GetStatus()
    {
        return new
        {
            Supported = Plugin.IsServerSupported,
            ServerVersion = Plugin.ServerVersionText,
            RequiredVersion = $"{Plugin.MinimumJellyfinVersion.Major}.{Plugin.MinimumJellyfinVersion.Minor}",
            Message = Plugin.IsServerSupported ? string.Empty : Plugin.UnsupportedMessage
        };
    }
}
