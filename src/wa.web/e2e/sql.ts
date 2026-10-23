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

// ---- T-005-04: lifecycle E2E helpers — flag override, status queries, blob ref manipulation ----

/** Convert "n" format GUID (no dashes) to standard format for SQL Server uniqueidentifier. */
function toGuidFormat(idN: string): string {
  const id = idN.replace(/-/g, '')
  return id.length === 32 ? `${id.slice(0,8)}-${id.slice(8,12)}-${id.slice(12,16)}-${id.slice(16,20)}-${id.slice(20,32)}` : idN
}

/** T-005-04: set a FeatureFlag value via direct SQL (flag override for E2E). */
export async function setFeatureFlag(key: string, value: string): Promise<void> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    await pool.request().input('key', key).input('val', value).query(
      'UPDATE [FeatureFlag] SET [Value]=@val, [UpdatedAtUtc]=GETUTCDATE() WHERE [Key]=@key'
    )
  } finally {
    await pool.close()
  }
}

/** T-005-04: reset limits.free.retentionDays and limits.free.graceDays to defaults (7 and 3). */
export async function resetRetentionFlags(): Promise<void> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    await pool.query(
      "UPDATE [FeatureFlag] SET [Value]='7', [UpdatedAtUtc]=GETUTCDATE() WHERE [Key]='limits.free.retentionDays'"
    )
    await pool.query(
      "UPDATE [FeatureFlag] SET [Value]='3', [UpdatedAtUtc]=GETUTCDATE() WHERE [Key]='limits.free.graceDays'"
    )
  } finally {
    await pool.close()
  }
}

/** T-005-04: get the current status (byte) of a transfer by ID. */
export async function getTransferStatus(transferIdN: string): Promise<number> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    const idFull = toGuidFormat(transferIdN)
    const result = await pool.request().input('id', idFull).query(
      'SELECT [Status] FROM [Transfer] WHERE [Id]=@id'
    )
    return Number(result.recordSet[0].Status)
  } finally {
    await pool.close()
  }
}

/** T-005-04: set ExpiredAtUtc to a specific ISO date on a transfer (bypass grace window). */
export async function setExpiredAtUtc(transferIdN: string, dateIso: string): Promise<void> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    const idFull = toGuidFormat(transferIdN)
    await pool.request().input('id', idFull).input('date', dateIso).query(
      'UPDATE [Transfer] SET [ExpiredAtUtc]=@date WHERE [Id]=@id'
    )
  } finally {
    await pool.close()
  }
}

/** T-005-04: set ExpiresAtUtc on a transfer (fallback if flag cache hasn't picked up retention=0). */
export async function setExpiresAtUtc(transferIdN: string, dateIso: string): Promise<void> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    const idFull = toGuidFormat(transferIdN)
    await pool.request().input('id', idFull).input('date', dateIso).query(
      'UPDATE [Transfer] SET [ExpiresAtUtc]=@date WHERE [Id]=@id'
    )
  } finally {
    await pool.close()
  }
}

/** T-005-04: get all BlobRef paths for a transfer (via FileItem join). Must be called BEFORE f-delete-transfers runs. */
export async function getBlobRefsForTransfer(transferIdN: string): Promise<string[]> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    const idFull = toGuidFormat(transferIdN)
    const result = await pool.request().input('id', idFull).query(
      'SELECT b.[BlobPath] FROM [BlobRef] b INNER JOIN [FileItem] f ON b.[Id]=f.[BlobRefId] WHERE f.[TransferId]=@id'
    )
    return (result.recordSet as Array<{ BlobPath: string }>).map(r => r.BlobPath)
  } finally {
    await pool.close()
  }
}

/** T-005-04: get BlobRef IDs for a transfer (must be called BEFORE f-delete-transfers runs, since it deletes FileItem rows). */
export async function getBlobRefIdsForTransfer(transferIdN: string): Promise<string[]> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    const idFull = toGuidFormat(transferIdN)
    const result = await pool.request().input('id', idFull).query(
      'SELECT f.[BlobRefId] FROM [FileItem] f WHERE f.[TransferId]=@id'
    )
    return (result.recordSet as Array<{ BlobRefId: string }>).map(r => r.BlobRefId)
  } finally {
    await pool.close()
  }
}

/** T-005-04: set PhysicallyDeletedAtUtc on specific BlobRefs by ID (bypass EC-005-4 buffer). Use after f-delete-transfers. */
export async function setPhysicallyDeletedByIds(blobRefIds: string[], dateIso: string): Promise<void> {
  if (blobRefIds.length === 0) return
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    for (const id of blobRefIds) {
      await pool.request().input('id', id).input('date', dateIso).query(
        'UPDATE [BlobRef] SET [PhysicallyDeletedAtUtc]=@date WHERE [Id]=@id'
      )
    }
  } finally {
    await pool.close()
  }
}

/** T-005-04: set PhysicallyDeletedAtUtc on all BlobRefs for a transfer to a past date (bypass EC-005-4 buffer). Must be called BEFORE f-delete-transfers. */
export async function setPhysicallyDeleted(transferIdN: string, dateIso: string): Promise<void> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    const idFull = toGuidFormat(transferIdN)
    await pool.request().input('id', idFull).input('date', dateIso).query(
      'UPDATE b SET [PhysicallyDeletedAtUtc]=@date FROM [BlobRef] b INNER JOIN [FileItem] f ON b.[Id]=f.[BlobRefId] WHERE f.[TransferId]=@id'
    )
  } finally {
    await pool.close()
  }
}

/** T-005-04: delete JobRun rows for a given job key (to allow re-claim in E2E). */
export async function deleteJobRuns(jobKey: string): Promise<void> {
  const pool = new mssql.ConnectionPool(SQL_SERVER_URL)
  try {
    await pool.connect()
    await pool.request().input('key', jobKey).query(
      'DELETE FROM [JobRun] WHERE [JobKey]=@key'
    )
  } finally {
    await pool.close()
  }
}
