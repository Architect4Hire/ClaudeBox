import { Routes } from '@angular/router';

import { RecipeList } from './recipes/recipe-list/recipe-list';
import { RecipeDetail } from './recipes/recipe-detail/recipe-detail';
import { RecipeForm } from './recipes/recipe-form/recipe-form';

export const routes: Routes = [
  { path: '', component: RecipeList },
  { path: 'recipes/new', component: RecipeForm },
  { path: 'recipes/:id/edit', component: RecipeForm },
  { path: 'recipes/:id', component: RecipeDetail },
  { path: '**', redirectTo: '' },
];
