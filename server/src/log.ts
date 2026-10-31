// One JSON line per event, on stderr. flo2-rvt never logs a token, a signed
// URL, or any file's contents: callers pass only the fields named here, and a
// field whose name or value looks like a secret or a URL is dropped before the
// line is written, so a mistake in a caller is contained rather than leaked.

export type LogValue = string | number | boolean | undefined;
export type LogFields = Record<string, LogValue>;

export interface Logger {
  info(event: string, fields?: LogFields): void;
  warn(event: string, fields?: LogFields): void;
  error(event: string, fields?: LogFields): void;
}

const SECRET_KEY = /token|secret|authorization|password|url|signature|credential/i;
const SECRET_VALUE = /https?:\/\/|bearer\s|x-amz-|eyJ[A-Za-z0-9_-]{8,}/i;
const MAX_VALUE = 300;

export function scrub(fields: LogFields = {}): Record<string, string | number | boolean> {
  const out: Record<string, string | number | boolean> = {};
  for (const [key, value] of Object.entries(fields)) {
    if (value === undefined || SECRET_KEY.test(key)) continue;
    if (typeof value === 'string') {
      if (SECRET_VALUE.test(value)) {
        out[key] = '[withheld]';
        continue;
      }
      out[key] = value.length > MAX_VALUE ? `${value.slice(0, MAX_VALUE)}…` : value;
      continue;
    }
    out[key] = value;
  }
  return out;
}

export function createLogger(write: (line: string) => void = (line) => process.stderr.write(`${line}\n`)): Logger {
  const emit = (level: string, event: string, fields?: LogFields) =>
    write(JSON.stringify({ at: new Date().toISOString(), level, event, ...scrub(fields) }));
  return {
    info: (event, fields) => emit('info', event, fields),
    warn: (event, fields) => emit('warn', event, fields),
    error: (event, fields) => emit('error', event, fields),
  };
}

/** A logger that keeps its lines, for tests. */
export function memoryLogger(): Logger & { lines: string[] } {
  const lines: string[] = [];
  return Object.assign(createLogger((line) => lines.push(line)), { lines });
}
