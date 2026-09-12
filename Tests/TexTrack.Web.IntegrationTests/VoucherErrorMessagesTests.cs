using Microsoft.EntityFrameworkCore;
using Npgsql;
using TexTrack.Web.Services;
using Xunit;

namespace TexTrack.Web.IntegrationTests;

public sealed class VoucherErrorMessagesTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "duplicates")]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "linked records")]
    [InlineData(PostgresErrorCodes.SerializationFailure, "Reload")]
    [InlineData(PostgresErrorCodes.DeadlockDetected, "Reload")]
    [InlineData(PostgresErrorCodes.UndefinedColumn, "administrator")]
    public void Provider_errors_are_translated_even_inside_wrappers(string state, string expected)
    {
        var database = new PostgresException("SECRET_SQL password=hidden", "ERROR", "ERROR", state);
        foreach (var error in new Exception[] { database, new DbUpdateException("SECRET_WRAPPER", database),
                     new InvalidOperationException("SECRET_WRAPPER", database) })
        {
            var message = VoucherErrorMessages.For(error);
            Assert.Contains(expected, message);
            Assert.DoesNotContain("SECRET", message);
            Assert.DoesNotContain("password", message);
            Assert.DoesNotContain(state, message);
        }
    }

    [Fact]
    public void Business_dependency_message_survives_but_unknown_internal_errors_do_not()
    {
        const string validation = "Material Out cannot be cancelled: linked Material In 42 consumes it.";
        Assert.Equal(validation, VoucherErrorMessages.For(new InvalidOperationException(validation)));
        Assert.DoesNotContain("SECRET", VoucherErrorMessages.For(new Exception("SECRET")));
        Assert.Contains("permission", VoucherErrorMessages.For(new UnauthorizedAccessException("SECRET")));
        Assert.Contains("Reload", VoucherErrorMessages.For(new DbUpdateConcurrencyException("SECRET")));
        Assert.Contains("status", VoucherErrorMessages.For(new OperationCanceledException("SECRET")));
    }

    [Theory]
    [InlineData("Sequence contains no elements")]
    [InlineData("Sequence contains more than one element")]
    [InlineData("Sequence contains no matching element")]
    [InlineData("Sequence contains more than one matching element")]
    public void Bare_framework_sequence_exceptions_do_not_leak_verbatim(string frameworkMessage)
    {
        var message = VoucherErrorMessages.For(new InvalidOperationException(frameworkMessage));
        Assert.DoesNotContain("Sequence", message);
        Assert.Contains("administrator", message);
    }

    [Fact]
    public void Real_linq_single_failure_does_not_leak_verbatim()
    {
        InvalidOperationException real;
        try { Array.Empty<int>().Single(); throw new Exception("unreachable"); }
        catch (InvalidOperationException e) { real = e; }

        Assert.Equal("Sequence contains no elements", real.Message);
        Assert.DoesNotContain("Sequence", VoucherErrorMessages.For(real));
    }

    [Fact]
    public async Task Real_postgresql_error_does_not_expose_missing_column_or_sql_state()
    {
        await using var environment = await IdentityTestEnvironment.CreateAsync();
        await using var db = environment.CreateDb();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("SELECT secret_internal_column FROM application_users"));
        var message = VoucherErrorMessages.For(error);
        Assert.Contains("administrator", message);
        Assert.DoesNotContain("secret_internal_column", message);
        Assert.DoesNotContain("application_users", message);
        Assert.DoesNotContain(error.SqlState, message);
    }
}
