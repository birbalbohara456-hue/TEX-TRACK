using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TexTrack.Web.Services;

/// <summary>Translates storage failures without exposing SQL, schema or connection details.</summary>
public static class VoucherErrorMessages
{
    public static string For(Exception exception)
    {
        var chain = new List<Exception>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
            chain.Add(current);
        var postgres = chain.OfType<PostgresException>().FirstOrDefault();
        if (chain.Any(x => x is DbUpdateConcurrencyException) ||
            postgres?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            return "Another operation changed this data. Reload the voucher and review it before trying again.";
        if (postgres?.SqlState == PostgresErrorCodes.UniqueViolation)
            return "A record with the same number or identifying details already exists. Reload and check for duplicates.";
        if (postgres?.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            return "This operation conflicts with linked records. Review the voucher dependencies and selected masters.";
        if (chain.Any(x => x is DbException or DbUpdateException))
            return "The database could not complete this operation. Contact the administrator if it persists.";
        if (exception is UnauthorizedAccessException)
            return "You do not have permission to perform this operation.";
        if (exception is OperationCanceledException)
            return "The operation was interrupted. Refresh and check the voucher status before retrying.";
        // Existing repositories use InvalidOperationException for business validation.
        // Never unwrap it: a nested provider error must not become a validation message.
        // Exception: .NET's own LINQ Single()/First() throw a bare InvalidOperationException
        // with one of these exact runtime strings when an assumption about the data turns out
        // wrong (e.g. an expected row is missing or duplicated) - that is an unexpected internal
        // failure, not application-authored business validation text, and must not pass through.
        if (exception is InvalidOperationException && exception.InnerException is null)
            return IsFrameworkSequenceMessage(exception.Message)
                ? "The operation could not be completed. Refresh and check the voucher status; contact the administrator if it persists."
                : exception.Message;
        return "The operation could not be completed. Refresh and check the voucher status; contact the administrator if it persists.";
    }

    private static bool IsFrameworkSequenceMessage(string message) => message is
        "Sequence contains no elements" or
        "Sequence contains more than one element" or
        "Sequence contains no matching element" or
        "Sequence contains more than one matching element";
}
