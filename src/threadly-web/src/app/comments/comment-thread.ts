import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EMPTY, expand, finalize, reduce } from 'rxjs';
import { CommentCard } from './comment-card';
import { CommentComposer } from './comment-composer';
import { Comment } from './comment.models';
import { CommentsApi } from './comments-api';
import { CommentViewState, ReplyBranch } from './comment-view-state';

@Component({
  selector: 'app-comment-thread',
  imports: [CommentCard, CommentComposer, RouterLink],
  templateUrl: './comment-thread.html',
  styleUrl: './comment-thread.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentThread implements OnInit {
  private readonly api = inject(CommentsApi);
  private readonly views = inject(CommentViewState);
  private readonly destroyRef = inject(DestroyRef);
  private readonly replyButton = viewChild.required<ElementRef<HTMLButtonElement>>('replyButton');

  readonly comment = input.required<Comment>();
  readonly level = input(1);
  readonly detail = input(false);
  readonly openReplies = input(false);
  readonly composing = signal(false);
  branch!: ReplyBranch;

  ngOnInit() {
    this.branch = this.views.branch(this.comment());
    if (this.openReplies()) this.branch.expanded.set(true);
    if (
      this.branch.expanded() &&
      (!this.branch.loaded() || this.branch.needsRefresh) &&
      this.level() < 4
    )
      this.loadMore();
  }

  toggleReplies() {
    this.branch.expanded.update((expanded) => !expanded);
    if (this.branch.expanded() && (!this.branch.loaded() || this.branch.needsRefresh))
      this.loadMore();
  }

  loadMore() {
    if (this.branch.loading()) return;
    if (this.branch.needsRefresh) {
      this.refreshReplies();
      return;
    }
    if (this.branch.loaded() && this.branch.nextCursor() === null) return;

    this.branch.loading.set(true);
    this.branch.error.set(null);
    const version = this.branch.replyVersion;
    this.api
      .getReplies(this.comment().id, this.branch.nextCursor())
      .pipe(
        finalize(() => this.finishLoading(version)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (page) => {
          const existingIds = new Set(this.branch.items().map((reply) => reply.id));
          this.branch.items.update((items) => [
            ...items,
            ...page.items.filter((reply) => !existingIds.has(reply.id)),
          ]);
          this.branch.nextCursor.set(page.nextCursor);
          this.branch.loaded.set(true);
        },
        error: () => this.branch.error.set('Replies could not be loaded. Please try again.'),
      });
  }

  closeComposer() {
    this.composing.set(false);
    this.replyButton().nativeElement.focus();
  }

  onCreated(reply: Comment) {
    this.closeComposer();
    this.branch.created.set(reply);
    this.views.recordReply(reply);
    if (this.branch.needsRefresh && !this.branch.loading()) this.refreshReplies();
  }

  private refreshReplies() {
    this.branch.loading.set(true);
    this.branch.error.set(null);
    const version = this.branch.replyVersion;
    // Re-read a complete branch in SQL order, including replies from other users.
    // Keep the old list intact until all bounded requests succeed.
    this.api
      .getReplies(this.comment().id)
      .pipe(
        expand((page) =>
          page.nextCursor ? this.api.getReplies(this.comment().id, page.nextCursor) : EMPTY,
        ),
        reduce((items, page) => {
          items.push(...page.items);
          return items;
        }, [] as Comment[]),
        finalize(() => this.finishLoading(version)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (items) => {
          this.branch.items.set(items);
          this.branch.nextCursor.set(null);
          if (version === this.branch.replyVersion) {
            this.branch.replyCount.set(items.length);
            this.branch.needsRefresh = false;
          }
        },
        error: () =>
          this.branch.error.set(
            'Your reply was posted, but the replies could not be refreshed. Please try again.',
          ),
      });
  }

  private finishLoading(version: number) {
    this.branch.loading.set(false);
    if (
      version !== this.branch.replyVersion &&
      this.branch.loaded() &&
      this.branch.nextCursor() === null
    ) {
      this.branch.needsRefresh = true;
      if (!this.branch.error() && !this.destroyRef.destroyed) this.refreshReplies();
    }
  }
}
