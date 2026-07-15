import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AsyncPipe } from '@angular/common';
import { BehaviorSubject, Observable, map, shareReplay, switchMap } from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeSummaryDto } from '../../models/recipe.models';
import { CategoryFilter } from '../category-filter/category-filter';

/**
 * Landing view: a card grid of recipe summaries with a category filter. The displayed cards are
 * fetched server-side per selected category, while the filter's options are derived once from the
 * unfiltered list so they stay stable regardless of the current selection. All data flows through
 * {@link RecipeService} and reaches the template via the `async` pipe — no manual subscriptions.
 */
@Component({
  selector: 'app-recipe-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AsyncPipe, RouterLink, CategoryFilter],
  templateUrl: './recipe-list.html',
  styleUrl: './recipe-list.css',
})
export class RecipeList {
  private readonly recipes = inject(RecipeService);

  /** Selected category, `null` for the unfiltered view. Drives the card query. */
  private readonly selectedCategory$ = new BehaviorSubject<string | null>(null);
  readonly selected$ = this.selectedCategory$.asObservable();

  /** Cards for the current filter — re-queried whenever the selection changes. */
  readonly recipes$: Observable<RecipeSummaryDto[]> = this.selectedCategory$.pipe(
    switchMap((category) => this.recipes.list(category ?? undefined)),
  );

  /** Filter options, unique and sorted, taken from the full (unfiltered) list. */
  readonly categories$: Observable<string[]> = this.recipes.list().pipe(
    map((summaries) => [...new Set(summaries.flatMap((r) => r.categories))].sort()),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  onCategorySelected(category: string | null): void {
    this.selectedCategory$.next(category);
  }
}
