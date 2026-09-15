import { useEffect, useRef, useState } from 'react';
import './styles/tokens.css';
import './styles/landing.css';
import { DropZone } from './features/landing/DropZone';
import {
  buildSendPayload,
  canSend,
  formatBytes,
  removeStagedFiles,
  stageFile,
  stagePastedFiles,
  stageSummaryText,
  type SendItem,
  type StagedFile,
} from './core/upload/fileStaging';

/** jsdom-friendly: read clipboard files off any Event without requiring a real ClipboardEvent. */
function pastedImageFiles(rawEvent: Event): File[] {
  const list =
    (rawEvent as Partial<Pick<ClipboardEvent, 'clipboardData'>>).clipboardData
      ?.files;
  return list
    ? Array.from(list).filter(file => file.type.startsWith('image/'))
    : [];
}

/**
 * Landing page (UI-Reference §3). T-031 / US-001-01: drop, picker, and Ctrl+V paste
 * all feed one shared staging list; total line shows "N files · X" per UI §5.1.
 *
 * T-032 / US-001-02: the list stays editable until "Send." — afterwards it locks into
 * upload state (edge case 2) so adds/removes/drops/paste are ignored; Send freezes the
 * selection-ordered payload (duplicate names preserved, AC #2) for T-034's engine.
 */
function App() {
  const [stagedFiles, setStagedFiles] = useState<StagedFile[]>([]);
  // Post-"Send." flag drives the lock; refs mirror it for the `[]`-deps handlers
  // and hold the ordered payload frozen at send time (duplicates preserved, AC #2).
  const [sent, setSent] = useState(false);
  const sentRef = useRef(false);
  const sendPayloadRef = useRef<SendItem[]>([]);

  /** Shared selection entry point; duplicate `File`s are kept once; locked post-"Send." */
  const addFiles = (incoming: File[]) => {
    if (sent || incoming.length === 0) return;
    setStagedFiles(previous => {
      const known = new Set<File>(previous.map(entry => entry.file));
      const additions = incoming.flatMap(file =>
        known.has(file) ? [] : [stageFile(file)],
      );
      return additions.length > 0 ? [...previous, ...additions] : previous;
    });
  };

  useEffect(() => {
    const onWindowPaste = (event: Event) => {
      if (sentRef.current) return; // locked list post-"Send." (US-001-02 edge case 2)
      const images = pastedImageFiles(event);
      if (images.length === 0) return;
      setStagedFiles(previous => [
        ...previous,
        ...stagePastedFiles(images, previous.map(entry => entry.name)),
      ]);
    };
    window.addEventListener('paste', onWindowPaste);
    return () => window.removeEventListener('paste', onWindowPaste);
  }, []);

  const removeFile = (id: string) => {
    if (sent) return;
    setStagedFiles(previous => removeStagedFiles(previous, id));
  };

  /** "Send.": capture the selection-ordered payload (duplicates preserved), then lock. */
  const handleSend = () => {
    if (!canSend(stagedFiles, sent)) return;
    sendPayloadRef.current = buildSendPayload(stagedFiles);
    sentRef.current = true;
    setSent(true);
  };

  const totalLine = stageSummaryText(stagedFiles);

  return (
    <div className="landing">
      <header className="topbar">
        <span className="topbar-brand">ProtoDrop</span>
        <div className="topbar-actions">
          <button type="button" className="btn btn-ghost">
            Sign in
          </button>
        </div>
      </header>
      <main className="dropzone">
        <DropZone onFilesSelected={addFiles} locked={sent} />

        {stagedFiles.length > 0 && (
          <section className="staging-panel" aria-label="Selected files">
            <ul className="staging-list">
              {stagedFiles.map(entry => (
                <li key={entry.id} className="file-row">
                  <span className="file-name" title={entry.name}>
                    {entry.name}
                  </span>
                  <span className="file-size">
                    {formatBytes(entry.size)}
                  </span>
                  <button
                    type="button"
                    className="file-remove"
                    aria-label={`Remove ${entry.name}`}
                    disabled={sent}
                    onClick={() => removeFile(entry.id)}
                  >
                    ✕
                  </button>
                </li>
              ))}
            </ul>

            <div className="staging-total">{totalLine}</div>

            <button
              type="button"
              className="btn btn-primary send-btn"
              onClick={handleSend}
              disabled={!canSend(stagedFiles, sent)}
            >
              Send.
            </button>
          </section>
        )}
      </main>
    </div>
  );
}

export default App;
