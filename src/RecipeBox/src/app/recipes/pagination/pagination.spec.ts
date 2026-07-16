import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Pagination } from './pagination';

async function setup(page: number, pageCount: number): Promise<ComponentFixture<Pagination>> {
  // Reset first: a couple of tests below build two fixtures to compare states, and TestBed refuses to
  // be reconfigured once instantiated.
  TestBed.resetTestingModule();
  await TestBed.configureTestingModule({ imports: [Pagination] }).compileComponents();

  const fixture = TestBed.createComponent(Pagination);
  fixture.componentRef.setInput('page', page);
  fixture.componentRef.setInput('pageCount', pageCount);
  await fixture.whenStable();
  return fixture;
}

/** The visible page strip, with gaps rendered as '…'. */
function strip(fixture: ComponentFixture<Pagination>): string[] {
  return Array.from(
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
      '.pagination__page, .pagination__gap',
    ),
  ).map((el) => el.textContent!.trim());
}

function step(fixture: ComponentFixture<Pagination>, label: 'Previous' | 'Next'): HTMLButtonElement {
  return Array.from(
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.pagination__step'),
  ).find((b) => b.textContent!.includes(label))!;
}

describe('Pagination', () => {
  it('renders nothing when there is only one page', async () => {
    const fixture = await setup(1, 1);

    expect((fixture.nativeElement as HTMLElement).querySelector('nav')).toBeNull();
  });

  it('renders every page when they all fit', async () => {
    const fixture = await setup(1, 4);

    expect(strip(fixture)).toEqual(['1', '2', '3', '4']);
  });

  it('elides the middle, keeping first, last and a window around the current page', async () => {
    const fixture = await setup(6, 12);

    expect(strip(fixture)).toEqual(['1', '…', '5', '6', '7', '…', '12']);
  });

  it('does not leave a gap standing in for a single page', async () => {
    // Page 3 of 5: the window reaches 2..4, so 1 and 5 are adjacent to it — a '…' hiding exactly one
    // page would be both silly and wider than the number it replaced.
    const fixture = await setup(3, 5);

    expect(strip(fixture)).toEqual(['1', '2', '3', '4', '5']);
  });

  it('marks only the current page with aria-current', async () => {
    const fixture = await setup(2, 5);
    const current = (fixture.nativeElement as HTMLElement).querySelectorAll('[aria-current="page"]');

    expect(current.length).toBe(1);
    expect(current[0].textContent!.trim()).toBe('2');
  });

  it('gives each page button a spoken label, not a bare numeral', async () => {
    const fixture = await setup(1, 3);
    const first = (fixture.nativeElement as HTMLElement).querySelector('.pagination__page');

    expect(first!.getAttribute('aria-label')).toBe('Page 1');
  });

  it('emits the requested page', async () => {
    const fixture = await setup(1, 5);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe((p) => emitted.push(p));

    step(fixture, 'Next').click();

    expect(emitted).toEqual([2]);
  });

  it('marks the steps unavailable at each end so the user cannot walk off the list', async () => {
    const first = await setup(1, 5);
    expect(step(first, 'Previous').getAttribute('aria-disabled')).toBe('true');
    expect(step(first, 'Next').getAttribute('aria-disabled')).toBeNull();

    const last = await setup(5, 5);
    expect(step(last, 'Previous').getAttribute('aria-disabled')).toBeNull();
    expect(step(last, 'Next').getAttribute('aria-disabled')).toBe('true');
  });

  /**
   * The `disabled` property would take the button out of the tab order, and browsers drop focus to
   * <body> when the focused element becomes disabled — so paging to the last page by keyboard would
   * dump the user at the top of the document with no announcement. aria-disabled keeps it focusable.
   */
  it('keeps a boundary step focusable, and ignores clicks on it', async () => {
    const fixture = await setup(1, 5);
    const previous = step(fixture, 'Previous');
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe((p) => emitted.push(p));

    expect(previous.disabled).toBe(false);
    previous.focus();
    expect(document.activeElement).toBe(previous);

    previous.click();

    expect(emitted).toEqual([]);
    // Still focused: the click did nothing, so the user has not lost their place.
    expect(document.activeElement).toBe(previous);
  });

  it('never emits an out-of-range or no-op page', async () => {
    const fixture = await setup(3, 5);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe((p) => emitted.push(p));

    fixture.componentInstance.go(0);
    fixture.componentInstance.go(6);
    fixture.componentInstance.go(3);

    expect(emitted).toEqual([]);
  });
});
