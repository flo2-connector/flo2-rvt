// A stand-in for Autodesk, on loopback, for tests that must cost nothing and
// touch no network. Each answer is a RECORDED Autodesk response (fixtures/
// autodesk/*.json, each naming where it was recorded), with only the ids and
// the signed URLs swapped for this server's own. It keeps what was uploaded in
// memory and "runs" the add-in by writing the outputs a probe would.

import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

const FIXTURES = new URL('./fixtures/', import.meta.url);

export function recorded(name: string): { status: number; body: unknown } {
  const f = JSON.parse(readFileSync(new URL(`autodesk/${name}.json`, FIXTURES), 'utf8')) as { status: number; body: unknown };
  return { status: f.status, body: structuredClone(f.body) };
}

export const SAMPLE_REPORT = readFileSync(new URL('sample-report.json', FIXTURES));
/** Made-up bytes standing for the public sample model; a test never needs a real .rvt. */
export const SAMPLE_INPUT = Buffer.alloc(70_000, 'rac_basic_sample_project.rvt stand-in bytes ');
export const SAMPLE_INPUT_SHA256 = createHash('sha256').update(SAMPLE_INPUT).digest('hex');

export interface Call {
  method: string;
  path: string;
  query: Record<string, string>;
  auth: string | undefined;
  json?: unknown;
  bytes?: number;
}

interface Upload {
  bucket: string;
  key: string;
  parts: Map<number, Buffer>;
}

export interface MockOptions {
  nickname?: string;
  /** An app with no nickname: forgeapps/me answers this client id until a PATCH sets one. */
  noNicknameFor?: string;
  /** Every nickname is taken: PATCH forgeapps/me answers 409. */
  nicknameTaken?: boolean;
  /** The app already owns an AppBundle under its client id. */
  ownsDataAsClientId?: boolean;
  /** Who owns a bucket that already exists: 'me' answers 409 then details 200; 'other' answers 409 then 403. */
  existingBucket?: 'me' | 'other';
  /** The WorkItem statuses GET returns in turn; the last repeats. On reaching 'success' the outputs are written. */
  statuses?: string[];
  /** Fail the first PUT of this part number with 403, as an expired signed URL does. */
  expireFirstPutOfPart?: number;
  /** Outputs the "add-in" writes on success (default all three). */
  writes?: Array<'report.json' | 'sheets.pdf' | 'model.ifc'>;
}

export class MockAps {
  readonly calls: Call[] = [];
  readonly objects = new Map<string, { bytes: Buffer; contentType: string }>();
  readonly workItems: unknown[] = [];
  readonly appBundleUploads: Array<{ fields: string[]; fileBytes: number }> = [];
  private readonly uploads = new Map<string, Upload>();
  private readonly buckets = new Map<string, 'me' | 'other'>();
  private readonly aliases = new Set<string>();
  private readonly entities = new Set<string>();
  private statusIndex = 0;
  private nicknameSet: string | undefined;
  private expired = false;
  serviceLimits: unknown = recorded('da-servicelimits-get').body;
  private server: Server | undefined;
  base = '';
  private readonly opts: MockOptions;

  constructor(opts: MockOptions = {}) {
    this.opts = opts;
  }

  async start(): Promise<string> {
    this.server = createServer((req, res) => void this.route(req, res));
    await new Promise<void>((resolve) => this.server?.listen(0, '127.0.0.1', resolve));
    this.base = `http://127.0.0.1:${(this.server.address() as AddressInfo).port}`;
    return this.base;
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve) => this.server?.close(() => resolve()));
  }

  /** Mark an AppBundle or Activity (by path segment, e.g. "appbundles/Flo2RvtProbe") as already existing, with alias prod. */
  preexisting(kind: 'appbundles' | 'activities', id: string, alias?: string): void {
    this.entities.add(`${kind}/${id}`);
    if (alias) this.aliases.add(`${kind}/${id}/${alias}`);
  }

  private send(res: ServerResponse, status: number, body: unknown, type = 'application/json'): void {
    const text = typeof body === 'string' && type !== 'application/json' ? body : JSON.stringify(body);
    res.writeHead(status, { 'Content-Type': type });
    res.end(text);
  }

  private async read(req: IncomingMessage): Promise<Buffer> {
    const chunks: Buffer[] = [];
    for await (const c of req) chunks.push(c as Buffer);
    return Buffer.concat(chunks);
  }

  private async route(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const url = new URL(req.url ?? '/', this.base);
    const path = url.pathname;
    const body = await this.read(req);
    const call: Call = {
      method: req.method ?? 'GET',
      path,
      query: Object.fromEntries(url.searchParams),
      auth: req.headers.authorization,
      bytes: body.length,
    };
    if ((req.headers['content-type'] ?? '').startsWith('application/json') && body.length) call.json = JSON.parse(body.toString('utf8'));
    this.calls.push(call);
    const m = (re: RegExp) => re.exec(path);
    let g: RegExpExecArray | null;

    // flo2.io's short-lived download link for the input.
    if (path === '/flo2/download/input') return this.send(res, 200, SAMPLE_INPUT.toString('latin1'), 'application/octet-stream');

    // OSS
    if (req.method === 'POST' && path === '/oss/v2/buckets') {
      const key = (call.json as { bucketKey: string }).bucketKey;
      if (this.buckets.has(key) || this.opts.existingBucket) {
        if (!this.buckets.has(key)) this.buckets.set(key, this.opts.existingBucket ?? 'me');
        const r = recorded('oss-buckets-post-conflict');
        return this.send(res, r.status, r.body);
      }
      this.buckets.set(key, 'me');
      const r = recorded('oss-buckets-post');
      return this.send(res, r.status, { ...(r.body as object), bucketKey: key });
    }
    if ((g = m(/^\/oss\/v2\/buckets\/([^/]+)\/details$/))) {
      const key = decodeURIComponent(g[1] as string);
      if (this.buckets.get(key) === 'other') return this.send(res, 403, { reason: 'Access denied' });
      const r = recorded('oss-bucket-details');
      return this.send(res, r.status, { ...(r.body as object), bucketKey: key });
    }
    if ((g = m(/^\/oss\/v2\/buckets\/([^/]+)\/objects\/([^/]+)\/signeds3upload$/))) {
      const bucket = decodeURIComponent(g[1] as string);
      const key = decodeURIComponent(g[2] as string);
      if (req.method === 'GET') {
        const parts = Number(call.query.parts ?? '1');
        const first = Number(call.query.firstPart ?? '1');
        let uploadKey = call.query.uploadKey;
        if (!uploadKey) {
          uploadKey = `upload-${this.uploads.size + 1}`;
          this.uploads.set(uploadKey, { bucket, key, parts: new Map() });
        }
        const r = recorded('oss-signeds3upload-get');
        const urls = Array.from({ length: parts }, (_, i) => `${this.base}/s3/put/${uploadKey}/${first + i}?X-Amz-Signature=test`);
        return this.send(res, 200, { ...(r.body as object), uploadKey, urls });
      }
      const up = this.uploads.get((call.json as { uploadKey: string }).uploadKey);
      if (!up || up.bucket !== bucket || up.key !== key) return this.send(res, 400, { reason: 'Unknown uploadKey' });
      const ordered = [...up.parts.entries()].sort((a, b) => a[0] - b[0]).map(([, b]) => b);
      const bytes = Buffer.concat(ordered);
      this.objects.set(`${bucket}/${key}`, { bytes, contentType: String(req.headers['x-ads-meta-content-type'] ?? '') });
      const r = recorded('oss-signeds3upload-post');
      return this.send(res, r.status, { ...(r.body as object), bucketKey: bucket, objectKey: key, size: bytes.length });
    }
    if ((g = m(/^\/s3\/put\/([^/]+)\/(\d+)$/))) {
      const part = Number(g[2]);
      if (this.opts.expireFirstPutOfPart === part && !this.expired) {
        this.expired = true;
        return this.send(res, 403, '<Error><Code>AccessDenied</Code><Message>Request has expired</Message></Error>', 'application/xml');
      }
      this.uploads.get(g[1] as string)?.parts.set(part, body);
      res.writeHead(200, { ETag: '"etag"' });
      res.end();
      return;
    }
    if ((g = m(/^\/oss\/v2\/buckets\/([^/]+)\/objects\/([^/]+)\/signeds3download$/))) {
      const bucket = decodeURIComponent(g[1] as string);
      const key = decodeURIComponent(g[2] as string);
      const obj = this.objects.get(`${bucket}/${key}`);
      if (!obj) return this.send(res, 404, { reason: 'Object not found' });
      const r = recorded('oss-signeds3download-get');
      return this.send(res, 200, { ...(r.body as object), url: `${this.base}/s3/get/${encodeURIComponent(bucket)}/${encodeURIComponent(key)}?X-Amz-Signature=test`, size: obj.bytes.length });
    }
    if ((g = m(/^\/s3\/get\/([^/]+)\/([^/]+)$/))) {
      const obj = this.objects.get(`${decodeURIComponent(g[1] as string)}/${decodeURIComponent(g[2] as string)}`);
      if (!obj) return this.send(res, 404, 'NoSuchKey', 'application/xml');
      res.writeHead(200, { 'Content-Type': obj.contentType, 'Content-Length': String(obj.bytes.length) });
      res.end(obj.bytes);
      return;
    }

    // Design Automation
    if (path === '/da/us-east/v3/forgeapps/me' && req.method === 'PATCH') {
      if (this.opts.nicknameTaken) return this.send(res, 409, '"Conflict (in use by someone else)."');
      const r = recorded('da-forgeapps-patch');
      this.nicknameSet = (call.json as { nickname: string }).nickname;
      res.writeHead(r.status);
      res.end();
      return;
    }
    if (path === '/da/us-east/v3/forgeapps/me' && req.method === 'GET') {
      const r = recorded('da-forgeapps-me');
      return this.send(res, r.status, this.nicknameSet ?? this.opts.noNicknameFor ?? this.opts.nickname ?? r.body);
    }
    if (req.method === 'GET' && (path === '/da/us-east/v3/appbundles' || path === '/da/us-east/v3/activities')) {
      const kind = path.endsWith('appbundles') ? 'appbundles' : 'activities';
      // Recorded pages; the activities recording keeps only its first page, so a second page is empty.
      const page = call.query.page
        ? kind === 'appbundles'
          ? recorded('da-appbundles-get-page2').body
          : { paginationToken: null, data: [] }
        : recorded(kind === 'appbundles' ? 'da-appbundles-get-page1' : 'da-activities-get').body;
      const body = page as { data: string[] };
      if (kind === 'appbundles' && !call.query.page && this.opts.ownsDataAsClientId && this.opts.noNicknameFor) body.data.push(`${this.opts.noNicknameFor}.OlderApp+prod`);
      return this.send(res, 200, body);
    }
    if (req.method === 'POST' && path === '/da/us-east/v3/workitems') {
      this.workItems.push(call.json);
      const r = recorded('da-workitems-post');
      return this.send(res, r.status, r.body);
    }
    if ((g = m(/^\/da\/us-east\/v3\/workitems\/([^/]+)$/))) {
      const id = g[1] as string;
      const statuses = this.opts.statuses ?? ['pending', 'inprogress', 'success'];
      const status = statuses[Math.min(this.statusIndex++, statuses.length - 1)] as string;
      if (status === 'success') this.runAddin();
      const r =
        status === 'pending'
          ? recorded('da-workitems-post')
          : status === 'inprogress'
            ? recorded('da-workitems-get-inprogress')
            : recorded('da-workitems-get-success');
      return this.send(res, 200, { ...(r.body as object), id, status });
    }
    if (path === '/authentication/v2/token') {
      return this.send(res, 200, { access_token: 'registration-token-for-tests-only', token_type: 'Bearer', expires_in: 3599 });
    }
    if (path === '/da/us-east/v3/servicelimits/me') {
      if (req.method === 'PUT') {
        const limits = this.serviceLimits as { frontendLimits: Record<string, unknown> };
        const sent = call.json as { frontendLimits?: Record<string, unknown> };
        limits.frontendLimits = { ...limits.frontendLimits, ...sent.frontendLimits };
        return this.send(res, 200, sent);
      }
      return this.send(res, 200, this.serviceLimits);
    }
    if (path === '/da/us-east/v3/engines') return this.send(res, 200, recorded('da-engines-get').body);
    if ((g = m(/^\/da\/us-east\/v3\/engines\/([^/]+)$/))) {
      const id = decodeURIComponent(g[1] as string);
      const year = /\+(\d{4})$/.exec(id)?.[1] ?? '';
      return this.send(res, 200, { ...(recorded('da-engines-id-get').body as object), id, productVersion: year, description: `Revit ${year}`, version: 1 });
    }
    if ((g = m(/^\/da\/us-east\/v3\/(appbundles|activities)$/)) && req.method === 'POST') {
      const kind = g[1] as 'appbundles' | 'activities';
      const id = (call.json as { id: string }).id;
      if (this.entities.has(`${kind}/${id}`)) return this.send(res, 409, `"${kind === 'appbundles' ? 'AppBundle' : 'Activity'} '${id}' already exists."`);
      this.entities.add(`${kind}/${id}`);
      return this.send(res, 200, this.entityAnswer(kind, id, 1));
    }
    if ((g = m(/^\/da\/us-east\/v3\/(appbundles|activities)\/([^/]+)\/versions$/)) && req.method === 'POST') {
      const kind = g[1] as 'appbundles' | 'activities';
      return this.send(res, 200, this.entityAnswer(kind, g[2] as string, 2));
    }
    if ((g = m(/^\/da\/us-east\/v3\/(appbundles|activities)\/([^/]+)\/aliases$/)) && req.method === 'POST') {
      this.aliases.add(`${g[1]}/${g[2]}/${(call.json as { id: string }).id}`);
      return this.send(res, 200, call.json);
    }
    if ((g = m(/^\/da\/us-east\/v3\/(appbundles|activities)\/([^/]+)\/aliases\/([^/]+)$/)) && req.method === 'PATCH') {
      if (!this.aliases.has(`${g[1]}/${g[2]}/${g[3]}`)) {
        const r = recorded('da-alias-patch-missing');
        return this.send(res, r.status, r.body);
      }
      return this.send(res, 200, { ...(call.json as object), id: g[3] });
    }
    if (path === '/s3/appbundle' && req.method === 'POST') {
      const text = body.toString('latin1');
      const fields = [...text.matchAll(/form-data; name="([^"]+)"/g)].map((x) => x[1] as string);
      const fileStart = text.indexOf('filename=');
      this.appBundleUploads.push({ fields, fileBytes: fileStart < 0 ? 0 : body.length - fileStart });
      res.writeHead(200);
      res.end();
      return;
    }
    return this.send(res, 404, { reason: `mock has no route for ${req.method} ${path}` });
  }

  private entityAnswer(kind: 'appbundles' | 'activities', id: string, version: number): unknown {
    if (kind === 'activities') {
      const r = recorded('da-activities-post');
      return { ...(r.body as object), id: `${this.owner()}.${id}`, version };
    }
    const r = recorded(version === 1 ? 'da-appbundles-post' : 'da-appbundles-versions-post');
    const b = r.body as { uploadParameters: { endpointURL: string } };
    b.uploadParameters.endpointURL = `${this.base}/s3/appbundle`;
    return { ...b, id: `${this.owner()}.${id}`, version };
  }

  private owner(): string {
    return this.nicknameSet ?? this.opts.noNicknameFor ?? this.opts.nickname ?? (recorded('da-forgeapps-me').body as string);
  }

  /** What the add-in would leave: the outputs, written where the WorkItem said to put them. */
  private runAddin(): void {
    const wi = this.workItems.at(-1) as { arguments: Record<string, { url: string }> } | undefined;
    if (!wi) return;
    const writes = this.opts.writes ?? ['report.json', 'sheets.pdf', 'model.ifc'];
    const content: Record<string, [string, Buffer, string]> = {
      report: ['report.json', SAMPLE_REPORT, 'application/json'],
      sheets: ['sheets.pdf', Buffer.from('%PDF-1.7 made-up\n'), 'application/pdf'],
      ifc: ['model.ifc', Buffer.from('ISO-10303-21; made-up\n'), 'application/x-step'],
    };
    for (const [param, [name, bytes, type]] of Object.entries(content)) {
      if (!writes.includes(name as 'report.json')) continue;
      const urn = wi.arguments[param]?.url ?? '';
      const m = /^urn:adsk\.objects:os\.object:([^/]+)\/(.+)$/.exec(urn);
      if (m) this.objects.set(`${decodeURIComponent(m[1] as string)}/${decodeURIComponent(m[2] as string)}`, { bytes, contentType: type });
    }
  }
}
