import { Router } from '@vaadin/router';

// Import all page components
import './components/pages/sg-login-page.js';
import './components/pages/sg-register-page.js';
import './components/pages/sg-repository-list.js';
import './components/pages/sg-repository-page.js';
import './components/pages/sg-document-page.js';
import './components/pages/sg-editor-page.js';
import './components/pages/sg-history-page.js';
import './components/pages/sg-proposal-list.js';
import './components/pages/sg-proposal-page.js';
import './components/pages/sg-proposal-create.js';
import './components/pages/sg-members-page.js';
import './components/pages/sg-admin-page.js';

export function initRouter(outlet: HTMLElement) {
  const router = new Router(outlet);

  router.setRoutes([
    { path: '/login', component: 'sg-login-page' },
    { path: '/register', component: 'sg-register-page' },
    { path: '/admin', component: 'sg-admin-page' },
    { path: '/', component: 'sg-repository-list' },
    { path: '/:slug', component: 'sg-repository-page' },
    { path: '/:slug/edit/new', component: 'sg-editor-page' },
    { path: '/:slug/edit/(.*)', component: 'sg-editor-page' },
    { path: '/:slug/history/(.*)', component: 'sg-history-page' },
    { path: '/:slug/proposals', component: 'sg-proposal-list' },
    { path: '/:slug/proposals/new', component: 'sg-proposal-create' },
    { path: '/:slug/proposals/:id', component: 'sg-proposal-page' },
    { path: '/:slug/members', component: 'sg-members-page' },
    { path: '/:slug/(.*)', component: 'sg-document-page' },
  ]);

  return router;
}
