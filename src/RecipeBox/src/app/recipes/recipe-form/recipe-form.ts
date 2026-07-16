import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

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

  /**
   * Present when editing; `null` when creating. Not readonly: a create that saves but then fails to
   * upload its image adopts the new id and switches to edit mode, so a retry can't create a second
   * recipe (see {@link onImageFailed}).
   */
  private recipeId: number | null;

  protected readonly isEdit = signal(false);
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /** The image the user picked but hasn't saved yet — a new recipe has no id to upload against. */
  private readonly stagedFile = signal<File | null>(null);
  /** Object URL for {@link stagedFile}, so the user sees the picture before committing to it. */
  protected readonly previewUrl = signal<string | null>(null);
  /** Whether the recipe being edited already has an image on the server. */
  protected readonly existingImage = signal(false);
  /** Set when the user removes an existing image; acted on at save, so it can still be undone. */
  protected readonly removeRequested = signal(false);
  /** A rejected file pick, reported next to the picker rather than at the top of the form. */
  protected readonly imageError = signal<string | null>(null);

  /** Mirrors the API's ceiling (UploadRecipeImageViewModelValidator.MaxBytes). */
  private readonly maxImageBytes = 5 * 1024 * 1024;
  private readonly acceptedImageTypes = ['image/jpeg', 'image/png', 'image/webp'];
  protected readonly acceptAttribute = this.acceptedImageTypes.join(',');
  /** True once an edit-mode load has failed: the form is then locked so a stale submit can't clobber the recipe. */
  protected readonly loadFailed = signal(false);
  /**
   * True while an edit-mode load is in flight. Without this the form renders its blank initial state
   * during the fetch, which is indistinguishable from a *create* form — so an edit could be typed
   * into and submitted against fields that were about to be overwritten by the arriving recipe.
   */
  protected readonly loading = signal(false);

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
      this.loading.set(true);
      // Disabled up front, for the same reason the form is locked on failure: until the recipe
      // lands, anything typed here would be silently discarded by patchFrom.
      this.form.disable();
      this.recipes
        .getById(this.recipeId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (recipe) => {
            this.patchFrom(recipe);
            this.loading.set(false);
            this.form.enable();
          },
          error: () => {
            // A failed load leaves the form without the recipe's real content. Keep it locked so the
            // user can't unknowingly submit a full-replace PUT that overwrites the server copy with
            // blanks.
            this.loading.set(false);
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

    // An object URL pins its Blob in memory until revoked, so every preview must be released — both
    // when it's replaced (see setStagedFile) and when the form goes away with one still showing.
    this.destroyRef.onDestroy(() => this.revokePreview());
  }

  // ── Image ────────────────────────────────────────────────────────────────────────────────────

  /**
   * Checks the pick and stages it. These checks are for feedback, not safety: they read the
   * browser's declared type, which the API pointedly ignores in favour of the file's magic number.
   * The point is to tell the user now, rather than after their recipe has already been saved.
   */
  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;

    if (file === null) {
      this.setStagedFile(null);
      return;
    }

    const rejection = this.rejectionFor(file);
    if (rejection !== null) {
      this.imageError.set(rejection);
      // Clear the input too, or it keeps showing the rejected filename as if it were staged.
      input.value = '';
      this.setStagedFile(null);
      return;
    }

    this.imageError.set(null);
    this.setStagedFile(file);
  }

  private rejectionFor(file: File): string | null {
    if (!this.acceptedImageTypes.includes(file.type)) {
      return 'Choose a JPEG, PNG, or WebP image.';
    }
    if (file.size > this.maxImageBytes) {
      return 'That image is larger than 5 MB. Choose a smaller one.';
    }
    return null;
  }

  /** Drops the recipe's image. Takes effect on save, so it's undoable until then. */
  protected removeImage(): void {
    this.setStagedFile(null);
    this.imageError.set(null);
    this.removeRequested.set(true);
  }

  /**
   * The address of the already-saved image, or null when there's none to show — because this is a
   * create, the recipe has no image, or the user has just removed it. Resolved here rather than in
   * the template so the "is there one, and where is it" question has a single answer.
   */
  protected savedImageUrl(): string | null {
    if (this.recipeId === null || !this.existingImage() || this.removeRequested()) {
      return null;
    }
    return this.recipes.imageUrl(this.recipeId);
  }

  /** True when the slot has something to show — a new pick, or the saved image. */
  protected hasImagePreview(): boolean {
    return this.previewUrl() !== null || this.savedImageUrl() !== null;
  }

  private setStagedFile(file: File | null): void {
    this.revokePreview();
    this.stagedFile.set(file);
    this.previewUrl.set(file === null ? null : URL.createObjectURL(file));
    if (file !== null) {
      // Picking a replacement is the opposite of removing.
      this.removeRequested.set(false);
    }
  }

  private revokePreview(): void {
    const url = this.previewUrl();
    if (url !== null) {
      URL.revokeObjectURL(url);
    }
  }

  /**
   * Whether a control's error should be shown *and* announced. Centralised so the visible message,
   * `aria-invalid` and `aria-describedby` can never disagree about it — three copies of
   * `touched && invalid` would eventually drift, and the failure mode is silent: an input marked
   * invalid while pointing at a description that isn't rendered.
   */
  protected showError(control: AbstractControl): boolean {
    return control.touched && control.invalid;
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
      // The image is a second request, and it can't go first: a new recipe has no id to upload
      // against until the save comes back with one.
      next: (saved) => this.saveImage(saved.id),
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

  /** Applies the staged image change, if any, then leaves for the saved recipe. */
  private saveImage(id: number): void {
    const image$ = this.pendingImageRequest(id);
    if (image$ === null) {
      this.router.navigate(['/recipes', id]);
      return;
    }

    image$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.router.navigate(['/recipes', id]),
      error: (err: HttpErrorResponse) => this.onImageFailed(id, err),
    });
  }

  private pendingImageRequest(id: number): Observable<void> | null {
    const file = this.stagedFile();
    if (file !== null) {
      return this.recipes.uploadImage(id, file);
    }
    if (this.removeRequested()) {
      return this.recipes.deleteImage(id);
    }
    return null;
  }

  /**
   * The recipe saved but its image didn't — so stay put and say so, rather than navigating away and
   * leaving the user to discover a missing picture.
   *
   * The important part is adopting the id: after a *create*, the recipe now exists, so re-submitting
   * this form as a create would collide with the name it just took and fail with a 409 the user
   * couldn't make sense of. Becoming an edit of the recipe we just made turns the retry into the
   * update it actually is.
   */
  private onImageFailed(id: number, err: HttpErrorResponse): void {
    this.submitting.set(false);
    this.recipeId = id;
    this.isEdit.set(true);

    this.errorMessage.set(
      err?.status === 400 || err?.status === 413
        ? 'The recipe was saved, but the image was rejected. Choose a JPEG, PNG, or WebP under 5 MB.'
        : 'The recipe was saved, but the image could not be uploaded. You can try again.',
    );
  }

  private patchFrom(recipe: RecipeDetailDto): void {
    this.form.patchValue({
      name: recipe.name,
      description: recipe.description ?? '',
      servings: recipe.servings,
    });

    // Outside the form: the image isn't part of the recipe's JSON, and a full-replace PUT of the
    // recipe deliberately leaves it alone. It's saved by its own request (see saveImage).
    this.existingImage.set(recipe.hasImage);

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
