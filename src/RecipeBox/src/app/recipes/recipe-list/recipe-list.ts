import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AsyncPipe } from '@angular/common';
import {
  BehaviorSubject,
  Observable,
  Subject,
  catchError,
  combineLatest,
  debounceTime,
  distinctUntilChanged,
  map,
  merge,
  of,
  shareReplay,
  startWith,
  switchMap,
  take,
} from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeSummaryDto } from '../../models/recipe.models';
import { CategoryFilter } from '../category-filter/category-filter';
import { RecipeSearch } from '../recipe-search/recipe-search';
import { Pagination } from '../pagination/pagination';

/** Recipes per page. Exported so the spec asserts against the real value rather than a copy. */
export const PAGE_SIZE = 12;

/** The outcome of the list request, before paging is applied. */
type ListState =
  | { status: 'loading' }
  | { status: 'loaded'; recipes: RecipeSummaryDto[] }
  | { status: 'error' };

/**
 * Everything the template renders, as one value. Folding the request state, the filter state and the
 * current page's slice into a single discriminated union means the template needs exactly one `async`
 * pipe and cannot render an inconsistent combination — e.g. an "empty" message next to a pager.
 */
export type ListView =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'empty'; hasFilter: boolean }
  | {
      status: 'loaded';
      recipes: RecipeSummaryDto[];
      page: number;
      pageCount: number;
      total: number;
      /** 1-based index of the first card on this page, for the "Showing X–Y of Z" announcement. */
      from: number;
      /** 1-based index of the last card on this page. */
      to: number;
    };

/**
 * Landing view: a paged card grid of recipe summaries, narrowed by a category filter and an
 * ingredient search box. Both filters are server-side and combine with AND, so this component owns
 * the single query that reflects them — the filter, search and pager components are presentational
 * and only emit.
 *
 * Paging is client-side: the API returns the whole filtered list (`GET api/recipes` takes no
 * skip/take), so this component slices it. The pager is fed `pageCount` rather than a page size,
 * which is what keeps the move to server-side paging — should the collection ever outgrow this —
 * contained to this file and the service.
 *
 * All data flows through {@link RecipeService} and reaches the template via the `async` pipe.
 */
@Component({
  selector: 'app-recipe-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AsyncPipe, RouterLink, CategoryFilter, RecipeSearch, Pagination],
  templateUrl: './recipe-list.html',
  styleUrl: './recipe-list.css',
})
export class RecipeList {
  private readonly recipes = inject(RecipeService);

  /** Selected category, `null` for "any". */
  private readonly selectedCategory$ = new BehaviorSubject<string | null>(null);
  readonly selected$ = this.selectedCategory$.asObservable();

  /** Raw ingredient term, updated on every keystroke. */
  private readonly searchTerm$ = new BehaviorSubject<string>('');
  readonly term$ = this.searchTerm$.asObservable();

  /** Pages the user has explicitly asked for. Merged with filter-driven resets into {@link page$}. */
  private readonly pageRequest$ = new Subject<number>();

  /** Fires when the user retries a failed load. */
  private readonly retry$ = new Subject<void>();

  /**
   * The filters currently in effect, as `[category, term]`.
   *
   * `startWith('')` matters: without it the initial debounce would delay the very first page load by
   * the debounce window. `distinctUntilChanged` then drops the duplicate `''` that the debounce emits
   * once it settles, and suppresses no-op re-queries when a keystroke leaves the trimmed term
   * unchanged (e.g. adding a trailing space).
   *
   * `shareReplay` is load-bearing, not an optimization: `state$` and `page$` both subscribe, and each
   * must see the *same* emissions. Without it every subscriber would re-run the debounce chain from
   * its own `startWith('')`, and a page reset could be driven by a different filter emission than the
   * request it is supposed to accompany.
   */
  private readonly filters$: Observable<[string | null, string]> = combineLatest([
    // distinctUntilChanged here mirrors the term branch below: the chips re-emit the category on
    // every click, including a click on the already-active one, which would otherwise re-issue the
    // request and bounce the user back to page 1 for no change in what they are looking at.
    this.selectedCategory$.pipe(distinctUntilChanged()),
    this.searchTerm$.pipe(
      debounceTime(300),
      startWith(''),
      map((term) => term.trim()),
      distinctUntilChanged(),
    ),
  ]).pipe(shareReplay({ bufferSize: 1, refCount: true }));

  /**
   * The list request for the current filters, as a state machine.
   *
   * Both `catchError` and `startWith` sit *inside* the `switchMap`, and that placement is the whole
   * point. Inside, they apply per request: each re-query shows its own loading state, and a failure
   * ends that request only. Hoisted outside, `catchError` would terminate the outer stream on the
   * first failure — the component would render its error state and then never respond to a filter
   * change again, because the subscription that feeds it would already be dead.
   */
  private readonly state$: Observable<ListState> = merge(
    this.filters$,
    // take(1) is not a tidy-up, it's the fix for a duplicate-request bug: filters$ is long-lived, so
    // without it each retry would leave another live subscription behind and every later filter
    // change would fan out into one request per retry the user had ever pressed.
    this.retry$.pipe(switchMap(() => this.filters$.pipe(take(1)))),
  ).pipe(
    // switchMap, so a slow response for an earlier term can't overwrite a newer one.
    switchMap(([category, term]) =>
      this.recipes.list(category ?? undefined, term || undefined).pipe(
        map((recipes): ListState => ({ status: 'loaded', recipes })),
        catchError(() => of<ListState>({ status: 'error' })),
        startWith<ListState>({ status: 'loading' }),
      ),
    ),
    shareReplay({ bufferSize: 1, refCount: true }),
  );

  /**
   * The current page.
   *
   * Any filter change resets it to 1, and that reset is *derived from the filter stream* rather than
   * pushed as a side effect from the change handlers. It has to be: without the reset, narrowing the
   * filters while on page 4 of 5 leaves the user staring at a blank grid, because the new result set
   * has only one page and the slice for page 4 is empty.
   */
  private readonly page$: Observable<number> = merge(
    this.filters$.pipe(map(() => 1)),
    this.pageRequest$,
  ).pipe(startWith(1), distinctUntilChanged());

  /** True when either filter is active — lets the template explain an empty result. */
  private readonly hasFilter$: Observable<boolean> = this.filters$.pipe(
    map(([category, term]) => category !== null || term !== ''),
  );

  /**
   * A card image's address. Delegated to the service rather than composed here: the API publishes
   * `hasImage`, not a URL, and knowing where the recipes resource lives is the service's job — a
   * template building the path itself would be a second place that knows the API's shape.
   */
  protected imageUrl(id: number): string {
    return this.recipes.imageUrl(id);
  }

  /** The single value the template renders. */
  readonly view$: Observable<ListView> = combineLatest([
    this.state$,
    this.page$,
    this.hasFilter$,
  ]).pipe(
    map(([state, page, hasFilter]): ListView => {
      if (state.status !== 'loaded') {
        return state;
      }

      const total = state.recipes.length;
      if (total === 0) {
        return { status: 'empty', hasFilter };
      }

      const pageCount = Math.ceil(total / PAGE_SIZE);
      // Clamp rather than trust: page$ and state$ settle independently, so for one tick after a
      // filter change the page can still be the old one. Clamping means that tick renders the last
      // real page instead of an empty slice.
      const current = Math.min(Math.max(page, 1), pageCount);
      const start = (current - 1) * PAGE_SIZE;
      const slice = state.recipes.slice(start, start + PAGE_SIZE);

      return {
        status: 'loaded',
        recipes: slice,
        page: current,
        pageCount,
        total,
        from: start + 1,
        to: start + slice.length,
      };
    }),
  );

  /**
   * Filter options, unique and sorted, taken from the full (unfiltered) list so they stay stable
   * regardless of the current selection. A failure here is deliberately swallowed to an empty list:
   * the filter is an enhancement, and losing it must not take down the grid beside it.
   */
  readonly categories$: Observable<string[]> = this.recipes.list().pipe(
    map((summaries) => [...new Set(summaries.flatMap((r) => r.categories))].sort()),
    catchError(() => of<string[]>([])),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  onCategorySelected(category: string | null): void {
    this.selectedCategory$.next(category);
  }

  onSearchTermChanged(term: string): void {
    this.searchTerm$.next(term);
  }

  onPageChange(page: number): void {
    this.pageRequest$.next(page);
  }

  onRetry(): void {
    this.retry$.next();
  }
}
