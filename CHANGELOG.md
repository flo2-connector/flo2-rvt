# Changelog

## 0.2.0 (unreleased)

The edit job: real Revit 2024 applies a change list to a model and saves the result as a new model. Contract v1 (v0 plus the edit; nothing of v0 changed).

- **Server**: `rvt_edit_start` (`file`, `change_list`, `max_minutes`). The change list is checked against the one schema, read from its file through zod's `fromJSONSchema`, and the limits the add-in holds (512 KiB canonical, 20,000 GlobalIds, Revit's model extent), uploaded beside the model as `changes.json` in its canonical form (keys sorted, no whitespace), and named in the start result by its SHA-256. The WorkItem names `<nickname>.Flo2RvtEdit+prod`; the job record says which job it is, so `rvt_job_status` lists an edit's outputs (`report.json`, `model.rvt`, `sheets.pdf`, `model.ifc`; an optional one not written is left off) and summarizes its report (outcome, changes applied, applied in part, rolled back, not applied, elements deleted, model saved), after a restart too. A job record written before this version reads as a probe's.
- **Add-in**: the `edit` job beside `probe`, chosen by the working folder (`changes.json` means the edit). The three change kinds, with their semantics stated in the README (what "on a level" is, how a GlobalId maps to an element as Revit's IFC exporter maps them, what moves with a level); each change in its own transaction, a failed one rolled back alone; warnings dismissed and recorded, errors rolling the change back; what Revit deleted, modified and added on its own recorded per change; the edited model exported (IFC, sheets) and saved once as a new `model.rvt`, a new central model with its worksets when the input is workshared; `report.json` last. A new compile-time reference, `Nice3point.Revit.Api.RevitAPIIFC` 2024.3.60, for a level's storey GlobalId.
- **Logic tests**: `addin/tests`, on .NET 10, for the add-in's pure logic (GlobalIds, lengths, the change-list check on the shared cases in `schema/test/change-lists.json`, outcomes, `report.json`'s shape).
- **CI guard**: `scripts/addin-save-guard.ts` takes over from v0's grep: the probe still commits and saves nothing, and only `Edit/ChangeRunner.cs` commits and only `Edit/SaveResult.cs` saves, once.
- **Registration**: one AppBundle version for both jobs, the probe Activity's new version, and the edit Activity with its `prod` alias.
- **Schema**: the same version 0 and the same lists; its descriptions now state each change kind's exact meaning.

## 0.1.0 (unreleased)

The first version: a read-only probe.

- **Server** (`server/`): an MCP server on the MCP TypeScript SDK v2.4.0, Streamable HTTP on `127.0.0.1:8740` behind a local bearer token, with the two contract tools `rvt_probe_start` and `rvt_job_status`. It refuses calls without flo2.io's hidden job context and uses only the short-lived token that context carries.
- **Design Automation client**: the person's transient OSS bucket, a streamed, chunked upload through signed S3 URLs that completes only when the bytes match the stated size and SHA-256, WorkItems with `limitProcessingTimeSec` and OSS object URNs, status reads, and signed download URLs for the outputs.
- **Change-list schema** (`schema/change-list.schema.json`): version 0, the element reference and three change kinds, with TypeScript and C# types generated from it. No edit job runs them yet.
- **Add-in** (`addin/`): a Revit 2024 add-in for the Automation API that opens the model, changes nothing, and writes `report.json`, `sheets.pdf` and `model.ifc`; packaged as an AppBundle zip.
- **Registration CLI** (`scripts/flo2-rvt.ts`): `flo2-rvt register` (the nickname first, on an app with no data; then the AppBundle, the Activity, their `prod` aliases and the monthly processing cap; never `DELETE /forgeapps/me`) and `flo2-rvt engines`.
- **CI**: lint, typecheck, generated-types check, tests, and the add-in build with its AppBundle as an artifact.
- **Monthly checks**: dependency currency with written holds, and the Autodesk watch (repositories, packages and documentation), which opens one issue when anything moved.
