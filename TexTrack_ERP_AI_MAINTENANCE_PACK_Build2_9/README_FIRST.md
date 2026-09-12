# TexTrack ERP AI Maintenance Pack — Build 2.9

This pack is the first document set an AI assistant should read before inspecting source code.

## Purpose

The goal is to avoid repeated full-project audits for small fixes. The project is large enough that opening every source file wastes credits, increases review time, and raises regression risk.

## Required reading order

1. `CURRENT_BASELINE.md`
2. `TEXTRACK_CODE_MAP.md`
3. `FROZEN_MODULES.md`
4. `KNOWN_ISSUES.md`
5. `AI_CHANGE_PROTOCOL.md`
6. `AI_TASK_TEMPLATE.md`
7. `TEXTRACK_MODULE_MANIFEST.json` when machine-readable scoping is useful

## Authoritative source package

`TexTrack_ERP_v0.5_Build2_9_JOB_WORKER_INVENTORY_AND_MI_VISUAL_FULL.zip`

Expected Build 2.9 SHA-256:

`3D659CDF3EE88C6630D52939E8251FA7E1639F11BBC60C59424162FD56C3F56B`

## Mandatory rule

Do not inspect the entire repository by default. Start with the module entry in the code map and inspect only the primary files listed there. Expand the review only when evidence proves a dependency is involved.
