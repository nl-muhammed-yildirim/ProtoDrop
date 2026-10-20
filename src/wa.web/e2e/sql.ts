import { createRequire } from 'node:module'

/**
 * T-003-09 (T-014 exit check): direct SQL access to flip seeded transfers into terminal states the API cannot reach.
 * Status=2 (Expired) is set only by the T-018 expiry jobs — not implemented yet — and Status=3 (DownloadLimit) via the
 * cap path needs MaxDownloads/DownloadsCount rows a plain API seed never fills (the store's guard reads them); both
 * terminal seeds need this raw UPDATE. mssql added as devDependency per AGENT.md §7 (ADR row logged, 2026-10-19).
 * Same local stack as global-setup.ts / seed-transfer.ts (SQL Server 2022, docker-compose.local.yml credentials).
 */

// CJS interop with the driver's Node build — e2e/ sits outside tsc -b (same convention as global-setup.ts).
const require = createRequire(import.meta.url)
const mssql = require('mssql')

/** Local SQL Server (docker-compose.local.yml + launchSettings Local profile — same server as the API's string). */
const SQL_SERVER_URL = process.env.E2E_SQL_URL ?? 'server=localhost,1433;user id=sa;password=YourStrong!Passw0rd;database=wa;encrypt=false'

/**
 * T-003-09 (AC-003-3): flip a seeded transfer to Expired (Status=2). No API/worker sets Status=2 yet (T-018 will),
 * so the expired-state E2E flips it here — one idempotent UPDATE on the transfer id returned by finalize/send.
 */
export async function setTransferExpired(transferIdN: string): Promise<void> {
  await setTransferStatus(transferIdN, 2)
}

/**
 * T-003-09 (AC-003-4/5): flip a seeded transfer to any status via raw SQL.
 * @param transferIdN the transfer Id in "n" format (no dashes), as returned by the finalize/send responses.
 * @param status 0 Draft / 1 Active / 2 Expired / 3 DownloadLimit / 4 Deleted (TA-4.2#4's closed mapping).
 */
export async function setTransferStatus(transferIdN: string, status: number): Promise<void> {
  // mssql 7.x: the connection string parses in the constructor; connect() with a string is not supported.
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    // "n" format (no dashes) → standard GUID format for SQL Server uniqueidentifier conversion.
    const idN = transferIdN.replace(/-/g, '')
    const idFull = idN.length === 32 ? `${idN.slice(0,8)}-${idN.slice(8,12)}-${idN.slice(12,16)}-${idN.slice(16,20)}-${idN.slice(20,32)}` : transferIdN
    await pool.request().input('id', idFull).input('s', status).query(
      'UPDATE [Transfer] SET [Status]=@s WHERE [Id]=@id',
    )
  } finally {
    await pool.close()
  }
}

/**
 * T-003-09 (AC-003-4): a well-formed but unknown linkId for the 404-vs-410 parity assertion — any 8-char string
 * from the LinkId charset (TA-9.6: Crockford base32 minus i,l,o,u) that was never minted; no seed needed.
 */
export const UNKNOWN_LINK_ID = 'e2e7kqw2'
