// The refusals a tool answers with: a plain sentence the agent and the person read, no URL, no token.

/** A refusal the agent should read: a plain sentence, no URL, no token. */
export class Refusal extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'Refusal';
  }
}

/**
 * The person's Autodesk app does not hold what the job needs: flo2-rvt was never set up on it,
 * or Autodesk said so when the WorkItem was submitted. The gateway reads it by its code
 * (`_meta["flo2.io/error"].code`, `not_set_up`) and offers the one-time setup.
 */
export class NotSetUp extends Refusal {
  /** Autodesk's own words, when Autodesk refused. */
  readonly autodeskSaid: string | undefined;
  constructor(message: string, autodeskSaid?: string) {
    super(message);
    this.name = 'NotSetUp';
    this.autodeskSaid = autodeskSaid;
  }
}

/** A client id as flo2-rvt shows it in a refusal or a log: `…` and its last four characters. */
export function clientIdShown(clientId: string): string {
  return `…${clientId.slice(-4)}`;
}

/** Text with every occurrence of the client id (in any case) written by its last four characters. */
export function withoutClientId(text: string, clientId: string): string {
  if (!clientId) return text;
  return text.replace(new RegExp(clientId.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi'), clientIdShown(clientId));
}

/**
 * Autodesk refused a step of a person's setup. The message says which step, in plain words and
 * by its call, with Autodesk's status and words beside them; the fields carry the same for the
 * log. The client id is written by its last four characters in both.
 */
export class SetupStopped extends Refusal {
  readonly operation: string;
  readonly status: number;
  /** Autodesk's own words, when it gave any (no URL in them, the client id by its last four). */
  readonly autodeskSaid: string | undefined;
  constructor(message: string, operation: string, status: number, autodeskSaid?: string) {
    super(message);
    this.name = 'SetupStopped';
    this.operation = operation;
    this.status = status;
    this.autodeskSaid = autodeskSaid;
  }
}
