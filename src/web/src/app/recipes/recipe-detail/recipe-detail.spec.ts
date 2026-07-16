import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
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
  hasImage: false,
};

async function setup(getById: ReturnType<typeof vi.fn>): Promise<ComponentFixture<RecipeDetail>> {
  await TestBed.configureTestingModule({
    imports: [RecipeDetail],
    providers: [
      provideRouter([]),
      {
        provide: RecipeService,
        useValue: { getById, imageUrl: (id: number) => `/api/recipes/${id}/image` },
      },
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
    const getById = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 404 })));
    const fixture = await setup(getById);

    expect(fixture.nativeElement.querySelector('.empty').textContent).toContain('not found');
  });

  /**
   * The distinction this pair protects: a 404 means the recipe is gone and the user should go back,
   * while a 500 means it may be perfectly fine and they should retry. Reporting the second as "not
   * found" tells the user something untrue and hides the retry.
   */
  it('shows a retryable error, not "not found", when the request fails for any other reason', async () => {
    const getById = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    const fixture = await setup(getById);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.empty')?.textContent).not.toContain('not found');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong');
    expect(el.querySelector('.empty__retry')).toBeTruthy();
  });

  it('re-requests the recipe when the user retries after a failure', async () => {
    const getById = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
      .mockReturnValueOnce(of(RECIPE));
    const fixture = await setup(getById);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.empty__retry')!
      .click();
    await fixture.whenStable();

    expect(getById).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Soup');
  });

  it('shows the photo as a banner, and drops the emoji hero, when the recipe has one', async () => {
    const fixture = await setup(vi.fn(() => of({ ...RECIPE, hasImage: true })));
    const el = fixture.nativeElement as HTMLElement;

    const banner = el.querySelector<HTMLImageElement>('.recipe__banner');
    expect(banner).not.toBeNull();
    expect(banner!.getAttribute('src')).toBe('/api/recipes/7/image');
    // The banner is the picture of the dish; keeping the emoji tile too would show two.
    expect(el.querySelector('.recipe__hero')).toBeNull();
  });

  it('shows the emoji hero and no banner when the recipe has no photo', async () => {
    const fixture = await setup(vi.fn(() => of(RECIPE)));
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.recipe__banner')).toBeNull();
    expect(el.querySelector('.recipe__hero')).not.toBeNull();
  });

  it('keeps the banner out of the accessibility tree', async () => {
    const fixture = await setup(vi.fn(() => of({ ...RECIPE, hasImage: true })));

    // The <h1> names the recipe; an alt would repeat it to a screen reader.
    const banner = fixture.nativeElement.querySelector('.recipe__banner') as HTMLImageElement;
    expect(banner.getAttribute('alt')).toBe('');
    expect(banner.getAttribute('aria-hidden')).toBe('true');
  });
});
