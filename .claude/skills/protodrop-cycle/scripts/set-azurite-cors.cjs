// Set Azurite CORS so the browser's direct block-blob uploads pass preflight.
// Run from src/wa.web (needs @azure/storage-blob in node_modules):
//   cp <this script> ./set-cors-tmp.cjs && node set-cors-tmp.cjs && rm set-cors-tmp.cjs
const { BlobServiceClient, StorageSharedKeyCredential } = require('@azure/storage-blob');
(async () => {
  const cred = new StorageSharedKeyCredential('devstoreaccount1',
    'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==');
  const svc = new BlobServiceClient(new URL('http://127.0.0.1:10000/devstoreaccount1'), cred);
  await svc.setProperties({ cors: [{
    allowedOrigins: '*',
    allowedMethods: 'GET,PUT,POST,HEAD',
    allowedHeaders: '*',
    exposedHeaders: '*',
    maxAgeInSeconds: 3600,
  }] });
  const after = await svc.getProperties();
  console.log('cors now:', JSON.stringify(after.cors));
})().catch((e) => { console.error(e.statusCode || e.message); process.exit(1); });
