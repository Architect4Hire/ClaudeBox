import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom, of, throwError } from 'rxjs';

import { PAGE_SIZE, RecipeList } from './recipe-list';
import { RecipeService } from '../../services/recipe.service';
import { RecipeSummaryDto } from '../../models/recipe.models';

function summary(
  id: number,
  name: string,
  categories: string[],
  hasImage = false,
): RecipeSummaryDto {
  return {
    id,
    name,
    description: null,
    servings: 2,
    categories,
    ingredientCount: 1,
    stepCount: 1,
    hasImage,
  };
}

const imageUrl = (id: number) => `/api/recipes/${id}/image`;

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
      providers: [
        provideRouter([]),
        { provide: RecipeService, useValue: { list, imageUrl } },
      ],
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

describe('RecipeList (pagination)', () => {
  /** `count` recipes, all in 'Main' except the last, which is the only 'Rare' one. */
  function many(count: number): RecipeSummaryDto[] {
    return Array.from({ length: count }, (_, i) =>
      summary(i + 1, `Recipe ${i + 1}`, i === count - 1 ? ['Rare'] : ['Main']),
    );
  }

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

  function titles(fixture: ComponentFixture<RecipeList>): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.card__title'),
    ).map((el) => el.textContent!.trim());
  }

  it('shows only the first page and does not page a list that fits', async () => {
    const fixture = await createWith(many(PAGE_SIZE));

    expect(titles(fixture).length).toBe(PAGE_SIZE);
    expect((fixture.nativeElement as HTMLElement).querySelector('.pagination')).toBeNull();
  });

  it('slices the list into pages once it overflows', async () => {
    const fixture = await createWith(many(PAGE_SIZE + 3));

    expect(titles(fixture).length).toBe(PAGE_SIZE);
    expect((fixture.nativeElement as HTMLElement).querySelector('.pagination')).toBeTruthy();

    fixture.componentInstance.onPageChange(2);
    await fixture.whenStable();

    expect(titles(fixture).length).toBe(3);
    expect(titles(fixture)[0]).toBe(`Recipe ${PAGE_SIZE + 1}`);
  });

  it('announces the range and total for the current page', async () => {
    const fixture = await createWith(many(PAGE_SIZE + 3));
    const count = (fixture.nativeElement as HTMLElement).querySelector('.list-count');

    // The region must be live, or filtering and paging are silent to a screen reader.
    expect(count!.getAttribute('role')).toBe('status');
    expect(count!.textContent!.replace(/\s+/g, ' ')).toContain(
      `Showing 1–${PAGE_SIZE} of ${PAGE_SIZE + 3} recipes`,
    );
  });

  /**
   * The bug this exists to prevent: sit on a later page, narrow the filter so the result set has
   * fewer pages than the one you're on, and the slice for that page is empty — a blank grid, no
   * cards, no explanation.
   */
  it('returns to page 1 when a filter narrows the results', async () => {
    const fixture = await createWith(many(PAGE_SIZE + 3));

    fixture.componentInstance.onPageChange(2);
    await fixture.whenStable();
    expect(titles(fixture)[0]).toBe(`Recipe ${PAGE_SIZE + 1}`);

    // 'Rare' matches a single recipe — one page's worth.
    fixture.componentInstance.onCategorySelected('Rare');
    await fixture.whenStable();

    expect(titles(fixture)).toEqual([`Recipe ${PAGE_SIZE + 3}`]);
    expect((fixture.nativeElement as HTMLElement).querySelector('.empty')).toBeNull();
  });

  /** Re-clicking the active chip changes nothing, so it must not refetch or bounce you off your page. */
  it('ignores a re-selection of the category already in effect', async () => {
    const fixture = await createWith(many(PAGE_SIZE + 3));

    fixture.componentInstance.onCategorySelected('Main');
    await fixture.whenStable();
    fixture.componentInstance.onPageChange(2);
    await fixture.whenStable();
    const before = titles(fixture);

    fixture.componentInstance.onCategorySelected('Main');
    await fixture.whenStable();

    // Still on page 2 of the same results, rather than reset to page 1.
    expect(titles(fixture)).toEqual(before);
  });

  it('clamps a page beyond the end rather than rendering an empty slice', async () => {
    const fixture = await createWith(many(PAGE_SIZE + 3));

    fixture.componentInstance.onPageChange(99);
    await fixture.whenStable();

    // Falls back to the last real page, not a blank grid.
    expect(titles(fixture).length).toBe(3);
  });
});

describe('RecipeList (failure)', () => {
  async function createWith(
    list: ReturnType<typeof vi.fn>,
  ): Promise<ComponentFixture<RecipeList>> {
    await TestBed.configureTestingModule({
      imports: [RecipeList],
      providers: [provideRouter([]), { provide: RecipeService, useValue: { list } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(RecipeList);
    await fixture.whenStable();
    return fixture;
  }

  it('reports a failed load as an error, not as "no recipes yet"', async () => {
    const fixture = await createWith(vi.fn(() => throwError(() => new Error('boom'))));
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong');
    expect(el.textContent).not.toContain('No recipes yet');
  });

  it('recovers when the user retries', async () => {
    const list = vi
      .fn()
      // The category options issue their own unfiltered request eagerly, before the grid's — so the
      // grid's first attempt is the *second* call. Stub them separately or the options quietly
      // swallow the failure this test is about.
      .mockReturnValueOnce(of([]))
      .mockReturnValueOnce(throwError(() => new Error('boom')))
      .mockReturnValue(of([summary(1, 'Pancakes', ['Breakfast'])]));
    const fixture = await createWith(list);
    expect((fixture.nativeElement as HTMLElement).querySelector('.empty__retry')).toBeTruthy();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.empty__retry')!.click();
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.card__title')?.textContent).toContain(
      'Pancakes',
    );
  });

  /**
   * A failed request must not kill the outer stream. If catchError sat outside the switchMap the
   * component would render its error state once and then ignore every subsequent filter change —
   * permanently, and silently.
   */
  it('still responds to filter changes after a failure', async () => {
    const list = vi
      .fn()
      .mockReturnValueOnce(of([])) // category options (see above)
      .mockReturnValueOnce(throwError(() => new Error('boom'))) // the grid's initial load
      .mockReturnValue(of([summary(2, 'Cake', ['Dessert'])]));
    const fixture = await createWith(list);
    // Guard the premise: without a real error state first, this test would pass vacuously.
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeTruthy();

    fixture.componentInstance.onCategorySelected('Dessert');
    await fixture.whenStable();

    expect(list).toHaveBeenLastCalledWith('Dessert', undefined);
    expect((fixture.nativeElement as HTMLElement).querySelector('.card__title')?.textContent).toContain(
      'Cake',
    );
  });

  /**
   * The category filter is an enhancement beside the grid, not a prerequisite for it. If its own
   * (unfiltered) request fails, the grid must still render.
   */
  it('keeps the grid when only the category options fail to load', async () => {
    let call = 0;
    // The options request is the component's first call; the grid's is the second.
    const list = vi.fn(() =>
      ++call === 1 ? throwError(() => new Error('boom')) : of([summary(1, 'Pancakes', ['Breakfast'])]),
    );
    const fixture = await createWith(list);
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.category-filter')).toBeNull();
    expect(el.querySelector('.card__title')?.textContent).toContain('Pancakes');
  });

  describe('card images', () => {
    async function renderWith(recipes: RecipeSummaryDto[]): Promise<HTMLElement> {
      TestBed.resetTestingModule();
      await TestBed.configureTestingModule({
        imports: [RecipeList],
        providers: [
          provideRouter([]),
          { provide: RecipeService, useValue: { list: vi.fn(() => of(recipes)), imageUrl } },
        ],
      }).compileComponents();

      const local = TestBed.createComponent(RecipeList);
      await local.whenStable();
      return local.nativeElement as HTMLElement;
    }

    it('shows the photo when the recipe has one', async () => {
      const el = await renderWith([summary(7, 'Pancakes', ['Breakfast'], true)]);

      const img = el.querySelector<HTMLImageElement>('.card__image');
      expect(img).not.toBeNull();
      expect(img!.getAttribute('src')).toBe('/api/recipes/7/image');
      // Lazy, because a full page of cards is a full page of photos.
      expect(img!.getAttribute('loading')).toBe('lazy');
      expect(el.querySelector('.card__band-emoji')).toBeNull();
    });

    it('falls back to the placeholder when the recipe has no photo', async () => {
      const el = await renderWith([summary(7, 'Pancakes', ['Breakfast'], false)]);

      expect(el.querySelector('.card__image')).toBeNull();
      expect(el.querySelector('.card__band-emoji')).not.toBeNull();
    });

    it('keeps the photo out of the accessibility tree', async () => {
      const el = await renderWith([summary(7, 'Pancakes', ['Breakfast'], true)]);

      // The card's own title names the dish; an alt here would have a screen reader announce it
      // twice, once as a heading and once as an image.
      expect(el.querySelector('.card__image')!.getAttribute('alt')).toBe('');
      expect(el.querySelector('.card__band')!.getAttribute('aria-hidden')).toBe('true');
    });
  });
});
