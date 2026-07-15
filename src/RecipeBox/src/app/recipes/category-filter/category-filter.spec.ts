import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CategoryFilter } from './category-filter';

describe('CategoryFilter', () => {
  let fixture: ComponentFixture<CategoryFilter>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CategoryFilter],
    }).compileComponents();

    fixture = TestBed.createComponent(CategoryFilter);
    fixture.componentRef.setInput('categories', ['Breakfast', 'Dessert']);
    fixture.componentRef.setInput('selected', null);
    await fixture.whenStable();
  });

  it('renders an "All" chip plus one per category', () => {
    const labels = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.chip'),
    ).map((b) => b.textContent?.trim());

    expect(labels).toEqual(['All', 'Breakfast', 'Dessert']);
  });

  it('emits the category name when a chip is clicked', () => {
    let emitted: string | null | undefined;
    fixture.componentInstance.selectedChange.subscribe((c) => (emitted = c));

    const dessert = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.chip'),
    ).find((b) => b.textContent?.trim() === 'Dessert');
    dessert!.click();

    expect(emitted).toBe('Dessert');
  });
});
