using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/antiforgery")]
[Authorize]
public sealed class AntiforgeryController(IAntiforgery antiforgery) : ControllerBase
{
    // [Authorize] on purpose: a token minted for an anonymous caller fails once they log in.
    [HttpGet("token")]
    [ProducesResponseType(typeof(AntiforgeryTokenResponse), StatusCodes.Status200OK)]
    public ActionResult<AntiforgeryTokenResponse> GetToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        Response.Headers.CacheControl = "no-store";

        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
    }
}
