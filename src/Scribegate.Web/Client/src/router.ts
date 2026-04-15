import { Router } from '@vaadin/router';

// Import all page components
import './components/pages/sg-login-page.js';
import './components/pages/sg-register-page.js';
import './components/pages/sg-repository-list.js';
import './components/pages/sg-repository-page.js';
import './components/pages/sg-document-page.js';
import './components/pages/sg-editor-page.js';
import './components/pages/sg-history-page.js';

export function initRouter(outlet: HTMLElement) {
  const router = new Router(outlet);

  router.setRoutes([
    { path: '/login', component: 'sg-login-page' },
    { path: '/register', component: 'sg-register-page' },
    { path: '/', component: 'sg-repository-list' },
    { path: '/:slug', component: 'sg-repository-page' },
    { path: '/:slug/edit/new', component: 'sg-editor-page' },
    { path: '/:slug/edit/(.*)', component: 'sg-editor-page' },
    { path: '/:slug/history/(.*)', component: 'sg-history-page' },
    { path: '/:slug/(.*)', component: 'sg-document-page' },
  ]);

  return router;
}
