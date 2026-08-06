import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Временная заглушка для маршрутов волн 1–3 (docs/PROGRESS.md, W0.2), чей
 * компонент фичи еще не реализован. Каждая последующая UI-задача заменяет
 * `loadComponent` своего пункта в app.routes.ts на реальный компонент —
 * этот файл при этом не редактируется.
 */
@Component({
  selector: 'app-under-construction',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="under-construction">
      <h1>{{ title() }}</h1>
      <p>Раздел в разработке.</p>
    </div>
  `,
  styles: [
    `
      .under-construction {
        padding: 2rem 0;
        color: var(--color-muted, #666);
      }

      h1 {
        margin: 0 0 0.5rem;
        font-size: 1.3rem;
        color: var(--color-secondary-text, #333);
      }
    `,
  ],
})
export class UnderConstructionComponent {
  readonly title = input('Раздел в разработке');
}
