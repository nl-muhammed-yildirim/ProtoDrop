import './styles/tokens.css';
import './styles/landing.css';
import { DropZone } from './features/landing/DropZone';

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
      </main>
    </div>
  );
}

export default App;
