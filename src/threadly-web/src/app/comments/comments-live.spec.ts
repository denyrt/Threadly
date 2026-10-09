import { Location, ViewportScroller } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { filter, firstValueFrom } from 'rxjs';
import { routes } from '../app.routes';
import { Comment } from './comment.models';
import { CommentViewState } from './comment-view-state';
import { COMMENTS_CONNECTION, CommentsLive } from './comments-live';
import { TestCommentsConnection } from './comments-live-testing';
import { CommentComposer } from './comment-composer';
import { provideTestTurnstile } from './turnstile-testing';
import { By } from '@angular/platform-browser';

const root: Comment = {
  id: 'root',
  username: 'Reader',
  email: 'reader@example.com',
  content: [{ type: 'text', html: 'Root body' }],
  createdAtUtc: '2026-10-09T10:00:00Z',
  parentId: null,
  replyCount: 12,
  attachments: [],
};
const reply = (id: string, parentId = 'root'): Comment => ({
  ...root,
  id,
  parentId,
  replyCount: 0,
});

describe('Comments live connection and snapshots', () => {
  let connection: TestCommentsConnection;
  let live: CommentsLive;
  let views: CommentViewState;
  let http: HttpTestingController;
  beforeEach(() => {
    vi.useFakeTimers();
    connection = new TestCommentsConnection();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: COMMENTS_CONNECTION, useValue: () => connection.asConnection() },
      ],
    });
    live = TestBed.inject(CommentsLive);
    views = TestBed.inject(CommentViewState);
    http = TestBed.inject(HttpTestingController);
    views.branch(root);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  async function connected() {
    live.watch('root');
    live.enter();
    await vi.advanceTimersByTimeAsync(151);
    http.expectOne('/api/comments/reply-counts').flush({ items: [{ id: 'root', replyCount: 12 }] });
    await vi.advanceTimersByTimeAsync(0);
  }

  it('creates one connection, registers once, and restores only current subscriptions after reconnect', async () => {
    const factory = TestBed.inject(COMMENTS_CONNECTION);
    expect(factory).toBeTruthy();
    await connected();
    const hide = live.watch('child');
    hide();
    connection.disconnect();
    connection.reconnect();
    await vi.advanceTimersByTimeAsync(151);
    expect(connection.starts).toBe(1);
    expect(connection.registrations).toBe(1);
    expect(connection.invocations.at(-1)).toEqual({ roots: true, ids: ['root'] });
    http.expectOne('/api/comments/reply-counts').flush({ items: [{ id: 'root', replyCount: 14 }] });
    await vi.advanceTimersByTimeAsync(0);
    expect(views.branch(root).replyCount()).toBe(14);
    expect(views.branch(root).changes.possible()).toBe(true);
    expect(views.branch(root).changes.target()).toBeNull();
  });

  it('coalesces a burst without duplicate effects and discards snapshots invalidated during a request', async () => {
    await connected();
    connection.emit('first', 'root');
    const version = views.branch(root).changes.version();
    connection.emit('first', 'root');
    expect(views.branch(root).changes.version()).toBe(version);
    for (let index = 0; index < 80; index++) connection.emit(`reply-${index}`, 'root');
    await vi.advanceTimersByTimeAsync(151);
    const first = http.expectOne('/api/comments/reply-counts');
    expect(first.request.body).toEqual({ ids: ['root'] });
    connection.emit('arrived-during-request', 'root');
    first.flush({ items: [{ id: 'root', replyCount: 90 }] });
    await vi.advanceTimersByTimeAsync(151);
    expect(views.branch(root).replyCount()).toBe(12);
    http.expectOne('/api/comments/reply-counts').flush({ items: [{ id: 'root', replyCount: 91 }] });
    await vi.advanceTimersByTimeAsync(0);
    expect(views.branch(root).replyCount()).toBe(91);
    expect(views.branch(root).changes.pending().length).toBe(50);
    expect(views.branch(root).changes.target()).toBe('arrived-during-request');
    http.expectNone((request) => request.method === 'GET');
  });

  it.each([true, false])(
    'deduplicates own replies when eventFirst=%s without clearing foreign pending IDs',
    async (eventFirst) => {
      await connected();
      connection.emit('foreign', 'root');
      if (eventFirst) connection.emit('mine', 'root');
      live.confirmCreated(reply('mine'));
      if (!eventFirst) connection.emit('mine', 'root');
      expect(views.branch(root).changes.pending()).toEqual(['foreign']);
      expect(views.branch(root).changes.target()).toBe('foreign');
      expect(views.branch(root).replyCount()).toBe(12);
      await vi.advanceTimersByTimeAsync(151);
      http
        .expectOne('/api/comments/reply-counts')
        .flush({ items: [{ id: 'root', replyCount: 14 }] });
      await vi.advanceTimersByTimeAsync(0);
      expect(views.branch(root).replyCount()).toBe(14);
    },
  );

  it.each([true, false])(
    'removes only the own root notification when eventFirst=%s',
    async (eventFirst) => {
      await connected();
      const changes = views.current.feedChanges;
      changes.acknowledge(changes.version());
      connection.emit('foreign');
      if (eventFirst) connection.emit('mine');
      live.confirmCreated({ ...root, id: 'mine' });
      if (!eventFirst) connection.emit('mine');
      expect(changes.pending()).toEqual(['foreign']);
    },
  );

  it('keeps root and grandchild effects out of unrelated parent counts', async () => {
    await connected();
    views.branch(reply('child'));
    live.watch('child');
    await vi.advanceTimersByTimeAsync(151);
    http.expectOne('/api/comments/reply-counts').flush({ items: [{ id: 'child', replyCount: 0 }] });
    connection.emit('grandchild', 'child');
    connection.emit('unrelated', 'elsewhere');
    await vi.advanceTimersByTimeAsync(151);
    const request = http.expectOne('/api/comments/reply-counts');
    expect(request.request.body.ids).toEqual(['child']);
    request.flush({ items: [{ id: 'child', replyCount: 1 }] });
    await vi.advanceTimersByTimeAsync(0);
    expect(views.branch(root).replyCount()).toBe(12);
    expect(views.branch(root).changes.target()).toBeNull();
    expect(views.branch(reply('child')).changes.target()).toBe('grandchild');
  });

  it('bounds initial retry attempts and supports a manual retry after exhaustion', async () => {
    const start = vi.spyOn(connection, 'start').mockRejectedValue(new Error('offline'));
    live.enter();
    await vi.advanceTimersByTimeAsync(42001);
    expect(start).toHaveBeenCalledTimes(4);
    expect(live.status()).toBe('unavailable');
    await vi.advanceTimersByTimeAsync(60000);
    expect(start).toHaveBeenCalledTimes(4);
    start.mockImplementation(async () => {
      connection.state = HubConnectionState.Connected;
    });
    live.retry();
    live.retry();
    await vi.advanceTimersByTimeAsync(1);
    expect(start).toHaveBeenCalledTimes(5);
    expect(live.status()).toBe('connected');
  });

  it('does not let an older count request overwrite a newer REST snapshot', async () => {
    await connected();
    live.syncCounts();
    await vi.advanceTimersByTimeAsync(151);
    const old = http.expectOne('/api/comments/reply-counts');
    views.applyCountSnapshot([{ ...root, replyCount: 14 }], views.countSnapshot());
    old.flush({ items: [{ id: 'root', replyCount: 12 }] });
    await vi.advanceTimersByTimeAsync(151);
    expect(views.branch(root).replyCount()).toBe(14);
    http.expectOne('/api/comments/reply-counts').flush({ items: [{ id: 'root', replyCount: 14 }] });
  });

  it('reconciles a reconnect that happens during a subscription invocation', async () => {
    let reject!: (reason: Error) => void;
    vi.spyOn(connection, 'invoke').mockImplementationOnce(
      () =>
        new Promise<void>((_resolve, fail) => {
          reject = fail;
        }),
    );
    live.watch('root');
    live.enter();
    await vi.advanceTimersByTimeAsync(0);
    connection.disconnect();
    connection.reconnect();
    await vi.advanceTimersByTimeAsync(0);
    reject(new Error('old connection closed'));
    await vi.advanceTimersByTimeAsync(151);
    expect(connection.invocations.at(-1)).toEqual({ roots: true, ids: ['root'] });
    expect(live.status()).toBe('connected');
    http.expectOne('/api/comments/reply-counts').flush({ items: [] });
  });

  it('handles reconnect exhaustion and refuses overlapping manual start cycles', async () => {
    await connected();
    connection.disconnect();
    live.retry();
    expect(connection.starts).toBe(1);
    connection.state = HubConnectionState.Disconnected;
    connection.closed();
    expect(live.status()).toBe('unavailable');
    live.retry();
    live.retry();
    await vi.advanceTimersByTimeAsync(151);
    expect(connection.starts).toBe(2);
    http.expectOne('/api/comments/reply-counts').flush({ items: [] });
  });

  it('waits for subscription acknowledgements and serializes navigation changes', async () => {
    let acknowledge!: () => void;
    const invoke = vi.spyOn(connection, 'invoke').mockImplementationOnce(
      () =>
        new Promise<void>((resolve) => {
          acknowledge = resolve;
        }),
    );
    const unwatch = live.watch('root');
    live.enter();
    await vi.advanceTimersByTimeAsync(0);
    http.expectNone('/api/comments/reply-counts');
    unwatch();
    live.watch('child');
    await vi.advanceTimersByTimeAsync(0);
    expect(invoke).toHaveBeenCalledTimes(1);
    acknowledge();
    await vi.advanceTimersByTimeAsync(151);
    expect(invoke).toHaveBeenLastCalledWith('SetSubscriptions', true, ['child']);
    const counts = http.expectOne('/api/comments/reply-counts');
    expect(counts.request.body.ids).toEqual(['child']);
    counts.flush({ items: [] });
  });

  it('caps subscriptions and reports degradation while keeping manual count reads available', async () => {
    live.enter();
    for (let index = 0; index < 105; index++) live.watch(`id-${index}`);
    await vi.advanceTimersByTimeAsync(151);
    expect(connection.invocations.at(-1)?.ids).toHaveLength(100);
    expect(live.limited()).toBe(true);
    http.expectOne('/api/comments/reply-counts').flush({ items: [] });
    await vi.advanceTimersByTimeAsync(0);
    live.syncCounts(['id-104']);
    await vi.advanceTimersByTimeAsync(151);
    const manual = http.expectOne('/api/comments/reply-counts');
    expect(manual.request.body.ids).toEqual(['id-104']);
    manual.flush({ items: [] });
  });

  it('bounds count failures and lets manual retry recover without a request storm', async () => {
    await connected();
    connection.emit('new', 'root');
    for (const delay of [151, 2001, 4001]) {
      await vi.advanceTimersByTimeAsync(delay);
      http
        .expectOne('/api/comments/reply-counts')
        .flush({}, { status: 503, statusText: 'Unavailable' });
    }
    await vi.advanceTimersByTimeAsync(60000);
    http.expectNone('/api/comments/reply-counts');
    expect(live.countsUnavailable()).toBe(true);
    live.retry();
    await vi.advanceTimersByTimeAsync(151);
    http.expectOne('/api/comments/reply-counts').flush({ items: [{ id: 'root', replyCount: 13 }] });
    await vi.advanceTimersByTimeAsync(0);
    expect(live.countsUnavailable()).toBe(false);
  });
});

describe('Live comment reading', () => {
  let connection: TestCommentsConnection;
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let views: CommentViewState;
  const scroll = { getScrollPosition: () => [0, 360], scrollToPosition: vi.fn() };
  beforeEach(async () => {
    connection = new TestCommentsConnection();
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideLocationMocks(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTestTurnstile(),
        { provide: ViewportScroller, useValue: scroll },
        { provide: COMMENTS_CONNECTION, useValue: () => connection.asConnection() },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    views = TestBed.inject(CommentViewState);
    harness = await RouterTestingHarness.create();
    TestBed.inject(Router).setUpLocationChangeListener();
  });
  afterEach(async () => {
    await counts();
    http.verify();
  });
  const element = () => harness.routeNativeElement as HTMLElement;
  const button = (text: string) =>
    [...element().querySelectorAll('button')].find((item) => item.textContent?.trim() === text)!;
  async function render() {
    await harness.fixture.whenStable();
    harness.detectChanges();
  }
  async function counts(values: Record<string, number> = { root: 12 }) {
    await new Promise((resolve) => setTimeout(resolve, 180));
    for (const request of http.match('/api/comments/reply-counts'))
      request.flush({
        items: request.request.body.ids.map((id: string) => ({ id, replyCount: values[id] ?? 0 })),
      });
    await render();
  }
  async function open() {
    await harness.navigateByUrl('/comments?page=2&sortBy=email&sortDirection=asc');
    http
      .expectOne('/api/comments?page=2&sortBy=email&sortDirection=asc')
      .flush({ items: [root], page: 2, pageSize: 25, totalCount: 30 });
    await render();
    await counts();
  }
  async function expand() {
    button('Show replies (12)').click();
    http
      .expectOne('/api/comments/root/replies')
      .flush({ items: [reply('older')], nextCursor: 'cursor' });
    await render();
    await counts();
  }

  it('shows one root banner without GET or moving cards and preserves events during refresh', async () => {
    await open();
    const card = element().querySelector('app-comment-thread');
    connection.emit('new-root');
    connection.emit('second-root');
    await render();
    expect(element().textContent).toContain('New comments available');
    expect(element().querySelector('app-comment-thread')).toBe(card);
    http.expectNone((request) => request.method === 'GET');
    button('Refresh').click();
    const refresh = http.expectOne('/api/comments?page=2&sortBy=email&sortDirection=asc');
    connection.emit('during-refresh');
    refresh.flush({ items: [root], page: 2, pageSize: 25, totalCount: 33 });
    await render();
    expect(element().textContent).toContain('New comments available');
    expect(element().querySelector('app-comment-thread')).toBe(card);
    button('Refresh').click();
    http
      .expectOne('/api/comments?page=2&sortBy=email&sortDirection=asc')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await render();
    expect(element().textContent).toContain('New comments available');
    expect(element().querySelector('app-comment-thread')).toBe(card);
    button('Refresh').click();
    http
      .expectOne('/api/comments?page=2&sortBy=email&sortDirection=asc')
      .flush({ items: [root], page: 2, pageSize: 25, totalCount: 34 });
    await render();
    expect(element().textContent).not.toContain('New comments available');
    expect(TestBed.inject(Router).url).toBe('/comments?page=2&sortBy=email&sortDirection=asc');
  });

  it.each([false, true])(
    'opens View new reply with expanded=%s and restores stale reading state on Back',
    async (expanded) => {
      await open();
      if (expanded) await expand();
      connection.emit('new-one', 'root');
      connection.emit('new-two', 'root');
      await render();
      await counts({ root: 14 });
      expect(views.branch(root).expanded()).toBe(expanded);
      http.expectNone((request) => request.method === 'GET');
      const link = [...element().querySelectorAll('a')].find(
        (item) => item.textContent === 'View new reply',
      )!;
      expect(link.getAttribute('href')).toBe('/comments/new-two');
      expect(link.target).not.toBe('_blank');
      const saved = views.current;
      link.click();
      await render();
      http.expectOne('/api/comments/new-two').flush(reply('new-two'));
      await render();
      expect(element().querySelector('a[href="/comments/root"]')).not.toBeNull();
      connection.emit('another-root');
      await render();
      expect(element().textContent).not.toContain('New comments available');
      expect(element().textContent).not.toContain('View new reply');
      const back = firstValueFrom(
        TestBed.inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
      );
      TestBed.inject(Location).back();
      await back;
      await render();
      expect(views.current).toBe(saved);
      expect(views.branch(root).expanded()).toBe(expanded);
      expect(views.branch(root).changes.stale()).toBe(true);
      expect(views.branch(root).changes.target()).toBe('new-two');
      expect(views.branch(root).nextCursor()).toBe(expanded ? 'cursor' : null);
      expect(scroll.scrollToPosition).toHaveBeenLastCalledWith([0, 360]);
      http.expectNone((request) => request.method === 'GET');
    },
  );

  it('retains drafts, files and CAPTCHA through events and reconnect, and blocks destructive refresh', async () => {
    await open();
    await expand();
    button('Reply').click();
    await render();
    const composer = harness.fixture.debugElement.query(By.directive(CommentComposer))
      .componentInstance as CommentComposer;
    const file = new File(['draft file'], 'draft.txt');
    composer.form.controls.text.setValue('Draft body');
    composer.files.set([file]);
    const token = composer.captchaToken();
    connection.emit('new-reply', 'root');
    connection.emit('new-root');
    connection.disconnect();
    connection.reconnect();
    await render();
    await counts({ root: 13 });
    expect(button('Refresh').disabled).toBe(true);
    expect(button('Refresh replies').disabled).toBe(true);
    expect(element().textContent).toContain('Finish or close open composers');
    expect(composer.form.controls.text.value).toBe('Draft body');
    expect(composer.files()[0]).toBe(file);
    expect(composer.captchaToken()).toBe(token);
    expect(composer.parent()?.id).toBe('root');
    http.expectNone((request) => request.method === 'GET');
  });

  it('refreshes one page, retains a concurrent invalidation, and removes hidden descendants from subscriptions', async () => {
    await open();
    await expand();
    connection.emit('new-reply', 'root');
    await render();
    button('Refresh replies').click();
    const refresh = http.expectOne('/api/comments/root/replies');
    connection.emit('during-refresh', 'root');
    refresh.flush({ items: [reply('first')], nextCursor: 'next-page' });
    await render();
    await counts({ root: 14 });
    expect(
      views
        .branch(root)
        .items()
        .map((item) => item.id),
    ).toEqual(['first']);
    expect(views.branch(root).nextCursor()).toBe('next-page');
    expect(views.branch(root).changes.target()).toBe('during-refresh');
    expect(button('Show more')).toBeUndefined();
    http.expectNone((request) => request.method === 'GET');
    button('Hide replies (14)').click();
    await render();
    expect(connection.invocations.at(-1)?.ids).toEqual(['root']);
    expect(views.branch(root).items()).toHaveLength(1);
  });

  it('does not invent a new reply target after reconnect and keeps manual controls available', async () => {
    await open();
    await expand();
    connection.disconnect();
    connection.reconnect();
    await render();
    await counts();
    expect(element().textContent).toContain('Replies may have changed');
    expect(element().textContent).not.toContain('View new reply');
    connection.state = HubConnectionState.Disconnected;
    connection.closed();
    await render();
    expect(element().textContent).toContain(
      'Live updates unavailable. You can still refresh manually.',
    );
    expect(button('Refresh').disabled).toBe(false);
    expect(button('Refresh replies').disabled).toBe(false);
  });

  it('offers manual refresh on a detail page with zero known replies while live is unavailable', async () => {
    await harness.navigateByUrl('/comments/root');
    http.expectOne('/api/comments/root').flush({ ...root, replyCount: 0 });
    await render();
    await counts({ root: 0 });
    connection.state = HubConnectionState.Disconnected;
    connection.closed();
    await render();
    button('Refresh replies').click();
    http
      .expectOne('/api/comments/root/replies')
      .flush({ items: [reply('missed')], nextCursor: null });
    await render();
    await counts({ root: 1 });
    expect(views.branch(root).replyCount()).toBe(1);
    expect(views.branch(root).expanded()).toBe(false);
    expect(button('Show replies (1)')).toBeTruthy();
  });

  it.each(['feed', 'replies'])('preserves a composer opened during %s refresh', async (target) => {
    await open();
    await expand();
    button(target === 'feed' ? 'Refresh' : 'Refresh replies').click();
    const request = http.expectOne(
      target === 'feed'
        ? '/api/comments?page=2&sortBy=email&sortDirection=asc'
        : '/api/comments/root/replies',
    );
    const child = element().querySelector('#replies-root app-comment-thread')!;
    child.querySelector<HTMLButtonElement>('button')!.click();
    await render();
    const composer = harness.fixture.debugElement.query(By.directive(CommentComposer))
      .componentInstance as CommentComposer;
    composer.form.controls.text.setValue('Typed while refresh was in flight');
    const file = new File(['content'], 'draft.txt');
    composer.files.set([file]);
    request.flush(
      target === 'feed'
        ? { items: [], page: 2, pageSize: 25, totalCount: 0 }
        : { items: [], nextCursor: null },
    );
    await render();
    expect(element().querySelector('#replies-root app-comment-thread')).toBe(child);
    expect(composer.files()[0]).toBe(file);
    expect(composer.form.controls.text.value).toBe('Typed while refresh was in flight');
    expect(element().textContent).toContain('Finish or close open composers, then refresh');
    expect(views.branch(root).nextCursor()).toBe('cursor');
  });

  it('retains a feed cache on Back after a manual refresh and sort navigation in the same component', async () => {
    await open();
    await expand();
    button('Refresh').click();
    http
      .expectOne('/api/comments?page=2&sortBy=email&sortDirection=asc')
      .flush({ items: [root], page: 2, pageSize: 25, totalCount: 30 });
    await render();
    const saved = views.current;
    await harness.navigateByUrl('/comments?page=1&sortBy=date&sortDirection=desc');
    http
      .expectOne('/api/comments?page=1&sortBy=date&sortDirection=desc')
      .flush({ items: [root], page: 1, pageSize: 25, totalCount: 30 });
    await render();
    const back = firstValueFrom(
      TestBed.inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    TestBed.inject(Location).back();
    await back;
    await render();
    expect(views.current).toBe(saved);
    expect(views.branch(root).expanded()).toBe(true);
    expect(views.branch(root).items()[0].id).toBe('older');
    http.expectNone((request) => request.method === 'GET');
  });
});
