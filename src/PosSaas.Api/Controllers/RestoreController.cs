using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;

namespace PosSaas.Api.Controllers;

/// <summary>
/// Models Section 7's restore state machine (NotStarted -> Verifying -> SafetyBackup ->
/// Restoring -> Completed/Failed/RolledBack) as an explicit status per tenant - see README
/// "Google Drive backup/restore": the actual multi-step verify/safety-backup/swap/rollback
/// sequence runs on-device against the local SQLite file and isn't implemented there yet, so
/// this is intentionally just the status the mobile client reports into and polls back.
/// </summary>
[ApiController]
[Authorize(Roles = "Owner,Manager")]
[Route("api/restore")]
public class RestoreController : ControllerBase
{
    // Process-lifetime only, matching the "not implemented on-device yet" status this
    // endpoint models - no need for a persisted entity until the on-device flow is real.
    private static readonly ConcurrentDictionary<Guid, RestoreStatus> StatusByTenant = new();

    [HttpGet("status")]
    public ActionResult<RestoreStatusDto> GetStatus()
    {
        var tenantId = User.GetTenantId();
        if (tenantId is null) return Unauthorized();

        var status = StatusByTenant.GetValueOrDefault(tenantId.Value, RestoreStatus.NotStarted);
        return Ok(new RestoreStatusDto(status.ToString()));
    }

    [HttpPost("start")]
    public ActionResult<RestoreStatusDto> Start()
    {
        var tenantId = User.GetTenantId();
        if (tenantId is null) return Unauthorized();

        StatusByTenant[tenantId.Value] = RestoreStatus.Verifying;
        return Ok(new RestoreStatusDto(RestoreStatus.Verifying.ToString()));
    }

    [HttpPost("report")]
    public ActionResult<RestoreStatusDto> ReportStatus([FromQuery] string status)
    {
        var tenantId = User.GetTenantId();
        if (tenantId is null) return Unauthorized();
        if (!Enum.TryParse<RestoreStatus>(status, true, out var parsed))
        {
            return BadRequest(new { message = "invalid restore status" });
        }

        StatusByTenant[tenantId.Value] = parsed;
        return Ok(new RestoreStatusDto(parsed.ToString()));
    }
}
