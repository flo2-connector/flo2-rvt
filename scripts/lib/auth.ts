// The one two-legged token the owner's registration run needs. flo2-rvt's
// server never mints a token; flo2.io's gateway does, per job.
//
// POST https://developer.api.autodesk.com/authentication/v2/token, Basic client
// credentials, grant_type=client_credentials and the scopes, space-separated.
// Adapted from getTwoLeggedToken in Autodesk's official Node SDK
// (github.com/autodesk-platform-services/aps-sdk-node, commit
// 0c1035bec2a108af26b390d3002c57f3596fe73a, authentication/source/custom-code/authenticationClient.ts).
// Copyright Autodesk, Inc. Apache License 2.0; see THIRD_PARTY_NOTICES.md. Changes: plain
// fetch in place of the SDK's axios client, and an error that never echoes the secret.

import { ApsError, apsJson, type ApsContext } from '../../server/src/aps/http.ts';

export interface Credentials {
  clientId: string;
  clientSecret: string;
}

/** Read APS_CLIENT_ID and APS_CLIENT_SECRET from the environment, never from a file. */
export function credentialsFromEnv(env: NodeJS.ProcessEnv = process.env): Credentials {
  const clientId = env.APS_CLIENT_ID?.trim();
  const clientSecret = env.APS_CLIENT_SECRET?.trim();
  if (!clientId || !clientSecret) {
    throw new Error('Set APS_CLIENT_ID and APS_CLIENT_SECRET in the environment (they are never read from a file).');
  }
  return { clientId, clientSecret };
}

export async function twoLeggedToken(creds: Credentials, scopes: string[], base: Omit<ApsContext, 'token'> = {}): Promise<string> {
  const basic = Buffer.from(`${creds.clientId}:${creds.clientSecret}`).toString('base64');
  const form = new URLSearchParams({ grant_type: 'client_credentials', scope: scopes.join(' ') });
  const { body } = await apsJson<{ access_token?: string }>(
    { ...base, token: '' },
    {
      operation: 'Authentication v2 token',
      method: 'POST',
      path: '/authentication/v2/token',
      auth: false,
      headers: { Authorization: `Basic ${basic}`, 'Content-Type': 'application/x-www-form-urlencoded', Accept: 'application/json' },
      body: form.toString(),
    },
  );
  if (!body?.access_token) throw new ApsError('Authentication v2 token', 200, 'the answer carried no access token');
  return body.access_token;
}
