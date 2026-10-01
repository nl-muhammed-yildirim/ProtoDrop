import './styles/tokens.css';
import './styles/landing.css';
import { Toaster } from './core/ui/Toast';
import { DropZone } from './features/landing/DropZone';
import { StagingList } from './features/landing/StagingList';
import { LinkScreen } from './features/landing/LinkScreen';

function App() {
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
        <DropZone />
        <StagingList />
        {/* US-002-01: LinkScreen renders null until finalize succeeds (AC-002-1). */}
        <LinkScreen />
      </main>
      <Toaster />
    </div>
  );
}

export default App;
