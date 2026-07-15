import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom, of } from 'rxjs';

import { RecipeList } from './recipe-list';
import { RecipeService } from '../../services/recipe.service';
import { RecipeSummaryDto } from '../../models/recipe.models';

function summary(id: number, name: string, categories: string[]): RecipeSummaryDto {
  return { id, name, description: null, servings: 2, categories, ingredientCount: 1, stepCount: 1 };
}

describe('RecipeList', () => {
  let fixture: ComponentFixture<RecipeList>;
  let list: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    list = vi.fn((category?: string) =>
      of(
        [
          summary(1, 'Pancakes', ['Breakfast']),
          summary(2, 'Cake', ['Dessert']),
        ].filter((r) => !category || r.categories.includes(category)),
      ),
    );

    await TestBed.configureTestingModule({
      imports: [RecipeList],
      providers: [provideRouter([]), { provide: RecipeService, useValue: { list } }],
    }).compileComponents();

    fixture = TestBed.createComponent(RecipeList);
    await fixture.whenStable();
  });

  it('renders a card per recipe returned by the service', () => {
    const titles = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.card__title'),
    ).map((el) => el.textContent?.trim());

    expect(titles).toEqual(['Pancakes', 'Cake']);
  });

  it('re-queries the service with the chosen category', async () => {
    fixture.componentInstance.onCategorySelected('Dessert');
    await fixture.whenStable();

    expect(list).toHaveBeenCalledWith('Dessert');
    const titles = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.card__title'),
    ).map((el) => el.textContent?.trim());
    expect(titles).toEqual(['Cake']);
  });
});

describe('RecipeList (edge cases)', () => {
  async function createWith(
    summaries: RecipeSummaryDto[],
  ): Promise<ComponentFixture<RecipeList>> {
    const list = vi.fn((category?: string) =>
      of(summaries.filter((r) => !category || r.categories.includes(category))),
    );
    await TestBed.configureTestingModule({
      imports: [RecipeList],
      providers: [provideRouter([]), { provide: RecipeService, useValue: { list } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(RecipeList);
    await fixture.whenStable();
    return fixture;
  }

  it('renders the empty state when the service returns no recipes', async () => {
    const fixture = await createWith([]);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelectorAll('.card__title').length).toBe(0);
    expect(el.querySelector('.empty')?.textContent).toContain('No recipes yet');
  });

  it('derives filter options as a sorted, de-duplicated category list', async () => {
    const fixture = await createWith([
      summary(1, 'Omelette', ['Main', 'Breakfast']),
      summary(2, 'Trifle', ['Dessert', 'Main']),
    ]);

    const categories = await firstValueFrom(fixture.componentInstance.categories$);

    expect(categories).toEqual(['Breakfast', 'Dessert', 'Main']);
  });
});
