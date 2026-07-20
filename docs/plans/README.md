# Design history

These are working documents from building SimpleWall, kept as a record rather
than maintained as documentation. They describe decisions as they were made,
including the ones that turned out to be wrong.

**If you want to use or configure SimpleWall**, you want
[Getting started](../GETTING-STARTED.md) and [Configuration](../CONFIGURATION.md)
instead. Nothing here is a spec, and parts of it are superseded by the code.

| File | What it is |
|---|---|
| `STATUS.md` | The running account — what's done, what's verified on hardware versus only unit-tested, what's still owed. The most useful file here |
| `2026-07-16-simple-wall-design.md` | Original design |
| `2026-07-16-simple-wall-implementation.md` | Implementation plan, including the delivery constraints that shaped it |
| `2026-07-16-spike-findings.md` | What the spike learned on the real hardware |
| `2026-07-16-acceptance.md` | Filled-in acceptance record from the deployment trip |
| `2026-07-19-clip-looks-design.md` | Design for moving brightness/contrast from the wall onto individual clips |

They're public because the interesting part of this project wasn't the code — it
was working against a machine reachable only by VNC, with no debugger and a
feedback loop measured in hours. The reasoning about how to get confidence
without access is the part worth reading.
