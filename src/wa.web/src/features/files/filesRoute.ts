/**
 * T-009-05 (UI-Reference §5.4): pure /files path matcher — App reads
 * window.location.pathname and passes a boolean by prop, so FilesPage stays free of
 * any window dependency (same convention as profileRoute.ts). Exact match only: the My
 * Files screen owns exactly this path; the landing DropZone keeps catching / and the
 * account/link routes keep their own predicates.
 */

export const FILES_PATH = '/files'

export function matchFilesPath(pathname: string): boolean {
  return pathname === FILES_PATH
}
