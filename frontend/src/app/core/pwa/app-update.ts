import { EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';
import { filter } from 'rxjs';

/**
 * How long after start a freshly downloaded version is applied by reloading the page,
 * provided the user has not touched the page yet (so a reload loses nothing).
 * A version found later, or after the first tap or key press, is applied on the next
 * app launch (Angular service worker default), so an open form is never reset by an update.
 */
export const STARTUP_RELOAD_WINDOW_MS = 10_000;

/** Reloads the page; extracted so tests can replace it. */
export function reloadPage(): void {
  document.location.reload();
}

/**
 * Applies new app versions shipped by the service worker (ADR-0008, "PWA").
 * Without it an installed app shows the new version only on the second launch after a deploy.
 */
export function provideAppUpdate(
  reload: () => void = reloadPage,
  now: () => number = Date.now,
): EnvironmentProviders {
  return provideAppInitializer(() => {
    const updates = inject(SwUpdate);
    if (!updates.isEnabled) {
      return;
    }

    const startedAt = now();
    let userInteracted = false;
    const markInteracted = (): void => {
      userInteracted = true;
    };
    for (const type of ['pointerdown', 'keydown'] as const) {
      document.addEventListener(type, markInteracted, { capture: true, once: true });
    }

    updates.versionUpdates
      .pipe(filter((event) => event.type === 'VERSION_READY'))
      .subscribe(() => {
        if (!userInteracted && now() - startedAt <= STARTUP_RELOAD_WINDOW_MS) {
          reload();
        }
      });

    // The cached app can no longer be served (its files were removed from the server).
    updates.unrecoverable.subscribe(() => reload());
  });
}

