import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { boxReset } from '../../styles/shared.js';

const MINUTE = 60;
const HOUR = 3600;
const DAY = 86400;

function parseUtc(dateStr: string): Date {
  // Backend returns UTC timestamps without 'Z' suffix — ensure correct parsing
  if (!dateStr.endsWith('Z') && !dateStr.includes('+') && !dateStr.includes('-', 10)) {
    return new Date(dateStr + 'Z');
  }
  return new Date(dateStr);
}

function timeAgo(dateStr: string): string {
  const date = parseUtc(dateStr);
  const now = new Date();
  const seconds = Math.floor((now.getTime() - date.getTime()) / 1000);

  if (seconds < MINUTE) return 'just now';
  if (seconds < HOUR) {
    const m = Math.floor(seconds / MINUTE);
    return `${m}m ago`;
  }
  if (seconds < DAY) {
    const h = Math.floor(seconds / HOUR);
    return `${h}h ago`;
  }
  const d = Math.floor(seconds / DAY);
  if (d < 30) return `${d}d ago`;

  return date.toLocaleDateString();
}

@customElement('sg-time-ago')
export class SgTimeAgo extends LitElement {
  static styles = [boxReset, css`
    :host { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); }
  `];

  @property() datetime = '';

  render() {
    if (!this.datetime) return html``;
    const full = parseUtc(this.datetime).toLocaleString();
    return html`<time datetime=${this.datetime} title=${full}>${timeAgo(this.datetime)}</time>`;
  }
}
