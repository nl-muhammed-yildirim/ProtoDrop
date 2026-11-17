// One-shot Azurite CORS initializer (local dev only).
//
// The browser uploads file blocks DIRECTLY to the staging blob container, so the
// account must answer OPTIONS preflights. Azurite has no built-in CORS and its
// service properties are ephemeral (no volume), so this runs once after azurite
// boots and applies the rule. Field names/shape matter: the @azure/storage-blob
// SDK mapper expects a flat `cors` array whose values are STRINGS (not arrays) —
// see setProperties({ cors: [...] }). Wrong shape silently sends an empty <Cors/>.
const { BlobServiceClient, StorageSharedKeyCredential } = require('@azure/storage-blob');

(async () => {
  const cred = new StorageSharedKeyCredential('devstoreaccount1',
    'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==');
  const svc = new BlobServiceClient(new URL('http://host.docker.internal:10000/devstoreaccount1'), cred);
  await svc.setProperties({ cors: [{
    allowedOrigins: '*',
    allowedMethods: 'GET,PUT,POST,HEAD',
    allowedHeaders: '*',
    exposedHeaders: '*',
    maxAgeInSeconds: 3600,
  }] });
  const after = await svc.getProperties();
  console.log('azurite CORS applied:', JSON.stringify(after.cors));
})().catch((e) => { console.error('cors init failed:', e.statusCode || e.message); process.exit(1); });
