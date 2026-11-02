# flo2-rvt

flo2-rvt lets an agent working through [flo2.io](https://flo2.io) have real Revit, in Autodesk's cloud, read a person's Revit model (`.rvt`) and change it through a fixed vocabulary of changes. It is an MCP server written in TypeScript that drives Autodesk Platform Services' **Automation API** (Design Automation v3) on the person's **own** Autodesk app, with a short-lived token flo2.io hands it for each job.

It runs two jobs, both paid by processing time on the person's app, both confirmed by the person on flo2.io first:

- **The probe** (v0) reads the model and changes nothing.
- **The edit** (v1) applies a change list to the model and saves the result as a **new** `.rvt`. The person's model is never changed. The edit's output `.rvt` can be the next edit's input, so changes stack.

**The probe.** Real Revit 2024 opens the model and changes nothing. It returns three files, which flo2.io saves into the person's design as new files:

| File | What it holds |
| --- | --- |
| `report.json` | The levels with their elevations in feet and millimetres, the sheets by number and name, the linked files (Revit links and CAD links or imports), element counts by category, the Revit release the file was saved in and the engine's version and build, and the warnings stored in the model. |
| `sheets.pdf` | Every printable sheet, in sheet-number order, in one PDF. Absent when the model has none. |
| `model.ifc` | Revit's own IFC export: IFC4 Design Transfer View, with base quantities. |

**The edit.** Real Revit 2024 opens the model, applies the change list in order, and returns up to four files, which flo2.io saves as new files beside the model:

| File | What it holds |
| --- | --- |
| `report.json` | Per change: what it named and matched, what it deleted, what Revit deleted with a host, changed or added on its own, the messages Revit raised, what is still in the model (what Revit would not delete, and what is still tied to an emptied level, each with why), and whether it was applied, applied in part, rolled back or not applied, judged from what is really left. Each element is named by its element id, IFC GlobalId, category and Revit class, and model elements are counted apart from Revit's internal ones. Then the levels after the edit, how the model was saved, and the model's stored warnings. Always written. |
| `model.rvt` | The edited model, a **new** file: a new central model, with its worksets, when the input is workshared. Absent when no change was kept. |
| `sheets.pdf` | The edited model's printable sheets in one PDF. Absent when it has none, or no change was kept. |
| `model.ifc` | Revit's own IFC export of the edited model, with the probe's options. Absent when no change was kept. |

flo2-rvt's design lives in reflow2, design `6ab9996ca4bdbc45` (reached through the hub); the settled choices behind it are in flo2's design.

## How a job travels

```
agent ──tools/call──▶ flo2.io gateway ──127.0.0.1:8740, bearer──▶ flo2-rvt ──person's APS app, job token──▶ Autodesk
                          │  holds the person's APS secret             │  holds no secret, keeps no token
                          │  mints a short-lived token per job         │  never opens the .rvt itself
                          ◀──── signed URLs of the outputs ────────────┘
```

0. Before the person is asked to approve a job, flo2.io's gateway calls `rvt_setup_check`: flo2-rvt reads, by GETs alone, whether the person's app holds the AppBundle and the job's Activity at `prod`, at this flo2-rvt's add-in version. If not, the person is told so in plain words and offered the one-time setup (`rvt_setup_run`, [below](#setting-up-a-persons-autodesk-app)); no job is made and nothing is uploaded.
1. `rvt_probe_start` or `rvt_edit_start`: flo2-rvt reads the job's Activity on the person's app first (an app that does not hold it is refused in plain words, before any bucket or upload), then makes (or finds) the person's transient bucket, streams the `.rvt` from flo2.io's short-lived download link into it through signed S3 upload URLs, checks the bytes against the size and SHA-256 flo2.io stated (no match, no job), uploads an edit's change list beside it, and submits a WorkItem with `limitProcessingTimeSec`. Autodesk's Revit reads the inputs and writes the outputs into the same bucket with the same token.
2. `rvt_job_status`, polled: Design Automation's own state, passed through. On success, the status result carries signed download URLs of the outputs for the gateway, which downloads them, saves them add-only, and strips them before the agent sees the result.
3. Autodesk deletes everything in a transient bucket after 24 hours.

## The contract with flo2.io's gateway (v3)

v1 is v0 with the edit job added: one more tool, `rvt_edit_start`, its outputs, and an edit's report summary on `rvt_job_status`. Nothing in v0 changed, so a v0 gateway still works for the probe. v2 (0.3.0) adds three counts to an edit's report summary and counts only model elements in its `elements_deleted`; nothing else changed, and a v1 gateway, which passes the summary through, still works. 0.4.0 keeps v2: the summary is unchanged, and its `left_on_level` now leaves out parts of kept elements, which were never left.

v3 (0.5.0) adds the setup of a person's own Autodesk app from flo2.io: two tools the gateway calls itself and never offers an agent (`rvt_setup_check`, `rvt_setup_run`), the hidden context's `setup` (what the person approved), `_meta["flo2.io/error"]` on a refused start, and a start that reads its Activity before it uploads anything. Nothing of v2 changed: a v2 gateway still works, and on an app flo2-rvt was never set up on its start is refused before any upload, in plain words.

### Transport

- **Streamable HTTP** on `127.0.0.1`, path `/mcp`, port `FLO2_RVT_PORT` (default `8740`). Loopback only; a foreign `Host` or `Origin` is refused (403).
- Every request carries `Authorization: Bearer <FLO2_RVT_LOCAL_TOKEN>` (32+ characters, from the environment). Anything else is 401.
- Built on the MCP TypeScript SDK **v2.4.0** (`@modelcontextprotocol/server` 2.4.0 with `@modelcontextprotocol/node` 2.1.2), through its `createMcpHandler`: a fresh server per request, no session. It serves the 2025 protocol revisions (an `initialize` with `2025-06-18`, or a `tools/call` on its own) and the 2026-07-28 revision.
- A client sends `Accept: application/json, text/event-stream`. Answers to 2025-revision requests come as a short SSE stream (`event: message`, one `data:` line holding the JSON-RPC response), as Streamable HTTP allows.

### Agent-visible tools

| Tool | Arguments | Result (`structuredContent`, also as JSON text) |
| --- | --- | --- |
| `rvt_probe_start` | `file`: string, the design file's name or id; `max_minutes`?: integer 1..30, default 15 | `{ job_id, state: "submitted", engine: "Autodesk.Revit+2024" }` |
| `rvt_edit_start` | `file`, as above; `change_list`: a change list ([below](#the-change-list)); `max_minutes`?, as above | `{ job_id, state: "submitted", engine: "Autodesk.Revit+2024", change_list_sha256 }` |
| `rvt_job_status` | `job_id`: string | `{ job_id, state, processing_seconds?, outputs?: [{ name, size }], report_summary?, error? }`. A probe's `report_summary` is `{ levels, sheets, links }`; an edit's is `{ outcome, changes, applied, applied_in_part, rolled_back, not_applied, elements_deleted, other_elements_deleted?, deleted_with_host?, left_on_level?, model_saved }`: from a report of add-in 0.3.0 (report version 2), `elements_deleted` counts model elements, `other_elements_deleted` Revit's internal elements, annotation and datums, `deleted_with_host` what Revit would not delete alone and deleted with its host, `left_on_level` the model elements still tied to an emptied level; a report of 0.2.0 (version 1) counted every element in `elements_deleted` and has none of the three. |

### Tools for the gateway (v3)

flo2.io's gateway calls these itself; its door never offers them to an agent.

| Tool | Arguments | Result (`structuredContent`, also as JSON text) |
| --- | --- | --- |
| `rvt_setup_check` | `job`?: `probe`, `edit` or `all` (default); `plan`?: boolean | `{ ready, state, why?, owner, nickname_set, expected: { addin_version, appbundle, activities }, found: [{ kind, id, state, addin_version?, version?, why? }], setup: { available, why?, addin_version, bundle_sha256?, bundle_bytes?, nickname?: { now, will_set, keeps_client_id }, deletes_nothing: true } }`. `state` is `ready`, `not_set_up` (something the job needs is missing) or `outdated` (there, but registered by another flo2-rvt). By GETs alone: `forgeapps/me`, the AppBundle `<owner>.Flo2RvtProbe+prod` and each Activity the job needs, each by its qualified id. With `plan`, `setup.nickname` says what the setup's nickname step would do, which on an app with no nickname reads the app's AppBundle and Activity lists. Annotated `readOnlyHint: true`. |
| `rvt_setup_run` | none | `{ state: "set_up", owner, owner_how, owner_note?, addin_version, bundle_sha256, bundle_bytes, appbundle: { id, version, created, alias }, activities: [{ id, version, created, alias }], monthly_hours: { requested, reported }, check }`, where `check` is `rvt_setup_check`'s result read back after the setup. Refused, and Autodesk never asked, without the hidden context's `setup`, when this server holds no AppBundle or one whose bytes are not its sha256, or when `setup.bundle_sha256` is not this server's AppBundle. Annotated `readOnlyHint: false`, `destructiveHint: true` (it moves aliases and sets the cap). |

An Activity is `ready` when its body is the one this flo2-rvt registers (engine, the AppBundle it names, its command line, each parameter's verb and local name, no other required parameter) and its description names this add-in version: every AppBundle and Activity description ends with `(flo2-rvt add-in <version>)`, the add-in's own `<Version>` (`addin/Flo2RvtProbe.csproj`, held equal to `ADDIN_VERSION` in `server/src/names.ts` by a test). A description that names another version, or none (an app registered by flo2-rvt 0.4.0 or earlier), is `outdated`, and a setup brings it up to date. The version is the add-in's, not the bundle's sha256, so a new flo2-rvt whose add-in has not changed needs no new setup. *Assumed until the first live setup*: that Autodesk returns an Activity's description as it was given, as the reference's example shows; an answer with no description at all is judged by its body alone, so a setup is never asked for again and again.

`state` is Design Automation's own status, passed through: `pending`, `inprogress`, `success`, `cancelled`, `failedLimitDataSize`, `failedLimitProcessingTime`, `failedDownload`, `failedInstructions`, `failedUpload`, `failedUploadOptional`, and any it adds. `processing_seconds` runs from the start of the input download to the end of the output upload, as Autodesk counts its limit, rounded up. Every tool carries annotations: a `title`, `readOnlyHint: false`, `openWorldHint: true` (and `destructiveHint: false`, since no job changes the person's file; `idempotentHint` false for the starts, true for status).

`change_list_sha256` is the SHA-256 of the change list in its canonical form (every object's keys sorted, no whitespace): the exact bytes flo2-rvt uploads as `changes.json`, which the add-in reads and names again in `report.json`. flo2.io computes the same form, so it can check that what runs is what the person approved.

### Hidden context

The gateway sets `params._meta["flo2.io/job"]` on every `tools/call`:

```json
{
  "person_id": "opaque",
  "design_id": "…",
  "aps": { "access_token": "…", "expires_at": "2026-10-31T12:59:59Z", "client_id": "…" },
  "input": { "download_url": "https://…", "file_name": "….rvt", "size": 123, "sha256": "…" },
  "limits": { "max_processing_sec": 1800 },
  "setup": { "monthly_hours": 2, "bundle_sha256": "…" }
}
```

- A call without it, or with it malformed, is refused (a tool error the agent reads). The agent's arguments are untrusted and bounded; everything that decides what happens at Autodesk comes from this context.
- `input` is required on `rvt_probe_start` and `rvt_edit_start`. The file must be a `.rvt`.
- `expires_at` may be an ISO-8601 time, or seconds (or milliseconds) since the epoch.
- **The token must outlive the job.** Autodesk's Revit stores the outputs with the same token when the job ends, so `rvt_probe_start` refuses a token valid for less than the job's time limit plus 600 s. A freshly minted two-legged token (3,599 s) covers the longest job (1,800 s).
- The time limit is the lower of `max_minutes × 60` and `limits.max_processing_sec`.
- flo2-rvt uses only this token, keeps it for no longer than the call (the WorkItem carries it to Autodesk), and never logs it.
- `setup` (v3) is required on `rvt_setup_run` and read nowhere else: what the person approved on flo2.io. `monthly_hours` (1 to 744) is the cap the setup sets on their app (Autodesk's `limitMonthlyProcessingTimeInHours`); `bundle_sha256` is the sha256 of the AppBundle they were shown, which must be the one this server holds.

### Results for the gateway

When a job reaches `success`, the status result carries:

- `_meta["flo2.io/outputs"] = [{ name, signed_url, expires_at, size, content_type }]`, one per output that exists (`report.json` `application/json`, `sheets.pdf` `application/pdf`, `model.ifc` `application/x-step`, and an edit's `model.rvt` `application/octet-stream`). An optional output the job did not write is left off the list. The URLs stay usable for 15 minutes.
- `_meta["flo2.io/usage"] = { processing_seconds, workitem_id }`.

`failedUploadOptional` (an optional output could not be stored) carries the same two keys for the outputs that were stored.

A refused call (v3) may carry `_meta["flo2.io/error"] = { code, billed, autodesk_said? }`, so the gateway acts on the case rather than on the words. Its one code, `not_set_up`, means the person's app does not hold what the job needs: a start found no Activity before it uploaded anything (`billed: false`, nothing uploaded), or Autodesk refused the WorkItem with a 400 or 404 naming a missing Activity or AppBundle (`billed: false`, `autodesk_said` its words; the model went only to the transient bucket). The text says so in plain words, says nothing was billed, and says to ask flo2.io for the one-time setup and start the job again, with Autodesk's own words quoted after.

### Names

- `job_id`: a v4 uuid flo2-rvt mints before the upload.
- Bucket key: `flo2rvt-` + the lowercased client id, reduced to OSS's `[-_.a-z0-9]` and cut to fit 128 characters, + `-t`. Transient (24 h).
- Objects: `<job_id>/input.rvt`, an edit's `<job_id>/changes.json`, `<job_id>/report.json`, `<job_id>/sheets.pdf`, `<job_id>/model.ifc`, an edit's `<job_id>/model.rvt`, and **`<job_id>/workitem.json`**, a small job record (the WorkItem id, the activity, the job, an edit's `change_list_sha256`, the time limit, when it was submitted). The WorkItem id only exists after the inputs and outputs are named, so it cannot be encoded in the `job_id`; the record is how a status call after a restart, with the `job_id` and a fresh token alone, finds the WorkItem and knows which job's outputs to list (a record without `job` is a probe's). An in-memory cache saves the read while the process lives.
- Activities: `<nickname>.Flo2RvtProbe+prod` and `<nickname>.Flo2RvtEdit+prod`, where the nickname is what `GET /forgeapps/me` answers: the app's nickname, or its client id when it has none. Both run the one AppBundle, `<nickname>.Flo2RvtProbe+prod`. Each description ends with `(flo2-rvt add-in <version>)`.

### Token scopes the gateway requests

```
bucket:create bucket:read data:create data:write data:read code:all
```

| Scope | For |
| --- | --- |
| `bucket:create` | `POST /oss/v2/buckets` (the transient bucket) |
| `bucket:read` | `GET /oss/v2/buckets/{key}/details` (a 409 on create is checked to be this app's bucket) |
| `data:create`, `data:write` | `GET`/`POST …/signeds3upload` (the input and the job record); Design Automation writing the outputs to their object URNs |
| `data:read` | `GET …/signeds3download` (the outputs and the job record); Design Automation reading the input's URN |
| `code:all` | `GET /da/us-east/v3/forgeapps/me`, `GET /activities/{id}` and `/appbundles/{id}` (the check), `POST /workitems`, `GET /workitems/{id}`; and a setup's calls (`PATCH /forgeapps/me`, `GET /appbundles` and `/activities`, `POST` AppBundles, Activities, their versions and aliases, `PATCH` aliases, `PUT` and `GET /servicelimits/me`) |

## The change list

`schema/change-list.schema.json` is the one JSON Schema of the change list an agent hands the edit job: version 0, the element reference (an IFC GlobalId, or a level by name or by its storey's GlobalId), and three change kinds: `delete_elements_on_level`, `delete_elements_by_global_id` and `set_level_elevation`. `npm run gen` builds both sides' types from it: `server/src/types/change-list.ts` and `addin/Generated/ChangeList.g.cs`. The add-in's build refuses C# types generated from another version of the schema.

```json
{
  "version": 0,
  "list_id": "sample-1",
  "description": "Take out the basement and lower the roof.",
  "changes": [
    { "kind": "delete_elements_on_level", "id": "c1", "level": { "level_name": "Level B1" } },
    { "kind": "delete_elements_by_global_id", "id": "c2", "elements": [{ "global_id": "2O2Fr$t4X7Zf8NOew3FLOH" }] },
    { "kind": "set_level_elevation", "id": "c3", "level": { "level_name": "Roof" },
      "elevation": { "value": 6096, "unit": "mm" }, "relative_to": "project_origin" }
  ]
}
```

It is checked three times before Revit acts on it, each time against the same rules: by flo2.io's gateway (before the person is asked), by the server (`server/src/change-list.ts`, which reads the schema file itself through zod's `fromJSONSchema`), and by the add-in (`addin/src/Edit/Logic/ChangeListCheck.cs`, which walks the JSON with closed key sets, so a key the add-in would not read is refused rather than skipped). On top of the schema all three hold the same limits: at most 512 KiB in canonical form, at most 20,000 GlobalIds in all, and no elevation beyond Revit's model extent (20 miles from the internal origin). `schema/test/change-lists.json` holds the cases and the verdicts every side must reach; the server's and the add-in's tests both read it.

### What each change kind does

Changes run in order, **each in its own Revit transaction**. Lengths are converted to Revit's foot exactly (1 ft = 304.8 mm).

**A level** is named by `level_name`, matched exactly (case and spaces count), or by `global_id`, the GlobalId of the `IfcBuildingStorey` Revit's exporter writes for it (see below). A level that is not found makes the change `not_applied`, and the report lists the model's levels.

**`delete_elements_on_level`** deletes every **model element on the level**, keeps the level, and lists what is still tied to it afterwards. An element is on level L when it is a model element and either its Revit level is L (`Element.LevelId`) or one of its base or reference level parameters is L: a wall's base constraint, a column's base level, a level-based family's level, a beam's or brace's reference level, a floor's level, a footprint roof's base level, a stair's or railing's base level, a duct's or pipe's reference level, a room's level, a group's level, a part's base level, or a family instance's **Schedule Level** (`INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM`, a beam's or a face-hosted family's "Schedule Level": what Revit schedules on L, and what its IFC exporter files under L's storey) (`addin/src/Edit/LevelRules.cs`).
- *A model element*: not a type, not drawn in one view only (annotation, detail items), in a category of Revit's Model kind, and not a level, grid, reference plane, base point, Revit link or CAD import, view, or part of a sketch.
- *Hosted elements go with their host*: a door or window in a deleted wall is deleted by Revit with it (Revit's `Document.Delete` deletes "any elements that are totally dependent upon" the ones named, and pinned elements "with no warnings"). The report lists them under `deleted_by_revit`.
- *A member of a group* is never deleted alone: it goes with its group, which is on L by the same rule.
- *Elements that only reach the level from below are kept*: a wall whose top constraint is L, a column whose top level is L, a stair whose top level is L. So are views, sheets, schedules and tags of elements that are kept, and the level itself.
- *Elements Revit will not delete alone* (`DocumentValidation.CanDeleteElement`: a curtain wall's mullions and panels, a door in a curtain wall, a wall's foundation) are not asked for; Revit deletes them with their host. After the transaction each is judged against what Revit deleted (`addin/src/Edit/Logic/Reconcile.cs`): deleted with its host, it is listed under `deleted_with_host` (done); still in the model, under `could_not`.
- *What is left is listed, never deleted by a guess.* After the transaction the add-in reads the model again and lists under `left_on_level` every element still tied to L, each with what ties it (`tied_by`) and why it was not taken (`reason`, `why`; `addin/src/Edit/Leftovers.cs`, `addin/src/Edit/Logic/LeftOnLevelRules.cs`): tied by its Revit settings (its level, a level parameter, a family hosted on the level itself) but not a model element, a member of a group on another level, or tied only by a setting the rule does not read; or filed by Revit's own IFC exporter under L's storey: an element with **no Revit level** (by its height, below), or one on a level that is not a building storey. An element with no level is listed, never deleted.
- *A part goes with its owner* (since 0.4.0). The IFC storey ties an element (`tied_by: ifc_storey`) only when the exporter exports it as a product of its own (below). One it exports only with another element is that element's part: a sketch's curves (a stair's sketch boundary, a rail's path extension lines) go to the sketch's owner, a top rail, handrail or a handrail's support to its railing, a curtain grid line, mullion or panel to its curtain wall, roof or system, an opening to what it cuts. When its owner is still in the model, the part is listed under it in `left_on_level.parts_of_kept_elements` (each owner by element id, GlobalId, category and Revit class, with `exists` and how Revit named it, `found_by`), and is not left. When its owner is gone, it is an orphan, left behind: listed in `elements` with `reason: orphaned_part` and the gone `owner`, and counted. One the exporter does not export on its own that names no owner, or that the exporter skips for another reason (its Export to IFC is No, a secondary design option, a demolished phase), and that no Revit setting ties to L, is listed in `left_on_level.not_exported` with the exporter's reason, and not counted. 0.3.0's re-run of the first live edit counted 14 parts of kept elements (four stair sketch lines, seven rail path extension lines, a top rail, two curtain grid lines) as left on the emptied level beside its 2 real leftovers, the wardrobes; the job's own IFC filed none of the 14 there.
- The change is `applied` when nothing it took is still in the model and no model element is left on the level; otherwise `applied_in_part`, and `why` says what is left. Views of the level, its sketch planes and other elements that are not model elements are listed in `left_on_level` and counted apart; they never make a change `applied_in_part`, and neither do parts of kept elements or what the exporter does not export.

**Which elements Revit's IFC exporter exports as products of their own**, from the same source and commit (`addin/src/Edit/Logic/ExportFilter.cs`, read in Revit by `ExportReader` in `addin/src/Edit/Leftovers.cs`), in the order the exporter applies it:
- *Its collector* (`Utility/ElementFilteringUtil.cs`, `GetExportFilter` and `GetClassFilter`): never a curtain grid line, an opening, a split face or a sketch; curves only when the export writes annotations (on for the IFC4 Design Transfer View the add-in exports; `Utility/ExportOptionsCache.cs`, `ExportAnnotations`); not centre lines, decals, property lines or topography contours; only elements in no design option or a primary one (`GetDesignOptionFilter`), and only new, existing or phaseless ones in the model's last phase (`GetPhaseStatusFilter`; `ExportOptionsCache.UpdateForDocument`).
- *Its Export to IFC setting* (`ShouldElementBeExported`, `GetExportElementState`): No on the element, or on its type when the element leaves it By Type, exports nothing.
- *Its class's exporter* (`Exporter/Exporter.cs`, `ExportElementImpl`): a curve only when it is a model curve or a curve by points, not a curve of another element's sketch (`ExporterIFCUtils.IsCurveFromOtherElementSketch`), of some length, with a sketch plane (`Exporter/CurveElementExporter.cs`, `ShouldCurveElementBeExported`, `ExportCurveElement`); a family instance unless it is invisible or hosted by a curtain wall, roof or system, which exports it (`Exporter/FamilyInstanceExporter.cs`, `ExportFamilyInstanceElement`; `Exporter/CurtainSystemExporter.cs`, `IsCurtainSystem`, `IsLegacyCurtainElement`); a railing's top rail, handrails and their supports only inside the railing's `IfcRailing` (`Exporter/RailingExporter.cs`, `CollectSubElements`, `ExportRailing`).
- Anything else is taken as exported on its own: where the rule cannot tell, the element is listed as left, never hidden. The rule is restated in this project's own terms; no code is copied (`THIRD_PARTY_NOTICES.md`).

**The storey Revit's IFC exporter files an element under**, as Autodesk's open-source exporter does it (github.com/Autodesk/revit-ifc, branch `Release_24.x.x`, commit `887c3d3e`, `Source/Revit.IFC.Export`; `addin/src/Edit/Logic/Storeys.cs` and `addin/src/Edit/Leftovers.cs`):
- *The storeys* (`Exporter/Exporter.cs`, `BeginDocumentExportCommon`): the levels sorted by `ProjectElevation`, then by id (`Utility/LevelUtil.cs`, `FindAllLevels`). A level with its "Building Story" setting on (`LEVEL_IS_BUILDING_STORY`; on when absent, `IsBuildingStory`) is an `IfcBuildingStorey`, as high as the distance to the next building storey at another elevation (0, open above, for the highest). A level that is not a building storey shares the storey of the building storey before it.
- *An element with a level*: its `IfcSpatialContainer` (or `OverrideElementContainer`) parameter first, when it names a level (`Utility/ParameterUtil.cs`, `OverrideContainmentParameter`); else its base level (`LevelUtil.GetBaseLevelIdForElement`: a wall's base constraint; a family instance's, or its top-level instance's, base level, then **Schedule Level**, then reference level; a truss's or an extrusion roof's reference level; a stair's base level; a duct's or pipe's reference level; a slab edge's floor's level; else `Element.LevelId`; a railing with none, its host's), and that level's storey.
- *An element with no level* (`Toolkit/PlacementSetter.cs`, `commonInit`): by the height of its bounding box's lowest point (a sketch-based line: its sketch plane's origin): the storey whose span holds it, a span running from 10 cm below the storey's elevation (`LevelUtil.LevelExtension`) to 10 cm below the next one's. Below every span, or with no bounding box, the lowest storey.

**`delete_elements_by_global_id`** deletes the elements its GlobalIds name. A GlobalId that names no element is listed under `not_found`; one whose element an earlier change already deleted, under `already_gone` (done, not missing); one that names a level, a type, a view, a link, a group member or anything else that is not a model element, or that two elements carry, under `could_not` with the reason. What is hosted in a deleted element goes with it, as above; an element it names that Revit will not delete alone, and deletes with another it names, is listed under `deleted_with_host`, as above.

**`set_level_elevation`** sets the level's elevation, `relative_to` either `project_origin` (Revit's `Level.ProjectElevation`) or `elevation_base` (Revit's `Level.Elevation`, measured from the level type's Elevation Base: the project base point or the survey point). Revit can set only `Elevation`; a target from the project origin moves it by the same amount, since the two differ by a fixed offset. **What is attached to the level moves with it, by Revit's own regeneration**: elements whose base or reference level it is keep their offset from it (the report counts them as `attached`), elements whose top is constrained to it change height (`top_constrained`), and hosted elements follow their hosts. The report gives the level's elevation before and after, both ways, in feet and millimetres, and every element Revit modified. A level already at that elevation is `not_applied`.

### How an IFC GlobalId maps to a Revit element

As Autodesk's open-source IFC exporter for Revit maps them ([revit-ifc](https://github.com/Autodesk/revit-ifc), branch `Release_24.x.x` at commit `887c3d3e`, `GUIDUtil.cs`), with its default options (the probe's and the edit's exports use them):
- **An element's GlobalId** is its `IfcGUID` parameter (`BuiltInParameter.IFC_GUID`; `IFC_TYPE_GUID` for a type) when that holds a valid IFC GUID and the element is not a curtain wall; otherwise Revit's export id for it, `ExportUtils.GetExportId(document, id)`, compressed to IFC's 22-character form (`addin/src/Edit/Logic/IfcGuid.cs`). An `IfcGUID` value is set when the model was imported from IFC or exported with "store the IFC GUID" on.
- **A level's storey GlobalId** is `ExporterIFCUtils.CreateAlternateGUID(level)`.
- The add-in indexes every element this way **before** any change runs. A GlobalId matches the element that has it as its exported GlobalId first; failing that, an element whose export id or `IfcGUID` value it is (an IFC exported with other settings); two elements either way is refused as ambiguous (Revit copies the `IfcGUID` parameter with a copied element, and the exporter then gives the copy a fresh GlobalId, in an order the add-in cannot know).
- What the exporter writes that is not an element (openings, the parts of a split wall, a curtain wall's sub-elements, the project, site and building) matches nothing, and is reported as not found.

### A failed change rolls back alone

Revit's failure processing is answered by the add-in, never by a person: every message is recorded in `revit_messages`. **A warning is dismissed and the change is kept. An error rolls the whole change back**, with no default resolution chosen on anyone's behalf (Revit's resolutions for errors are edits of their own, such as "delete the element", that the list did not ask for); so does an exception from the Revit API. Then the job goes on to the next change. Each change's `status` is `applied`, `applied_in_part`, `rolled_back` or `not_applied`; the list's `outcome` is `all_applied`, `partly_applied`, `none_applied`, or `refused` when the list itself could not be read. The model is exported and saved when at least one change was kept, and not otherwise.

This was chosen over stopping at the first failure because a list is approved as a set of independent edits, each reported on its own, and the input is never changed: a later change that depended on a failed one says so itself (its level or elements are not found). Changes that must stand or fall together go in a list of their own, whose `outcome` then says whether all of them were applied.

### How the result is saved

The job box opens a workshared input **detached from its central, with its worksets preserved** (the probe's `report.json` showed `is_workshared: true, opened_detached: true` for such a model). The edit then saves it once, as a new file, `model.rvt` (`addin/src/Edit/SaveResult.cs`):
- **Workshared input: saved as a new central model**, with `WorksharingSaveAsOptions.SaveAsCentral = true`, then `WorksharingUtils.RelinquishOwnership` of everything, and the report lists each user workset and who Revit says owns it after the save.
- **Not workshared: saved as a plain file**, with no worksharing options.

Verified against Autodesk's documentation: Revit 2024's [`Document.SaveAs(String, SaveAsOptions)`](https://rvtdocs.com/2024/df74c7e1-a98a-7751-676a-e9b074566f62) refuses a document that "was opened detached" unless `WorksharingSaveAsOptions.SaveAsCentral` is true, and refuses worksharing options on a document that is not workshared; the 2024 API's own notes for `SaveAsCentral` say the same; Autodesk's post on [worksharing in Design Automation for Revit](https://aps.autodesk.com/blog/worksharing-model-support-revit-design-automation-0) (2020) gives the same recipe (save as central, relinquish), and says Design Automation opens workshared models detached with their worksets preserved. Assumed until the first live edit: that the save and the relinquish behave on Design Automation as they do in desktop Revit, and what the architect sees on opening it. A file-based central keeps its incremental backups and ownership in a backup folder beside it (the 2024 API's notes on `SaveAsOptions.MaximumBackups`), which is not part of the output; the architect opens `model.rvt` the way any workshared model received from outside the office is opened: detached, saved as their own new central.

## The add-in

`addin/` is a Revit 2024 add-in for the Automation API (C#, .NET Framework 4.8): an `IExternalDBApplication` that answers `DesignAutomationBridge.DesignAutomationReadyEvent`, packaged as an AppBundle zip (`Flo2RvtProbe.bundle/PackageContents.xml`, `Contents/Flo2RvtProbe.addin`, `Contents/Flo2RvtProbe.dll`).

- A small job registry (Autodesk's registered-tool pattern from aps-sample-revit-mcp-tools-bundle) holds two jobs, `probe` and `edit`. The working folder says which runs: `changes.json`, the edit Activity's required input, means the edit (`addin/src/Edit/Logic/JobSelection.cs`). The probe Activity has no parameter that could put one there.
- The job box opens `input.rvt` from the working folder itself (detached from its central, all worksets open, when the file is workshared), runs the job, and **closes the input without saving**. The edit writes its result as a new file before that, through its one save path.
- The probe reads the levels, sheets, links, element counts, versions and stored warnings; prints the sheets with `Document.Export` and `PDFExportOptions` (`Combine`); exports IFC with `IFCExportOptions` (`IFC4DTV`, `ExportBaseQuantities`); and writes `report.json` last, recording any export that failed. Each export runs inside a transaction that is **rolled back**: Revit requires an open transaction for IFC export. The probe commits nothing.
- The edit runs each change through `addin/src/Edit/ChangeRunner.cs`, the only file that commits a transaction, and saves through `addin/src/Edit/SaveResult.cs`, the only file that saves, with exactly one `SaveAs`. CI runs `scripts/addin-save-guard.ts` before the build: it fails if any other file commits, saves, synchronizes or relinquishes, if the save path holds more than one `SaveAs` or commits, or if the probe's files name the edit's.
- A heartbeat writes to the job log every 20 seconds, so a long open or export is not taken for an idle job.
- It builds against reference assemblies from NuGet, on any OS: `Nice3point.Revit.Api.RevitAPI` 2024.3.60 (MIT packaging of Autodesk's reference assembly), `Autodesk.Forge.DesignAutomation.Revit` 2024.0.2 (Autodesk's bridge) and `Microsoft.NETFramework.ReferenceAssemblies.net48`. None is shipped in the bundle; the engine provides them.

The probe ran live once, on 2026-10-31 (108 s), and the edit (0.2.0) once, the same day, on Autodesk's sample model (108 s). That first edit's report said 44 elements could not be deleted that Revit had deleted with their curtain walls, left three elements on the emptied level unreported, and named 15 deleted internal elements by nothing: 0.3.0 reconciles every change with the model after it, lists what is left, takes Schedule-Level elements, and names every element by its Revit class. 0.3.0's re-run of that edit, the same day, counted 14 parts of kept elements as left on the emptied level beside its 2 real leftovers: 0.4.0 ties an element to a storey only when Revit's IFC exporter exports it on its own, and lists each part under its owner. `addin/tests` runs the first edit's c3, from its element ids and GlobalIds, and the re-run's c3, from its element ids, Revit classes and categories, through the add-in's logic, and checks the report's invariants (`addin/src/Edit/Logic/ReportCheck.cs`), which the edit job also checks on every change before it writes `report.json`. CI proves the add-in compiles, that the bundle has the expected layout, and that its pure logic passes `addin/tests`.

**Every element the report names**, the add-in indexed before any change ran (`addin/src/Edit/ElementIndex.cs`): its GlobalId, category, Revit class (`Element.GetType().Name`) and whether it is a model element. Revit's collectors do not list every element (the first live edit deleted 15 that none had listed), so after them the index asks Revit for every element id up to the highest listed one, plus a margin, for 60 s at most; `edit.index` in the report says how many it found that way and how far it got.

## Running the server

```sh
npm ci --omit=dev
FLO2_RVT_LOCAL_TOKEN=… FLO2_RVT_PORT=8740 node server/src/index.ts
```

`FLO2_RVT_BUNDLE` names the AppBundle zip a setup uploads (default `addin/dist/Flo2RvtProbe.bundle.zip` in the checkout), with its sha256 in `<zip>.sha256` beside it, as `sha256sum` writes it. On flo2.io, flo2.io's install (`ops/flo2-rvt-install` in the flo2 repository) puts there the zip this commit's CI built (the `Flo2RvtProbe.bundle` artifact of the CI run on the pinned commit), checked against the sha256 flo2.io pins for that commit; nothing on flo2.io builds the add-in. The server reads and hashes the zip again at every setup. Without it, jobs run as before and a setup is refused, saying why; the start log says which (`bundle_ready` or `bundle_absent`).

Node 22.18 or later runs the TypeScript directly. On flo2.io it runs as its own service under its own unprivileged user, with network reach and no access to design files on disk. It logs one JSON line per event to stderr, never a token, a signed URL or a file's contents.

## Setting up a person's Autodesk app

A person creates their own Autodesk app once (step 1 below) and enters its keys on flo2.io (flo2.io/account, Cloud Revit). **Nothing is run on their computer.** The first time a job is asked for on an app that is not set up, flo2.io says so and offers flo2-rvt's one-time setup; the person approves it on flo2.io the way they approve a job, told what it does, and it is recorded in their design like a job. flo2.io's gateway then calls `rvt_setup_run` with a token from the keys it already holds for that person's jobs, and flo2-rvt's server runs the registration below (`server/src/aps/registration.ts`, the same code as the command line) with the AppBundle it holds. It runs again, the same way, when a new flo2-rvt brings a new add-in version.

The command line (`npx flo2-rvt register`, step 3) stays, for an owner who runs flo2-rvt with their own keys rather than through flo2.io, and for developers.

1. **Create the Autodesk app** (Autodesk's [Create an App](https://aps.autodesk.com/en/docs/oauth/v2/tutorials/create-app/) walkthrough):
   1. Sign in at [aps.autodesk.com](https://aps.autodesk.com) (create an account if needed).
   2. Have a developer hub: with an APS plan, create one at [manage.autodesk.com](https://manage.autodesk.com) under Products and Services > Hubs > Create hub > APS developer hub. The Automation API is a paid API; check the plan's caps and the current price.
   3. Profile menu > **My applications** > choose the hub > **Create application**. Name it, choose the **Server-to-Server App** type (client credentials, two-legged tokens), and under **API Access** select the **Automation API** and the **Data Management API** (for its storage). Save.
   4. Copy the Client ID and Client Secret into your environment as `APS_CLIENT_ID` and `APS_CLIENT_SECRET`, and into flo2.io's encrypted store as flo2.io asks. Never put them in a file in this repository.
2. **Get the AppBundle**: download `Flo2RvtProbe.bundle` from the CI run's artifacts, or build it: `dotnet build addin -c Release` writes `addin/dist/Flo2RvtProbe.bundle.zip`.
3. **Register** (flo2.io does this from its server once the person approves it; by hand, on your own machine, once, and again for a new add-in version):

   ```sh
   npm ci
   APS_NICKNAME=my_flo2rvt_app APS_MONTHLY_HOURS=2 npx flo2-rvt register --bundle addin/dist/Flo2RvtProbe.bundle.zip
   ```

   For a new add-in (such as 0.2.0, which adds the edit, 0.3.0 or 0.4.0), run the same command with the new bundle: it adds a version of the AppBundle, a version of the probe Activity, creates the edit Activity (or a version of it), and moves every `prod` alias. The nickname and the cap are kept.

   In this order:
   1. **The nickname, first.** Autodesk lets an app take a nickname only while it has no AppBundle or Activity ([Task 3, Create a Nickname](https://aps.autodesk.com/en/docs/design-automation/v3/tutorials/revit/step3-create-nickname/)). On a fresh app it sets `APS_NICKNAME` (letters, digits and underscores; default `flo2rvt_` and the first twelve letters and digits of the client id, lowercased) with `PATCH /forgeapps/me`. Nicknames are global: a taken one stops the run before anything is created, and asks for another `APS_NICKNAME`. A nickname already set is kept. An app that already has data and no nickname carries on with its client id as the owner prefix: setting one then would need `DELETE /forgeapps/me`, which deletes everything on the app, and this command never calls it.
   2. The AppBundle, uploaded as a new AppBundle or a new version of it, and its `prod` alias. One AppBundle, `Flo2RvtProbe`, carries both jobs.
   3. The `Flo2RvtProbe` and `Flo2RvtEdit` Activities on the `Autodesk.Revit+2024` engine, named `<owner>.Flo2RvtProbe+prod` and `<owner>.Flo2RvtEdit+prod`, each with its `prod` alias. The edit Activity takes the model (`rvtFile`, `input.rvt`) and the change list (`changes`, `changes.json`, required), and puts `report.json` (required), `model.rvt`, `sheets.pdf` and `model.ifc` (each optional).
   4. Autodesk's own monthly processing cap on the app (`frontendLimits.limitMonthlyProcessingTimeInHours`, `APS_MONTHLY_HOURS`, default 2), read back.

   It prints the owner prefix and how it was settled, each version and alias, and the cap Autodesk reports. flo2-rvt's server reads the same prefix from `GET /forgeapps/me` for every job. It sends GET, POST, PATCH and PUT, never DELETE. From flo2.io, the cap is the person's own flo2.io monthly limit in whole hours, rounded up, and the nickname is the default one.
4. **Check the engines** whenever the monthly check asks: `npx flo2-rvt engines` lists Design Automation's Revit engines beside the dates Autodesk's Engine Lifecycle Policy page gives. The Revit 2024 engine takes no new AppBundles or Activities after **2027-03-29** and is removed on 2029-03-29.

## Development

```sh
npm ci
npm run lint && npm run typecheck && npm run gen:check && npm test
node scripts/addin-save-guard.ts   # the add-in commits and saves only on the edit job's one path
dotnet build addin -c Release      # the .NET SDK (10.0) builds the net48 add-in on Linux too
dotnet run --project addin/tests/Flo2RvtLogic.Tests.csproj -c Release   # the add-in's pure logic, on .NET 10
```

The tests cost nothing and touch no network: Autodesk is a loopback stand-in that answers with responses recorded from Autodesk's own services (`server/test/fixtures/autodesk/`, each naming its source), and the monthly check's tests replay recorded GitHub, NuGet, npm and documentation responses. Test data uses Autodesk's public sample model's name and made-up values, and, for the first live edit's change c3, the element ids and GlobalIds of that sample's elements (`addin/tests/fixtures/`).

## Monthly checks

`.github/workflows/dependencies.yml`, on the first of each month and on demand:

- **currency** (`scripts/dependency-currency.ts`): every npm and NuGet pin and GitHub Action against its latest release, and the Revit engine against its deprecation date. A newer major fails unless `dependency-holds.json` holds it back with a reason and a date to look again.
- **autodesk** (`scripts/autodesk-watch.ts`, `watch/autodesk.json`): Autodesk's repositories (pinned by commit), Autodesk's Design Automation and APS SDK packages, and the documentation of the Automation API, sign-in and storage, read as data the way Autodesk's own aps-docs-portal skill does. When anything moved it opens one issue: the commits since each pin, the version changes, the pages added, removed or changed (portal links from the contents files), new Change History entries in full, and the Revit engine dates, with links to the [HTTP reference](https://aps.autodesk.com/en/docs/design-automation/v3/reference/http/) and [developer's guide](https://aps.autodesk.com/en/docs/design-automation/v3/developers_guide/overview/). Read it, then move the pins with `npm run watch:autodesk -- --update` in a pull request.

## Privacy

This repository is public. It never names or holds a person's model, building, address, architect or family, nor their files, reports or job outputs. Tests and examples use Autodesk's public sample model (`rac_basic_sample_project.rvt`) and made-up names. A person's files live only in their own flo2 design.

## Later

- More change kinds, one at a time as the loop needs them, each a schema version.
- Activity signing ([aps-designautomation-signer](https://github.com/autodesk-platform-services/aps-designautomation-signer)), so that only payloads signed by flo2.io run. Not in v0: Autodesk requires the signature only for WorkItems submitted with a three-legged token, v0 submits with the app's own two-legged token under its own nickname, and signing needs a long-lived private key that flo2-rvt, which holds only a per-job token, must not hold.
- Running standalone on a person's own machine with their own keys, shipped as a plugin, after the first live probe.

## Licence

Apache-2.0 (`LICENSE`). Code adapted from Autodesk's MIT and Apache-2.0 repositories is listed, by repository and commit, in `THIRD_PARTY_NOTICES.md`.
