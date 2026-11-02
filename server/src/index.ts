// flo2-rvt's entry point. It reads its settings from the environment, binds
// loopback only, and holds nothing across restarts but what it can rebuild.
//
//   FLO2_RVT_LOCAL_TOKEN  the bearer token flo2.io's gateway presents (required, 32+ characters)
//   FLO2_RVT_PORT         the port on 127.0.0.1 (default 8740)
//   FLO2_RVT_BUNDLE       the AppBundle zip a setup uploads, with `<zip>.sha256` beside it
//                         (default addin/dist/Flo2RvtProbe.bundle.zip in this checkout, where
//                         flo2.io's install puts the one CI built for this commit). Without it
//                         jobs still run; a setup is refused, saying why.

import { fileURLToPath } from 'node:url';
import { createHttpServer, DEFAULT_PORT, HOST, MCP_PATH, MIN_TOKEN_LENGTH } from './http.ts';
import { JobService } from './jobs.ts';
import { createLogger } from './log.ts';
import { SERVER_VERSION } from './mcp.ts';
import { BUNDLE_FILE } from './names.ts';
import { readBundle } from './setup.ts';

const logger = createLogger();

const localToken = process.env.FLO2_RVT_LOCAL_TOKEN ?? '';
if (localToken.length < MIN_TOKEN_LENGTH) {
  logger.error('config_invalid', { setting: 'FLO2_RVT_LOCAL_TOKEN', need: `${MIN_TOKEN_LENGTH}+ characters` });
  process.exit(2);
}
const port = Number(process.env.FLO2_RVT_PORT ?? DEFAULT_PORT);
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  logger.error('config_invalid', { setting: 'FLO2_RVT_PORT' });
  process.exit(2);
}

const bundle = { path: process.env.FLO2_RVT_BUNDLE?.trim() || fileURLToPath(new URL(`../../addin/dist/${BUNDLE_FILE}`, import.meta.url)) };
const held = readBundle(bundle);
if (held.available) logger.info('bundle_ready', { sha256: held.sha256, bytes: held.bytes.length });
else logger.warn('bundle_absent', { why: held.why });

const server = createHttpServer({ localToken, jobs: new JobService({ logger, bundle }), logger });
server.listen(port, HOST, () => logger.info('listening', { host: HOST, port, path: MCP_PATH, version: SERVER_VERSION }));

const stop = (signal: string) => {
  logger.info('stopping', { signal });
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(0), 10_000).unref();
};
process.on('SIGTERM', () => stop('SIGTERM'));
process.on('SIGINT', () => stop('SIGINT'));
