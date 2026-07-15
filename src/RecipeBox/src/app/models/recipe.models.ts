/**
 * TypeScript mirrors of the API's boundary types. These MUST track the C# service/view models
 * exactly (RecipeSummaryServiceModel, RecipeDetailServiceModel, CreateRecipeViewModel and their
 * nested records in src/RecipeBox.ApiService/Managers/Models/**). ASP.NET serializes to camelCase;
 * C# `string?` maps to `string | null` and `decimal` to `number`.
 */

/** Lightweight recipe projection for list views. Mirrors `RecipeSummaryServiceModel`. */
export interface RecipeSummaryDto {
  id: number;
  name: string;
  description: string | null;
  servings: number;
  categories: string[];
  ingredientCount: number;
  stepCount: number;
}

/** An ingredient line. Mirrors `IngredientServiceModel`. */
export interface IngredientDto {
  name: string;
  quantity: number;
  unit: string | null;
}

/** One instruction step; `order` is 1-based. Mirrors `StepServiceModel`. */
export interface StepDto {
  order: number;
  instruction: string;
}

/** Full recipe projection. Mirrors `RecipeDetailServiceModel`; `steps` arrive ordered by `order`. */
export interface RecipeDetailDto {
  id: number;
  name: string;
  description: string | null;
  servings: number;
  ingredients: IngredientDto[];
  steps: StepDto[];
  categories: string[];
  tags: string[];
}

/** An ingredient line within a create request. Mirrors `CreateIngredientViewModel`. */
export interface CreateIngredientRequest {
  name: string;
  quantity: number;
  unit: string | null;
}

/** An ordered instruction within a create request; `order` is 1-based. Mirrors `CreateStepViewModel`. */
export interface CreateStepRequest {
  order: number;
  instruction: string;
}

/** Inbound shape for creating a recipe. Mirrors `CreateRecipeViewModel`. */
export interface CreateRecipeRequest {
  name: string;
  description: string | null;
  servings: number;
  ingredients: CreateIngredientRequest[];
  steps: CreateStepRequest[];
}

/**
 * Inbound shape for replacing a recipe's editable content. Mirrors `UpdateRecipeViewModel` — the
 * same fields as a create (taxonomy is managed elsewhere, so an update leaves categories/tags alone).
 */
export interface UpdateRecipeRequest {
  name: string;
  description: string | null;
  servings: number;
  ingredients: CreateIngredientRequest[];
  steps: CreateStepRequest[];
}
