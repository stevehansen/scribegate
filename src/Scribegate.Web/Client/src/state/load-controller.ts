import type { ReactiveController, ReactiveControllerHost } from 'lit';
import { ApiException } from '../api/client.js';

export type LoadStatus = 'loading' | 'success' | 'error';

/**
 * Per-page load-state encapsulation. Owns the loading/success/error state
 * machine and the requestUpdate plumbing for one async fetch. No shared cache,
 * no dedup, no cross-component invalidation — pages call `reload()` explicitly
 * after a mutation to refresh exactly the controllers they own. See RFC #8.
 */
export class LoadController<T> implements ReactiveController {
  status: LoadStatus = 'loading';
  data: T | undefined;
  error: string | undefined;

  constructor(
    private readonly host: ReactiveControllerHost,
    private readonly fetcher: () => Promise<T>,
    private readonly options: { autoload?: boolean } = {},
  ) {
    host.addController(this);
  }

  hostConnected(): void {
    if (this.options.autoload !== false) {
      void this.reload();
    }
  }

  async reload(): Promise<void> {
    this.status = 'loading';
    this.error = undefined;
    this.host.requestUpdate();

    try {
      this.data = await this.fetcher();
      this.status = 'success';
    } catch (e) {
      this.error = e instanceof ApiException ? e.error.message : 'Failed to load.';
      this.status = 'error';
    }

    this.host.requestUpdate();
  }
}
