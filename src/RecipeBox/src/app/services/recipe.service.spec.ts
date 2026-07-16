import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { RecipeService } from './recipe.service';
import {
  CreateRecipeRequest,
  RecipeDetailDto,
  RecipeSummaryDto,
  UpdateRecipeRequest,
} from '../models/recipe.models';

describe('RecipeService', () => {
  let service: RecipeService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RecipeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('list() GETs api/recipes with no params by default', () => {
    const summaries: RecipeSummaryDto[] = [
      {
        id: 1,
        name: 'Pancakes',
        description: null,
        servings: 4,
        categories: ['Breakfast'],
        ingredientCount: 5,
        stepCount: 3,
      },
    ];

    let received: RecipeSummaryDto[] | undefined;
    service.list().subscribe((r) => (received = r));

    const req = httpMock.expectOne('/api/recipes');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush(summaries);

    expect(received).toEqual(summaries);
  });

  it('list(category) sets the category query param', () => {
    service.list('Dessert').subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/recipes');
    expect(req.request.params.get('category')).toBe('Dessert');
    req.flush([]);
  });

  it('list(undefined, ingredient) sets only the ingredient query param', () => {
    service.list(undefined, 'flour').subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/recipes');
    expect(req.request.params.get('ingredient')).toBe('flour');
    expect(req.request.params.has('category')).toBe(false);
    req.flush([]);
  });

  it('list(category, ingredient) sets both query params', () => {
    service.list('Dessert', 'flour').subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/recipes');
    expect(req.request.params.get('category')).toBe('Dessert');
    expect(req.request.params.get('ingredient')).toBe('flour');
    req.flush([]);
  });

  it('list() omits blank filters rather than sending empty params', () => {
    service.list('', '').subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/recipes');
    expect(req.request.params.keys().length).toBe(0);
    req.flush([]);
  });

  it('getById() GETs api/recipes/{id}', () => {
    const detail: RecipeDetailDto = {
      id: 7,
      name: 'Soup',
      description: 'Warm',
      servings: 2,
      ingredients: [{ name: 'Water', quantity: 1.5, unit: 'L' }],
      steps: [{ order: 1, instruction: 'Boil' }],
      categories: ['Dinner'],
      tags: ['vegan'],
    };

    let received: RecipeDetailDto | undefined;
    service.getById(7).subscribe((r) => (received = r));

    const req = httpMock.expectOne('/api/recipes/7');
    expect(req.request.method).toBe('GET');
    req.flush(detail);

    expect(received).toEqual(detail);
  });

  it('create() POSTs the request body to api/recipes', () => {
    const request: CreateRecipeRequest = {
      name: 'Toast',
      description: null,
      servings: 1,
      ingredients: [{ name: 'Bread', quantity: 2, unit: null }],
      steps: [{ order: 1, instruction: 'Toast it' }],
      categories: ['Breakfast'],
      tags: ['quick'],
    };
    const created: RecipeDetailDto = {
      id: 42,
      name: 'Toast',
      description: null,
      servings: 1,
      ingredients: [{ name: 'Bread', quantity: 2, unit: null }],
      steps: [{ order: 1, instruction: 'Toast it' }],
      categories: [],
      tags: [],
    };

    let received: RecipeDetailDto | undefined;
    service.create(request).subscribe((r) => (received = r));

    const req = httpMock.expectOne('/api/recipes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(created);

    expect(received).toEqual(created);
  });

  it('update() PUTs the request body to api/recipes/{id}', () => {
    const request: UpdateRecipeRequest = {
      name: 'Sourdough',
      description: 'Tangy',
      servings: 8,
      ingredients: [{ name: 'Starter', quantity: 1, unit: 'cup' }],
      steps: [{ order: 1, instruction: 'Feed' }, { order: 2, instruction: 'Bake' }],
      categories: ['Baking'],
      tags: ['rustic'],
    };
    const updated: RecipeDetailDto = {
      id: 5,
      name: 'Sourdough',
      description: 'Tangy',
      servings: 8,
      ingredients: [{ name: 'Starter', quantity: 1, unit: 'cup' }],
      steps: [{ order: 1, instruction: 'Feed' }, { order: 2, instruction: 'Bake' }],
      categories: [],
      tags: [],
    };

    let received: RecipeDetailDto | undefined;
    service.update(5, request).subscribe((r) => (received = r));

    const req = httpMock.expectOne('/api/recipes/5');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush(updated);

    expect(received).toEqual(updated);
  });
});
