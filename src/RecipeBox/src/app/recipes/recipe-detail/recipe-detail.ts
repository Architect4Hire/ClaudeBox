import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AsyncPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subject, catchError, map, merge, of, startWith, switchMap, take } from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeDetailDto } from '../../models/recipe.models';

/**
 * The four things that can be true of the load.
 *
 * `not-found` and `error` are deliberately separate. They mean opposite things to the user: one says
 * "this recipe doesn't exist, go back", the other says "the recipe may well exist, we just couldn't
 * reach it — try again". Collapsing a 500 or a dropped connection into "Recipe not found" tells the
 * user something false and hides the retry that would actually help them.
 */
type DetailState =
  | { status: 'loading' }
  | { status: 'loaded'; recipe: RecipeDetailDto }
  | { status: 'not-found' }
  | { status: 'error' };

/**
 * Shows one recipe — its header, ingredients, and ordered steps — resolved from the `:id` route
 * param through {@link RecipeService}. The stream is consumed with the `async` pipe.
 */
@Component({
  selector: 'app-recipe-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AsyncPipe, RouterLink],
  templateUrl: './recipe-detail.html',
  styleUrl: './recipe-detail.css',
})
export class RecipeDetail {
  private readonly recipes = inject(RecipeService);
  private readonly route = inject(ActivatedRoute);

  /** Fires when the user retries after a failed load. */
  private readonly retry$ = new Subject<void>();

  readonly state$: Observable<DetailState> = merge(
    this.route.paramMap,
    // take(1) so a retry re-reads the current id once, instead of leaving a live paramMap
    // subscription behind that would re-fire on every later navigation.
    this.retry$.pipe(switchMap(() => this.route.paramMap.pipe(take(1)))),
  ).pipe(
    switchMap((params) =>
      this.recipes.getById(Number(params.get('id'))).pipe(
        map((recipe): DetailState => ({ status: 'loaded', recipe })),
        // Only a 404 means the recipe isn't there. Everything else — 500, timeout, offline — is an
        // error the user can retry.
        catchError((err: HttpErrorResponse) =>
          of<DetailState>(err?.status === 404 ? { status: 'not-found' } : { status: 'error' }),
        ),
        startWith<DetailState>({ status: 'loading' }),
      ),
    ),
  );

  /**
   * The banner image's address. Delegated to the service, which owns where the recipes resource
   * lives — the API publishes `hasImage`, not a URL.
   */
  protected imageUrl(id: number): string {
    return this.recipes.imageUrl(id);
  }

  onRetry(): void {
    this.retry$.next();
  }
}
