# flo2-rvt

flo2-rvt lets an agent working through [flo2.io](https://flo2.io) have real Revit, in Autodesk's cloud, read a person's Revit model (`.rvt`) and, in later versions, change it through a fixed vocabulary of changes. It is an MCP server written in TypeScript that drives Autodesk Platform Services' **Automation API** (Design Automation v3) on the person's **own** Autodesk app, with a short-lived token flo2.io hands it for each job.

**v0 is a read-only probe.** Real Revit 2024 opens the model and changes nothing. It returns three files, which flo2.io saves into the person's design as new files:

| File | What it holds |
| --- | --- |
| `report.json` | The levels with their elevations in feet and millimetres, the sheets by number and name, the linked files (Revit links and CAD links or imports), element counts by category, the Revit release the file was saved in and the engine's version and build, and the warnings stored in the model. |
| `sheets.pdf` | Every printable sheet, in sheet-number order, in one PDF. Absent when the model has none. |
| `model.ifc` | Revit's own IFC export: IFC4 Design Transfer View, with base quantities. |

flo2-rvt's design lives in reflow2, design `6ab9996ca4bdbc45` (reached through the hub); the settled choices behind it are in flo2's design.

## How a job travels

```
agent ──tools/call──▶ flo2.io gateway ──127.0.0.1:8740, bearer──▶ flo2-rvt ──person's APS app, job token──▶ Autodesk
                          │  holds the person's APS secret             │  holds no secret, keeps no token
                          │  mints a short-lived token per job         │  never opens the .rvt itself
                          ◀──── signed URLs of the outputs ────────────┘
```

1. `rvt_probe_start`: flo2-rvt makes (or finds) the person's transient bucket, streams the `.rvt` from flo2.io's short-lived download link into it through signed S3 upload URLs, checks the bytes against the size and SHA-256 flo2.io stated (no match, no job), and submits a WorkItem with `limitProcessingTimeSec`. Autodesk's Revit reads the input and writes the outputs into the same bucket with the same token.
2. `rvt_job_status`, polled: Design Automation's own state, passed through. On success, the status result carries signed download URLs of the outputs for the gateway, which downloads them, saves them add-only, and strips them before the agent sees the result.
3. Autodesk deletes everything in a transient bucket after 24 hours.

## The contract with flo2.io's gateway (v0)

### Transport

- **Streamable HTTP** on `127.0.0.1`, path `/mcp`, port `FLO2_RVT_PORT` (default `8740`). Loopback only; a foreign `Host` or `Origin` is refused (403).
- Every request carries `Authorization: Bearer <FLO2_RVT_LOCAL_TOKEN>` (32+ characters, from the environment). Anything else is 401.
- Built on the MCP TypeScript SDK **v2.4.0** (`@modelcontextprotocol/server` 2.4.0 with `@modelcontextprotocol/node` 2.1.2), through its `createMcpHandler`: a fresh server per request, no session. It serves the 2025 protocol revisions (an `initialize` with `2025-06-18`, or a `tools/call` on its own) and the 2026-07-28 revision.
- A client sends `Accept: application/json, text/event-stream`. Answers to 2025-revision requests come as a short SSE stream (`event: message`, one `data:` line holding the JSON-RPC response), as Streamable HTTP allows.

### Agent-visible tools

| Tool | Arguments | Result (`structuredContent`, also as JSON text) |
| --- | --- | --- |
| `rvt_probe_start` | `file`: string, the design file's name or id; `max_minutes`?: integer 1..30, default 15 | `{ job_id, state: "submitted", engine: "Autodesk.Revit+2024" }` |
| `rvt_job_status` | `job_id`: string | `{ job_id, state, processing_seconds?, outputs?: [{ name, size }], report_summary?: { levels, sheets, links }, error? }` |

`state` is Design Automation's own status, passed through: `pending`, `inprogress`, `success`, `cancelled`, `failedLimitDataSize`, `failedLimitProcessingTime`, `failedDownload`, `failedInstructions`, `failedUpload`, `failedUploadOptional`, and any it adds. `processing_seconds` runs from the start of the input download to the end of the output upload, as Autodesk counts its limit, rounded up. Both tools carry annotations: a `title`, `readOnlyHint: false`, `openWorldHint: true` (and `destructiveHint: false`; `idempotentHint` false for start, true for status).

### Hidden context

The gateway sets `params._meta["flo2.io/job"]` on every `tools/call`:

```json
{
  "person_id": "opaque",
  "design_id": "…",
  "aps": { "access_token": "…", "expires_at": "2026-10-31T12:59:59Z", "client_id": "…" },
  "input": { "download_url": "https://…", "file_name": "….rvt", "size": 123, "sha256": "…" },
  "limits": { "max_processing_sec": 1800 }
}
```

- A call without it, or with it malformed, is refused (a tool error the agent reads). The agent's arguments are untrusted and bounded; everything that decides what happens at Autodesk comes from this context.
- `input` is required on `rvt_probe_start` only. The file must be a `.rvt`.
- `expires_at` may be an ISO-8601 time, or seconds (or milliseconds) since the epoch.
- **The token must outlive the job.** Autodesk's Revit stores the outputs with the same token when the job ends, so `rvt_probe_start` refuses a token valid for less than the job's time limit plus 600 s. A freshly minted two-legged token (3,599 s) covers the longest job (1,800 s).
- The time limit is the lower of `max_minutes × 60` and `limits.max_processing_sec`.
- flo2-rvt uses only this token, keeps it for no longer than the call (the WorkItem carries it to Autodesk), and never logs it.

### Results for the gateway

When a job reaches `success`, the status result carries:

- `_meta["flo2.io/outputs"] = [{ name, signed_url, expires_at, size, content_type }]`, one per output that exists (`report.json` `application/json`, `sheets.pdf` `application/pdf`, `model.ifc` `application/x-step`). The URLs stay usable for 15 minutes.
- `_meta["flo2.io/usage"] = { processing_seconds, workitem_id }`.

`failedUploadOptional` (an optional output could not be stored) carries the same two keys for the outputs that were stored.

### Names

- `job_id`: a v4 uuid flo2-rvt mints before the upload.
- Bucket key: `flo2rvt-` + the lowercased client id, reduced to OSS's `[-_.a-z0-9]` and cut to fit 128 characters, + `-t`. Transient (24 h).
- Objects: `<job_id>/input.rvt`, `<job_id>/report.json`, `<job_id>/sheets.pdf`, `<job_id>/model.ifc`, and **`<job_id>/workitem.json`**, a small job record (the WorkItem id, the activity, the time limit, when it was submitted). The WorkItem id only exists after the inputs and outputs are named, so it cannot be encoded in the `job_id`; the record is how a status call after a restart, with the `job_id` and a fresh token alone, finds the WorkItem. An in-memory cache saves the read while the process lives.
- Activity: `<nickname>.Flo2RvtProbe+prod`, where the nickname is what `GET /forgeapps/me` answers: the app's nickname, or its client id when it has none.

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
| `code:all` | `GET /da/us-east/v3/forgeapps/me`, `POST /workitems`, `GET /workitems/{id}` |

## The change list

`schema/change-list.schema.json` is the one JSON Schema of the change list an agent will hand an edit job: version 0, the element reference (an IFC GlobalId, or a level by name or by its storey's GlobalId), and the first three change kinds: `delete_elements_on_level`, `delete_elements_by_global_id` and `set_level_elevation`. The schema accepts them; **no edit job runs them in v0.** `npm run gen` builds both sides' types from it: `server/src/types/change-list.ts` and `addin/Generated/ChangeList.g.cs`. The add-in's build refuses C# types generated from another version of the schema.

## The add-in

`addin/` is a Revit 2024 add-in for the Automation API (C#, .NET Framework 4.8): an `IExternalDBApplication` that answers `DesignAutomationBridge.DesignAutomationReadyEvent`, packaged as an AppBundle zip (`Flo2RvtProbe.bundle/PackageContents.xml`, `Contents/Flo2RvtProbe.addin`, `Contents/Flo2RvtProbe.dll`).

- A small job registry (Autodesk's registered-tool pattern from aps-sample-revit-mcp-tools-bundle) holds one job, `probe`. Edit jobs will join it one change kind at a time.
- The job box opens `input.rvt` from the working folder itself (detached from its central, all worksets open, when the file is workshared), runs the job, and **closes the model without saving**.
- The probe reads the levels, sheets, links, element counts, versions and stored warnings; prints the sheets with `Document.Export` and `PDFExportOptions` (`Combine`); exports IFC with `IFCExportOptions` (`IFC4DTV`, `ExportBaseQuantities`); and writes `report.json` last, recording any export that failed. Each export runs inside a transaction that is **rolled back**: Revit requires an open transaction for IFC export. Nothing is ever committed; CI fails the build if `addin/src` holds a `Commit`, `Save`, `SaveAs` or `SynchronizeWithCentral` call.
- A heartbeat writes to the job log every 20 seconds, so a long open or export is not taken for an idle job.
- It builds against reference assemblies from NuGet, on any OS: `Nice3point.Revit.Api.RevitAPI` 2024.3.60 (MIT packaging of Autodesk's reference assembly), `Autodesk.Forge.DesignAutomation.Revit` 2024.0.2 (Autodesk's bridge) and `Microsoft.NETFramework.ReferenceAssemblies.net48`. None is shipped in the bundle; the engine provides them.

It has not been run in Revit yet: CI proves it compiles and that the bundle has the expected layout. The first live probe is its first run.

## Running the server

```sh
npm ci --omit=dev
FLO2_RVT_LOCAL_TOKEN=… FLO2_RVT_PORT=8740 node server/src/index.ts
```

Node 22.18 or later runs the TypeScript directly. On flo2.io it runs as its own service under its own unprivileged user, with network reach and no access to design files on disk. It logs one JSON line per event to stderr, never a token, a signed URL or a file's contents.

## The owner's one-time setup

1. **Create the Autodesk app** (Autodesk's [Create an App](https://aps.autodesk.com/en/docs/oauth/v2/tutorials/create-app/) walkthrough):
   1. Sign in at [aps.autodesk.com](https://aps.autodesk.com) (create an account if needed).
   2. Have a developer hub: with an APS plan, create one at [manage.autodesk.com](https://manage.autodesk.com) under Products and Services > Hubs > Create hub > APS developer hub. The Automation API is a paid API; check the plan's caps and the current price.
   3. Profile menu > **My applications** > choose the hub > **Create application**. Name it, choose the **Server-to-Server App** type (client credentials, two-legged tokens), and under **API Access** select the **Automation API** and the **Data Management API** (for its storage). Save.
   4. Copy the Client ID and Client Secret into your environment as `APS_CLIENT_ID` and `APS_CLIENT_SECRET`, and into flo2.io's encrypted store as flo2.io asks. Never put them in a file in this repository.
2. **Get the AppBundle**: download `Flo2RvtProbe.bundle` from the CI run's artifacts, or build it: `dotnet build addin -c Release` writes `addin/dist/Flo2RvtProbe.bundle.zip`.
3. **Register** (on your own machine, once, and again for a new add-in version):

   ```sh
   npm ci
   APS_NICKNAME=my_flo2rvt_app APS_MONTHLY_HOURS=2 npx flo2-rvt register --bundle addin/dist/Flo2RvtProbe.bundle.zip
   ```

   In this order:
   1. **The nickname, first.** Autodesk lets an app take a nickname only while it has no AppBundle or Activity ([Task 3, Create a Nickname](https://aps.autodesk.com/en/docs/design-automation/v3/tutorials/revit/step3-create-nickname/)). On a fresh app it sets `APS_NICKNAME` (letters, digits and underscores; default `flo2rvt_` and the first twelve letters and digits of the client id, lowercased) with `PATCH /forgeapps/me`. Nicknames are global: a taken one stops the run before anything is created, and asks for another `APS_NICKNAME`. A nickname already set is kept. An app that already has data and no nickname carries on with its client id as the owner prefix: setting one then would need `DELETE /forgeapps/me`, which deletes everything on the app, and this command never calls it.
   2. The AppBundle, uploaded as a new AppBundle or a new version of it, and its `prod` alias.
   3. The `Flo2RvtProbe` Activity on the `Autodesk.Revit+2024` engine, named `<owner>.Flo2RvtProbe+prod`, and its `prod` alias.
   4. Autodesk's own monthly processing cap on the app (`frontendLimits.limitMonthlyProcessingTimeInHours`, `APS_MONTHLY_HOURS`, default 2), read back.

   It prints the owner prefix and how it was settled, each version and alias, and the cap Autodesk reports. flo2-rvt's server reads the same prefix from `GET /forgeapps/me` for every job.
4. **Check the engines** whenever the monthly check asks: `npx flo2-rvt engines` lists Design Automation's Revit engines beside the dates Autodesk's Engine Lifecycle Policy page gives. The Revit 2024 engine takes no new AppBundles or Activities after **2027-03-29** and is removed on 2029-03-29.

## Development

```sh
npm ci
npm run lint && npm run typecheck && npm run gen:check && npm test
dotnet build addin -c Release      # the .NET SDK (10.0) builds the net48 add-in on Linux too
```

The tests cost nothing and touch no network: Autodesk is a loopback stand-in that answers with responses recorded from Autodesk's own services (`server/test/fixtures/autodesk/`, each naming its source), and the monthly check's tests replay recorded GitHub, NuGet, npm and documentation responses. Test data uses Autodesk's public sample model's name and made-up values.

## Monthly checks

`.github/workflows/dependencies.yml`, on the first of each month and on demand:

- **currency** (`scripts/dependency-currency.ts`): every npm and NuGet pin and GitHub Action against its latest release, and the Revit engine against its deprecation date. A newer major fails unless `dependency-holds.json` holds it back with a reason and a date to look again.
- **autodesk** (`scripts/autodesk-watch.ts`, `watch/autodesk.json`): Autodesk's repositories (pinned by commit), Autodesk's Design Automation and APS SDK packages, and the documentation of the Automation API, sign-in and storage, read as data the way Autodesk's own aps-docs-portal skill does. When anything moved it opens one issue: the commits since each pin, the version changes, the pages added, removed or changed (portal links from the contents files), new Change History entries in full, and the Revit engine dates, with links to the [HTTP reference](https://aps.autodesk.com/en/docs/design-automation/v3/reference/http/) and [developer's guide](https://aps.autodesk.com/en/docs/design-automation/v3/developers_guide/overview/). Read it, then move the pins with `npm run watch:autodesk -- --update` in a pull request.

## Privacy

This repository is public. It never names or holds a person's model, building, address, architect or family, nor their files, reports or job outputs. Tests and examples use Autodesk's public sample model (`rac_basic_sample_project.rvt`) and made-up names. A person's files live only in their own flo2 design.

## Later

- Edit jobs: the change list run by the add-in (deleting elements, setting a level's elevation), saving a **new** `.rvt`; Autodesk's aps-count-delete-walls and aps-auto-delete-walls samples are the model for the first kind.
- Activity signing ([aps-designautomation-signer](https://github.com/autodesk-platform-services/aps-designautomation-signer)), so that only payloads signed by flo2.io run. Not in v0: Autodesk requires the signature only for WorkItems submitted with a three-legged token, v0 submits with the app's own two-legged token under its own nickname, and signing needs a long-lived private key that flo2-rvt, which holds only a per-job token, must not hold.
- Running standalone on a person's own machine with their own keys, shipped as a plugin, after the first live probe.

## Licence

Apache-2.0 (`LICENSE`). Code adapted from Autodesk's MIT and Apache-2.0 repositories is listed, by repository and commit, in `THIRD_PARTY_NOTICES.md`.
