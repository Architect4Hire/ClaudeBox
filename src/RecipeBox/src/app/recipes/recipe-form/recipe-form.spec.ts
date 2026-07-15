import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

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
};

interface ServiceStub {
  getById: ReturnType<typeof vi.fn>;
  create: ReturnType<typeof vi.fn>;
  update: ReturnType<typeof vi.fn>;
}

async function setup(
  params: Record<string, string>,
  service: Partial<ServiceStub>,
): Promise<{ fixture: ComponentFixture<RecipeForm>; navigate: ReturnType<typeof vi.fn> }> {
  const navigate = vi.fn();
  await TestBed.configureTestingModule({
    imports: [RecipeForm],
    providers: [
      { provide: RecipeService, useValue: service },
      { provide: Router, useValue: { navigate } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params) } } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(RecipeForm);
  await fixture.whenStable();
  return { fixture, navigate };
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
    });
    expect(navigate).toHaveBeenCalledWith(['/recipes', 7]);
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
});
