using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosSaas.Api.Auth;
using PosSaas.Api.Dtos;
using PosSaas.Domain.Common;
using PosSaas.Domain.Entities;
using PosSaas.Infrastructure.Persistence;

namespace PosSaas.Api.Controllers;

/// <summary>
/// Backup metadata (Section 3, 7 Phase 8) - see README "Google Drive backup/restore": this
/// records checksum/size/version after the mobile app has already done the on-device
/// encryption + Drive upload; it does not perform that upload itself.
/// </summary>
[ApiController]
[Authorize(Roles = "Owner,Manager")]
[Route("api/backups")]
public class BackupsController : ControllerBase
{
    private readonly PosSaasStore _store;
    public BackupsController(PosSaasStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Backup>>> GetBackups()
        => Ok(await _store.Backups.GetAllAsync(User.GetTenantId()));

    [HttpPost]
    public async Task<ActionResult<Backup>> RecordBackup(RecordBackupRequest request)
    {
        var backup = new Backup
        {
            TenantId = User.GetTenantId(),
            Checksum = request.Checksum,
            SizeBytes = request.SizeBytes,
            Version = request.Version,
            Status = BackupStatus.Completed
        };
        await _store.Backups.AddAsync(backup);
        return Ok(backup);
    }
}
