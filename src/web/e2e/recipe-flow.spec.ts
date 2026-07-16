import { test, expect, type Page } from '@playwright/test';

/**
 * End-to-end walk of the core RecipeBox journey:
 * create → view detail (ingredients + ordered steps) → edit → filter by category.
 *
 * Cleanup: afterEach deletes the recipe it created (DELETE /api/recipes/{id}, which also retires the
 * category it orphans). The per-run suffix on the recipe name and category is kept as well, because
 * afterEach does not run if the process is killed — a Ctrl-C, crash, or CI timeout between create and
 * cleanup would otherwise leave a row whose name permanently collides with every future run. Belt and
 * braces: cleanup handles the normal path, the suffix keeps the abnormal path recoverable, and
 * filtering by a category only this run created stays deterministic either way.
 *
 * Selectors are role/label/placeholder based. Two traps worth knowing before you edit them:
 *  - The recipe's own "Name" field is a wrapping <label>, while each ingredient row's name input
 *    uses placeholder="Name". Both expose the accessible name "Name", so an unscoped
 *    getByRole('textbox', { name: 'Name' }) matches several elements and trips strict mode.
 *    Reach the recipe field with getByLabel (labels only, never placeholders) and the row fields
 *    through their fieldset.
 *  - The <fieldset> legends ("Ingredients", "Steps", "Categories") give each group an accessible
 *    name, which is what makes that scoping possible.
 */

/** Distinguishes this run's data from every previous run's leftovers. */
const runId = Math.random().toString(36).slice(2, 8);
const recipeName = `Garlic Butter Shrimp ${runId}`;
const runCategory = `E2E ${runId}`;

const ingredients = [
  { name: 'Shrimp', qty: '500', unit: 'g' },
  { name: 'Garlic', qty: '4', unit: 'cloves' },
];
const initialSteps = [
  'Pat the shrimp dry and season with salt.',
  'Sear the shrimp, then add garlic and butter.',
];
const addedStep = 'Finish with lemon juice and parsley.';

const group = (page: Page, name: string) => page.getByRole('group', { name });

/** A detail-page panel, located by its heading rather than its styling hooks. */
const panel = (page: Page, heading: string) =>
  page.locator('section').filter({ has: page.getByRole('heading', { name: heading, level: 2 }) });

/** Recipe cards are the only links wrapping an <h2>, which separates them from their tag lists. */
const cards = (page: Page) =>
  page.getByRole('link').filter({ has: page.getByRole('heading', { level: 2 }) });

const filterChip = (page: Page, name: string) =>
  page
    .getByRole('navigation', { name: 'Filter recipes by category' })
    .getByRole('button', { name, exact: true });

async function fillIngredient(
  page: Page,
  index: number,
  { name, qty, unit }: (typeof ingredients)[number],
) {
  const rows = group(page, 'Ingredients');
  await rows.getByPlaceholder('Name').nth(index).fill(name);
  await rows.getByPlaceholder('Qty').nth(index).fill(qty);
  await rows.getByPlaceholder('Unit').nth(index).fill(unit);
}

async function fillStep(page: Page, index: number, instruction: string) {
  await group(page, 'Steps').getByPlaceholder('Instruction').nth(index).fill(instruction);
}

/** Set once the recipe exists, so cleanup knows what to remove even if a later step fails. */
let createdRecipeId: number | null = null;

test.afterEach(async ({ request }) => {
  if (createdRecipeId === null) return;

  // Same origin as the UI: the dev server proxies /api to the API (see proxy.conf.js), so this needs
  // no second endpoint to discover. Deleting the recipe also retires the category it orphans.
  const response = await request.delete(`/api/recipes/${createdRecipeId}`);
  createdRecipeId = null;

  // Surfaced rather than swallowed — silent cleanup failure is how a database quietly fills up.
  expect(response.status(), 'cleanup should delete the recipe this run created').toBe(204);
});

test('create, view, edit, and filter a recipe', async ({ page }) => {
  await test.step('create the recipe', async () => {
    await page.goto('/');
    await page.getByRole('link', { name: 'New recipe' }).click();
    await expect(page.getByRole('heading', { name: 'New recipe', level: 1 })).toBeVisible();

    await page.getByLabel('Name', { exact: true }).fill(recipeName);
    await page
      .getByLabel('Description')
      .fill('Fast skillet shrimp in a lemony garlic butter sauce.');
    await page.getByLabel('Servings').fill('3');

    // Each group starts with one empty row; every ingredient/step past the first needs its own.
    await fillIngredient(page, 0, ingredients[0]);
    await group(page, 'Ingredients').getByRole('button', { name: 'Add ingredient' }).click();
    await fillIngredient(page, 1, ingredients[1]);

    await fillStep(page, 0, initialSteps[0]);
    await group(page, 'Steps').getByRole('button', { name: 'Add step' }).click();
    await fillStep(page, 1, initialSteps[1]);

    await group(page, 'Categories').getByRole('button', { name: 'Add category' }).click();
    await group(page, 'Categories').getByPlaceholder('e.g. Dessert').fill(runCategory);

    await page.getByRole('button', { name: 'Create recipe' }).click();

    // A successful create redirects to the new recipe's detail route.
    await expect(page).toHaveURL(/\/recipes\/\d+$/);

    // Record the id straight away: from here on, a failure still has something to clean up.
    createdRecipeId = Number(/\/recipes\/(\d+)$/.exec(page.url())![1]);
  });

  await test.step('detail shows ingredients and steps in order', async () => {
    await expect(page.getByRole('heading', { name: recipeName, level: 1 })).toBeVisible();
    await expect(page.getByText('🍴 Serves 3')).toBeVisible();

    const ingredientItems = panel(page, 'Ingredients').getByRole('listitem');
    await expect(ingredientItems).toHaveCount(ingredients.length);
    for (const [i, { name, qty, unit }] of ingredients.entries()) {
      await expect(ingredientItems.nth(i)).toContainText(`${qty} ${unit}`);
      await expect(ingredientItems.nth(i)).toContainText(name);
    }

    // An array assertion pins both the count and the order of the rendered steps.
    await expect(panel(page, 'Steps').getByRole('listitem')).toHaveText([
      new RegExp(`1\\s*${escapeRegExp(initialSteps[0])}`),
      new RegExp(`2\\s*${escapeRegExp(initialSteps[1])}`),
    ]);
  });

  await test.step('edit the recipe', async () => {
    await page.getByRole('link', { name: 'Edit' }).click();
    await expect(page.getByRole('heading', { name: 'Edit recipe', level: 1 })).toBeVisible();

    // The form should arrive pre-populated with what was just saved.
    await expect(page.getByLabel('Name', { exact: true })).toHaveValue(recipeName);
    await expect(group(page, 'Steps').getByPlaceholder('Instruction')).toHaveCount(
      initialSteps.length,
    );

    await page.getByLabel('Servings').fill('6');
    await group(page, 'Steps').getByRole('button', { name: 'Add step' }).click();
    await fillStep(page, 2, addedStep);

    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page).toHaveURL(/\/recipes\/\d+$/);

    await expect(page.getByText('🍴 Serves 6')).toBeVisible();
    await expect(panel(page, 'Steps').getByRole('listitem')).toHaveText([
      new RegExp(`1\\s*${escapeRegExp(initialSteps[0])}`),
      new RegExp(`2\\s*${escapeRegExp(initialSteps[1])}`),
      new RegExp(`3\\s*${escapeRegExp(addedStep)}`),
    ]);
  });

  await test.step('filter by category', async () => {
    await page.getByRole('link', { name: '← All recipes' }).click();

    // The header renders before the cards resolve, so gate on a card — count() never auto-waits
    // and would otherwise read 0 off the "Loading recipes…" state.
    //
    // Gate on *any* card, not this run's: the grid pages at PAGE_SIZE and this run's recipe sorts
    // wherever its name falls, so it is usually not on page 1 at all. Finding it unfiltered is the
    // filter step's job, below.
    await expect(cards(page).first()).toBeVisible();

    const unfilteredCount = await cards(page).count();
    expect(unfilteredCount).toBeGreaterThan(1);

    // This run invented its category, so it must narrow the grid to exactly this run's recipe.
    await filterChip(page, runCategory).click();
    await expect(filterChip(page, runCategory)).toHaveAttribute('aria-pressed', 'true');
    await expect(cards(page)).toHaveCount(1);
    await expect(cards(page).first()).toContainText(recipeName);

    await filterChip(page, 'All').click();
    await expect(cards(page)).toHaveCount(unfilteredCount);
  });
});

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
