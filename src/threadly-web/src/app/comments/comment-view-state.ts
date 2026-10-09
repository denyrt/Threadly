import { ViewportScroller } from '@angular/common';
import { afterNextRender, inject, Injectable, Injector, signal } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { Comment, CommentFeedQuery, CommentPage } from './comment.models';
import { CommentChanges } from './comment-live-state';

export class ReplyBranch {
  readonly items = signal<Comment[]>([]);
  readonly expanded = signal(false);
  readonly loaded = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly nextCursor = signal<string | null>(null);
  readonly replyCount = signal(0);
  readonly created = signal<Comment | null>(null);
  refreshFailed = false;
  countRevision = 0;
  readonly changes = new CommentChanges();
}

class CommentView {
  readonly feedChanges = new CommentChanges();
  readonly branches = new Map<string, ReplyBranch>();
  page: CommentPage | null = null;
  feedQuery: CommentFeedQuery | null = null;
  comment: Comment | null = null;
  scroll: [number, number] = [0, 0];
  canGoBack = false;
}

// Only reading state for recent in-app history entries is retained. A reload starts a fresh view.
@Injectable({ providedIn: 'root' })
export class CommentViewState {
  private readonly router = inject(Router);
  private readonly scroller = inject(ViewportScroller);
  private readonly injector = inject(Injector);
  private readonly history = new Map<number, CommentView>();
  readonly roots = new CommentChanges();
  readonly composers = signal(0);
  current = new CommentView();

  constructor() {
    this.history.set(this.router.currentNavigation()?.id ?? 0, this.current);
    this.router.events.subscribe((event) => {
      if (!(event instanceof NavigationStart)) return;

      this.current.scroll = this.scroller.getScrollPosition();
      const restored = event.restoredState
        ? this.history.get(event.restoredState.navigationId)
        : undefined;
      this.current = restored ?? new CommentView();
      if (restored) {
        this.current.feedChanges.invalidate();
        for (const branch of this.current.branches.values()) branch.changes.invalidate();
      } else this.current.feedChanges.copyFrom(this.roots);
      if (!restored) this.current.canGoBack = !event.restoredState;
      this.history.set(event.id, this.current);
      if (this.history.size > 40) this.history.delete(this.history.keys().next().value!);
    });
  }

  branch(comment: Comment): ReplyBranch {
    let branch = this.current.branches.get(comment.id);
    if (!branch) {
      branch = new ReplyBranch();
      branch.replyCount.set(comment.replyCount);
      this.current.branches.set(comment.id, branch);
    }
    return branch;
  }

  selectFeed(query: CommentFeedQuery) {
    const previous = this.current.feedQuery;
    if (
      previous?.page !== query.page ||
      previous.sortBy !== query.sortBy ||
      previous.sortDirection !== query.sortDirection
    ) {
      this.current.page = null;
      this.current.branches.clear();
      this.current.scroll = [0, 0];
    }
    this.current.feedQuery = query;
    return this.current;
  }

  invalidateFeedPages() {
    for (const view of new Set(this.history.values())) {
      view.feedChanges.dirty.set(true);
      view.feedChanges.version.update((version) => version + 1);
    }
  }

  restoreScroll() {
    const position = this.current.scroll;
    afterNextRender(() => this.scroller.scrollToPosition(position), { injector: this.injector });
  }

  invalidateRoots(commentId?: string) {
    this.roots.invalidate(commentId);
    for (const view of new Set(this.history.values())) view.feedChanges.invalidate(commentId);
  }

  invalidateReplies(parentId: string, commentId?: string) {
    for (const view of new Set(this.history.values())) {
      view.branches.get(parentId)?.changes.invalidate(commentId);
    }
  }

  confirmCreated(comment: Comment) {
    this.roots.own(comment.id, false);
    for (const view of new Set(this.history.values())) {
      view.feedChanges.own(comment.id, false);
      if (comment.parentId) view.branches.get(comment.parentId)?.changes.own(comment.id, true);
    }
  }

  updateCount(id: string, count: number) {
    for (const view of new Set(this.history.values())) {
      const branch = view.branches.get(id);
      if (!branch) continue;
      if (branch.replyCount() !== count && !branch.changes.stale()) branch.changes.invalidate();
      branch.replyCount.set(count);
      branch.countRevision++;
    }
  }

  countSnapshot() {
    return new Map(
      [...this.current.branches].map(
        ([id, branch]) => [id, [branch.countRevision, branch.changes.version()]] as const,
      ),
    );
  }

  applyCountSnapshot(items: Comment[], snapshot: ReturnType<CommentViewState['countSnapshot']>) {
    for (const item of items) {
      const branch = this.current.branches.get(item.id);
      const previous = snapshot.get(item.id);
      if (
        branch &&
        previous &&
        previous[0] === branch.countRevision &&
        previous[1] === branch.changes.version()
      )
        this.updateCount(item.id, item.replyCount);
    }
  }
}
