import { useUploadEngine } from './core/upload/UploadEngine'
import DropZone from './features/landing/DropZone'
import StagingList from './features/landing/StagingList'

export default function App() {
  const hasFiles = useUploadEngine((s) => s.files.length > 0)
  return (
    <main>
      <DropZone />
      {hasFiles && <StagingList />}
    </main>
  )
}
