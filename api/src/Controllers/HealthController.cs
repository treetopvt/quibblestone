// ----------------------------------------------------------------------------
//  HealthController - liveness probe for the QuibbleStone API.
//
//  GET /health returns a tiny JSON document that the web client, the deploy
//  pipeline, and the Azure App Service health check can all poll to confirm the
//  API is up. This is the REST half of the walking skeleton (the real-time half
//  is GameHub). It demonstrates the controller pattern the charter calls for
//  (README section 4) without pulling in any domain logic.
// ----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace QuibbleStone.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    // GET /health -> { status, service, version, utc }
    [HttpGet]
    public IActionResult Get()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        return Ok(new
        {
            status = "ok",
            service = "quibblestone-api",
            version,
            utc = DateTimeOffset.UtcNow,
        });
    }
}
