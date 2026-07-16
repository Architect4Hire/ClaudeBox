import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { RecipeDetail } from './recipe-detail';
import { RecipeService } from '../../services/recipe.service';
import { RecipeDetailDto } from '../../models/recipe.models';

const RECIPE: RecipeDetailDto = {
  id: 7,
  name: 'Soup',
  description: 'Warm',
  servings: 2,
  ingredients: [{ name: 'Water', quantity: 1.5, unit: 'L' }],
  steps: [
    { order: 1, instruction: 'Boil' },
    { order: 2, instruction: 'Serve' },
  ],
  categories: ['Dinner'],
  tags: [],
};

async function setup(getById: ReturnType<typeof vi.fn>): Promise<ComponentFixture<RecipeDetail>> {
  await TestBed.configureTestingModule({
    imports: [RecipeDetail],
    providers: [
      provideRouter([]),
      { provide: RecipeService, useValue: { getById } },
      { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id: '7' })) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(RecipeDetail);
  await fixture.whenStable();
  return fixture;
}

describe('RecipeDetail', () => {
  it('renders the recipe with its ordered steps', async () => {
    const getById = vi.fn(() => of(RECIPE));
    const fixture = await setup(getById);

    expect(getById).toHaveBeenCalledWith(7);
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Soup');
    const steps = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.steps__text'),
    ).map((el) => el.textContent?.trim());
    expect(steps).toEqual(['Boil', 'Serve']);
  });

  it('shows a not-found message when the recipe is missing', async () => {
    const getById = vi.fn(() => throwError(() => new Error('404')));
    const fixture = await setup(getById);

    expect(fixture.nativeElement.querySelector('.empty').textContent).toContain('not found');
  });
});
