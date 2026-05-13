import { test, expect } from '@playwright/test';

// Golden-path smoke: author registers via UI, creates repo + document, then
// submits a proposal. A second user is registered out-of-band and added as a
// Reviewer (the API forbids self-review), then switched into via localStorage
// swap to approve through the UI.
//
// One spec, one browser context — splitting into multiple `test()` blocks
// would only buy us isolation we don't need, and would double the number of
// dotnet processes Playwright starts during local dev (`reuseExistingServer`
// is per-test-suite, not per-test).

function newUser(label: string) {
  const ts = Date.now().toString(36);
  const rand = Math.random().toString(36).slice(2, 8);
  return {
    username: `e2e${label}${ts}${rand}`.toLowerCase(),
    email: `e2e+${label}${ts}${rand}@example.test`,
    password: 'correct-horse-battery-staple',
  };
}

async function acceptTosIfPresent(page: import('@playwright/test').Page) {
  const tos = page.getByRole('checkbox');
  if (await tos.count()) {
    await tos.first().check();
  }
}

test('register → create repo → create document → submit proposal → approve', async ({ page, request }) => {
  const author = newUser('a');
  const reviewer = newUser('r');
  const repoName = 'Smoke Test';
  const repoSlug = 'smoke-test';
  const docPath = 'intro';

  // 1. Register author through the UI. As the first user on a fresh DB they
  //    become an admin, which bypasses the 24h new-account gate that would
  //    otherwise block proposal creation.
  await page.goto('/register');
  await expect(page.getByRole('heading', { name: 'Create account' })).toBeVisible();
  await page.getByLabel('Username').fill(author.username);
  await page.getByLabel('Email').fill(author.email);
  await page.getByLabel('Password').fill(author.password);
  await acceptTosIfPresent(page);
  await page.getByRole('button', { name: 'Create account' }).click();

  await page.waitForURL('**/');
  await expect(page.getByRole('heading', { name: 'Repositories', exact: true })).toBeVisible();

  // Grab the author's JWT for the out-of-band API calls below.
  const authorToken = await page.evaluate(() => localStorage.getItem('sg_token'));
  expect(authorToken).toBeTruthy();

  // 2. Create the repository (private — adding the reviewer as a member gives
  //    them access without exposing the repo publicly).
  await page.getByRole('button', { name: 'New repository' }).click();
  await page.getByLabel('Name').fill(repoName);
  await page.getByRole('button', { name: 'Create' }).click();

  await page.waitForURL(`**/${author.username}/${repoSlug}`);
  await expect(
    page.getByRole('heading', { name: new RegExp(`${author.username}/${repoName}`) })
  ).toBeVisible();

  // 3. Create the document.
  await page.getByRole('link', { name: 'New document' }).click();
  await page.waitForURL(`**/${author.username}/${repoSlug}/edit/new`);
  await page.getByLabel('Path').fill(docPath);
  await page.getByLabel('Commit message').fill('Initial content');
  await page.getByPlaceholder('Write your markdown here...').fill('# Hello\n\nFirst doc.\n');
  await page.getByRole('button', { name: 'Save' }).click();

  await page.waitForURL(`**/${author.username}/${repoSlug}/${docPath}`);
  await expect(page.getByRole('heading', { name: 'Hello', level: 1 })).toBeVisible();

  // 4. Register the reviewer out-of-band and add them to the repo as a
  //    Reviewer. Doing this via the API keeps the UI test focused on the
  //    main flow — there's a separate (future) members-page spec for the
  //    member-management UI itself.
  const regResp = await request.post('/api/v1/auth/register', {
    data: {
      username: reviewer.username,
      email: reviewer.email,
      password: reviewer.password,
      acceptTos: true,
    },
  });
  expect(regResp.ok(), `register reviewer: ${await regResp.text()}`).toBeTruthy();
  const reviewerToken: string = (await regResp.json()).token;

  const addMember = await request.post(
    `/api/v1/repositories/${author.username}/${repoSlug}/members`,
    {
      headers: { Authorization: `Bearer ${authorToken}` },
      data: { username: reviewer.username, role: 'Reviewer' },
    },
  );
  expect(addMember.ok(), `add reviewer: ${await addMember.text()}`).toBeTruthy();

  // 5. Create the proposal as the author.
  await page.goto(`/${author.username}/${repoSlug}/proposals`);
  await page.getByRole('link', { name: 'New proposal' }).click();
  await page.waitForURL(`**/${author.username}/${repoSlug}/proposals/new`);

  await page.getByLabel('Title').fill('Update intro');
  await page.getByLabel('Document path').fill(`${docPath}.md`);
  await page.getByPlaceholder('Write your proposed markdown content...').fill(
    '# Hello\n\nUpdated text from the smoke test.\n'
  );
  await page.getByRole('button', { name: 'Create Proposal' }).click();

  await page.waitForURL(
    new RegExp(`/${author.username}/${repoSlug}/proposals/[0-9a-f-]{36}$`)
  );
  const proposalUrl = page.url();

  await expect(page.getByRole('heading', { name: /Update intro/ })).toBeVisible();
  await expect(page.getByText('Open', { exact: true })).toBeVisible();

  // 6. Switch the SPA over to the reviewer's identity and approve. localStorage
  //    is the canonical auth state for the SPA, so swapping it + reload is
  //    indistinguishable from a logout/login as far as the app is concerned.
  await page.evaluate((tok) => localStorage.setItem('sg_token', tok), reviewerToken);
  await page.goto(proposalUrl);

  await expect(page.getByRole('button', { name: 'Approve' })).toBeVisible();
  await page.getByRole('button', { name: 'Approve' }).click();

  await expect(page.getByText('Approved', { exact: true })).toBeVisible({ timeout: 15_000 });
});
