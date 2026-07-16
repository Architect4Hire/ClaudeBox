import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RecipeSearch } from './recipe-search';

describe('RecipeSearch', () => {
  let component: RecipeSearch;
  let fixture: ComponentFixture<RecipeSearch>;

  function input(): HTMLInputElement {
    return (fixture.nativeElement as HTMLElement).querySelector('input')!;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RecipeSearch],
    }).compileComponents();

    fixture = TestBed.createComponent(RecipeSearch);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('renders a labelled search input', () => {
    const label = (fixture.nativeElement as HTMLElement).querySelector('label');

    expect(input().type).toBe('search');
    // The label must point at the input, or the box is unusable with a screen reader.
    expect(label?.getAttribute('for')).toBe(input().id);
  });

  it('emits the term on each keystroke', () => {
    const emitted: string[] = [];
    component.termChange.subscribe((term) => emitted.push(term));

    input().value = 'flour';
    input().dispatchEvent(new Event('input'));

    expect(emitted).toEqual(['flour']);
  });

  it('shows the current term passed in by the parent', async () => {
    fixture.componentRef.setInput('term', 'yeast');
    await fixture.whenStable();

    expect(input().value).toBe('yeast');
  });

  it('offers no clear button until there is something to clear', async () => {
    const clearButton = () =>
      (fixture.nativeElement as HTMLElement).querySelector('.recipe-search__clear');

    expect(clearButton()).toBeNull();

    fixture.componentRef.setInput('term', 'yeast');
    await fixture.whenStable();

    expect(clearButton()).not.toBeNull();
  });

  it('emits an empty term when cleared', async () => {
    fixture.componentRef.setInput('term', 'yeast');
    await fixture.whenStable();
    const emitted: string[] = [];
    component.termChange.subscribe((term) => emitted.push(term));

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.recipe-search__clear')!
      .click();

    expect(emitted).toEqual(['']);
  });
});
