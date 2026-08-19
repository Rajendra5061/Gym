import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { initTheme } from '@/lib/theme';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import './styles/base.css';

initTheme();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {/* Opt in to the two React Router v7 behaviours this app already relies on, which also
        clears the deprecation warnings the console printed on every page. v7_startTransition
        routes navigation through React 18 transitions; v7_relativeSplatPath only affects
        relative links nested under a splat route, and the single "*" route here just
        redirects to "/", so neither changes what this app renders. */}
    <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
