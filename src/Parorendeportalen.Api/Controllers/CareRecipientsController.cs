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
    ICurrentNextOfKinAccessor currentNextOfKin
) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CareRecipientResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CareRecipientResponse>>> Get(
        CancellationToken cancellationToken
    )
    {
        var careRecipientIds = await currentNextOfKin.GetCareRecipientIdsAsync(cancellationToken);
        var careRecipients = await careRecipientService.GetByIdsAsync(
            careRecipientIds,
            cancellationToken
        );
        return Ok(careRecipients);
    }

    //404 for someone elses id (OWASP, BOLA)
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CareRecipientResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CareRecipientResponse>> GetById(
        int id,
        CancellationToken cancellationToken
    )
    {
        if (!await currentNextOfKin.HasAccessToAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var careRecipient = await careRecipientService.GetByIdAsync(id, cancellationToken);
        return careRecipient is null ? NotFound() : Ok(careRecipient);
    }
}
