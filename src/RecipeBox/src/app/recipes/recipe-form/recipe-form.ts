import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormArray,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';

import { RecipeService } from '../../services/recipe.service';
import {
  CreateIngredientRequest,
  CreateRecipeRequest,
  CreateStepRequest,
  RecipeDetailDto,
} from '../../models/recipe.models';

type IngredientGroup = FormGroup<{
  name: FormControl<string>;
  quantity: FormControl<number>;
  unit: FormControl<string>;
}>;

type StepGroup = FormGroup<{
  instruction: FormControl<string>;
}>;

/**
 * Create/edit form for a recipe and its ingredients + ordered steps. The route decides the mode: a
 * `:id` param means edit (the existing recipe is loaded through {@link RecipeService} and patched in),
 * otherwise create. Step order is positional — derived from the array index on submit. All persistence
 * goes through the service; the one-shot load/save subscriptions are cleaned up with
 * `takeUntilDestroyed`.
 */
@Component({
  selector: 'app-recipe-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './recipe-form.html',
  styleUrl: './recipe-form.css',
})
export class RecipeForm {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly recipes = inject(RecipeService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** Present when editing; `null` when creating. */
  private readonly recipeId: number | null;

  protected readonly isEdit = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** True once an edit-mode load has failed: the form is then locked so a stale submit can't clobber the recipe. */
  protected readonly loadFailed = signal(false);

  readonly form = this.fb.group({
    name: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
    description: this.fb.control('', [Validators.maxLength(2000)]),
    servings: this.fb.control(1, [Validators.required, Validators.min(1)]),
    ingredients: this.fb.array<IngredientGroup>([]),
    steps: this.fb.array<StepGroup>([]),
    // Taxonomy is optional: each is a plain free-text name. Blank rows are dropped on submit, so no
    // `required` here — only the DB max-length is enforced client-side.
    categories: this.fb.array<FormControl<string>>([]),
    tags: this.fb.array<FormControl<string>>([]),
  });

  constructor() {
    const idParam = this.route.snapshot.paramMap.get('id');
    this.recipeId = idParam ? Number(idParam) : null;

    if (this.recipeId !== null) {
      this.isEdit.set(true);
      this.recipes
        .getById(this.recipeId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (recipe) => this.patchFrom(recipe),
          error: () => {
            // A failed load leaves the form without the recipe's real content. Lock it so the user
            // can't unknowingly submit a full-replace PUT that overwrites the server copy with blanks.
            this.loadFailed.set(true);
            this.form.disable();
            this.errorMessage.set('Could not load this recipe. Please try again.');
          },
        });
    } else {
      // Start a create with one blank ingredient and one blank step so the form isn't empty.
      this.addIngredient();
      this.addStep();
    }
  }

  get ingredients(): FormArray<IngredientGroup> {
    return this.form.controls.ingredients;
  }

  get steps(): FormArray<StepGroup> {
    return this.form.controls.steps;
  }

  get categories(): FormArray<FormControl<string>> {
    return this.form.controls.categories;
  }

  get tags(): FormArray<FormControl<string>> {
    return this.form.controls.tags;
  }

  addIngredient(): void {
    this.ingredients.push(
      this.fb.group({
        name: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
        quantity: this.fb.control(1, [Validators.required, Validators.min(0)]),
        unit: this.fb.control('', [Validators.maxLength(50)]),
      }),
    );
  }

  removeIngredient(index: number): void {
    this.ingredients.removeAt(index);
  }

  addStep(): void {
    this.steps.push(
      this.fb.group({
        instruction: this.fb.control('', [Validators.required, Validators.maxLength(2000)]),
      }),
    );
  }

  removeStep(index: number): void {
    this.steps.removeAt(index);
  }

  addCategory(value = ''): void {
    this.categories.push(this.fb.control(value, [Validators.maxLength(100)]));
  }

  removeCategory(index: number): void {
    this.categories.removeAt(index);
  }

  addTag(value = ''): void {
    this.tags.push(this.fb.control(value, [Validators.maxLength(100)]));
  }

  removeTag(index: number): void {
    this.tags.removeAt(index);
  }

  submit(): void {
    // Never save from a form that failed to load its recipe (see loadFailed).
    if (this.loadFailed()) {
      return;
    }

    if (this.form.invalid || this.ingredients.length === 0 || this.steps.length === 0) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    const request = this.toRequest();
    const save$ =
      this.recipeId !== null
        ? this.recipes.update(this.recipeId, request)
        : this.recipes.create(request);

    save$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => this.router.navigate(['/recipes', saved.id]),
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.errorMessage.set(
          err?.status === 409
            ? 'A recipe with that name already exists.'
            : 'Something went wrong saving the recipe.',
        );
      },
    });
  }

  private patchFrom(recipe: RecipeDetailDto): void {
    this.form.patchValue({
      name: recipe.name,
      description: recipe.description ?? '',
      servings: recipe.servings,
    });

    this.ingredients.clear();
    for (const ingredient of recipe.ingredients) {
      this.ingredients.push(
        this.fb.group({
          name: this.fb.control(ingredient.name, [Validators.required, Validators.maxLength(200)]),
          quantity: this.fb.control(ingredient.quantity, [Validators.required, Validators.min(0)]),
          unit: this.fb.control(ingredient.unit ?? '', [Validators.maxLength(50)]),
        }),
      );
    }

    this.steps.clear();
    for (const step of recipe.steps) {
      this.steps.push(
        this.fb.group({
          instruction: this.fb.control(step.instruction, [
            Validators.required,
            Validators.maxLength(2000),
          ]),
        }),
      );
    }

    this.categories.clear();
    for (const category of recipe.categories) {
      this.addCategory(category);
    }

    this.tags.clear();
    for (const tag of recipe.tags) {
      this.addTag(tag);
    }
  }

  /** Projects the form value onto the wire request, deriving 1-based step order from position. */
  private toRequest(): CreateRecipeRequest {
    const value = this.form.getRawValue();
    const description = value.description.trim();

    const ingredients: CreateIngredientRequest[] = value.ingredients.map((i) => ({
      name: i.name.trim(),
      quantity: i.quantity,
      unit: i.unit.trim() === '' ? null : i.unit.trim(),
    }));

    const steps: CreateStepRequest[] = value.steps.map((s, index) => ({
      order: index + 1,
      instruction: s.instruction.trim(),
    }));

    return {
      name: value.name.trim(),
      description: description === '' ? null : description,
      servings: value.servings,
      ingredients,
      steps,
      categories: this.normalizeNames(value.categories),
      tags: this.normalizeNames(value.tags),
    };
  }

  /** Trims, drops blanks, and de-duplicates names case-insensitively (keeping the first spelling). */
  private normalizeNames(names: string[]): string[] {
    const seen = new Set<string>();
    const result: string[] = [];
    for (const raw of names) {
      const name = raw.trim();
      if (name === '') {
        continue;
      }
      const key = name.toLowerCase();
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      result.push(name);
    }
    return result;
  }
}
