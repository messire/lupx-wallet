import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwUpdate, UnrecoverableStateEvent, VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import type { Mock } from 'vitest';

import { STARTUP_RELOAD_WINDOW_MS, provideAppUpdate } from './app-update';

describe('provideAppUpdate', () => {
  let versionUpdates: Subject<VersionEvent>;
  let unrecoverable: Subject<UnrecoverableStateEvent>;
  let reload: Mock<() => void>;
  let clock: number;

  const versionReady = (): VersionEvent => ({
    type: 'VERSION_READY',
    currentVersion: { hash: 'old' },
    latestVersion: { hash: 'new' },
  });

  async function init(isEnabled: boolean): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: SwUpdate, useValue: { isEnabled, versionUpdates, unrecoverable } },
        provideAppUpdate(reload, () => clock),
      ],
    });
    await TestBed.inject(ApplicationInitStatus).donePromise;
  }

  beforeEach(() => {
    versionUpdates = new Subject();
    unrecoverable = new Subject();
    reload = vi.fn<() => void>();
    clock = 1_000;
  });

  it('reloads when a new version is ready right after start', async () => {
    await init(true);
    clock += STARTUP_RELOAD_WINDOW_MS;
    versionUpdates.next(versionReady());
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does not reload when a new version arrives later, so an open form is kept', async () => {
    await init(true);
    clock += STARTUP_RELOAD_WINDOW_MS + 1;
    versionUpdates.next(versionReady());
    expect(reload).not.toHaveBeenCalled();
  });

  it('does not reload after the user has tapped or typed, even right after start', async () => {
    await init(true);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }));
    versionUpdates.next(versionReady());
    expect(reload).not.toHaveBeenCalled();
  });

  it('ignores events other than VERSION_READY', async () => {
    await init(true);
    versionUpdates.next({ type: 'VERSION_DETECTED', version: { hash: 'new' } });
    expect(reload).not.toHaveBeenCalled();
  });

  it('reloads when the cached app can no longer be served', async () => {
    await init(true);
    clock += STARTUP_RELOAD_WINDOW_MS * 10;
    unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing files' });
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does nothing when the service worker is disabled', async () => {
    await init(false);
    versionUpdates.next(versionReady());
    unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing files' });
    expect(reload).not.toHaveBeenCalled();
  });
});
