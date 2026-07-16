import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';

import { RecipeForm } from './recipe-form';
import { RecipeService } from '../../services/recipe.service';
import { RecipeDetailDto } from '../../models/recipe.models';

const EXISTING: RecipeDetailDto = {
  id: 7,
  name: 'Soup',
  description: 'Warm',
  servings: 2,
  ingredients: [{ name: 'Water', quantity: 1.5, unit: 'L' }],
  steps: [{ order: 1, instruction: 'Boil' }],
  categories: [],
  tags: [],
  hasImage: false,
};

interface ServiceStub {
  getById: ReturnType<typeof vi.fn>;
  create: ReturnType<typeof vi.fn>;
  update: ReturnType<typeof vi.fn>;
  uploadImage: ReturnType<typeof vi.fn>;
  deleteImage: ReturnType<typeof vi.fn>;
  imageUrl: (id: number) => string;
}

/** A real, valid JPEG-signature File, since the form checks type and size before staging. */
function jpegFile(name = 'photo.jpg', bytes = 1024): File {
  return new File([new Uint8Array(bytes).fill(0xff)], name, { type: 'image/jpeg' });
}

/**
 * Puts a file into the picker the way a user does. The component reads `event.target.files`, which
 * is read-only, so it has to be redefined — there's no way to set it through the public DOM API.
 */
async function pickFile(fixture: ComponentFixture<RecipeForm>, file: File): Promise<void> {
  const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
    'input[type="file"]',
  );
  expect(input, 'no file input').toBeTruthy();
  Object.defineProperty(input!, 'files', { value: [file], configurable: true });
  input!.dispatchEvent(new Event('change'));
  await fixture.whenStable();
}

async function setup(
  params: Record<string, string>,
  service: Partial<ServiceStub>,
): Promise<{ fixture: ComponentFixture<RecipeForm>; navigate: ReturnType<typeof vi.fn> }> {
  const navigate = vi.fn();
  await TestBed.configureTestingModule({
    imports: [RecipeForm],
    providers: [
      {
        provide: RecipeService,
        useValue: { imageUrl: (id: number) => `/api/recipes/${id}/image`, ...service },
      },
      { provide: Router, useValue: { navigate } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params) } } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(RecipeForm);
  await fixture.whenStable();
  return { fixture, navigate };
}

/**
 * Clicks a real "Add …" button rather than calling the component method. Going through the DOM is
 * both closer to what a user does and what marks this OnPush component dirty — calling the method
 * directly mutates the FormArray without telling change detection, so the new row never renders and
 * assertions about it pass or fail for the wrong reason.
 */
async function clickAdd(fixture: ComponentFixture<RecipeForm>, label: string): Promise<void> {
  const button = Array.from(
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.add'),
  ).find((b) => b.textContent!.trim() === label);
  expect(button, `no "${label}" button`).toBeTruthy();
  button!.click();
  await fixture.whenStable();
}

describe('RecipeForm', () => {
  it('creates a recipe and navigates to it on submit', async () => {
    const create = vi.fn(() => of({ ...EXISTING, id: 42 }));
    const { fixture, navigate } = await setup({}, { create });
    const form = fixture.componentInstance;

    form.form.patchValue({ name: 'Pancakes', description: '', servings: 4 });
    form.ingredients.at(0).patchValue({ name: 'Flour', quantity: 2, unit: 'cups' });
    form.steps.at(0).patchValue({ instruction: 'Mix' });

    form.submit();

    expect(create).toHaveBeenCalledWith({
      name: 'Pancakes',
      description: null,
      servings: 4,
      ingredients: [{ name: 'Flour', quantity: 2, unit: 'cups' }],
      steps: [{ order: 1, instruction: 'Mix' }],
      categories: [],
      tags: [],
    });
    expect(navigate).toHaveBeenCalledWith(['/recipes', 42]);
  });

  it('loads the recipe in edit mode and calls update on submit', async () => {
    const getById = vi.fn(() => of(EXISTING));
    const update = vi.fn(() => of(EXISTING));
    const { fixture, navigate } = await setup({ id: '7' }, { getById, update });
    const form = fixture.componentInstance;

    expect(getById).toHaveBeenCalledWith(7);
    expect(form.form.controls.name.value).toBe('Soup');
    expect(form.ingredients.length).toBe(1);

    form.submit();

    expect(update).toHaveBeenCalledWith(7, {
      name: 'Soup',
      description: 'Warm',
      servings: 2,
      ingredients: [{ name: 'Water', quantity: 1.5, unit: 'L' }],
      steps: [{ order: 1, instruction: 'Boil' }],
      categories: [],
      tags: [],
    });
    expect(navigate).toHaveBeenCalledWith(['/recipes', 7]);
  });

  it('submits normalized categories and tags (trimmed, de-duped, blanks dropped)', async () => {
    const create = vi.fn(() => of({ ...EXISTING, id: 5 }));
    const { fixture } = await setup({}, { create });
    const form = fixture.componentInstance;

    form.form.patchValue({ name: 'Brownies', description: '', servings: 9 });
    form.ingredients.at(0).patchValue({ name: 'Chocolate', quantity: 200, unit: 'g' });
    form.steps.at(0).patchValue({ instruction: 'Bake' });

    // A blank row is dropped; a case-different duplicate collapses to the first spelling.
    form.addCategory('Dessert');
    form.addCategory('  ');
    form.addTag('vegetarian');
    form.addTag('Vegetarian');
    form.tags.at(0).markAsUntouched();

    form.submit();

    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({
        categories: ['Dessert'],
        tags: ['vegetarian'],
      }),
    );
  });

  it('loads existing categories and tags into the form in edit mode', async () => {
    const withTaxonomy: RecipeDetailDto = {
      ...EXISTING,
      categories: ['Soup', 'Comfort'],
      tags: ['warm'],
    };
    const getById = vi.fn(() => of(withTaxonomy));
    const update = vi.fn(() => of(withTaxonomy));
    const { fixture } = await setup({ id: '7' }, { getById, update });
    const form = fixture.componentInstance;

    expect(form.categories.controls.map((c) => c.value)).toEqual(['Soup', 'Comfort']);
    expect(form.tags.controls.map((c) => c.value)).toEqual(['warm']);

    form.submit();

    expect(update).toHaveBeenCalledWith(
      7,
      expect.objectContaining({ categories: ['Soup', 'Comfort'], tags: ['warm'] }),
    );
  });

  it('surfaces a conflict message and does not navigate on a 409', async () => {
    const create = vi.fn(() => throwError(() => ({ status: 409 })));
    const { fixture, navigate } = await setup({}, { create });
    const form = fixture.componentInstance;

    form.form.patchValue({ name: 'Taken', description: '', servings: 1 });
    form.ingredients.at(0).patchValue({ name: 'Flour', quantity: 1, unit: '' });
    form.steps.at(0).patchValue({ instruction: 'Mix' });

    form.submit();
    fixture.detectChanges();

    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.error').textContent).toContain('already exists');
  });

  /**
   * A blank edit form is indistinguishable from a create form. Rendering one while the GET is still
   * in flight invites the user to type into fields that patchFrom is about to overwrite.
   */
  it('shows a loading state instead of an empty form while the recipe is being fetched', async () => {
    const pending = new Subject<RecipeDetailDto>();
    const getById = vi.fn(() => pending.asObservable());
    const { fixture } = await setup({ id: '7' }, { getById, update: vi.fn() });
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.form-loading')?.getAttribute('role')).toBe('status');
    expect(el.querySelector('form')).toBeNull();
    expect(fixture.componentInstance.form.disabled).toBe(true);

    pending.next(EXISTING);
    pending.complete();
    await fixture.whenStable();

    expect(el.querySelector('.form-loading')).toBeNull();
    expect(el.querySelector('form')).toBeTruthy();
    // Re-enabled, or the loaded recipe could be viewed but never edited.
    expect(fixture.componentInstance.form.enabled).toBe(true);
  });

  it('locks the form and blocks submit when the recipe fails to load in edit mode', async () => {
    const getById = vi.fn(() => throwError(() => ({ status: 500 })));
    const update = vi.fn(() => of(EXISTING));
    const { fixture } = await setup({ id: '7' }, { getById, update });
    const form = fixture.componentInstance;
    fixture.detectChanges();

    expect(form.form.disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('.error').textContent).toContain('Could not load');

    form.submit();
    expect(update).not.toHaveBeenCalled();
  });

  /**
   * Placeholders are not labels: they are unreliably exposed to assistive tech and they vanish as
   * soon as the field has content. Every input therefore owes a real accessible name.
   */
  it('gives every input an accessible name, not just a placeholder', async () => {
    const { fixture } = await setup({}, { create: vi.fn() });
    await clickAdd(fixture, 'Add category');
    await clickAdd(fixture, 'Add tag');

    const el = fixture.nativeElement as HTMLElement;
    // Guard the premise: name, description, photo, servings, ingredient x3, step, category, tag.
    expect(el.querySelectorAll('input, textarea').length).toBe(10);
    const unnamed = Array.from(el.querySelectorAll<HTMLInputElement>('input, textarea')).filter(
      (input) => {
        if (input.getAttribute('aria-label')) {
          return false;
        }
        // Either wrapped by a <label>, or pointed at by one via for/id.
        const wrapping = input.closest('label');
        const explicit = input.id ? el.querySelector(`label[for="${input.id}"]`) : null;
        return !wrapping && !explicit;
      },
    );

    expect(unnamed.map((i) => i.getAttribute('placeholder') ?? i.outerHTML)).toEqual([]);
  });

  it('distinguishes the repeated Remove buttons by position', async () => {
    const { fixture } = await setup({}, { create: vi.fn() });
    await clickAdd(fixture, 'Add ingredient');

    const labels = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.row__remove'),
    ).map((b) => b.getAttribute('aria-label'));

    // All-identical "Remove" names are useless in a screen reader's element list, which strips the
    // visual context that tells them apart.
    expect(labels).toEqual(['Remove ingredient 1', 'Remove ingredient 2', 'Remove step 1']);
    expect(new Set(labels).size).toBe(labels.length);
  });

  it('ties a field error to its input so it is announced on focus', async () => {
    const { fixture } = await setup({}, { create: vi.fn() });
    const form = fixture.componentInstance;

    form.form.controls.name.markAsTouched();
    form.form.patchValue({ name: '' });
    fixture.detectChanges();

    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      'input[formcontrolname="name"]',
    )!;
    expect(input.getAttribute('aria-invalid')).toBe('true');

    const describedBy = input.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    // The description must actually exist — a dangling aria-describedby announces nothing.
    const description = (fixture.nativeElement as HTMLElement).querySelector(`#${describedBy}`);
    expect(description?.textContent).toContain('A name is required');
  });

  it('drops the invalid markers once the field is valid', async () => {
    const { fixture } = await setup({}, { create: vi.fn() });
    const form = fixture.componentInstance;

    form.form.controls.name.markAsTouched();
    form.form.patchValue({ name: 'Pancakes' });
    fixture.detectChanges();

    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      'input[formcontrolname="name"]',
    )!;
    // Absent, not "false": aria-invalid="false" is a state a screen reader may still announce.
    expect(input.getAttribute('aria-invalid')).toBeNull();
    expect(input.getAttribute('aria-describedby')).toBeNull();
  });

  it('does not submit and marks the form touched when a required field is invalid', async () => {
    const create = vi.fn(() => of({ ...EXISTING, id: 1 }));
    const { fixture, navigate } = await setup({}, { create });
    const form = fixture.componentInstance;

    // Fill the child rows so the only invalid field is the required, blank name.
    form.ingredients.at(0).patchValue({ name: 'Flour', quantity: 1, unit: '' });
    form.steps.at(0).patchValue({ instruction: 'Mix' });
    form.form.patchValue({ name: '' });

    form.submit();

    expect(create).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(form.form.controls.name.touched).toBe(true);
  });

  it('surfaces a generic error and does not navigate on a non-409 failure', async () => {
    const create = vi.fn(() => throwError(() => ({ status: 500 })));
    const { fixture, navigate } = await setup({}, { create });
    const form = fixture.componentInstance;

    form.form.patchValue({ name: 'Pancakes', description: '', servings: 2 });
    form.ingredients.at(0).patchValue({ name: 'Flour', quantity: 1, unit: '' });
    form.steps.at(0).patchValue({ instruction: 'Mix' });

    form.submit();
    fixture.detectChanges();

    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.error').textContent).toContain(
      'Something went wrong',
    );
  });

  it('re-derives step order from position after a step is removed', async () => {
    const create = vi.fn(() => of({ ...EXISTING, id: 3 }));
    const { fixture } = await setup({}, { create });
    const form = fixture.componentInstance;

    form.form.patchValue({ name: 'Stew', description: '', servings: 4 });
    form.ingredients.at(0).patchValue({ name: 'Water', quantity: 1, unit: 'L' });

    // Two steps, then drop the first — the survivor must submit as order 1, not 2.
    form.steps.at(0).patchValue({ instruction: 'First' });
    form.addStep();
    form.steps.at(1).patchValue({ instruction: 'Second' });
    form.removeStep(0);

    form.submit();

    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({
        steps: [{ order: 1, instruction: 'Second' }],
      }),
    );
  });

  describe('photo', () => {
    /** Fills the minimum a create needs, so submit() gets past validation. */
    function fillValid(fixture: ComponentFixture<RecipeForm>): void {
      const form = fixture.componentInstance;
      form.form.patchValue({ name: 'Pancakes', description: '', servings: 4 });
      form.ingredients.at(0).patchValue({ name: 'Flour', quantity: 2, unit: 'cups' });
      form.steps.at(0).patchValue({ instruction: 'Mix' });
    }

    it('uploads the staged photo after the recipe is created, using the new id', async () => {
      const create = vi.fn(() => of({ ...EXISTING, id: 42 }));
      const uploadImage = vi.fn(() => of(undefined));
      const { fixture, navigate } = await setup({}, { create, uploadImage });
      const file = jpegFile();

      await pickFile(fixture, file);
      fillValid(fixture);
      fixture.componentInstance.submit();
      await fixture.whenStable();

      // The order is forced, not chosen: a new recipe has no id to upload against until it's saved.
      expect(uploadImage).toHaveBeenCalledWith(42, file);
      expect(navigate).toHaveBeenCalledWith(['/recipes', 42]);
    });

    it('does not touch the image endpoints when no photo was staged', async () => {
      const create = vi.fn(() => of({ ...EXISTING, id: 42 }));
      const uploadImage = vi.fn(() => of(undefined));
      const deleteImage = vi.fn(() => of(undefined));
      const { fixture, navigate } = await setup({}, { create, uploadImage, deleteImage });

      fillValid(fixture);
      fixture.componentInstance.submit();
      await fixture.whenStable();

      expect(uploadImage).not.toHaveBeenCalled();
      expect(deleteImage).not.toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/recipes', 42]);
    });

    it('deletes the image when the user removed it and saved', async () => {
      const getById = vi.fn(() => of({ ...EXISTING, hasImage: true }));
      const update = vi.fn(() => of({ ...EXISTING, hasImage: true }));
      const deleteImage = vi.fn(() => of(undefined));
      const { fixture, navigate } = await setup({ id: '7' }, { getById, update, deleteImage });

      const remove = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
        '.image-field__remove',
      );
      expect(remove, 'no remove button for a recipe with an image').toBeTruthy();
      remove!.click();
      await fixture.whenStable();

      fixture.componentInstance.submit();
      await fixture.whenStable();

      expect(deleteImage).toHaveBeenCalledWith(7);
      expect(navigate).toHaveBeenCalledWith(['/recipes', 7]);
    });

    it('rejects a file that is not an image without staging it', async () => {
      const create = vi.fn(() => of({ ...EXISTING, id: 42 }));
      const uploadImage = vi.fn(() => of(undefined));
      const { fixture } = await setup({}, { create, uploadImage });

      await pickFile(fixture, new File(['nope'], 'notes.txt', { type: 'text/plain' }));

      // Client-side, this is only about telling the user now rather than after their recipe saves —
      // the API re-checks the bytes regardless.
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('#image-error')!.textContent).toContain('JPEG, PNG, or WebP');
      expect(el.querySelector('.image-field__img')).toBeNull();

      fillValid(fixture);
      fixture.componentInstance.submit();
      await fixture.whenStable();
      expect(uploadImage).not.toHaveBeenCalled();
    });

    it('rejects a file over the size limit without staging it', async () => {
      const { fixture } = await setup({}, { create: vi.fn(() => of(EXISTING)) });

      await pickFile(fixture, jpegFile('huge.jpg', 5 * 1024 * 1024 + 1));

      expect(
        (fixture.nativeElement as HTMLElement).querySelector('#image-error')!.textContent,
      ).toContain('larger than 5 MB');
    });

    it('previews the staged photo before it is saved', async () => {
      const { fixture } = await setup({}, { create: vi.fn(() => of(EXISTING)) });

      await pickFile(fixture, jpegFile());

      const img = (fixture.nativeElement as HTMLElement).querySelector('.image-field__img');
      expect(img).not.toBeNull();
      // An object URL, not the API address — the file only exists in the browser at this point.
      expect(img!.getAttribute('src')).toMatch(/^blob:/);
    });

    it('shows the saved photo when editing a recipe that has one', async () => {
      const getById = vi.fn(() => of({ ...EXISTING, hasImage: true }));
      const { fixture } = await setup({ id: '7' }, { getById, update: vi.fn() });

      const img = (fixture.nativeElement as HTMLElement).querySelector('.image-field__img');
      expect(img!.getAttribute('src')).toBe('/api/recipes/7/image');
    });

    it('becomes an edit of the new recipe when the photo upload fails after a create', async () => {
      const create = vi.fn(() => of({ ...EXISTING, id: 42 }));
      const update = vi.fn(() => of({ ...EXISTING, id: 42 }));
      const uploadImage = vi
        .fn()
        .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
        .mockReturnValueOnce(of(undefined));
      const { fixture, navigate } = await setup({}, { create, update, uploadImage });

      await pickFile(fixture, jpegFile());
      fillValid(fixture);
      fixture.componentInstance.submit();
      await fixture.whenStable();

      // The recipe saved; only its photo didn't. Navigating away would leave the user to discover
      // that for themselves.
      expect(navigate).not.toHaveBeenCalled();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.error')!.textContent).toContain('recipe was saved');

      // The load-bearing part: retrying must now *update* recipe 42, not create a second one — a
      // second create would collide with the name the first one already took and 409.
      fixture.componentInstance.submit();
      await fixture.whenStable();

      expect(create).toHaveBeenCalledTimes(1);
      expect(update).toHaveBeenCalledTimes(1);
      expect(update).toHaveBeenCalledWith(42, expect.objectContaining({ name: 'Pancakes' }));
      expect(navigate).toHaveBeenCalledWith(['/recipes', 42]);
    });

    it('explains a rejected upload differently from a failed one', async () => {
      const create = vi.fn(() => of({ ...EXISTING, id: 42 }));
      const uploadImage = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 400 })));
      const { fixture } = await setup({}, { create, uploadImage });

      await pickFile(fixture, jpegFile());
      fillValid(fixture);
      fixture.componentInstance.submit();
      await fixture.whenStable();

      // A 400 means the API looked at the bytes and said no — tell the user what it accepts.
      expect((fixture.nativeElement as HTMLElement).querySelector('.error')!.textContent).toContain(
        'rejected',
      );
    });
  });
});
