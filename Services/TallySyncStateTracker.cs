using Microsoft.EntityFrameworkCore;
using TexTrack.Web.Data;

namespace TexTrack.Web.Services;

internal static class TallySyncStateTracker
{
    public static async Task MarkUpdatedNotExportedAsync(
        TexTrackDbContext db, long voucherId, string actor, CancellationToken cancellationToken)
    {
        var records = await db.TallySyncRecords.Where(x => x.VoucherId == voucherId).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var record in records)
        {
            record.SyncState = "UpdatedNotExported";
            record.ModifiedAtUtc = now;
            record.ModifiedBy = actor;
            record.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }
    }
}
