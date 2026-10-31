// The owner's registration run and the engine list, against recorded Autodesk
// responses on the loopback stand-in. No network, no real key.

import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { MockAps } from '../../server/test/mock-aps.ts';
import { main } from '../flo2-rvt.ts';
import { activityBody } from '../lib/registration.ts';

const SECRET = 'made-up-secret-never-printed';
const ENV = { APS_CLIENT_ID: 'MadeUpClientId0123456789abcdefAB', APS_CLIENT_SECRET: SECRET };

let dir = '';
let zip = '';
before(() => {
  dir = mkdtempSync(join(tmpdir(), 'flo2-rvt-test-'));
  zip = join(dir, 'Flo2RvtProbe.bundle.zip');
  writeFileSync(zip, Buffer.from('PK made-up AppBundle bytes'));
});
after(() => rmSync(dir, { recursive: true, force: true }));

async function runCli(aps: MockAps, argv: string[], env: Record<string, string> = ENV): Promise<{ code: number; lines: string[] }> {
  const lines: string[] = [];
  const code = await main(argv, { env, out: (l) => lines.push(l), apsBaseUrl: aps.base });
  return { code, lines };
}

describe('flo2-rvt register', () => {
  test('on a new app it creates the AppBundle and the Activity, their prod aliases, and the monthly cap', async () => {
    const aps = new MockAps();
    await aps.start();
    try {
      const { code, lines } = await runCli(aps, ['register', '--bundle', zip], { ...ENV, APS_MONTHLY_HOURS: '3' });
      assert.equal(code, 0, lines.join('\n'));

      const token = aps.calls.find((c) => c.path === '/authentication/v2/token');
      assert.equal(token?.auth, `Basic ${Buffer.from(`${ENV.APS_CLIENT_ID}:${SECRET}`).toString('base64')}`);

      const paths = aps.calls.map((c) => `${c.method} ${c.path}`).filter((p) => !p.includes('/authentication/'));
      assert.deepEqual(paths, [
        'GET /da/us-east/v3/forgeapps/me',
        'POST /da/us-east/v3/appbundles',
        'POST /s3/appbundle',
        'PATCH /da/us-east/v3/appbundles/Flo2RvtProbe/aliases/prod',
        'POST /da/us-east/v3/appbundles/Flo2RvtProbe/aliases',
        'POST /da/us-east/v3/activities',
        'PATCH /da/us-east/v3/activities/Flo2RvtProbe/aliases/prod',
        'POST /da/us-east/v3/activities/Flo2RvtProbe/aliases',
        'PUT /da/us-east/v3/servicelimits/me',
        'GET /da/us-east/v3/servicelimits/me',
      ]);
      const daCalls = aps.calls.filter((c) => c.path.startsWith('/da/'));
      assert.ok(daCalls.every((c) => c.auth === 'Bearer registration-token-for-tests-only'));

      const bundle = aps.calls.find((c) => c.path === '/da/us-east/v3/appbundles');
      assert.deepEqual(bundle?.json, { id: 'Flo2RvtProbe', engine: 'Autodesk.Revit+2024', description: 'flo2-rvt read-only probe for Revit 2024' });
      const upload = aps.appBundleUploads[0];
      assert.equal(upload?.fields.at(-1), 'file', 'the file field comes last');
      assert.ok(upload?.fields.includes('policy') && upload.fields.includes('x-amz-signature'));

      const activity = aps.calls.find((c) => c.path === '/da/us-east/v3/activities');
      assert.deepEqual(activity?.json, { id: 'Flo2RvtProbe', ...activityBody('SdkTester') });
      assert.deepEqual(aps.calls.find((c) => c.method === 'PUT')?.json, { frontendLimits: { limitMonthlyProcessingTimeInHours: 3 } });

      assert.match(lines.join('\n'), /Activity SdkTester\.Flo2RvtProbe\+prod: created 1; alias prod created/);
      assert.match(lines.join('\n'), /asked for 3 h; Autodesk now reports 3 h/);
      assert.ok(!lines.join('\n').includes(SECRET));
      assert.ok(!lines.join('\n').includes('registration-token-for-tests-only'));
    } finally {
      await aps.stop();
    }
  });

  test('on an app that has them, it adds versions and moves the prod aliases', async () => {
    const aps = new MockAps();
    await aps.start();
    aps.preexisting('appbundles', 'Flo2RvtProbe', 'prod');
    aps.preexisting('activities', 'Flo2RvtProbe', 'prod');
    try {
      const { code, lines } = await runCli(aps, ['register', '--bundle', zip]);
      assert.equal(code, 0, lines.join('\n'));
      assert.ok(aps.calls.some((c) => c.path === '/da/us-east/v3/appbundles/Flo2RvtProbe/versions'));
      const version = aps.calls.find((c) => c.path === '/da/us-east/v3/activities/Flo2RvtProbe/versions');
      assert.equal((version?.json as { id?: string }).id, undefined, 'a new version carries no id');
      assert.match(lines.join('\n'), /AppBundle Flo2RvtProbe: new version 2; alias prod moved/);
      assert.equal(aps.calls.find((c) => c.method === 'PUT')?.json && (aps.calls.find((c) => c.method === 'PUT')?.json as { frontendLimits: { limitMonthlyProcessingTimeInHours: number } }).frontendLimits.limitMonthlyProcessingTimeInHours, 2, 'the default cap is 2 hours');
    } finally {
      await aps.stop();
    }
  });

  test('without the keys in the environment it stops before calling Autodesk', async () => {
    const aps = new MockAps();
    await aps.start();
    try {
      await assert.rejects(runCli(aps, ['register', '--bundle', zip], {}), /APS_CLIENT_ID and APS_CLIENT_SECRET/);
      assert.equal(aps.calls.length, 0);
    } finally {
      await aps.stop();
    }
  });

  test('the Activity runs revitcoreconsole with the AppBundle on the 2024 engine and names the four files', () => {
    const body = activityBody('SdkTester') as { commandLine: string[]; engine: string; appbundles: string[]; parameters: Record<string, { verb: string; localName: string; required: boolean }> };
    assert.equal(body.engine, 'Autodesk.Revit+2024');
    assert.deepEqual(body.appbundles, ['SdkTester.Flo2RvtProbe+prod']);
    assert.ok(!body.commandLine[0]?.includes('/i '), 'the add-in opens the model itself');
    assert.deepEqual(Object.fromEntries(Object.entries(body.parameters).map(([k, v]) => [k, [v.verb, v.localName, v.required]])), {
      rvtFile: ['get', 'input.rvt', true],
      report: ['put', 'report.json', true],
      sheets: ['put', 'sheets.pdf', false],
      ifc: ['put', 'model.ifc', false],
    });
  });
});

describe('flo2-rvt engines', () => {
  test('lists the Revit engines the API offers beside the lifecycle page dates', async () => {
    const aps = new MockAps();
    await aps.start();
    try {
      const { code, lines } = await runCli(aps, ['engines']);
      assert.equal(code, 0);
      const text = lines.join('\n');
      for (const id of ['Autodesk.Revit+2018', 'Autodesk.Revit+2019', 'Autodesk.Revit+2020', 'Autodesk.Revit+2021']) assert.ok(text.includes(id), id);
      assert.ok(!text.includes('Autodesk.AutoCAD'), 'only Revit engines');
      assert.match(text, /Autodesk\.Revit\+2021 .*March 29, 2024 .*March 29, 2026/);
      assert.match(text, /Autodesk\.Revit\+2024\s+\(not offered by the API; the policy page lists it: deprecated March 29, 2027, removed March 29, 2029\)/);
    } finally {
      await aps.stop();
    }
  });
});
