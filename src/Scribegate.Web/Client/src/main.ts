import './styles/global.scss';
import './components/app-shell.js';
import { authState } from './state/auth-state.js';
import { themeManager } from './state/theme.js';

// Initialize theme before first paint
themeManager.init();

// Pick up an OIDC callback token before the first authenticated API call, and
// immediately scrub it from the address bar.
authState.consumeOidcCallback();

// Load user on startup if token exists
authState.loadUser();
