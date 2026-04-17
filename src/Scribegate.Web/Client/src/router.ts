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
import './components/pages/sg-share-page.js';
import './components/pages/sg-settings-page.js';
import './components/pages/sg-webhooks-page.js';
import './components/pages/sg-templates-page.js';

export function initRouter(outlet: HTMLElement) {
  const router = new Router(outlet);

  router.setRoutes([
    { path: '/login', component: 'sg-login-page' },
    { path: '/register', component: 'sg-register-page' },
    { path: '/admin', component: 'sg-admin-page' },
    { path: '/settings', component: 'sg-settings-page' },
    { path: '/s/:token', component: 'sg-share-page' },
    { path: '/', component: 'sg-repository-list' },
    { path: '/:owner/:slug', component: 'sg-repository-page' },
    { path: '/:owner/:slug/edit/new', component: 'sg-editor-page' },
    { path: '/:owner/:slug/edit/(.*)', component: 'sg-editor-page' },
    { path: '/:owner/:slug/history/(.*)', component: 'sg-history-page' },
    { path: '/:owner/:slug/proposals', component: 'sg-proposal-list' },
    { path: '/:owner/:slug/proposals/new', component: 'sg-proposal-create' },
    { path: '/:owner/:slug/proposals/:id', component: 'sg-proposal-page' },
    { path: '/:owner/:slug/members', component: 'sg-members-page' },
    { path: '/:owner/:slug/webhooks', component: 'sg-webhooks-page' },
    { path: '/:owner/:slug/templates', component: 'sg-templates-page' },
    { path: '/:owner/:slug/(.*)', component: 'sg-document-page' },
  ]);

  return router;
}
