import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Presentational ingredient search box. Like its sibling `CategoryFilter` it holds no data of its own
 * and injects no service: the parent passes the current `term` and the component emits `termChange`
 * on every keystroke.
 *
 * Debouncing deliberately lives in the parent rather than here — the parent combines this term with
 * the selected category into a single query, so it owns that query's timing. Keeping this component
 * dumb is also what ensures exactly one component issues the list request.
 */
@Component({
  selector: 'app-recipe-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recipe-search.html',
  styleUrl: './recipe-search.css',
})
export class RecipeSearch {
  /** The term currently in effect. Owned by the parent. */
  readonly term = input<string>('');

  /** Emits the raw term on each keystroke; the parent debounces and trims it. */
  readonly termChange = output<string>();

  onInput(value: string): void {
    this.termChange.emit(value);
  }

  clear(): void {
    this.termChange.emit('');
  }
}
