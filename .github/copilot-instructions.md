# Copilot Instructions

## Project safety rules

- Use PowerShell for repository commands on Windows.
- Preserve unrelated working-tree changes; this repository may contain concurrent user, Codex and Claude work.
- Never edit, rename, reorder or delete an existing numbered SQL migration. Add the next forward-only migration after inspecting the complete chain.
- Treat IDs and immutable revision keys—not display names—as persisted business identity.
- Keep UI-only changes inside Razor, scoped CSS and shared UI JavaScript. Do not alter voucher posting, valuation or database logic without an explicit business requirement and behavioral tests.
- Keep authorization and company isolation at service/repository boundaries; UI visibility is not an authorization control.
- Never commit passwords, connection credentials, API keys, local AI settings, certificates, database dumps or recovery artifacts.
- Do not weaken MO/MI dependency, quantity, variant, chronology, valuation, cancellation or audit-history controls to make a UI flow pass.
- Never represent Tally compatibility as verified without a real export/import round trip through the targeted Tally version.
- Run focused tests first, followed by the complete .NET and JavaScript suites before proposing a release.
- Read `README.md`, `PROJECT_BLUEPRINT.md`, `README_AI_PROJECT_HANDOVER.md` and the latest audit handoff before modifying the production engine.
