import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { CommentCard } from './comment-card';
import { CommentComposer } from './comment-composer';
import { Comment } from './comment.models';
import { CommentsApi } from './comments-api';
import { CommentViewState, ReplyBranch } from './comment-view-state';
import { CommentsLive } from './comments-live';

@Component({
  selector: 'app-comment-thread',
  imports: [CommentCard, CommentComposer, RouterLink],
  templateUrl: './comment-thread.html',
  styleUrl: './comment-thread.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentThread implements OnInit {
  private readonly api = inject(CommentsApi);
  readonly views = inject(CommentViewState);
  readonly live = inject(CommentsLive);
  private readonly destroyRef = inject(DestroyRef);
  private readonly replyButton = viewChild.required<ElementRef<HTMLButtonElement>>('replyButton');

  readonly comment = input.required<Comment>();
  readonly level = input(1);
  readonly detail = input(false);
  readonly openReplies = input(false);
  readonly active = input(true);
  readonly composing = signal(false);
  branch!: ReplyBranch;

  constructor() {
    effect((cleanup) => {
      if (this.active()) cleanup(this.live.watch(this.comment().id));
    });
  }

  ngOnInit() {
    this.branch = this.views.branch(this.comment());
    if (this.openReplies()) this.branch.expanded.set(true);
    if (this.branch.expanded() && !this.branch.loaded() && this.level() < 4) this.loadMore();
  }

  toggleReplies() {
    this.branch.expanded.update((expanded) => !expanded);
    if (this.branch.expanded() && !this.branch.loaded()) this.loadMore();
  }

  loadMore() {
    if (this.branch.loading() || (this.branch.loaded() && this.branch.changes.stale())) return;
    if (this.branch.loaded() && this.branch.nextCursor() === null) return;

    this.branch.loading.set(true);
    this.branch.error.set(null);
    this.branch.refreshFailed = false;
    const version = this.branch.changes.version();
    this.api
      .getReplies(this.comment().id, this.branch.nextCursor())
      .pipe(
        finalize(() => this.branch.loading.set(false)),
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
          this.branch.changes.acknowledge(version);
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
  }

  refreshReplies() {
    if (this.branch.loading() || this.views.composers() > 0) return;
    this.branch.loading.set(true);
    this.branch.error.set(null);
    this.branch.refreshFailed = false;
    const version = this.branch.changes.version();
    this.api
      .getReplies(this.comment().id)
      .pipe(
        finalize(() => this.branch.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (page) => {
          if (this.views.composers() > 0) {
            this.branch.refreshFailed = true;
            this.branch.error.set('Finish or close open composers, then refresh replies again.');
            return;
          }
          this.branch.items.set(page.items);
          this.branch.nextCursor.set(page.nextCursor);
          this.branch.loaded.set(true);
          this.branch.changes.acknowledge(version);
          this.live.syncCounts([this.comment().id]);
        },
        error: () => {
          this.branch.refreshFailed = true;
          this.branch.error.set(
            'Replies could not be refreshed. Your previous replies are still shown.',
          );
        },
      });
  }
}
