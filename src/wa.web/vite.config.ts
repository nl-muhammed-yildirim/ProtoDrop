import { fileURLToPath } from 'node:url'

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'
// Exact-match ids: the imports in these packages are always bare package names or these subpaths.
const AZURE_BROWSER_ALIASES: Array<[string, string]> = [
  ['@typespec/ts-http-runtime/internal/policies', '@typespec/ts-http-runtime/dist/browser/policies/internal.js'],
  ['@typespec/ts-http-runtime/internal/util', '@typespec/ts-http-runtime/dist/browser/util/internal.js'],
  ['@typespec/ts-http-runtime/internal/logger', '@typespec/ts-http-runtime/dist/browser/logger/internal.js'],
  ['@azure/storage-blob', '@azure/storage-blob/dist/browser/index.js'],
  ['@azure/core-rest-pipeline', '@azure/core-rest-pipeline/dist/browser/index.js'],
  ['@azure/storage-common', '@azure/storage-common/dist/browser/index.js'],
  ['@typespec/ts-http-runtime', '@typespec/ts-http-runtime/dist/browser/index.js'],
]

// The @azure/* packages ship node/browser builds selected by the "node"/"browser" export condition.
// Vite's browser build already prefers the browser build; aliasing pins it explicitly so Vitest
// (which runs in Node) resolves the fetch-based transports — the same code path as the real app.
function azureBrowserAliases(): Record<string, string> {
  const aliases: Record<string, string> = {}
  for (const [find, target] of AZURE_BROWSER_ALIASES) {
    aliases[find] = fileURLToPath(new URL(`./node_modules/${target}`, import.meta.url))
  }
  return aliases
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
  },
  resolve: {
    alias: azureBrowserAliases(),
  },
  test: {
    server: {
      deps: {
        // Let Vite (and the aliases above) process these packages in tests instead of plain Node
        // import, which resolves the "node" export condition and picks transports rejecting Blob bodies.
        inline: [/@azure\/storage-blob/, /@azure\/core-rest-pipeline/, /@azure\/storage-common/, /@typespec\/ts-http-runtime/],
      },
    },
  },
})
