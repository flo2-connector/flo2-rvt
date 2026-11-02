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
