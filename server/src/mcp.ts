// The tools, as the contract (v8) names them: the four an agent sees through flo2.io
// (rvt_probe_start, rvt_edit_start, since v6 rvt_new_model_start, rvt_job_status), and the two flo2.io's gateway calls
// itself and never offers an agent (rvt_setup_check before it asks the person for a job,
// rvt_setup_run once the person has approved the setup). Each refuses a call that does not
// carry flo2.io's hidden job context, and treats the caller's own arguments as untrusted.

import { McpServer } from '@modelcontextprotocol/server';
import type { CallToolResult } from '@modelcontextprotocol/server';
import { ApsError } from './aps/http.ts';
import { BucketTakenError } from './aps/oss.ts';
import * as z from 'zod/v4';
import {
  EditStartArgs,
  EditStartResult,
  JobContextSchema,
  JobStatusArgs,
  JobStatusResult,
  META_ERROR,
  NewModelStartArgs,
  NewModelStartResult,
  META_JOB,
  META_OUTPUTS,
  META_USAGE,
  ProbeStartArgs,
  ProbeStartResult,
  SetupCheckArgs,
  SetupCheckResult,
  SetupRunResult,
  type ErrorMeta,
  type JobContext,
} from './contract.ts';
import { JobService, NotSetUp, Refusal } from './jobs.ts';
import type { Logger } from './log.ts';

export const SERVER_NAME = 'flo2-rvt';
export const SERVER_VERSION = '0.16.0';

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
  if (err instanceof NotSetUp) {
    // The gateway reads the case by its code, and offers the person the one-time setup.
    logger.warn('not_set_up', { tool, autodesk_refused: err.autodeskSaid !== undefined });
    const meta: ErrorMeta = { code: 'not_set_up', billed: false, ...(err.autodeskSaid ? { autodesk_said: err.autodeskSaid } : {}) };
    return { ...text(err.message), _meta: { [META_ERROR]: meta } };
  }
  if (err instanceof Refusal || err instanceof BucketTakenError) return text(err.message);
  if (err instanceof ApsError && err.operation === 'DA forgeapps/me' && (err.status === 401 || err.status === 403)) {
    logger.warn('autodesk_refused', { tool, operation: err.operation, status: err.status });
    return text(
      `Autodesk refused to let flo2-rvt read this app's Design Automation settings (HTTP ${err.status}). The app may not have the Automation API in its API Access (aps.autodesk.com, My applications, the app, API Access). Nothing was uploaded and nothing ran.`,
    );
  }
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
        "Start a READ-ONLY probe of a Revit model (.rvt) in the design. Real Revit 2024, in Autodesk's cloud (Design Automation), opens the model and changes nothing: it lists the levels with their elevations, the sheets, the linked files and the element counts by category (since add-in 0.10.0 also every wall, floor and door type it holds, its placed rooms with their boundaries, and how its shared coordinates sit on its internal ones, for an edit that adds; since add-in 0.11.0 every foundation type, each Structural Foundations family type and wall foundation type with its length parameters, type or instance, their values and whether Revit holds them read-only or by a formula, which an edit's sizes name), prints every sheet to one PDF, and exports Autodesk's own IFC. Since add-in 0.12.0 it also SAVES the families named in save_families (at most 8, each by its family name exactly as the report lists it) out of the model as family files (.rfa, Revit 2024): each loadable family the model holds under that name is saved as <name>.rfa and kept by flo2.io as a new file in the design, where an edit can name it as a family_file (a footing's or pile's family for another model); a name the model does not hold, a system or in-place family, or one Revit will not save, is reported with why and not saved, and the model itself is never changed. This starts a PAID job on the person's own Autodesk account, billed by processing time, which flo2.io confirms and caps. It returns a job_id at once; call rvt_job_status with it, and again as soon as each answer comes, until the state is success or a failure (flo2.io holds each status call until the job moves, so add no wait of your own).",
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
        const started = await jobs.startProbe(job.ctx, args.max_minutes, args.save_families, ctx.mcpReq.signal);
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
        "Start an EDIT of a Revit model (.rvt) in the design. Real Revit 2024, in Autodesk's cloud (Design Automation), opens the model and applies a change list from flo2-rvt's fixed vocabulary, in order, each change in its own transaction: delete_elements_on_level (every model element whose base, reference or Schedule Level it is; what is hosted in them goes with them; what is still tied to the level afterwards, such as a family placed on no level that the IFC export files under it, is listed with why, never deleted by a guess), delete_elements_by_global_id (elements named by their IFC GlobalId, as Revit's own IFC export writes them), set_level_elevation (what is on the level moves with it; each runs alone), and move_levels_together (change-list version 1: several levels moved by one offset in ONE transaction, so what spans two of them is meant to keep its shape and move whole; use it to shift a stack of levels; the report lists whatever Revit reshaped on its own, reshaped_by_revit, since add-in 0.7.0, each expected and why or not, with a stair's or a stair run's climb (a run naming its stair) and a room's number, name and area, and the warnings the move added to the model, warnings_added, since add-in 0.8.0). A model group in the way of a level move (Revit's \"Changes to groups are allowed only in group edit mode\") rolls the move back and the report names the groups, unless the change says groups_in_the_way \"ungroup\": then only the group instances Revit names are ungrouped in the new model, and their copies are no longer linked. Say \"ungroup\" only once the person has agreed to it. Since change-list version 3 (add-in 0.10.0) a list also ADDS new content, each from a type the model already holds, named by its family and type as the probe's report lists them (types): add_wall (a straight wall on a level from start to end, rising by a height or to a level above, its ends joined unless joins says do_not_join), add_floor (a floor on a level over a closed outline, a walkway), add_room (a room with a name and a number at a point that walls enclose; one nothing encloses is rolled back) and add_door (a door into a wall the model has, by GlobalId, or one an earlier add_wall adds, by its change id, at a point along it). Every point is x and y in `coordinates` \"shared\" (Revit's shared coordinates, the frame of Revit's own IFC export of the model and of the probe's rooms[].boundary_shared_ft) or \"internal\", with its unit; its height is its level's. Since change-list version 5 (add-in 0.13.0) a list also adds LEVELS: add_level (a level by name at an elevation, from the project origin or its elevation base, its name one no level of the model holds; a building storey, so the IFC holds an IfcBuildingStorey for it; later changes place on it by its name). Since change-list version 4 (add-in 0.11.0) a list also adds FOUNDATIONS: add_footing (an isolated footing at a point on a level, turned by rotation_deg), add_pile (a pile at a point, hanging plumb from its top) and add_wall_footing (a continuous footing under a wall the model has, by GlobalId, or one an earlier add_wall adds, by its change id). A footing or pile stands with its top, as Revit holds it (its Elevation at Top), at top_offset from the level, whatever its family's insertion point (since add-in 0.13.1 the add-in moves it by the difference once placed, on its offset from its level), and the add-in sets a footing's own Export to IFC As to IfcFooting and a pile's to IfcPile, so the IFC holds an IfcFooting or an IfcPile. Since change-list version 6 (add-in 0.14.0) a door's type may come from a door family file (.rfa) the design keeps, named by family_file, as a footing's or pile's may, and a door may be sized as they are (its Width and Height on Autodesk's Single-Flush). Since change-list version 7 (add-in 0.15.0) a list also makes a DRAWING SET: add_drawing_set, put last, after what it draws, makes a floor plan of every level, the four elevations, two sections through the middle and a 3D view, each on a numbered sheet of its own (A-101 on for the plans, lowest level first; A-201 to A-204 the north, east, south and west elevations; A-301 and A-302 the east-west and north-south sections; A-401 the 3D view), with the model's first title block (or the one title_block names, or none when the model holds none), each view cropped to the building at the largest standard scale that fits its sheet; the job prints them, with the model's other printable sheets, into sheets.pdf. Its options ask for less, never for another kind of view: views (plans, elevations, sections, isometric), levels, empty_levels (include or leave_out) and title_block. A level nothing stands on still gets its plan unless empty_levels says leave_out. It adds no building element; report.json's drawing_set says, for each view and sheet, what was made, its scale and where its viewport sits, or why not. A door, footing or pile is of a type the model holds, or of one in a family file (.rfa) the design keeps, named by family_file, which flo2.io hands over with the job and the add-in loads first (a family the model already holds under that name is used as it is); the family must be in the category its changes need, Doors for a door's, Structural Foundations for a footing's or pile's, so one file serves doors or foundations, never both. `sizes` sets length parameters by their names, a foundation's as the probe lists them (types.foundations), a door's as its family names them: an instance parameter on the new element, a type parameter only on a copy of the type made under `new_type`, never on a type the model holds (a `new_type` the family already holds is placed again only when its sizes are those asked). flo2.io requires every foundation size and top offset to come from the calc record the edit names (numbers_from); a door's sizes may, and need not. The report names each element added (added: element id, GlobalId, type, level, a wall's ends, length and height, a floor's area beside its outline's, a room's number, name and area, a door's host wall, width and height, and for a door from a family file or sized, the type it was copied from and every length parameter as Revit holds it beside each size asked; a footing's or pile's point, where its insertion point, top and bottom stand, the correction applied for its top, its rotation, the IFC class the add-in set, and every length parameter as Revit holds it; a wall footing's host wall, width, thickness and length; a new level's elevation both ways and its storey's GlobalId) and each family file loaded (families, each with the category it needed). A change Revit raises an error for is rolled back alone and the others go on. The result is saved as a NEW model.rvt (a new central model when the input is workshared), with its IFC and sheets PDF, and report.json says what each change matched and deleted (each element by GlobalId, category and Revit class, model elements counted apart from Revit's internal ones), what Revit deleted with a host or changed on its own, what is still in the model (could not be deleted, or left on the level), and whether it was applied, applied in part or rolled back. The input model is never changed. This starts a PAID job on the person's own Autodesk account, billed by processing time, which flo2.io confirms (showing the person the changes in plain words) and caps. It returns a job_id at once; call rvt_job_status with it, and again as soon as each answer comes, until the state is success or a failure (flo2.io holds each status call until the job moves, so add no wait of your own).",
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
    'rvt_new_model_start',
    {
      title: "Make a NEW Revit model in Autodesk's cloud and fill it with a change list (paid)",
      description:
        "Start a job that makes a NEW Revit model, one the design does not have yet, and fills it with its first change list, in ONE job. Real Revit 2024, in Autodesk's cloud (Design Automation), makes the project (Application.NewProjectDocument) either from Revit's own project with no template, in `units` \"imperial\" or \"metric\" (no file needed; its levels and types are Revit's defaults, few of them), or from `template_file`, a Revit project template (.rte, Revit 2024 or older) the design keeps, which flo2.io hands over with the job; give exactly one of the two. Then it loads the family files the list names, applies the change list in order, each change in its own transaction as an edit does (the same vocabulary as rvt_edit_start, change-list version 7: add_level for the levels, then add_wall, add_floor, add_room, add_door, add_footing, add_wall_footing and add_pile on them by their names; a footing or pile stands with its top, as Revit holds it (its Elevation at Top), at top_offset from the level, and the add-in sets a footing's own Export to IFC As to IfcFooting and a pile's to IfcPile; a door's type may come from a door family file (.rfa) the design keeps, named by family_file, as a footing's or pile's may, and be sized on a copy of its type under new_type, as Revit's own project with no template holds no door family, since change-list version 6; and since change-list version 7 add_drawing_set, last, a drawing set of the new model, a floor plan of every level, the four elevations, two sections and a 3D view, each on a numbered sheet, which adds no building element, so the list must add more than a set), exports the new model's IFC, prints its sheets into sheets.pdf, and saves it as model.rvt, a new file in the design. The list must add something: an empty model is not a result anyone asked for. A change naming a type the new model does not hold is not applied, and its why names the types it holds; report.json lists every wall, floor, door and foundation type the new model holds (types, as the probe's report does) and the levels it started with (new_model.levels_at_start), so the next edit can name them. In a new model, shared coordinates are its internal ones unless its template sets them otherwise (report.json's coordinates say). The model is saved only when at least one change was kept. This starts a PAID job on the person's own Autodesk account, billed by processing time, which flo2.io confirms (showing the person that a new model will be made, from what, and the changes in plain words) and caps. It returns a job_id at once; call rvt_job_status with it, and again as soon as each answer comes, until the state is success or a failure (flo2.io holds each status call until the job moves, so add no wait of your own).",
      inputSchema: NewModelStartArgs,
      outputSchema: NewModelStartResult,
      annotations: {
        title: "Make a NEW Revit model in Autodesk's cloud and fill it with a change list (paid)",
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
        const started = await jobs.startNewModel(job.ctx, args.max_minutes, { units: args.units, template_file: args.template_file }, args.change_list, ctx.mcpReq.signal);
        return { content: [{ type: 'text', text: JSON.stringify(started) }], structuredContent: started };
      } catch (err) {
        return failure(err, logger, 'rvt_new_model_start');
      }
    },
  );

  server.registerTool(
    'rvt_job_status',
    {
      title: "Read a cloud-Revit job's status",
      description:
        "Read where a cloud-Revit job stands, from the job_id rvt_probe_start, rvt_edit_start or rvt_new_model_start returned. The state is Autodesk's own: pending, inprogress, success, or a failure such as failedInstructions or failedLimitProcessingTime. On success it lists the results (a probe's report.json, sheets.pdf and model.ifc, and since add-in 0.12.0 each family it saved, family1.rfa ..., named by its family; an edit's or a new model's report.json, model.rvt, sheets.pdf and model.ifc), which flo2.io saves into the design as new files, and a summary of the report (a probe's: its levels, sheets and links counted, and the families it saved and those it did not, with why; an edit's: its outcome, how many changes were applied, applied in part, rolled back or not applied, the model elements deleted and the other elements apart, what was deleted with its host, the model elements left on a level, how many elements a level move reshaped that nothing in the list asked for (reshaped_unexpected, since add-in 0.7.0), how many elements the changes added (elements_added, since add-in 0.10.0), how many sheets a drawing set made (sheets_added, since add-in 0.15.0; sheets.pdf prints them), and whether a new model was saved; a new model's, since add-in 0.13.0, also what it started from, whether Revit made it and why not, the levels it started with, and how many wall, floor, door and foundation types it holds, which its report.json names under types). Call it again as soon as each answer comes, until the state is final: flo2.io holds each call until the job moves, so add no wait of your own.",
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

  server.registerTool(
    'rvt_setup_check',
    {
      title: "Check flo2-rvt is set up on the person's Autodesk app (read-only)",
      description:
        "Called by flo2.io's gateway, never offered to an agent: before a job is put to the person, read whether their own Autodesk app holds what the job needs (the AppBundle and the job's Activity at prod, at this flo2-rvt's add-in version), by GETs alone, and with `plan`, what the one-time setup would do there. Nothing is uploaded, created or billed.",
      inputSchema: SetupCheckArgs,
      outputSchema: SetupCheckResult,
      annotations: {
        title: "Check flo2-rvt is set up on the person's Autodesk app (read-only)",
        readOnlyHint: true,
        openWorldHint: true,
      },
    },
    async (args, ctx) => {
      const job = readJobContext(ctx.mcpReq._meta);
      if (!job.ok) return text(job.why);
      try {
        const result = await jobs.setupCheck(job.ctx, args.job, args.plan, ctx.mcpReq.signal);
        return { content: [{ type: 'text', text: JSON.stringify(result) }], structuredContent: result };
      } catch (err) {
        return failure(err, logger, 'rvt_setup_check');
      }
    },
  );

  server.registerTool(
    'rvt_setup_run',
    {
      title: "Set flo2-rvt up on the person's Autodesk app, as they approved",
      description:
        "Called by flo2.io's gateway, never offered to an agent, and only once the person has approved the setup on flo2.io (the hidden context's `setup` carries what they approved: the monthly cap and the AppBundle's sha256). On their own Autodesk app it keeps the app's nickname and never sets one (an app with none is named by its client id, which Autodesk always accepts), uploads this server's AppBundle (checked against its sha256 and the one approved), creates or versions the probe, edit and new-model Activities with their prod aliases, and sets Autodesk's monthly processing cap; then reads it all back. A step Autodesk refuses stops it with which step, and Autodesk's status and words. It never deletes anything, and costs no processing time.",
      inputSchema: z.object({}),
      outputSchema: SetupRunResult,
      annotations: {
        title: "Set flo2-rvt up on the person's Autodesk app, as they approved",
        readOnlyHint: false,
        destructiveHint: true,
        idempotentHint: false,
        openWorldHint: true,
      },
    },
    async (_args, ctx) => {
      const job = readJobContext(ctx.mcpReq._meta);
      if (!job.ok) return text(job.why);
      try {
        const result = await jobs.setupRun(job.ctx, ctx.mcpReq.signal);
        return { content: [{ type: 'text', text: JSON.stringify(result) }], structuredContent: result };
      } catch (err) {
        return failure(err, logger, 'rvt_setup_run');
      }
    },
  );

  return server;
}
