import { useEffect, useState } from 'react';
import './styles/tokens.css';
import './styles/landing.css';
import { DropZone } from './features/landing/DropZone';
import {
  formatBytes,
  stageFile,
  stagePastedFiles,
  stageSummaryText,
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
 * Files remain staged here — upload starts later with the Send action (T-032).
 */
function App() {
  const [stagedFiles, setStagedFiles] = useState<StagedFile[]>([]);

  /** Shared selection entry point; duplicate `File`s are kept once. */
  const addFiles = (incoming: File[]) => {
    if (incoming.length === 0) return;
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
    setStagedFiles(previous => previous.filter(entry => entry.id !== id));
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
        <DropZone onFilesSelected={addFiles} />

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
                    onClick={() => removeFile(entry.id)}
                  >
                    ✕
                  </button>
                </li>
              ))}
            </ul>

            <div className="staging-total">{totalLine}</div>

            <button type="button" className="btn btn-primary send-btn">
              Send.
            </button>
          </section>
        )}
      </main>
    </div>
  );
}

export default App;
