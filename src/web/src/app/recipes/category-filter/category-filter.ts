import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Presentational category picker. It holds no data of its own: the parent passes the available
 * `categories` (derived from the loaded recipe summaries) and the currently `selected` one, and the
 * component emits `selectedChange` when the user picks one — `null` meaning "All".
 */
@Component({
  selector: 'app-category-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './category-filter.html',
  styleUrl: './category-filter.css',
})
export class CategoryFilter {
  /** Category names offered as filters, in display order. */
  readonly categories = input.required<readonly string[]>();

  /** Currently active category, or `null` for the unfiltered "All" view. */
  readonly selected = input<string | null>(null);

  /** Emits the chosen category, or `null` when the user clears the filter. */
  readonly selectedChange = output<string | null>();

  select(category: string | null): void {
    this.selectedChange.emit(category);
  }
}
