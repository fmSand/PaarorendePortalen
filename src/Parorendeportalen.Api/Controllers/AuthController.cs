using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(INextOfKinService nextOfKinService, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(NextOfKinResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NextOfKinResponse>> Me(CancellationToken cancellationToken)
    {
        var externalId = User.FindFirst("sub")?.Value;
        if (externalId is null)
        {
            return Unauthorized();
        }

        var nextOfKin = await nextOfKinService.GetByExternalIdAsync(externalId, cancellationToken);

        return nextOfKin is null ? NotFound() : Ok(nextOfKin);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var externalId = User.FindFirst("sub")?.Value;
        if (externalId is not null)
        {
            var nextOfKin = await nextOfKinService.GetByExternalIdAsync(externalId, cancellationToken);
            if (nextOfKin is not null)
            {
                logger.LogInformation("NextOfKin {NextOfKinId} logged out.", nextOfKin.Id);
            }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }
}
