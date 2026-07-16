import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AsyncPipe } from '@angular/common';
import {
  BehaviorSubject,
  Observable,
  combineLatest,
  debounceTime,
  distinctUntilChanged,
  map,
  shareReplay,
  startWith,
  switchMap,
} from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeSummaryDto } from '../../models/recipe.models';
import { CategoryFilter } from '../category-filter/category-filter';
import { RecipeSearch } from '../recipe-search/recipe-search';

/**
 * Landing view: a card grid of recipe summaries, narrowed by a category filter and an ingredient
 * search box. Both filters are server-side and combine with AND, so this component owns the single
 * query that reflects them — the two filter components are presentational and only emit.
 *
 * The filter's category options are derived once from the unfiltered list, so they stay stable
 * regardless of the current selection. All data flows through {@link RecipeService} and reaches the
 * template via the `async` pipe — no manual subscriptions.
 */
@Component({
  selector: 'app-recipe-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AsyncPipe, RouterLink, CategoryFilter, RecipeSearch],
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

  /**
   * The filters currently in effect, as `[category, term]`.
   *
   * `startWith('')` matters: without it the initial debounce would delay the very first page load by
   * the debounce window. `distinctUntilChanged` then drops the duplicate `''` that the debounce emits
   * once it settles, and suppresses no-op re-queries when a keystroke leaves the trimmed term
   * unchanged (e.g. adding a trailing space).
   *
   * `shareReplay` is load-bearing, not an optimization: every consumer must see the *same* emissions.
   * Without it each subscriber would re-run the debounce chain from its own `startWith('')`, so a
   * consumer that subscribes late (`hasFilter$`, reached only once the grid is empty) would read a
   * stale empty term and mislabel a no-match result as "no recipes yet".
   */
  private readonly filters$: Observable<[string | null, string]> = combineLatest([
    this.selectedCategory$,
    this.searchTerm$.pipe(
      debounceTime(300),
      startWith(''),
      map((term) => term.trim()),
      distinctUntilChanged(),
    ),
  ]).pipe(shareReplay({ bufferSize: 1, refCount: true }));

  /** Cards for the current filters — re-queried whenever either changes. */
  readonly recipes$: Observable<RecipeSummaryDto[]> = this.filters$.pipe(
    // switchMap, so a slow response for an earlier term can't overwrite a newer one.
    switchMap(([category, term]) => this.recipes.list(category ?? undefined, term || undefined)),
  );

  /** True when either filter is active — lets the template explain an empty result. */
  readonly hasFilter$: Observable<boolean> = this.filters$.pipe(
    map(([category, term]) => category !== null || term !== ''),
  );

  /** Filter options, unique and sorted, taken from the full (unfiltered) list. */
  readonly categories$: Observable<string[]> = this.recipes.list().pipe(
    map((summaries) => [...new Set(summaries.flatMap((r) => r.categories))].sort()),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  onCategorySelected(category: string | null): void {
    this.selectedCategory$.next(category);
  }

  onSearchTermChanged(term: string): void {
    this.searchTerm$.next(term);
  }
}
