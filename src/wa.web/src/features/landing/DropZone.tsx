import { useEffect, useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import './../../styles/landing.css';
import { useUploadEngine } from '../../core/upload/UploadEngine';
import { showToast } from '../../core/ui/Toast';

// US-001-01: clipboard images often arrive named "image.png" or with no name — stage them
// under a deterministic name that never collides with already-staged files.
const PASTED_IMAGE_NAME = 'pasted-image.png';

function nextPastedName(takenNames: string[]): string {
  if (!takenNames.includes(PASTED_IMAGE_NAME)) return PASTED_IMAGE_NAME;
  let index = 2;
  while (takenNames.includes(`pasted-image-${index}.png`)) index += 1;
  return `pasted-image-${index}.png`;
}

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
  // REVIEW(919cb3c): dragleave fires when the pointer crosses child elements (h2, button),
  // so the highlight flickers mid-drag. Track enter/leave depth instead of resetting on leave.
  const dragDepth = useRef(0);
  const addFiles = useUploadEngine((state) => state.addFiles);

  const openPicker = () => inputRef.current?.click();

  // US-001-01: Ctrl+V anywhere on the landing page stages clipboard images.
  useEffect(() => {
    const onPaste = (event: ClipboardEvent) => {
      const source = event.clipboardData?.files;
      if (!source || source.length === 0) return;
      const takenNames = useUploadEngine.getState().files.map((file) => file.name);
      addFiles(
        Array.from(source).map((file) => {
          if (file.name && file.name !== 'image.png') return file;
          const name = nextPastedName(takenNames);
          takenNames.push(name);
          return new File([file], name, { type: file.type });
        }),
      );
    };
    document.addEventListener('paste', onPaste);
    return () => document.removeEventListener('paste', onPaste);
  }, [addFiles]);

  const handleDragEnter = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    dragDepth.current += 1;
    if (dragDepth.current > 0) setDragOver(true);
  };

 const handleDragLeave = () => {
    dragDepth.current -= 1;
    if (dragDepth.current <= 0) {
      dragDepth.current = 0;
      setDragOver(false);
    }
  };

 const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDragOver(false);
    dragDepth.current = 0; // reset counter after a drop
    const dropped = Array.from(event.dataTransfer?.files ?? []);
    if (dropped.length === 0) {
      // EC-001-4: an empty folder or unsupported payload must not disappear silently.
      showToast('info', 'Some files were skipped');
      return;
    }
    addFiles(dropped);
  };

 const onInputChange = (event: ChangeEvent<HTMLInputElement>) => {
    addFiles(Array.from(event.target.files ?? []));
    event.target.value = ''; // let the same file be picked again after removal
  };

  return (
    <div
      className="dropzone-card"
      data-dragover={dragOver}
      onDrop={onDrop}
      onDragEnter={handleDragEnter}
      onDragLeave={handleDragLeave}
      onDragOver={(event) => event.preventDefault()} // allow drop, depth counter maintains highlight
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
        onChange={onInputChange}
        style={{ display: 'none' }}
      />
    </div>
  );
}
