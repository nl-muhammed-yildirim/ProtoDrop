import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import App from './App.tsx';

describe('App smoke test', () => {
  it('renders the ProtoDrop landing', () => {
    render(<App />);
    expect(screen.getByText('ProtoDrop')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Upload files' })).toBeDefined();
  });
});
