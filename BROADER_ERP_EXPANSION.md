# TexTrack — Broader ERP Expansion

Recorded: 2026-09-04

## Status and scope boundary

This is a future-direction document, not an implementation plan or a statement of existing capabilities. These ideas are explicitly deferred and must not be mixed into the current garment-production fixes or used to expand the production-freeze scope.

Current defects take priority. Customer tagging, material-demand checks, and child-stage production-versus-existing-stock changes belong to their separately agreed production scope; they are not specified by this document.

No feature below is authorized for implementation merely by being listed here. Each requires requirements, design review, tests, and an explicit implementation decision.

## 1. Controlled customization and extensibility

Vision: TexTrack should be adaptable to different organizations and industries, taking inspiration from the customization concepts discussed for Tally TDL, Odoo, ERPNext, and Zoho. This does not imply compatibility with those products or their extension languages.

Customizations will initially be developed and maintained by the TexTrack team. A public plugin marketplace, customer-executable scripts, and a new scripting language are not current deliverables.

### Potential extension layers

- Configuration: optional fields, labels, report columns, defaults, and feature switches.
- Team-built extensions: reports, approval workflows, integrations, and industry-specific modules.
- Protected core: stock posting, valuation, voucher relationships, company isolation, permissions, and audit history.

Extensions should call validated application services rather than bypass business rules through direct database writes. Adding UI fields must not bypass server-side validation or authorization.

### Future design safeguards

- Define supported, versioned extension contracts when concrete extension needs arise.
- Keep customer-specific changes separate from shared core behaviour where practical.
- Apply company-scoped access controls to custom fields, reports, workflows, and integrations.
- Preserve audit trails for extension-driven changes.
- Version customizations and their schema migrations; test upgrades against historical data.
- Run regression tests proving extensions cannot invalidate inventory, valuation, or voucher integrity.
- Define feature activation and deactivation rules so disabling a module cannot strand or erase historical transactions.
- Review current architecture before claiming these extension boundaries already exist.

Do not undertake a broad refactor solely to anticipate hypothetical extensions before the current production freeze.

## 2. Future production outputs and loss handling

Keep the following concepts distinct:

| Capability | Intended distinction |
| --- | --- |
| Process loss / wastage | Consumed input with no recoverable inventory output, such as evaporation. |
| Recoverable scrap | Leftovers that may be received into stock, reused, or sold. |
| By-products | Additional usable or saleable outputs of a production process. |
| Rejections / rework | Defective output requiring disposition or further processing; not automatically good finished output. |

Possible future design: BOMs specify expected yields or loss allowances; production transactions record actual input consumption and output quantities. This is a proposal, not a finalized rule.

Before implementation, decide:

- Quantity rules, units and conversions, tolerances, and reconciliation by process.
- Valuation and cost allocation across good output, scrap, by-products, and losses.
- Approval requirements for deviations from expected consumption or yield.
- Output godowns, reusable item identities, and traceability to source jobs/stages.
- Treatment of partial receipts, cancellations, reversals, and rework cycles.
- Reporting and historical migration behaviour.

These capabilities should be optional and enabled only where needed. Neither garment-specific assumptions nor unrestricted consumption overrides should become universal defaults.

## 3. Delivery discipline

1. Complete and verify the existing production defects first.
2. Deliver separately approved production enhancements in bounded increments.
3. Revisit this future backlog only through explicit scoping decisions.
4. For each expansion, document business rules, acceptance cases, security impact, migration impact, and regression evidence before release.

The objective is an extensible ERP over time, not continual feature growth that prevents the current module from stabilizing.

## 4. ERP-wide global search — requested 2026-09-04

The user requested that an ERP-wide global search bar be recorded as a needed feature. This is backlog documentation only, not implemented functionality or an addition to the current bug-fix pass.

Global search is distinct from register-specific Excel-style header filters and searchable master-selection dropdowns. Those retain their own purposes.

Proposed scope for later design: search supported masters, vouchers and report/menu destinations from a shared entry point, show clearly identified result types, and open the selected record or page. Exact searchable fields, placement, shortcut, and ranking remain to be agreed.

Required safeguards: enforce company scope and user permissions on the server before returning results, counts or suggestions; make financial-year scope explicit; do not expose restricted records through previews. Opening a result must recheck access. Plan bounded results, pagination and query performance so search remains usable as data grows.

Do not build this feature until separately authorized after the current priorities are addressed.
