import { provideQuietCommentsLive } from './comments-live-testing';
import { provideTestTurnstile } from './turnstile-testing';
import { Location, ViewportScroller } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, provideRouter, Router } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { Comment } from './comment.models';

describe('Comment replies', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const scroll = { getScrollPosition: () => [0, 320], scrollToPosition: vi.fn() };
  const root: Comment = {
    id: 'root',
    username: 'Reader',
    email: 'reader@example.com',
    content: [{ type: 'text', html: 'Root comment' }],
    createdAtUtc: '2026-10-05T12:00:00.1234567Z',
    parentId: null,
    replyCount: 100,
    attachments: [],
  };
  const reply = (id: string, parentId = root.id, replyCount = 0): Comment => ({
    ...root,
    id,
    parentId,
    replyCount,
    content: [{ type: 'text', html: `Reply ${id}` }],
  });

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideTestTurnstile(),
        provideQuietCommentsLive(),
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideLocationMocks(),
        { provide: ViewportScroller, useValue: scroll },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    TestBed.inject(Router).setUpLocationChangeListener();
    scroll.scrollToPosition.mockClear();
  });
  afterEach(() => http.verify());

  const element = () => harness.routeNativeElement as HTMLElement;
  async function render() {
    await harness.fixture.whenStable();
    harness.detectChanges();
  }
  function button(label: string, within: ParentNode = element()): HTMLButtonElement {
    const found = Array.from(within.querySelectorAll('button')).find((item) =>
      item.textContent?.trim().startsWith(label),
    );
    if (!found) throw new Error(`Missing button: ${label}`);
    return found;
  }
  const branch = (id: string) => element().querySelector(`#replies-${id}`)!;
  async function openFeed(items = [root], page = 2) {
    await harness.navigateByUrl(`/comments?page=${page}`);
    http
      .expectOne(`/api/comments?page=${page}&sortBy=date&sortDirection=desc`)
      .flush({ items, page, pageSize: 25, totalCount: 26 });
    await render();
  }
  function flushReplies(
    parentId: string,
    items: Comment[],
    nextCursor: string | null = null,
    cursor?: string,
  ) {
    const url = `/api/comments/${parentId}/replies` + (cursor ? `?cursor=${cursor}` : '');
    http.expectOne(url).flush({ items, nextCursor });
  }
  async function compose(parentId = root.id) {
    button('Reply').click();
    await render();
    for (const [field, value] of Object.entries({
      username: 'Writer',
      email: 'writer@example.com',
      text: 'New reply',
    })) {
      const input = element().querySelector(`#reply-${parentId}-${field}`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }
  function submit() {
    element()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  }

  it('loads only on expansion, prevents overlapping requests, and retains children and cursors on collapse', async () => {
    await openFeed();
    http.expectNone((request) => request.url.endsWith('/replies'));
    button('Show replies (100)').click();
    await render();
    expect(branch(root.id).textContent).toContain('Loading replies');
    button('Hide replies').click();
    await render();
    button('Show replies (100)').click();
    await render();
    flushReplies(root.id, [reply('one')], 'next');
    await render();
    button('Hide replies').click();
    await render();
    expect(branch(root.id).hasAttribute('hidden')).toBe(true);
    button('Show replies (100)').click();
    await render();
    http.expectNone((request) => request.url.endsWith('/replies'));
    button('Show more').click();
    button('Show more').click();
    flushReplies(root.id, [reply('one'), reply('two')], null, 'next');
    await render();
    expect(branch(root.id).querySelectorAll('app-comment-thread').length).toBe(2);
    expect(branch(root.id).textContent).toContain('Reply two');
  });

  it('isolates branch failures and retries the same cursor without losing loaded children', async () => {
    await openFeed([
      root,
      { ...root, id: 'other', replyCount: 1, content: [{ type: 'text', html: 'Other root' }] },
    ]);
    button('Show replies (100)').click();
    flushReplies(root.id, [reply('one')], 'next');
    await render();
    button('Show replies (1)').click();
    flushReplies('other', [reply('other-child', 'other')]);
    await render();
    button('Show more').click();
    http
      .expectOne('/api/comments/root/replies?cursor=next')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await render();
    expect(branch('other').textContent).toContain('Reply other-child');
    expect(branch(root.id).textContent).toContain('Reply one');
    button('Retry replies').click();
    flushReplies(root.id, [reply('two')], null, 'next');
    await render();
    expect(element().querySelector('[role="alert"]')).toBeNull();
  });

  it('retries an initial failure and shows an empty branch', async () => {
    await openFeed();
    button('Show replies (100)').click();
    http.expectOne('/api/comments/root/replies').error(new ProgressEvent('error'));
    await render();
    button('Retry replies').click();
    flushReplies(root.id, []);
    await render();
    expect(branch(root.id).textContent).toContain('No replies yet.');
  });

  it('posts on page two without inserting into a partial branch or disturbing its cursor', async () => {
    await openFeed();
    button('Show replies (100)').click();
    flushReplies(
      root.id,
      Array.from({ length: 10 }, (_, index) => reply(`item${index}`)),
      'page-two',
    );
    await render();
    await compose();
    expect(element().textContent).toContain('Reply to Reader');
    submit();
    const post = http.expectOne({ method: 'POST', url: '/api/comments' });
    expect(post.request.body.parentId).toBe(root.id);
    submit();
    http.expectNone((request) => request.method === 'POST');
    await render();
    expect(button('Posting…').disabled).toBe(true);
    post.flush(reply('new'));
    await render();
    expect(TestBed.inject(Router).url).toBe('/comments?page=2');
    expect(element().textContent).toContain('Reply added.');
    expect(branch(root.id).querySelectorAll('app-comment-thread').length).toBe(10);
    expect(branch(root.id).textContent).not.toContain('Reply new');
    expect(element().querySelector('a[href="/comments/new"]')).not.toBeNull();
    button('Hide replies').click();
    await render();
    expect(button('Show replies (100)')).toBeTruthy();
    button('Show replies (100)').click();
    await render();
    expect(branch(root.id).textContent).toContain('Refresh replies before loading more');
    button('Refresh replies').click();
    flushReplies(root.id, [reply('first-page')], 'page-two');
    await render();
    http.expectNone((request) => request.url === '/api/comments');
  });

  it('refreshes only the first page even with nextCursor and retains the previous list on failure', async () => {
    await openFeed([{ ...root, replyCount: 1 }]);
    button('Show replies (1)').click();
    flushReplies(root.id, [reply('older')]);
    await render();
    await compose();
    submit();
    http.expectOne({ method: 'POST', url: '/api/comments' }).flush(reply('new'));
    await render();
    http.expectNone('/api/comments/root/replies');
    button('Refresh replies').click();
    http
      .expectOne('/api/comments/root/replies')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await render();
    expect(branch(root.id).textContent).toContain('Reply older');
    expect(element().textContent).toContain('Reply added.');
    button('Retry replies').click();
    flushReplies(root.id, [reply('server-first')], 'continuation');
    http.expectNone('/api/comments/root/replies?cursor=continuation');
    await render();
    expect(
      Array.from(branch(root.id).querySelectorAll('.text')).map((item) => item.textContent),
    ).toEqual(['Reply server-first']);
    button('Show more').click();
    flushReplies(root.id, [reply('older'), reply('new')], null, 'continuation');
    await render();
  });

  it('keeps failed drafts and restores focus on cancel with unique IDs across composers', async () => {
    await openFeed();
    button('＋ New comment').click();
    await render();
    await compose();
    const ids = Array.from(element().querySelectorAll('[id]')).map((item) => item.id);
    expect(ids.length).toBe(new Set(ids).size);
    const form = element().querySelector('#composer-root form')!;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    http
      .expectOne({ method: 'POST', url: '/api/comments' })
      .flush(
        { errors: { parentId: ['Parent is unavailable.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await render();
    expect((element().querySelector('#reply-root-text') as HTMLTextAreaElement).value).toBe(
      'New reply',
    );
    expect(element().textContent).toContain('Parent is unavailable.');
    const opener = button('Reply');
    const focus = vi.spyOn(opener, 'focus');
    button('Cancel', element().querySelector('#composer-root')!).click();
    await render();
    expect(focus).toHaveBeenCalled();
    expect(element().querySelector('#composer-root')).toBeNull();
  });

  it('continues after four levels, opens the destination replies, and restores the feed path on back', async () => {
    await openFeed([{ ...root, replyCount: 1 }]);
    for (const [parent, child] of [
      ['root', 'level2'],
      ['level2', 'level3'],
      ['level3', 'level4'],
    ]) {
      button('Show replies (1)').click();
      flushReplies(parent, [reply(child, parent, 1)]);
      await render();
    }
    http.expectNone('/api/comments/level4/replies');
    const continuation = element().querySelector(
      'a[href="/comments/level4?replies=1"]',
    ) as HTMLAnchorElement;
    expect(continuation.target).not.toBe('_blank');
    continuation.click();
    await render();
    http.expectOne('/api/comments/level4').flush(reply('level4', 'level3', 1));
    await render();
    flushReplies('level4', [reply('level5', 'level4')]);
    await render();
    expect(element().textContent).toContain('Reply level5');
    expect(element().querySelector('a[href="/comments/level3"]')).not.toBeNull();
    const navigatedBack = firstValueFrom(
      TestBed.inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    button('← Back').click();
    await navigatedBack;
    await render();
    expect(TestBed.inject(Router).url).toBe('/comments?page=2');
    expect(element().textContent).toContain('Reply level4');
    expect(branch(root.id).hasAttribute('hidden')).toBe(false);
    expect(scroll.scrollToPosition).toHaveBeenLastCalledWith([0, 320]);
    http.expectNone((request) => request.method === 'GET');
  });

  it('supports direct reply URLs with a parent link and a feed exit and collapses replies initially', async () => {
    await harness.navigateByUrl('/comments/child');
    http.expectOne('/api/comments/child').flush(reply('child', 'root', 1));
    await render();
    expect(element().querySelector('a[href="/comments"]')).not.toBeNull();
    expect(element().querySelector('a[href="/comments/root"]')).not.toBeNull();
    http.expectNone('/api/comments/child/replies');
    expect(button('Show replies (1)')).toBeTruthy();
  });

  it('goes back through successive comment views', async () => {
    await openFeed();
    await harness.navigateByUrl('/comments/one');
    http.expectOne('/api/comments/one').flush(reply('one'));
    await render();
    await harness.navigateByUrl('/comments/two');
    http.expectOne('/api/comments/two').flush(reply('two', 'one'));
    await render();
    const navigatedBack = firstValueFrom(
      TestBed.inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    TestBed.inject(Location).back();
    await navigatedBack;
    await render();
    expect(TestBed.inject(Router).url).toBe('/comments/one');
    expect(element().textContent).toContain('Reply one');
    http.expectNone('/api/comments/one');
  });

  it('keeps invalidation without rereading when a reply is posted during loading', async () => {
    await openFeed([{ ...root, replyCount: 1 }]);
    button('Show replies (1)').click();
    const initial = http.expectOne('/api/comments/root/replies');
    await compose();
    submit();
    http.expectOne({ method: 'POST', url: '/api/comments' }).flush(reply('new'));
    initial.flush({ items: [reply('older')], nextCursor: null });
    http.expectNone('/api/comments/root/replies');
    await render();
    expect(branch(root.id).textContent).not.toContain('Reply new');
    expect(branch(root.id).textContent).toContain('Refresh replies before loading more');
    expect(branch(root.id).querySelectorAll('app-comment-thread').length).toBe(1);
  });

  it('does not recursively refresh or clear a second reply posted during refresh', async () => {
    await openFeed([{ ...root, replyCount: 1 }]);
    button('Show replies (1)').click();
    flushReplies(root.id, [reply('older')]);
    await render();
    await compose();
    submit();
    http.expectOne({ method: 'POST', url: '/api/comments' }).flush(reply('first'));
    await render();
    button('Refresh replies').click();
    const refresh = http.expectOne('/api/comments/root/replies');
    await render();
    await compose();
    submit();
    http.expectOne({ method: 'POST', url: '/api/comments' }).flush(reply('second'));
    refresh.flush({ items: [reply('older'), reply('first')], nextCursor: null });
    http.expectNone('/api/comments/root/replies');
    await render();
    expect(branch(root.id).querySelectorAll('app-comment-thread').length).toBe(2);
    expect(branch(root.id).textContent).toContain('Refresh replies before loading more');
    button('Hide replies').click();
    await render();
    expect(button('Show replies (1)')).toBeTruthy();
  });

  it('updates the immediate parent in saved views and preserves neighbouring branches after posting deeper', async () => {
    await openFeed([{ ...root, replyCount: 1 }]);
    button('Show replies (1)').click();
    flushReplies(root.id, [reply('child', 'root', 1)]);
    await render();
    button('Show replies (1)').click();
    flushReplies('child', [reply('grandchild', 'child')]);
    await render();
    await harness.navigateByUrl('/comments/child');
    http.expectOne('/api/comments/child').flush(reply('child', 'root', 1));
    await render();
    await compose('child');
    submit();
    http
      .expectOne({ method: 'POST', url: '/api/comments' })
      .flush(reply('new-grandchild', 'child'));
    await render();
    const navigatedBack = firstValueFrom(
      TestBed.inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    button('← Back').click();
    await navigatedBack;
    await render();
    http.expectNone('/api/comments/child/replies');
    expect(branch('child').textContent).toContain('Reply grandchild');
    expect(branch('child').textContent).toContain('Refresh replies before loading more');
    http.expectNone('/api/comments/root/replies');
    button('Hide replies', element().querySelector('app-comment-card')!).click();
    await render();
    expect(button('Show replies (1)')).toBeTruthy();
  });
});
