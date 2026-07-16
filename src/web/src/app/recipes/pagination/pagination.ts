import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * One slot in the rendered page strip: a page number, or `null` for an elided run of pages.
 * Modelling the gap as `null` rather than a tagged union keeps the template's narrowing to a plain
 * `@if (slot === null)`, which the strict-template checker handles without help.
 */
type PageSlot = number | null;

/** Pages shown either side of the current one before the strip elides. */
const WINDOW = 1;

/**
 * Presentational pager. Like its siblings `CategoryFilter` and `RecipeSearch` it owns no data and
 * injects no service: the parent passes the current `page` and the total `pageCount`, and the
 * component emits `pageChange`.
 *
 * It deliberately knows nothing about *what* is being paged, nor about page size — the parent slices
 * the list and decides how many pages that makes. That is what keeps this component testable in
 * isolation and reusable for any future list.
 */
@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pagination.html',
  styleUrl: './pagination.css',
})
export class Pagination {
  /** The active page, 1-based. */
  readonly page = input.required<number>();

  /** Total number of pages. A value of 1 or less renders nothing. */
  readonly pageCount = input.required<number>();

  /** Emits the requested page. Never fires for the current page or an out-of-range one. */
  readonly pageChange = output<number>();

  /**
   * The page strip: always the first and last page, plus a window around the current one, with gaps
   * standing in for whatever is skipped. Building it from a Set means the edge cases collapse on
   * their own — near page 1 the window and the leading `1` simply overlap and de-duplicate, so there
   * is no separate branch for "current is at the start".
   */
  readonly slots = computed<PageSlot[]>(() => {
    const count = this.pageCount();
    const current = this.page();
    if (count <= 1) {
      return [];
    }

    const pages = new Set<number>([1, count]);
    for (let page = current - WINDOW; page <= current + WINDOW; page++) {
      if (page >= 1 && page <= count) {
        pages.add(page);
      }
    }

    const slots: PageSlot[] = [];
    let previous = 0;
    for (const page of [...pages].sort((a, b) => a - b)) {
      const skipped = previous === 0 ? 0 : page - previous - 1;
      if (skipped === 1) {
        // Never elide a single page: the gap would take as much room as the number it hides, so it
        // costs a click and buys nothing.
        slots.push(previous + 1);
      } else if (skipped > 1) {
        slots.push(null);
      }
      slots.push(page);
      previous = page;
    }
    return slots;
  });

  /** Bounds are guarded here so no caller can be nudged off the end of the list. */
  go(page: number): void {
    if (page < 1 || page > this.pageCount() || page === this.page()) {
      return;
    }
    this.pageChange.emit(page);
  }
}
