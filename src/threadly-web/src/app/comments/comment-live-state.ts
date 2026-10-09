import { computed, signal } from '@angular/core';

// Generations describe local invalidations, never SQL versions or delivery order.
export class CommentChanges {
  readonly version = signal(0);
  readonly pending = signal<readonly string[]>([]);
  readonly possible = signal(false);
  readonly dirty = signal(false);
  readonly stale = computed(() => this.dirty() || this.possible() || this.pending().length > 0);
  readonly target = computed(() => this.pending().at(-1) ?? null);

  invalidate(commentId?: string) {
    this.version.update((value) => value + 1);
    if (commentId) {
      const ids = this.pending().filter((id) => id !== commentId);
      if (ids.length >= 50) this.possible.set(true);
      this.pending.set([...ids.slice(-49), commentId]);
    } else {
      this.possible.set(true);
    }
  }

  own(commentId: string, replies: boolean) {
    this.pending.update((ids) => ids.filter((id) => id !== commentId));
    if (replies) {
      this.version.update((value) => value + 1);
      this.dirty.set(true);
    }
  }

  acknowledge(version: number) {
    if (version !== this.version()) return;
    this.pending.set([]);
    this.possible.set(false);
    this.dirty.set(false);
  }

  copyFrom(other: CommentChanges) {
    this.version.set(other.version());
    this.pending.set(other.pending());
    this.possible.set(other.possible());
    this.dirty.set(other.dirty());
  }
}
