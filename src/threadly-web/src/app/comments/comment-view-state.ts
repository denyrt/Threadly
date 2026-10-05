import { ViewportScroller } from '@angular/common';
import { afterNextRender, inject, Injectable, Injector, signal } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { Comment, CommentPage } from './comment.models';

export class ReplyBranch {
  readonly items = signal<Comment[]>([]);
  readonly expanded = signal(false);
  readonly loaded = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly nextCursor = signal<string | null>(null);
  readonly replyCount = signal(0);
  readonly created = signal<Comment | null>(null);
  needsRefresh = false;
  replyVersion = 0;
}

class CommentView {
  readonly branches = new Map<string, ReplyBranch>();
  page: CommentPage | null = null;
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

  restoreScroll() {
    const position = this.current.scroll;
    afterNextRender(() => this.scroller.scrollToPosition(position), { injector: this.injector });
  }

  recordReply(reply: Comment) {
    if (!reply.parentId) return;
    for (const view of new Set(this.history.values())) {
      const branch = view.branches.get(reply.parentId);
      if (!branch) continue;
      branch.replyCount.update((count) => count + 1);
      branch.replyVersion++;
      if (branch.loaded() && branch.nextCursor() === null) branch.needsRefresh = true;
    }
  }
}
