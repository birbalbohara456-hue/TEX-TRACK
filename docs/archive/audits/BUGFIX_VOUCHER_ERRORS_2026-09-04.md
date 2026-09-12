# Voucher database error disclosure — remediation

## Scope

Addresses the raw database error disclosure paths identified by H3-1 / L2-5 in JWO, MJO, Material Out and Material In. No new production features, stock rules, migrations, or live deployment are included.

## Changes

- Added `Services/VoucherErrorMessages.cs` to translate direct and wrapped PostgreSQL/EF failures into safe user messages.
- Duplicate-key errors suggest checking duplicate identifying details; foreign-key errors direct users to dependencies/masters; concurrency/serialization/deadlock errors request a reload and review.
- Other database errors no longer reveal provider text, SQL states, tables, columns, connection information or stack details in these handled paths.
- JWO/MJO/MO/MI repository failure results and JWO/MO/MI UI exception handlers use the shared translator rather than displaying deepest exception messages.
- MI save's private error-message helper now delegates to the translator instead of unwrapping the provider exception.
- Removed the incorrect blanket MO cancellation advice that every database-update error means missing migrations and requires a restart. A restart may apply migrations and is not appropriate generic error recovery.
- Existing standalone InvalidOperationException business-validation messages are retained, including linked voucher references. Storage exceptions anywhere in the inner-exception chain take precedence and are sanitized.

## Boundaries

This is targeted database-error sanitization, not a claim that every application error path is hardened. Existing validation uses InvalidOperationException rather than a dedicated user-safe exception type; a future explicit validation-exception contract would distinguish unexpected framework InvalidOperationExceptions more rigorously. No persistent diagnostic logging system or new global error boundary is introduced here. Repository exceptions that are propagated still retain their original detail internally; only their presentation is translated.

## Tests

`VoucherErrorMessagesTests` checks direct and wrapped provider errors, concurrency, duplicate/linked records, permissions, interruptions, generic failures, and preservation of a business dependency message. A disposable PostgreSQL schema test generates a real missing-column error and verifies that neither the column/table nor SQL state appears in the user message.

## Deployment

Verification: full .NET/PostgreSQL suite passed 217/217 (zero failures/skips), and all five JavaScript test files passed (88 checks). Application and tests compiled successfully. No live browser acceptance was performed. Git whitespace checks report existing whitespace in the accumulated voucher UI changes; this patch did not reformat unrelated lines.

No live database changes or server restart. Previously pending migrations in the overall working tree still require the documented backup/rehearsal procedure before deployment.
