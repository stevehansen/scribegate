import './styles/global.scss';
import './components/app-shell.js';
import { authState } from './state/auth-state.js';
import { themeManager } from './state/theme.js';

// Initialize theme before first paint
themeManager.init();

// Load user on startup if token exists
authState.loadUser();
