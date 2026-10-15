import { createRequire } from 'node:module';

/**
 * T-001-09: Azurite (compose service, no config file) ships with CORS off — a browser PUT to :10000 is a
 * cross-origin call that fails the preflight. Set the one rule the web needs via Set Service Properties
 * (?comp=properties&restype=service), then read it back and fail fast if it didn't stick. The SDK credential
 * (StorageSharedKeyCredential) authenticates against real Azurite 3.37.0 — no hand-rolled STS here; the
 * downgraded @azure/storage-blob emits x-ms-version 2024-08-04, which this Azurite accepts (ADR row).
 */

const require = createRequire(import.meta.url);
// CJS interop with the SDK's Node build — NodeRequire's call signature is untyped here; e2e/ sits outside tsc -b.
const s = require('@azure/storage-blob');

const ACCOUNT_KEY_B64 = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==';
const ACCOUNT_NAME = 'devstoreaccount1';
// Same account URL the API mints against (Wa:Blob:AccountUrl, Local profile).
const ACCOUNT_URL = 'http://127.0.0.1:10000/devstoreaccount1';

interface CorsRuleLike { allowedOrigins?: unknown }

export default async function globalSetup(): Promise<void> {
  const svc = new s.BlobServiceClient(ACCOUNT_URL, new s.StorageSharedKeyCredential(ACCOUNT_NAME, ACCOUNT_KEY_B64));

  await svc.setProperties({
    cors: [{
      allowedOrigins: 'http://localhost:5173', // the Vite origin (playwright.config baseURL).
      allowedMethods: 'GET,PUT,POST,DELETE',
      allowedHeaders: '*',
      exposedHeaders: '*',
      maxAgeInSeconds: 300,
    }],
  });

  const props = await svc.getProperties();
  const rules = Array.isArray(props.cors) ? (props.cors as CorsRuleLike[]) : [];
  if (!rules.some((r) => typeof r.allowedOrigins === 'string' && r.allowedOrigins.includes('http://localhost:5173'))) {
    throw new Error(`Azurite CORS rule for http://localhost:5173 not set — rules read back: ${JSON.stringify(rules)}`);
  }
}
