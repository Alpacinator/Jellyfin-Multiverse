using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.CrossAuth.Api;

/// <summary>
/// Put [RequireSupportedServer] on a controller and every endpoint in it is blocked, with a clear
/// "please update Jellyfin" message, when the server is older than Plugin.MinimumJellyfinVersion.
/// HTTP 426 means "Upgrade Required".
/// Related to: Plugin.cs (does the version check), FederationController, CatalogController
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireSupportedServerAttribute : ActionFilterAttribute
{
    /// <summary>Runs before each endpoint. If the server is too old we answer right here and stop.</summary>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!Plugin.IsServerSupported)
        {
            context.Result = new ObjectResult(new { Message = Plugin.UnsupportedMessage })
            {
                StatusCode = StatusCodes.Status426UpgradeRequired
            };
        }
    }
}
