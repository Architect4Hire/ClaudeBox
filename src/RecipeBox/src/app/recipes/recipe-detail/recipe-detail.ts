import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AsyncPipe } from '@angular/common';
import { Observable, catchError, map, of, switchMap } from 'rxjs';

import { RecipeService } from '../../services/recipe.service';
import { RecipeDetailDto } from '../../models/recipe.models';

/** Distinguishes "still loading" from "loaded but not found" for the template. */
type DetailState =
  | { status: 'loading' }
  | { status: 'loaded'; recipe: RecipeDetailDto }
  | { status: 'not-found' };

/**
 * Shows one recipe — its header, ingredients, and ordered steps — resolved from the `:id` route
 * param through {@link RecipeService}. The stream is consumed with the `async` pipe; a missing recipe
 * (404) is mapped to a not-found state rather than surfacing as an error.
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

  readonly state$: Observable<DetailState> = this.route.paramMap.pipe(
    switchMap((params) =>
      this.recipes.getById(Number(params.get('id'))).pipe(
        map((recipe): DetailState => ({ status: 'loaded', recipe })),
        catchError(() => of<DetailState>({ status: 'not-found' })),
      ),
    ),
  );
}
