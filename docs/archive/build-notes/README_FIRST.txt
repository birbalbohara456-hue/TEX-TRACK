TEXTRACK ERP v0.4 BUILD 2.0.11 - COMPLETE PROJECT

This complete package consolidates the Tally-aligned JWO build and the keyboard/menu/focus corrections.

IMPORTANT
- Back up your current project folder and PostgreSQL database.
- Extract into a new folder.
- Do not rerun PostgreSQL setup when the database is already working.
- Start with START_TEXTRACK.bat or: dotnet run --project TexTrack.Web.csproj
- Press Ctrl+F5 once in the browser after first launch.

FOCUS ACCEPTANCE TEST
1. Alt+T -> Job Work Out Order -> Enter.
2. Alt+C to create a voucher.
3. Press Esc as required to open Exit Voucher.
4. Select Yes and press Enter.
5. Without clicking the mouse, press Up/Down, Enter, Alt+C and F4 on the JWO list.

Expected footer: v0.4 Build 2.0.11 Verified Return Focus

TexTrack ERP v0.4 Build 2 — Tally-Aligned JWO Finalisation
==========================================================

BASE
- Continues from the confirmed v0.4 Build 1.1.2 compile-stable project.
- Existing PostgreSQL company/master/JWO data is retained.

START
1. Extract the complete ZIP into a new folder.
2. Open TexTrack.sln in Visual Studio.
3. Build > Clean Solution.
4. Build > Rebuild Solution.
5. Run with Ctrl+F5, or run START_TEXTRACK.bat.

DATABASE
- Do NOT run PostgreSQL setup again.
- Migration 006_jwo_tally_alignment.sql runs automatically once.
- It adds per-FG and per-component godown references, XML valuation fields,
  and custom Voucher Type parent inheritance.

EXPECTED FOOTER
v0.4 Build 2 Tally-Aligned JWO

IMPORTANT
- Take a database backup before testing a migration build.
- Test with a new JWO first.
- Existing historical JWO rows can open with blank godowns, but must be completed
  before they can be resaved under the strengthened rules.

See V0.4_BUILD2_RELEASE_NOTES.txt and V0.4_BUILD2_TEST_CHECKLIST.txt.
