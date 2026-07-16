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

  /**
   * GET api/recipes — recipe summaries, optionally narrowed by category and/or ingredient. The two
   * filters combine with AND: passing both returns recipes in that category that also contain that
   * ingredient. `ingredient` is matched as a case-insensitive substring of an ingredient name.
   */
  list(category?: string, ingredient?: string): Observable<RecipeSummaryDto[]> {
    let params = new HttpParams();
    if (category) {
      params = params.set('category', category);
    }
    if (ingredient) {
      params = params.set('ingredient', ingredient);
    }
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

  /**
   * GET api/recipes/{id}/image — the address of a recipe's image, for an `<img src>`.
   *
   * The API returns `hasImage` rather than a URL, so composing the address is this service's job:
   * it already owns where the recipes resource lives, and a component building the path itself would
   * be the one place in the app that knows the API's shape. Check `hasImage` before using this — for
   * a recipe without one the URL is a 404.
   *
   * Not an Observable: it's a string for the browser to fetch, not a request we make. The response
   * carries an ETag, so the browser revalidates and a 304 costs nothing — which is why this URL is
   * stable rather than cache-busted.
   */
  imageUrl(id: number): string {
    return `${this.recipesUrl}/${id}/image`;
  }

  /**
   * PUT api/recipes/{id}/image — sets a recipe's image, replacing any existing one.
   *
   * Multipart rather than JSON, and the only method here that isn't. The API ignores the declared
   * content type and filename and reads the format from the bytes, so nothing is gained by dressing
   * the part up. Rejects with 400 if the file isn't a JPEG, PNG, or WebP, or is over 5MB.
   */
  uploadImage(id: number, file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file);
    // No explicit Content-Type: the browser must set it, because only it knows the multipart boundary.
    return this.http.put<void>(`${this.recipesUrl}/${id}/image`, form);
  }

  /** DELETE api/recipes/{id}/image — removes a recipe's image. */
  deleteImage(id: number): Observable<void> {
    return this.http.delete<void>(`${this.recipesUrl}/${id}/image`);
  }
}
