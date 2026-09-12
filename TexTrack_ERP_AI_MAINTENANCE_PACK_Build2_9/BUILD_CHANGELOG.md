# Build Changelog — Current Baseline and Pending Patch

## Build 2.9 — Job Worker, Inventory and Material In Visual

Authoritative ZIP:

`TexTrack_ERP_v0.5_Build2_9_JOB_WORKER_INVENTORY_AND_MI_VISUAL_FULL.zip`

SHA-256:

`3D659CDF3EE88C6630D52939E8251FA7E1639F11BBC60C59424162FD56C3F56B`

### Major additions

- Job Worker Control page and scoped CSS
- Inventory Closing Stock page and scoped CSS
- Stock Register page and scoped CSS
- Operational reporting models/service
- Material In list and persisted-view components
- simplified Reports menu
- source-voucher drill-down support

### Build 2.9 source files added

- `Components/Pages/Reports/InventoryClosingStock.razor`
- `Components/Pages/Reports/InventoryClosingStock.razor.css`
- `Components/Pages/Reports/JobWorkerControl.razor`
- `Components/Pages/Reports/JobWorkerControl.razor.css`
- `Components/Pages/Reports/StockRegister.razor`
- `Components/Pages/Reports/StockRegister.razor.css`
- `Components/Vouchers/MaterialIn/MaterialInPersistedView.razor`
- `Components/Vouchers/MaterialIn/MaterialInPersistedView.razor.css`
- `Components/Vouchers/MaterialIn/MaterialInVoucherList.razor`
- `Components/Vouchers/MaterialIn/MaterialInVoucherList.razor.css`
- `Models/OperationalReportingModels.cs`
- `Services/OperationalReportingService.cs`

### Important protected behavior retained

- Material In save/posting engine
- automatic component consumption
- FIFO allocation
- stock posting
- voucher numbering
- existing migrations

## Pending targeted patch after Build 2.9

Proposed scope only:

1. JWC complete-block paging and no child-row clipping.
2. Inventory report Esc/menu focus restoration.
3. Closing Stock one-level-back hierarchy restoration.

Expected primary files:

- `Components/Pages/Reports/JobWorkerControl.razor`
- `Components/Pages/Reports/JobWorkerControl.razor.css`
- `Components/Pages/Reports/InventoryClosingStock.razor`
- `Components/Pages/Reports/StockRegister.razor`
- `Components/Layout/MainLayout.razor`
- focused report browser tests

Do not include Material In visual refactoring in the same patch unless separately approved.
