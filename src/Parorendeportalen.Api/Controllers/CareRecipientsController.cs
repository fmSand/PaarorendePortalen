using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parorendeportalen.Api.Dtos;
using Parorendeportalen.Api.Services;

namespace Parorendeportalen.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class CareRecipientsController(
    ICareRecipientService careRecipientService,
    ICurrentNextOfKinAccessor currentNextOfKin) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CareRecipientResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CareRecipientResponse>>> Get(CancellationToken cancellationToken)
    {
        var careRecipientId = await currentNextOfKin.GetCareRecipientIdAsync(cancellationToken);
        var careRecipient = await careRecipientService.GetByIdAsync(careRecipientId, cancellationToken);
        return Ok(careRecipient is null ? [] : new[] { careRecipient });
    }

    //404 for someone else's id (OWASP API #1, BOLA)
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CareRecipientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CareRecipientResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var ownCareRecipientId = await currentNextOfKin.GetCareRecipientIdAsync(cancellationToken);
        if (id != ownCareRecipientId)
        {
            return NotFound();
        }

        var careRecipient = await careRecipientService.GetByIdAsync(id, cancellationToken);
        return careRecipient is null ? NotFound() : Ok(careRecipient);
    }
}
