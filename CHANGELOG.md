# Changelog

## 0.1.0 (unreleased)

The first version: a read-only probe.

- **Server** (`server/`): an MCP server on the MCP TypeScript SDK v2.4.0, Streamable HTTP on `127.0.0.1:8740` behind a local bearer token, with the two contract tools `rvt_probe_start` and `rvt_job_status`. It refuses calls without flo2.io's hidden job context and uses only the short-lived token that context carries.
- **Design Automation client**: the person's transient OSS bucket, a streamed, chunked upload through signed S3 URLs that completes only when the bytes match the stated size and SHA-256, WorkItems with `limitProcessingTimeSec` and OSS object URNs, status reads, and signed download URLs for the outputs.
- **Change-list schema** (`schema/change-list.schema.json`): version 0, the element reference and three change kinds, with TypeScript and C# types generated from it. No edit job runs them yet.
- **Add-in** (`addin/`): a Revit 2024 add-in for the Automation API that opens the model, changes nothing, and writes `report.json`, `sheets.pdf` and `model.ifc`; packaged as an AppBundle zip.
- **Registration CLI** (`scripts/flo2-rvt.ts`): `flo2-rvt register` (AppBundle, Activity, `prod` aliases, the monthly processing cap) and `flo2-rvt engines`.
- **CI**: lint, typecheck, generated-types check, tests, and the add-in build with its AppBundle as an artifact.
- **Monthly checks**: dependency currency with written holds, and the Autodesk watch (repositories, packages and documentation), which opens one issue when anything moved.
