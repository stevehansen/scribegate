import { describe, it, expect, vi } from 'vitest';
import type { ReactiveControllerHost } from 'lit';
import { LoadController } from '../state/load-controller.js';
import { ApiException } from '../api/client.js';

class FakeHost implements ReactiveControllerHost {
  updates = 0;
  controllers: Array<{ hostConnected?(): void; hostDisconnected?(): void }> = [];
  addController(c: { hostConnected?(): void; hostDisconnected?(): void }): void {
    this.controllers.push(c);
  }
  removeController(): void {}
  requestUpdate(): void { this.updates++; }
  readonly updateComplete = Promise.resolve(true);
  // The two below aren't part of the published Lit interface but jsdom-side
  // implementations sometimes hit them; keep no-ops.
  addInitializer(): void {}
}

describe('LoadController', () => {
  it('autoload triggers fetcher on hostConnected and lands in success', async () => {
    const host = new FakeHost();
    const fetcher = vi.fn(() => Promise.resolve({ id: 1 }));
    const ctl = new LoadController(host, fetcher);

    expect(ctl.status).toBe('loading');
    expect(host.controllers).toHaveLength(1);

    // Simulate Lit's lifecycle.
    host.controllers[0].hostConnected!();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(ctl.status).toBe('success');
    expect(ctl.data).toEqual({ id: 1 });
    expect(ctl.error).toBeUndefined();
  });

  it('captures ApiException error message', async () => {
    const host = new FakeHost();
    const apiError = new ApiException(403, { code: 'FORBIDDEN', message: 'no.' });
    const fetcher = vi.fn(() => Promise.reject(apiError));
    const ctl = new LoadController(host, fetcher);

    await ctl.reload();

    expect(ctl.status).toBe('error');
    expect(ctl.error).toBe('no.');
  });

  it('falls back to "Failed to load." for non-ApiException rejections', async () => {
    const host = new FakeHost();
    const ctl = new LoadController(host, () => Promise.reject(new Error('boom')));

    await ctl.reload();

    expect(ctl.status).toBe('error');
    expect(ctl.error).toBe('Failed to load.');
  });

  it('reload after error transitions back through loading to success', async () => {
    const host = new FakeHost();
    let attempt = 0;
    const ctl = new LoadController(host, () => {
      attempt++;
      return attempt === 1 ? Promise.reject(new Error('x')) : Promise.resolve('ok');
    });

    await ctl.reload();
    expect(ctl.status).toBe('error');

    await ctl.reload();
    expect(ctl.status).toBe('success');
    expect(ctl.data).toBe('ok');
    expect(ctl.error).toBeUndefined();
  });

  it('autoload: false skips the initial fetch on hostConnected', async () => {
    const host = new FakeHost();
    const fetcher = vi.fn(() => Promise.resolve('x'));
    new LoadController(host, fetcher, { autoload: false });

    host.controllers[0].hostConnected!();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(fetcher).not.toHaveBeenCalled();
  });
});
