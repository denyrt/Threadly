import { provideTestTurnstile } from './turnstile-testing';
import { provideHttpClient } from '@angular/common/http';
import { ViewportScroller } from '@angular/common';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { Comment, CommentPage } from './comment.models';

describe('Comments', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  const comment: Comment = {
    id: '019abcde-1234-7000-8000-123456789abc',
    username: 'Denis42',
    email: 'denis@example.com',
    content: [{ type: 'text', html: 'Hello, Threadly!\nA second line.' }],
    createdAtUtc: '2026-10-05T10:30:00Z',
    parentId: null,
    replyCount: 0,
    attachments: [],
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideTestTurnstile(),
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ViewportScroller,
          useValue: { getScrollPosition: () => [0, 0], scrollToPosition: vi.fn() },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => http.verify());

  function element(): HTMLElement {
    return harness.routeNativeElement as HTMLElement;
  }

  function button(label: string): HTMLButtonElement {
    const button = Array.from(element().querySelectorAll('button')).find((value) =>
      value.textContent?.includes(label),
    );
    if (!button) {
      throw new Error(`Button not found: ${label}`);
    }
    return button;
  }

  async function render() {
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  function respondWithPage(page: number, items: Comment[] = [], totalCount = items.length) {
    const response: CommentPage = { items, page, pageSize: 25, totalCount };
    http
      .expectOne({
        method: 'GET',
        url: `/api/comments?page=${page}&sortBy=date&sortDirection=desc`,
      })
      .flush(response);
  }

  async function openFeed(page = 1, items: Comment[] = [], totalCount = items.length) {
    await harness.navigateByUrl(`/comments?page=${page}`);
    respondWithPage(page, items, totalCount);
    await render();
  }

  function fillField(id: string, value: string) {
    const input = element().querySelector(`#${id}`) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  function submitForm() {
    element()
      .querySelector('form')
      ?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  }

  async function openComposer() {
    button('New comment').click();
    await render();
  }

  function fillValidComment() {
    fillField('username', comment.username);
    fillField('email', comment.email);
    fillField('text', comment.content[0].html);
  }

  it('shows loading and an empty feed', async () => {
    await harness.navigateByUrl('/');
    expect(element().textContent).toContain('Loading comments…');
    respondWithPage(1);
    await render();

    expect(element().textContent).toContain('The conversation starts here.');
    expect(element().querySelector('nav.pagination')).toBeNull();
  });

  it('loads a requested page and navigates to the next page', async () => {
    await openFeed(1, [comment], 26);
    expect(button('Previous').disabled).toBe(true);
    expect(element().textContent).toContain(comment.content[0].html);
    expect(element().querySelector('.permalink')?.getAttribute('href')).toBe(
      `/comments/${comment.id}`,
    );

    button('Next').click();
    await render();
    expect(TestBed.inject(Router).url).toBe('/comments?page=2&sortBy=date&sortDirection=desc');
    respondWithPage(
      2,
      [{ ...comment, id: 'second', content: [{ type: 'text', html: 'Second page' }] }],
      26,
    );
    await render();
    expect(element().textContent).toContain('Second page');
    expect(element().textContent).not.toContain('Hello, Threadly!');
    expect(button('Next').disabled).toBe(true);
  });

  it('offers a return to the first page for an empty distant page', async () => {
    await openFeed(10, [], 1);
    expect(element().textContent).toContain('This page is empty.');
    button('Back to first page').click();
    await render();
    respondWithPage(1, [comment]);
    await render();
    expect(element().textContent).toContain(comment.username);
  });

  it('recovers from a failed list request', async () => {
    await harness.navigateByUrl('/comments');
    http
      .expectOne('/api/comments?page=1&sortBy=date&sortDirection=desc')
      .flush({}, { status: 503, statusText: 'Service Unavailable' });
    await render();
    expect(element().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');

    button('Try again').click();
    respondWithPage(1, [comment]);
    await render();
    expect(element().querySelector('[role="alert"]')).toBeNull();
    expect(element().textContent).toContain(comment.content[0].html);
  });

  it('validates fields locally and does not post invalid or whitespace-only text', async () => {
    await openFeed();
    await openComposer();
    fillField('username', 'bad_name');
    fillField('email', 'invalid');
    fillField('text', '   ');
    submitForm();
    await render();

    http.expectNone((request) => request.method === 'POST');
    expect(element().querySelector('#username-error')?.textContent).toContain(
      'ASCII letters and digits',
    );
    expect(element().querySelector('#email-error')?.textContent).toContain('valid email');
    expect(element().querySelector('#text-error')?.textContent).toContain('required');
  });

  it('requires every field before posting', async () => {
    await openFeed();
    await openComposer();
    submitForm();
    await render();

    http.expectNone((request) => request.method === 'POST');
    for (const field of ['username', 'email', 'text']) {
      expect(element().querySelector(`#${field}-error`)?.textContent).toContain('required');
      expect(element().querySelector(`#${field}`)?.getAttribute('aria-invalid')).toBe('true');
    }
  });

  it.each([
    { field: 'username', limit: 32, value: 'a'.repeat(33) },
    {
      field: 'email',
      limit: 254,
      value: `${'a'.repeat(65)}@${'b'.repeat(63)}.${'c'.repeat(63)}.${'d'.repeat(61)}`,
    },
    { field: 'text', limit: 2000, value: 'a'.repeat(2001) },
  ])('rejects $field longer than $limit characters', async ({ field, limit, value }) => {
    await openFeed();
    await openComposer();
    fillValidComment();
    fillField(field, value);
    submitForm();
    await render();

    http.expectNone((request) => request.method === 'POST');
    expect(element().querySelector(`#${field}-error`)?.textContent).toContain(
      `Use at most ${limit} characters.`,
    );
  });

  it('accepts valid values at the maximum lengths', async () => {
    await openFeed();
    await openComposer();
    const values = {
      username: 'a'.repeat(32),
      email: `${'a'.repeat(64)}@${'b'.repeat(63)}.${'c'.repeat(63)}.${'d'.repeat(61)}`,
      text: 'a'.repeat(2000),
    };
    for (const [field, value] of Object.entries(values)) {
      fillField(field, value);
    }
    submitForm();

    const post = http.expectOne({ method: 'POST', url: '/api/comments' });
    const payload = {
      captchaToken: 'test-token',
      username: values.username,
      email: values.email,
      content: [{ type: 'text' as const, html: values.text }],
    };
    expect(post.request.body).toEqual(payload);
    post.flush({ ...comment, ...payload });
    respondWithPage(1, [{ ...comment, ...payload }]);
    await render();
    expect(element().textContent).toContain('Comment posted.');
  });

  it('prevents duplicate submissions and refreshes the first page after creating from page two', async () => {
    await openFeed(2, [comment], 26);
    await openComposer();
    fillValidComment();
    submitForm();
    await render();

    const post = http.expectOne({ method: 'POST', url: '/api/comments' });
    expect(post.request.body).toEqual({
      captchaToken: 'test-token',
      username: comment.username,
      email: comment.email,
      content: comment.content,
    });
    expect(button('Posting…').disabled).toBe(true);
    expect(button('Cancel').disabled).toBe(true);
    submitForm();
    http.expectNone((request) => request.method === 'POST');

    post.flush(comment);
    await render();
    respondWithPage(1, [comment], 27);
    await render();
    expect(TestBed.inject(Router).url).toBe('/comments?page=1&sortBy=date&sortDirection=desc');
    expect(element().querySelector('form')).toBeNull();
    expect(element().textContent).toContain('Comment posted.');
    expect(element().textContent).toContain('27 comments');
  });

  it('shows server field errors, preserves the draft, and permits a corrected submission', async () => {
    await openFeed();
    await openComposer();
    fillValidComment();
    submitForm();
    http
      .expectOne({ method: 'POST', url: '/api/comments' })
      .flush(
        { status: 400, errors: { email: ['Email was rejected by the server.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await render();
    expect(element().querySelector('#email-error')?.textContent).toContain(
      'rejected by the server',
    );
    expect((element().querySelector('#text') as HTMLTextAreaElement).value).toBe(
      comment.content[0].html,
    );

    fillField('email', 'new@example.com');
    submitForm();
    http
      .expectOne({ method: 'POST', url: '/api/comments' })
      .flush({ ...comment, email: 'new@example.com' });
    respondWithPage(1, [{ ...comment, email: 'new@example.com' }]);
    await render();
    expect(element().textContent).toContain('new@example.com');
    expect(element().querySelector('form')).toBeNull();
  });

  it('keeps the draft editable after a network failure', async () => {
    await openFeed();
    await openComposer();
    fillValidComment();
    submitForm();
    http
      .expectOne({ method: 'POST', url: '/api/comments' })
      .error(new ProgressEvent('network error'));
    await render();

    expect(element().querySelector('[role="alert"]')?.textContent).toContain(
      'may have been posted',
    );
    expect((element().querySelector('#text') as HTMLTextAreaElement).value).toBe(
      comment.content[0].html,
    );
    expect((element().querySelector('#text') as HTMLTextAreaElement).disabled).toBe(false);
    expect(button('Post comment').disabled).toBe(false);
  });

  it('reads a comment by its URL and renders allowed markup', async () => {
    await harness.navigateByUrl(`/comments/${comment.id}`);
    expect(element().textContent).toContain('Loading comment…');
    http.expectOne({ method: 'GET', url: `/api/comments/${comment.id}` }).flush({
      ...comment,
      content: [{ type: 'text', html: '<strong>This is text</strong>\nSecond line' }],
    });
    await render();

    const text = element().querySelector('.text') as HTMLElement;
    expect(text.textContent).toBe('This is text\nSecond line');
    expect(text.querySelector('strong')?.textContent).toBe('This is text');
    expect(element().querySelector('time')?.getAttribute('datetime')).toBe(comment.createdAtUtc);
    expect(element().querySelector('.permalink')).toBeNull();
  });

  it('shows a missing comment and provides a way back to the feed', async () => {
    await harness.navigateByUrl(`/comments/${comment.id}`);
    http
      .expectOne(`/api/comments/${comment.id}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    await render();

    expect(element().textContent).toContain('This comment could not be found.');
    expect(element().querySelector('a')?.getAttribute('href')).toBe('/comments');
    expect(element().querySelector('button')).toBeNull();
  });

  it('cancels an obsolete detail request when navigating to another comment', async () => {
    await harness.navigateByUrl(`/comments/${comment.id}`);
    const previous = http.expectOne(`/api/comments/${comment.id}`);
    await harness.navigateByUrl('/comments/next');
    expect(previous.cancelled).toBe(true);
    http
      .expectOne('/api/comments/next')
      .flush({ ...comment, id: 'next', content: [{ type: 'text', html: 'Current comment' }] });
    await render();
    expect(element().textContent).toContain('Current comment');
  });
});
