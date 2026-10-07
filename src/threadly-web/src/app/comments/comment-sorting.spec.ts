import { ViewportScroller } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { filter, firstValueFrom } from 'rxjs';
import { routes } from '../app.routes';
import { CommentViewState } from './comment-view-state';
import { Comment, CommentSortBy, CommentSortDirection } from './comment.models';

describe('Comment sorting', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let router: Router;
  const scroll = { getScrollPosition: () => [0, 480], scrollToPosition: vi.fn() };
  const root: Comment = {
    id: 'root',
    username: 'Reader',
    email: 'reader@example.com',
    content: [{ type: 'text', html: 'A sorted root' }],
    createdAtUtc: '2026-10-05T12:00:00Z',
    parentId: null,
    replyCount: 12,
    attachments: [],
  };
  const query = (
    page = 2,
    sortBy: CommentSortBy = 'username',
    sortDirection: CommentSortDirection = 'asc',
  ) => `page=${page}&sortBy=${sortBy}&sortDirection=${sortDirection}`;
  const element = () => harness.routeNativeElement as HTMLElement;
  const select = (id: string) => element().querySelector<HTMLSelectElement>(`#${id}`)!;
  const button = (label: string) =>
    Array.from(element().querySelectorAll('button')).find((value) =>
      value.textContent?.includes(label),
    )!;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideLocationMocks(),
        { provide: ViewportScroller, useValue: scroll },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
    router.setUpLocationChangeListener();
    scroll.scrollToPosition.mockClear();
  });
  afterEach(() => http.verify());

  async function render() {
    await harness.fixture.whenStable();
    harness.detectChanges();
  }
  function respond(parameters = query(), items = [root], page = 2, totalCount = 60) {
    const request = http.expectOne(`/api/comments?${parameters}`);
    expect(request.request.method).toBe('GET');
    request.flush({ items, page, pageSize: 25, totalCount });
  }
  async function open(parameters = query(), items = [root], page = 2, totalCount = 60) {
    await harness.navigateByUrl(`/comments?${parameters}`);
    respond(parameters, items, page, totalCount);
    await render();
  }
  async function change(id: string, value: string) {
    select(id).value = value;
    select(id).dispatchEvent(new Event('change', { bubbles: true }));
    await render();
  }
  async function back() {
    const navigated = firstValueFrom(
      router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    button('← Back').click();
    await navigated;
    await render();
  }

  it.each([
    ['date', 'asc'],
    ['date', 'desc'],
    ['username', 'asc'],
    ['username', 'desc'],
    ['email', 'asc'],
    ['email', 'desc'],
  ] as const)(
    'restores %s %s from a direct URL and sends the full API query',
    async (field, direction) => {
      await open(query(2, field, direction));
      expect(select('sort-by').value).toBe(field);
      expect(select('sort-direction').value).toBe(direction);
      expect(element().textContent).toContain('Page 2');
      expect(element().textContent).not.toContain('Latest comments');
      expect(element().querySelector('label[for="sort-by"]')?.textContent).toBe('Sort by');
      expect(element().querySelector('label[for="sort-direction"]')?.textContent).toBe('Direction');
    },
  );

  it('uses defaults for omitted URL parameters', async () => {
    await harness.navigateByUrl('/comments');
    respond(query(1, 'date', 'desc'), [], 1, 0);
    await render();
    expect(select('sort-by').value).toBe('date');
    expect(select('sort-direction').value).toBe('desc');
    expect(router.url).toBe('/comments');
  });

  it.each([
    ['page=0&sortBy=email&sortDirection=asc', query(1, 'email', 'asc')],
    ['page=2147483648&sortBy=email', query(1, 'email', 'desc')],
    ['page=1.5', query(1, 'date', 'desc')],
    ['page=1e2', query(1, 'date', 'desc')],
    ['page=2&page=3', query(1, 'date', 'desc')],
    ['page=&sortBy=&sortDirection=', query(1, 'date', 'desc')],
    ['page=2&sortBy=0&sortDirection=ASC', query(2, 'date', 'desc')],
    ['page=2&sortBy=username&sortDirection=wrong', query(2, 'username', 'desc')],
    ['page=2&sortBy=email&sortBy=username&sortDirection=asc', query(2, 'date', 'asc')],
  ])('normalizes invalid URL %s once using replaceUrl', async (input, expected) => {
    const navigate = vi.spyOn(router, 'navigate');
    await harness.navigateByUrl(`/comments?${input}`);
    await render();
    expect(router.url).toBe(`/comments?${expected}`);
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate.mock.calls[0][1]?.replaceUrl).toBe(true);
    respond(expected, [], Number(new URLSearchParams(expected).get('page')), 0);
    await render();
  });

  it('resets the page on either sort change and preserves sorting on pagination and empty-page return', async () => {
    await open();
    await change('sort-by', 'email');
    expect(router.url).toBe(`/comments?${query(1, 'email', 'asc')}`);
    respond(query(1, 'email', 'asc'), [root], 1);
    await render();
    button('Next').click();
    await render();
    expect(router.url).toBe(`/comments?${query(2, 'email', 'asc')}`);
    respond(query(2, 'email', 'asc'));
    await render();
    await change('sort-direction', 'desc');
    expect(router.url).toBe(`/comments?${query(1, 'email', 'desc')}`);
    respond(query(1, 'email', 'desc'), [root], 1);
    await render();
    await open(query(10, 'email', 'desc'), [], 10);
    button('Back to first page').click();
    await render();
    expect(router.url).toBe(`/comments?${query(1, 'email', 'desc')}`);
    respond(query(1, 'email', 'desc'), [root], 1);
    await render();
  });

  it('cancels obsolete requests during rapid changes', async () => {
    await harness.navigateByUrl(`/comments?${query()}`);
    const older = http.expectOne(`/api/comments?${query()}`);
    await change('sort-by', 'email');
    const intermediate = http.expectOne(`/api/comments?${query(1, 'email', 'asc')}`);
    expect(older.cancelled).toBe(true);
    await change('sort-direction', 'desc');
    expect(intermediate.cancelled).toBe(true);
    expect(element().textContent).toContain('Loading comments');
    respond(query(1, 'email', 'desc'), [root], 1);
    await render();
    expect(element().textContent).toContain('A sorted root');
    expect(select('sort-direction').value).toBe('desc');
  });

  it('restores sorted cards, nested replies, cursor and scroll after opening a thread and Back', async () => {
    await open();
    button('Show replies').click();
    http.expectOne('/api/comments/root/replies').flush({
      items: [{ ...root, id: 'child', parentId: 'root', replyCount: 1 }],
      nextCursor: 'next',
    });
    await render();
    button('Show replies').click();
    http.expectOne('/api/comments/child/replies').flush({
      items: [{ ...root, id: 'grandchild', parentId: 'child', replyCount: 0 }],
      nextCursor: null,
    });
    await render();
    element().querySelector<HTMLAnchorElement>('a[href="/comments/child"]')!.click();
    await render();
    http.expectOne('/api/comments/child').flush({ ...root, id: 'child', parentId: 'root' });
    await render();
    await back();
    expect(router.url).toBe(`/comments?${query()}`);
    expect(select('sort-by').value).toBe('username');
    expect(select('sort-direction').value).toBe('asc');
    expect(element().querySelector('#replies-child')?.hasAttribute('hidden')).toBe(false);
    expect(element().querySelectorAll('app-comment-card')).toHaveLength(3);
    expect(scroll.scrollToPosition).toHaveBeenLastCalledWith([0, 480]);
    http.expectNone((request) => request.method === 'GET');
    button('Show more').click();
    http.expectOne('/api/comments/root/replies?cursor=next').flush({ items: [], nextCursor: null });
    await render();
  });

  it('does not reuse a restored cache whose sort does not match the URL', async () => {
    await open();
    const cached = TestBed.inject(CommentViewState).current;
    await harness.navigateByUrl('/comments/root');
    http.expectOne('/api/comments/root').flush(root);
    await render();
    cached.feedQuery = { page: 2, sortBy: 'email', sortDirection: 'asc' };
    await back();
    respond(query(), [{ ...root, content: [{ type: 'text', html: 'Fresh selection' }] }]);
    await render();
    expect(element().textContent).toContain('Fresh selection');
  });

  it('keeps reply expansion separate between sorts containing the same root', async () => {
    await open();
    button('Show replies').click();
    http.expectOne('/api/comments/root/replies').flush({ items: [], nextCursor: null });
    await render();
    await change('sort-by', 'email');
    respond(query(1, 'email', 'asc'), [root], 1);
    await render();
    expect(button('Hide replies')).toBeUndefined();
    expect(button('Show replies')).toBeTruthy();
  });

  it.each([1, 2])(
    'reloads page one with the chosen sort after create from page %i and keeps the notice',
    async (page) => {
      await open(query(page, 'email', 'asc'), [root], page);
      button('New comment').click();
      await render();
      for (const [field, value] of Object.entries({
        username: 'Zulu',
        email: 'zulu@example.com',
        text: 'Newly posted',
      })) {
        const input = element().querySelector<HTMLInputElement>(`#${field}`)!;
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      element()
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      http.expectOne({ method: 'POST', url: '/api/comments' }).flush({ ...root, id: 'new' });
      await render();
      respond(query(1, 'email', 'asc'), [root], 1, 61);
      await render();
      expect(router.url).toBe(`/comments?${query(1, 'email', 'asc')}`);
      expect(element().textContent).toContain('Comment posted.');
      expect(element().querySelector('a[href="/comments/new"]')).toBeNull();
      expect(select('sort-by').value).toBe('email');
      expect(select('sort-direction').value).toBe('asc');
    },
  );
});
