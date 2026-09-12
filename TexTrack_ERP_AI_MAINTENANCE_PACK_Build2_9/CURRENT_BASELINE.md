# Current Baseline — TexTrack ERP Build 2.9

## Product and platform

- Product: TexTrack ERP
- Stack: C# / modern .NET, ASP.NET Core / Blazor, PostgreSQL
- Desktop access direction: WebView2 Windows wrapper
- Deployment direction: local-server-first
- Production source of truth: JWO → Material Out → Material In

## Authoritative package

`TexTrack_ERP_v0.5_Build2_9_JOB_WORKER_INVENTORY_AND_MI_VISUAL_FULL.zip`

SHA-256:

`3D659CDF3EE88C6630D52939E8251FA7E1639F11BBC60C59424162FD56C3F56B`

## Confirmed Build 2.9 architecture

- Operational report queries: `Services/OperationalReportingService.cs`
- Operational report DTOs: `Models/OperationalReportingModels.cs`
- Job Worker Control page: `Components/Pages/Reports/JobWorkerControl.razor`
- Closing Stock page: `Components/Pages/Reports/InventoryClosingStock.razor`
- Stock Register page: `Components/Pages/Reports/StockRegister.razor`
- Reports/menu shell: `Components/Layout/MainLayout.razor`
- Material In posting engine: `Services/MaterialInRepository.cs`
- Material In route/create orchestration: `Components/Pages/Vouchers/MaterialIn.razor`
- Material In saved list: `Components/Vouchers/MaterialIn/MaterialInVoucherList.razor`
- Material In persisted view: `Components/Vouchers/MaterialIn/MaterialInPersistedView.razor`
- Shared keyboard framework: `Components/Keyboard/*`

## Confirmed working behavior to preserve

- Material In save succeeds in the currently tested database.
- Automatic component consumption works.
- Partial receipt consumption works.
- Prior MI consumption is deducted.
- Consumption is capped by actual unconsumed Material Out quantity.
- FIFO Material Out allocation works.
- RM consumption and FG receipt post atomically.
- Voucher numbering works.
- Stable JWO/MO/MI IDs are used.
- Build 2.9 renders the Job Worker Control T-format side by side.
- Actual FG names and variant children are displayed.
- Closing Stock displays groups, items, quantities, colours, sizes and weighted values.
- Stock Register exists and supports source-voucher drill-down.
- JWO second-field Enter behavior passed Codex browser testing.

## Known migration caution

The package contains migrations `001` through `013`. Earlier development encountered a stock-movement check-constraint error for Material In movement kinds. Before declaring a future production release safe for new installations, run a clean-database JWO → MO → MI save test. Do not assume an existing development database proves clean-install migration safety.

## Current development priority

Do not redesign report calculations or posting engines. The current immediate work is a narrow UI/navigation patch:

1. Job Worker Control must show every row and page complete JWO blocks.
2. Inventory report Esc behavior must return correctly and retain focus.
3. Closing Stock Esc must return exactly one hierarchy level.
