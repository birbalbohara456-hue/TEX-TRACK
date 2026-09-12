namespace TexTrack.Web.Models;

public sealed record VoucherAuditTrailRow(
    Guid VoucherAuditIdentity,
    long VoucherId,
    string VoucherType,
    string VoucherTypeCode,
    string VoucherNumber,
    string FinancialYear,
    int RevisionCount,
    string FirstAction,
    DateTimeOffset FirstRecordedAtUtc,
    string FirstRecordedBy,
    string LastAction,
    DateTimeOffset LastRecordedAtUtc,
    string LastRecordedBy,
    bool IsDeleted);

public sealed record VoucherAuditRevisionRow(
    long Id,
    Guid VoucherAuditIdentity,
    long VoucherId,
    int RevisionNumber,
    int SnapshotSchemaVersion,
    string Action,
    string Reason,
    DateTimeOffset RecordedAtUtc,
    string RecordedBy,
    string SnapshotJson,
    string ChangesJson,
    string ContentHash,
    string PreviousChainHash,
    string ChainHash,
    bool IntegrityVerified);

public sealed record VoucherAuditStampModel(
    Guid VoucherAuditIdentity,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string LastEditedBy,
    DateTimeOffset LastEditedAtUtc,
    string LastAction,
    bool IsLegacyBaseline);
