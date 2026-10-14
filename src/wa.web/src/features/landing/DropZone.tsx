import { useEffect, useRef, useState } from 'react'
import type { DragEvent as ReactDragEvent, FormEvent } from 'react'

import { useUploadEngine } from '../../core/upload/UploadEngine'

interface Entry { name: string; size: number; file?: File }

/**
 * Boundary shape for drop sources. lib.dom's DataTransfer satisfies it directly
 * (FileList is ArrayLike<File> via its index signature), so the handler passes
 * `dt` with no cast. `entries` is Chromium-only and absent from lib.dom — plain
 * file drops have none; folder drops carry FileSystemEntry objects whose file
 * contents are already flattened into `files`.
 */
interface DropSource {
  files: ArrayLike<File>
  entries?: ArrayLike<unknown>
}

/** Stage every dropped file; empty folders (entries with no files) count as skipped. */
export function collectFiles(source: DropSource): { entries: Entry[]; skipped: number } {
  const staged: Entry[] = []
  for (let i = 0; i < source.files.length; i++) {
    const file = source.files[i]
    staged.push({ name: file.name, size: file.size, file })
  }
  // AC-001-4 signal: folder drop with no files inside → entries present but zero files.
  const skipped = staged.length ? 0 : (source.entries?.length ?? 0)
  return { entries: staged, skipped }
}

/** Boundary shape for clipboard slots; lib.dom's DataTransferItem satisfies it (single unchecked cast at the read below). */
interface ClipboardSlotLike { type?: string; getAsFile?: () => File | null }

/** Pasted image → one entry named `pasted-image.png`; non-image or missing slot → empty. */
export function pastedImages(items: ArrayLike<unknown>): Entry[] {
  const item = items[0] as ClipboardSlotLike // unchecked: well-known DOM shape (DataTransferItem), slot may be undefined
  if (!item || typeof item.type !== 'string' || !item.type.startsWith('image/')) return []
  if (typeof item.getAsFile !== 'function') return []
  const file = item.getAsFile()
  return file ? [{ name: 'pasted-image.png', size: file.size, file }] : []
}

export function stageFiles(entries: Array<Entry>): void {
  if (!entries.length) return
  useUploadEngine.getState().addFiles(entries)
}

type Toast = string | null

/** Window paste event as browsers actually emit it; lib.dom's ClipboardEvent omits the key modifiers. */
interface PasteEventLike { ctrlKey?: boolean; metaKey?: boolean; clipboardData?: DataTransfer | null }

const SKIP_TOAST = 'Some files were skipped'
const PASTE_TOAST = 'Image added'
const EMPTY_ITEMS: unknown[] = []

export function DropZone() {
  const [dragOver, setDragOver] = useState(false)
  const [toast, setToast] = useState<Toast>(null)
  const timerRef = useRef<number | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    function onPaste(e: Event): void {
      // lib.dom's ClipboardEvent omits modifier keys; every browser puts them on the event object.
      const pe = e as PasteEventLike // unchecked: window paste event, well-known DOM shape (ClipboardEvent + key modifiers)
      if (!pe.ctrlKey && !pe.metaKey) return
      const staged = pastedImages(pe.clipboardData?.items ?? EMPTY_ITEMS)
      stageFiles(staged)
      if (staged.length) setToast(PASTE_TOAST) // a non-image paste must not clear another visible toast
    }
    window.addEventListener('paste', onPaste)
    return () => { window.removeEventListener('paste', onPaste) }
  }, [])

  useEffect(() => {
    if (!toast) return
    timerRef.current = window.setTimeout(() => setToast(null), 5000)
    return () => {
      if (timerRef.current !== null) window.clearTimeout(timerRef.current)
    }
  }, [toast])

  function drop(e: ReactDragEvent<HTMLElement>): void {
    e.preventDefault()
    setDragOver(false)
    const dt = e.dataTransfer
    if (!dt) return
    const result = collectFiles(dt) // DataTransfer is structurally a DropSource (no cast).
    stageFiles(result.entries)
    if (result.skipped > 0) setToast(SKIP_TOAST)
  }

  function browse(e: FormEvent<HTMLInputElement>): void {
    const files = e.currentTarget.files ?? []
    stageFiles(Array.from(files)) // FileList entries satisfy Entry via name/size/file.
  }

  return (
    <div
      className={`dz${dragOver ? ' dz-dragover' : ''}`}
      role="button"
      aria-label="Upload files"
      onClick={() => fileInputRef.current?.click()}
      onDragEnter={(e) => { if (e.dataTransfer) setDragOver(true) }}
      onDragLeave={() => setDragOver(false)}
      onDrop={drop}
    >
      <div className="dz-icon" aria-hidden="true">
        {/* upload icon: arrow into tray */}
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 3v12" />
          <path d="m7 8 5-5 5 5" />
          <path d="M4 15h3l1.5 4H9.5L8 15H4Z" />
          <path d="M4 20h16" />
        </svg>
      </div>
      <h1 className="dz-title">Send your files.</h1>
      <p className="dz-secondary">or choose files</p>
      <button
        type="button"
        className="btn-primary dz-button"
        onClick={(e) => { e.stopPropagation(); fileInputRef.current?.click() }}
      >Choose files</button>
      <input
        ref={fileInputRef}
        type="file"
        multiple
        aria-label="Upload files"
        onChange={browse}
        onClick={(e) => e.stopPropagation()} // programmatic .click() bubbles; stop it from re-entering the zone handler
      />
      {toast && (
        <div className="dz-toast" role="status">
          {toast}
        </div>
      )}
    </div>
  )
}

export default DropZone
