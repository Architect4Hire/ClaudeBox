import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import {
  CreateRecipeRequest,
  RecipeDetailDto,
  RecipeSummaryDto,
  UpdateRecipeRequest,
} from '../models/recipe.models';

/**
 * The single HttpClient gateway for the recipes resource. Components inject this service and
 * consume the returned observables (via the `async` pipe) — they never touch HttpClient directly.
 * Mirrors the three actions on the API's RecipesController (`api/recipes`).
 */
@Injectable({ providedIn: 'root' })
export class RecipeService {
  private readonly http = inject(HttpClient);
  /** Same-origin base; the real API host is proxied in by Aspire's injected config. */
  private readonly recipesUrl = `${environment.apiBaseUrl}/recipes`;

  /** GET api/recipes — recipe summaries, optionally filtered to a single category. */
  list(category?: string): Observable<RecipeSummaryDto[]> {
    const params = category ? new HttpParams().set('category', category) : undefined;
    return this.http.get<RecipeSummaryDto[]>(this.recipesUrl, { params });
  }

  /** GET api/recipes/{id} — one recipe with its ingredients and ordered steps. */
  getById(id: number): Observable<RecipeDetailDto> {
    return this.http.get<RecipeDetailDto>(`${this.recipesUrl}/${id}`);
  }

  /** POST api/recipes — creates a recipe with its ingredients and ordered steps. */
  create(request: CreateRecipeRequest): Observable<RecipeDetailDto> {
    return this.http.post<RecipeDetailDto>(this.recipesUrl, request);
  }

  /** PUT api/recipes/{id} — replaces a recipe's header, ingredients, and ordered steps. */
  update(id: number, request: UpdateRecipeRequest): Observable<RecipeDetailDto> {
    return this.http.put<RecipeDetailDto>(`${this.recipesUrl}/${id}`, request);
  }
}
