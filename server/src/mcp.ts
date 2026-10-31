// The three tools the agent sees, as the contract (v1) names them. Each refuses a
// call that does not carry flo2.io's hidden job context, and treats the
// agent's own arguments as untrusted.

import { McpServer } from '@modelcontextprotocol/server';
import type { CallToolResult } from '@modelcontextprotocol/server';
import { ApsError } from './aps/http.ts';
import { BucketTakenError } from './aps/oss.ts';
import {
  EditStartArgs,
  EditStartResult,
  JobContextSchema,
  JobStatusArgs,
  JobStatusResult,
  META_JOB,
  META_OUTPUTS,
  META_USAGE,
  ProbeStartArgs,
  ProbeStartResult,
  type JobContext,
} from './contract.ts';
import { JobService, Refusal } from './jobs.ts';
import type { Logger } from './log.ts';

export const SERVER_NAME = 'flo2-rvt';
export const SERVER_VERSION = '0.2.0';

function text(message: string): CallToolResult {
  return { content: [{ type: 'text', text: message }], isError: true };
}

/** The hidden context, or the reason it is refused. */
export function readJobContext(meta: Record<string, unknown> | undefined): { ok: true; ctx: JobContext } | { ok: false; why: string } {
  const raw = meta?.[META_JOB];
  if (raw === undefined) {
    return { ok: false, why: 'Refused: flo2-rvt is reached only through flo2.io, which hands over the job with each call. This call carried none.' };
  }
  const parsed = JobContextSchema.safeParse(raw);
  if (!parsed.success) {
    return { ok: false, why: "Refused: the job flo2.io handed over is not in the form flo2-rvt reads (params._meta[\"flo2.io/job\"])." };
  }
  return { ok: true, ctx: parsed.data };
}

function failure(err: unknown, logger: Logger, tool: string): CallToolResult {
  if (err instanceof Refusal || err instanceof BucketTakenError) return text(err.message);
  if (err instanceof ApsError) {
    logger.warn('autodesk_refused', { tool, operation: err.operation, status: err.status });
    return text(`Autodesk refused the call: ${err.message}.`);
  }
  logger.error('tool_failed', { tool, error: err instanceof Error ? err.name : 'unknown' });
  return text('flo2-rvt failed while running the call; nothing was changed. The server log has the details.');
}

export function buildServer(jobs: JobService, logger: Logger): McpServer {
  const server = new McpServer({ name: SERVER_NAME, version: SERVER_VERSION });

  server.registerTool(
    'rvt_probe_start',
    {
      title: "Probe a Revit model in Autodesk's cloud (read-only, paid)",
      description:
        "Start a READ-ONLY probe of a Revit model (.rvt) in the design. Real Revit 2024, in Autodesk's cloud (Design Automation), opens the model and changes nothing: it lists the levels with their elevations, the sheets, the linked files and the element counts by category, prints every sheet to one PDF, and exports Autodesk's own IFC. This starts a PAID job on the person's own Autodesk account, billed by processing time, which flo2.io confirms and caps. It returns a job_id at once; poll rvt_job_status every minute or so until the state is success or a failure.",
      inputSchema: ProbeStartArgs,
      outputSchema: ProbeStartResult,
      annotations: {
        title: "Probe a Revit model in Autodesk's cloud (read-only, paid)",
        readOnlyHint: false,
        destructiveHint: false,
        idempotentHint: false,
        openWorldHint: true,
      },
    },
    async (args, ctx) => {
      const job = readJobContext(ctx.mcpReq._meta);
      if (!job.ok) return text(job.why);
      try {
        const started = await jobs.startProbe(job.ctx, args.max_minutes, ctx.mcpReq.signal);
        return { content: [{ type: 'text', text: JSON.stringify(started) }], structuredContent: started };
      } catch (err) {
        return failure(err, logger, 'rvt_probe_start');
      }
    },
  );

  server.registerTool(
    'rvt_edit_start',
    {
      title: "Edit a Revit model in Autodesk's cloud, saved as a new model (paid)",
      description:
        "Start an EDIT of a Revit model (.rvt) in the design. Real Revit 2024, in Autodesk's cloud (Design Automation), opens the model and applies a change list from flo2-rvt's fixed vocabulary, in order, each change in its own transaction: delete_elements_on_level (every model element whose base or reference level it is; what is hosted in them goes with them), delete_elements_by_global_id (elements named by their IFC GlobalId, as Revit's own IFC export writes them), and set_level_elevation (what is on the level moves with it). A change Revit raises an error for is rolled back alone and the others go on. The result is saved as a NEW model.rvt (a new central model when the input is workshared), with its IFC and sheets PDF, and report.json says what each change matched, deleted and could not do, what Revit deleted or changed on its own, and whether it was applied or rolled back. The input model is never changed. This starts a PAID job on the person's own Autodesk account, billed by processing time, which flo2.io confirms (showing the person the changes in plain words) and caps. It returns a job_id at once; poll rvt_job_status every minute or so until the state is success or a failure.",
      inputSchema: EditStartArgs,
      outputSchema: EditStartResult,
      annotations: {
        title: "Edit a Revit model in Autodesk's cloud, saved as a new model (paid)",
        readOnlyHint: false,
        destructiveHint: false,
        idempotentHint: false,
        openWorldHint: true,
      },
    },
    async (args, ctx) => {
      const job = readJobContext(ctx.mcpReq._meta);
      if (!job.ok) return text(job.why);
      try {
        const started = await jobs.startEdit(job.ctx, args.max_minutes, args.change_list, ctx.mcpReq.signal);
        return { content: [{ type: 'text', text: JSON.stringify(started) }], structuredContent: started };
      } catch (err) {
        return failure(err, logger, 'rvt_edit_start');
      }
    },
  );

  server.registerTool(
    'rvt_job_status',
    {
      title: "Read a cloud-Revit job's status",
      description:
        "Read where a cloud-Revit job stands, from the job_id rvt_probe_start or rvt_edit_start returned. The state is Autodesk's own: pending, inprogress, success, or a failure such as failedInstructions or failedLimitProcessingTime. On success it lists the results (a probe's report.json, sheets.pdf and model.ifc; an edit's report.json, model.rvt, sheets.pdf and model.ifc), which flo2.io saves into the design as new files, and a summary of the report (an edit's: its outcome, how many changes were applied, applied in part, rolled back or not applied, the elements deleted, and whether a new model was saved). Poll it about once a minute.",
      inputSchema: JobStatusArgs,
      outputSchema: JobStatusResult,
      annotations: {
        title: "Read a cloud-Revit job's status",
        readOnlyHint: false,
        destructiveHint: false,
        idempotentHint: true,
        openWorldHint: true,
      },
    },
    async (args, ctx) => {
      const job = readJobContext(ctx.mcpReq._meta);
      if (!job.ok) return text(job.why);
      try {
        const { result, outputs, usage } = await jobs.status(job.ctx, args.job_id, ctx.mcpReq.signal);
        const reply: CallToolResult = { content: [{ type: 'text', text: JSON.stringify(result) }], structuredContent: result };
        if (outputs) reply._meta = { [META_OUTPUTS]: outputs, ...(usage ? { [META_USAGE]: usage } : {}) };
        return reply;
      } catch (err) {
        return failure(err, logger, 'rvt_job_status');
      }
    },
  );

  return server;
}
