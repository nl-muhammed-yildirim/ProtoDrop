import { useRef, useState, type DragEvent } from 'react';
import './../../styles/landing.css';

const uploadIcon = (
  <svg
    className="dropzone-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <path d="M12 16V4m0 0L7 9m5-5 5 5" />
    <path d="M4 15v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3" />
  </svg>
);

export function DropZone() {
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const openPicker = () => inputRef.current?.click();

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragOver(false);
    // Files arrive here once the UploadEngine (T-004+) takes over staging.
  };

  return (
    <div
      className="dropzone-card"
      data-dragover={dragOver}
      onDrop={onDrop}
      onDragOver={(event) => {
        event.preventDefault();
        setDragOver(true);
      }}
      onDragLeave={() => setDragOver(false)}
      onClick={openPicker}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          openPicker();
        }
      }}
      role="button"
      tabIndex={0}
      aria-label="Upload files"
    >
      {uploadIcon}
      <h2 className="dropzone-title">Send your files.</h2>
      <p className="dropzone-hint">Drag and drop, or choose files</p>
      <button
        type="button"
        className="btn btn-primary"
        onClick={(event) => {
          event.stopPropagation();
          openPicker();
        }}
      >
        Choose files
      </button>
      <input
        ref={inputRef}
        type="file"
        multiple
        aria-label="Choose files"
        onChange={() => undefined}
        style={{ display: 'none' }}
      />
    </div>
  );
}
