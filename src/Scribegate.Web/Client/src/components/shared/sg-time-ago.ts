import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';

const MINUTE = 60;
const HOUR = 3600;
const DAY = 86400;

function timeAgo(dateStr: string): string {
  const date = new Date(dateStr);
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
  static styles = css`
    :host { font-size: 0.75rem; color: #6c757d; }
  `;

  @property() datetime = '';

  render() {
    if (!this.datetime) return html``;
    const full = new Date(this.datetime).toLocaleString();
    return html`<time datetime=${this.datetime} title=${full}>${timeAgo(this.datetime)}</time>`;
  }
}
