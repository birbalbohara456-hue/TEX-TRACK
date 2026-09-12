# AI Task Template — TexTrack ERP

Copy this template for future Codex/Claude/ChatGPT tasks.

```text
AUTHORITATIVE BASE
<exact ZIP name>
<expected SHA-256>

READ FIRST
- CURRENT_BASELINE.md
- TEXTRACK_CODE_MAP.md
- FROZEN_MODULES.md
- KNOWN_ISSUES.md
- AI_CHANGE_PROTOCOL.md
- TEXTRACK_MODULE_MANIFEST.json

TASK
<one precise defect or feature>

TASK LEVEL
Level 1 Visual / Level 2 Keyboard-Navigation / Level 3 Report Query / Level 4 Transaction Engine

REPRODUCTION
1. ...
2. ...
3. ...

CURRENT RESULT
...

EXPECTED RESULT
...

PRIMARY FILES TO REVIEW
- ...

FILES ALLOWED TO CHANGE
- ...

PROTECTED FILES — DO NOT MODIFY
- ...

SCOPE EXPANSION RULE
Do not inspect or modify any additional file unless a concrete reference/call path proves it is required. Explain the evidence before expanding scope.

TESTS REQUIRED
- focused test ...
- build
- relevant browser/integration smoke test

DELIVERABLE
- root cause
- exact files changed
- exact behavior changed
- tests run and results
- complete ZIP only if requested
- SHA-256

Do not claim success without running the required tests.
```
