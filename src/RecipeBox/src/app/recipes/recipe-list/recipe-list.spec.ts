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
        [summary(1, 'Pancakes', ['Breakfast']), summary(2, 'Cake', ['Dessert'])].filter(
          (r) => !category || r.categories.includes(category),
        ),
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

    expect(list).toHaveBeenCalledWith('Dessert', undefined);
    const titles = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.card__title'),
    ).map((el) => el.textContent?.trim());
    expect(titles).toEqual(['Cake']);
  });
});

describe('RecipeList (ingredient search)', () => {
  let fixture: ComponentFixture<RecipeList>;
  let list: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    vi.useFakeTimers();
    list = vi.fn((category?: string, ingredient?: string) =>
      of(
        [summary(1, 'Bread', ['Baking']), summary(2, 'Soup', ['Mains'])].filter(
          (r) =>
            (!category || r.categories.includes(category)) &&
            // Stand-in for the API's ingredient match: "Bread" is the only floury one.
            (!ingredient || r.name === 'Bread'),
        ),
      ),
    );

    await TestBed.configureTestingModule({
      imports: [RecipeList],
      providers: [provideRouter([]), { provide: RecipeService, useValue: { list } }],
    }).compileComponents();

    fixture = TestBed.createComponent(RecipeList);
    fixture.detectChanges();
  });

  afterEach(() => vi.useRealTimers());

  it('loads the unfiltered list immediately, without waiting out the debounce', () => {
    // startWith('') is what buys this: otherwise the first paint would stall for the debounce window.
    expect(list).toHaveBeenCalledWith(undefined, undefined);
  });

  it('debounces keystrokes into a single query on the final term', () => {
    list.mockClear();

    fixture.componentInstance.onSearchTermChanged('f');
    fixture.componentInstance.onSearchTermChanged('fl');
    fixture.componentInstance.onSearchTermChanged('flour');
    // Mid-flight: nothing issued yet.
    expect(list).not.toHaveBeenCalled();

    vi.advanceTimersByTime(300);

    expect(list).toHaveBeenCalledTimes(1);
    expect(list).toHaveBeenCalledWith(undefined, 'flour');
  });

  it('combines the search term with the selected category', () => {
    fixture.componentInstance.onCategorySelected('Baking');
    fixture.componentInstance.onSearchTermChanged('flour');
    vi.advanceTimersByTime(300);

    expect(list).toHaveBeenLastCalledWith('Baking', 'flour');
  });

  it('sends a cleared term as undefined rather than an empty string', () => {
    fixture.componentInstance.onSearchTermChanged('flour');
    vi.advanceTimersByTime(300);
    list.mockClear();

    fixture.componentInstance.onSearchTermChanged('');
    vi.advanceTimersByTime(300);

    expect(list).toHaveBeenLastCalledWith(undefined, undefined);
  });

  it('does not re-query when a keystroke leaves the trimmed term unchanged', () => {
    fixture.componentInstance.onSearchTermChanged('flour');
    vi.advanceTimersByTime(300);
    list.mockClear();

    // Only trailing whitespace changed, so the effective term is identical.
    fixture.componentInstance.onSearchTermChanged('flour ');
    vi.advanceTimersByTime(300);

    expect(list).not.toHaveBeenCalled();
  });

  it('explains an empty result when a filter is active', async () => {
    fixture.componentInstance.onSearchTermChanged('saffron');
    list.mockImplementation(() => of([]));
    vi.advanceTimersByTime(300);
    fixture.detectChanges();

    const empty = (fixture.nativeElement as HTMLElement).querySelector('.empty')?.textContent;
    expect(empty).toContain('No recipes match that filter');
  });
});

describe('RecipeList (edge cases)', () => {
  async function createWith(summaries: RecipeSummaryDto[]): Promise<ComponentFixture<RecipeList>> {
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
