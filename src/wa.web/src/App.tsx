import './styles/tokens.css';
import './styles/landing.css';
import { Toaster } from './core/ui/Toast';
import { DropZone } from './features/landing/DropZone';
import { StagingList } from './features/landing/StagingList';

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
      </main>
      <Toaster />
    </div>
  );
}

export default App;
