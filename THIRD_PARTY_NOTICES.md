# Third-party notices

flo2-rvt is Apache-2.0 (see `LICENSE`). It carries code adapted from Autodesk's
open-source samples and SDKs, and test fixtures recorded from Autodesk's services and
documentation. Each source is named below by repository and commit, with the files that
carry it; each of those files also names its source in a header comment.

## Code adapted from Autodesk's repositories

### autodesk-platform-services/aps-sample-revit-mcp-tools-bundle

Commit `13d02b1464d0a22839fff9d3ce41b84180268c1a`. MIT License, Copyright (c) 2026 Autodesk, Inc.

- `addin/bundle/PackageContents.xml`, `addin/bundle/Contents/Flo2RvtProbe.addin`: the AppBundle's manifest and add-in file.
- `addin/src/ProbeApp.cs`: the `IExternalDBApplication` entry point that subscribes to `DesignAutomationBridge.DesignAutomationReadyEvent`.
- `addin/src/Jobs/JobBox.cs`: the registered-tool pattern (an attribute naming each tool, a builder that registers them, a box that opens the model and dispatches), as flo2-rvt's job registry.

### autodesk-platform-services/aps-export-revit-pdf

Commit `4b62539284e404530c4fe2705fee45712c19d0d7`, `ExportToPdfsPlugin/ExportToPdfs.cs`. MIT License, Copyright (c) 2022 Autodesk Inc.
That file also carries this notice, reproduced as it asks:

> Revit API .NET Labs
>
> Copyright (C) 2007-2019 by Autodesk, Inc.
>
> Permission to use, copy, modify, and distribute this software for any purpose and without fee is hereby granted, provided that the above copyright notice appears in all copies and that both that copyright notice and the limited warranty and restricted rights notice below appear in all supporting documentation.
>
> AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS. AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE. AUTODESK, INC. DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE UNINTERRUPTED OR ERROR FREE.
>
> Use, duplication, or disclosure by the U.S. Government is subject to restrictions set forth in FAR 52.227-19 (Commercial Computer Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii) (Rights in Technical Data and Computer Software), as applicable.

- `addin/src/Probe/Exports.cs`: the PDF export (`PDFExportOptions` with `Combine`, `Document.Export` inside a transaction that is rolled back).
- `addin/src/ProbeApp.cs`: the DesignAutomationReady handler's shape.

### autodesk-platform-services/aps-sdk-node

Commit `0c1035bec2a108af26b390d3002c57f3596fe73a`. Apache License 2.0, Copyright Autodesk, Inc.

- `server/src/aps/oss.ts`: the chunked upload of `oss/source/custom-code/ossFileTransfer.ts` (5 MiB parts, signed URLs asked for 25 at a time with `firstPart` and the `uploadKey`, URLs asked for again on a 403, one completion). Changed: it streams instead of taking a whole buffer, and completes only when the bytes match the size and SHA-256 flo2.io stated.
- `scripts/lib/auth.ts`: the two-legged token request of `authentication/source/custom-code/authenticationClient.ts` (`getTwoLeggedToken`). Changed: plain `fetch`, and no error ever echoes the secret.

### autodesk-platform-services/skills

Commit `3e85dafcec0cc0898ff0dae0069670300f58435f`, `skills/aps-docs-portal/SKILL.md`. MIT License, Copyright (c) 2026 Autodesk.

- `scripts/lib/watch.ts`: `tocIndex` is a port of the skill's `jq` filter that flattens an API's contents file into portal URL, title and source; the check reads the documentation the way the skill says to (contents JSON and static page HTML, never the portal pages).

### Flows followed, no code copied

- autodesk-platform-services/aps-design-automation-nodejs, commit `49bef9f20d41af96d86a7d0c45b1667dc042ed3a` (its `package.json` declares MIT): create an AppBundle or Activity, or add a version on a 409, then patch the alias or create it on a 404; WorkItem arguments as OSS object URNs with the token in `headers`.
- autodesk-platform-services/aps-revit-automation-cli, commit `dcfdf26e4c2540e2d4f45dd6d2117b979795e0f6`, MIT License, Copyright (c) 2026 Autodesk Platform Services: the same flow, and a Revit command line with no `/i` when the add-in opens the model itself.
- autodesk-platform-services/aps-da-acad-c3d-skill, commit `42b1e64d1fd5861b4e8af6418662a7534e763e83`, MIT License, Copyright (c) 2026 Autodesk, Inc.: the heartbeat guidance and the URL-encoding of OSS object URNs (`references/da-guardrails.md`).

## Test fixtures recorded from Autodesk

- `server/test/fixtures/autodesk/da-*.json`: responses recorded by Autodesk-Forge/forge-api-dotnet-design.automation (commit `280da5ec8d3cda63b5232956711dc964c65557e5`, `tests/e2e/recordings/`), Apache License 2.0, Copyright Autodesk, Inc.; trimmed, with signatures and tokens elided. `da-engines-*.json` are the Automation API reference's own examples.
- `server/test/fixtures/autodesk/oss-*.json`: the examples in autodesk-platform-services/aps-sdk-openapi (commit `d411c0851cfb078ff15ac442223b495b82e3a0b7`, `oss/oss.yaml`), Apache License 2.0, Copyright Autodesk, Inc.
- `scripts/test/fixtures/recorded/docs-*`: a pruned contents file and short excerpts of four Automation API documentation pages, recorded 2026-10-31 from Autodesk's documentation service. Autodesk documentation, (c) Autodesk, Inc., kept only as test fixtures and not covered by this repository's licence.
- `scripts/test/fixtures/recorded/github-*`, `nuget-*`, `npm-*`: public API responses, trimmed to the fields the check reads.

## Build-time references, not redistributed

The AppBundle zip holds only flo2-rvt's own `Flo2RvtProbe.dll` and its two manifest files. These packages are compile-time references and are never copied into it; the Revit engine provides the assemblies at run time:

- `Nice3point.Revit.Api.RevitAPI` 2024.3.60 (github.com/Nice3point/RevitApi): MIT-licensed packaging of Autodesk's `RevitAPI.dll` reference assembly, which remains Autodesk's.
- `Autodesk.Forge.DesignAutomation.Revit` 2024.0.2: Autodesk's DesignAutomationBridge, "Autodesk Content" under the Autodesk Web Services API Terms of Service, per the package's own licence file.
- `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3: Microsoft, MIT License.

## The MIT License

The MIT-licensed sources above are used under these terms, with their copyright lines as given above:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The Apache-2.0-licensed sources are used under the licence in `LICENSE`.
