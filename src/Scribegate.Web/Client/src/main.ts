import './styles/global.scss';
import './components/app-shell.js';
import { authState } from './state/auth-state.js';

// Load user on startup if token exists
authState.loadUser();
