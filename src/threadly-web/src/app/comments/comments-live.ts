import { DestroyRef, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { firstValueFrom, timeout } from 'rxjs';
import { Comment, CommentCreated } from './comment.models';
import { CommentViewState } from './comment-view-state';
import { CommentsApi } from './comments-api';

export const COMMENTS_CONNECTION = new InjectionToken('Comments connection factory', {
  providedIn: 'root',
  factory: () => () =>
    new HubConnectionBuilder()
      .withUrl('/hubs/comments')
      .withAutomaticReconnect([0, 2000, 10000, 30000])
      .configureLogging(LogLevel.None)
      .build(),
});

@Injectable({ providedIn: 'root' })
export class CommentsLive {
  private readonly connection = inject(COMMENTS_CONNECTION)();
  private readonly views = inject(CommentViewState);
  private readonly api = inject(CommentsApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly status = signal<'connecting' | 'connected' | 'reconnecting' | 'unavailable'>(
    'connecting',
  );
  readonly limited = signal(false);
  readonly countsUnavailable = signal(false);
  private readonly watched = new Map<symbol, string>();
  private sections = 0;
  private subscriptionRevision = 0;
  private appliedRevision = -1;
  private applying = false;
  private connectionEpoch = 0;
  private scheduled = false;
  private starting = false;
  private startAttempt = 0;
  private everStarted = false;
  private confirmedIds = new Set<string>();
  private confirmedRoots = false;
  private readonly seen = new Map<string, number>();
  private readonly own = new Map<string, number>();
  private readonly dirtyCounts = new Set<string>();
  private countTimer?: ReturnType<typeof setTimeout>;
  private startTimer?: ReturnType<typeof setTimeout>;
  private counting = false;
  private countFailures = 0;

  constructor() {
    // Registered once, before the first start; reconnect never adds handlers.
    this.connection.on('CommentCreated', (event: CommentCreated) => this.receive(event));
    this.connection.onreconnecting(() => {
      this.status.set('reconnecting');
      this.resetSubscriptions();
      this.markGap();
    });
    this.connection.onreconnected(() => {
      this.resetSubscriptions();
      this.markGap();
      this.scheduleSubscriptions();
    });
    this.connection.onclose(() => {
      if (this.destroyRef.destroyed) return;
      this.status.set('unavailable');
      this.resetSubscriptions();
      this.markGap();
    });
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.startTimer);
      clearTimeout(this.countTimer);
      void this.connection.stop();
    });
  }

  enter() {
    this.sections++;
    this.changed();
    if (!this.everStarted) void this.start();
    return () => {
      this.sections--;
      this.changed();
    };
  }

  watch(id: string) {
    const owner = Symbol();
    this.watched.set(owner, id);
    this.changed();
    // Re-entering cached reading state still needs a SQL snapshot, even if the group stayed joined.
    if (this.confirmedIds.has(id)) this.queueCounts([id]);
    return () => {
      this.watched.delete(owner);
      this.changed();
    };
  }

  retry() {
    if (this.starting || this.connection.state === HubConnectionState.Reconnecting) return;
    this.countFailures = 0;
    this.queueCounts(this.activeIds());
    if (this.connection.state === HubConnectionState.Connected) {
      this.appliedRevision = -1;
      this.scheduleSubscriptions();
    } else {
      clearTimeout(this.startTimer);
      this.startAttempt = 0;
      void this.start();
    }
  }

  confirmCreated(comment: Comment) {
    this.remember(this.own, comment.id);
    this.views.confirmCreated(comment);
    if (comment.parentId) this.syncCounts([comment.parentId]);
  }

  syncCounts(ids = this.activeIds()) {
    this.countFailures = 0;
    this.queueCounts(ids);
  }

  private activeIds() {
    return [...new Set(this.watched.values())].slice(0, 100);
  }

  private changed() {
    this.subscriptionRevision++;
    this.limited.set(new Set(this.watched.values()).size > 100);
    const active = new Set(this.watched.values());
    for (const id of this.dirtyCounts) if (!active.has(id)) this.dirtyCounts.delete(id);
    this.scheduleSubscriptions();
  }

  private async start() {
    if (
      this.starting ||
      this.connection.state !== HubConnectionState.Disconnected ||
      this.destroyRef.destroyed
    )
      return;
    this.starting = true;
    this.everStarted = true;
    this.status.set('connecting');
    try {
      await this.connection.start();
      this.startAttempt = 0;
      this.resetSubscriptions();
      this.markGap();
      this.scheduleSubscriptions();
    } catch {
      this.status.set('unavailable');
      this.markGap();
      const delay = [2000, 10000, 30000][this.startAttempt++];
      if (delay !== undefined && !this.destroyRef.destroyed)
        this.startTimer = setTimeout(() => void this.start(), delay);
    } finally {
      this.starting = false;
    }
  }

  private resetSubscriptions() {
    this.connectionEpoch++;
    this.confirmedIds.clear();
    this.confirmedRoots = false;
    this.appliedRevision = -1;
    this.subscriptionRevision++;
  }

  private markGap() {
    if (this.sections) this.views.invalidateRoots();
    for (const id of this.activeIds()) this.views.invalidateReplies(id);
  }

  private scheduleSubscriptions() {
    if (this.scheduled || this.destroyRef.destroyed) return;
    this.scheduled = true;
    queueMicrotask(() => {
      this.scheduled = false;
      void this.applySubscriptions();
    });
  }

  private async applySubscriptions() {
    if (
      this.applying ||
      this.connection.state !== HubConnectionState.Connected ||
      this.destroyRef.destroyed
    )
      return;
    this.applying = true;
    const epoch = this.connectionEpoch;
    let failed = false;
    try {
      while (
        this.appliedRevision !== this.subscriptionRevision &&
        this.connection.state === HubConnectionState.Connected
      ) {
        const revision = this.subscriptionRevision;
        const ids = this.activeIds();
        const roots = this.sections > 0;
        await this.connection.invoke('SetSubscriptions', roots, ids);
        if (epoch !== this.connectionEpoch) return;
        if (this.destroyRef.destroyed || this.connection.state !== HubConnectionState.Connected)
          return;
        // A new subscription may have missed a create between REST and the acknowledgement.
        if (roots && !this.confirmedRoots) this.views.invalidateRoots();
        this.confirmedRoots = roots;
        const added = ids.filter((id) => !this.confirmedIds.has(id));
        this.confirmedIds = new Set(ids);
        this.queueCounts(added);
        this.appliedRevision = revision;
      }
      this.status.set('connected');
    } catch {
      if (epoch === this.connectionEpoch) {
        failed = true;
        this.status.set('unavailable');
        this.markGap();
      }
    } finally {
      this.applying = false;
      if (
        !failed &&
        this.appliedRevision !== this.subscriptionRevision &&
        this.connection.state === HubConnectionState.Connected
      )
        this.scheduleSubscriptions();
    }
  }

  private receive(event: CommentCreated) {
    if (
      !event ||
      typeof event.eventId !== 'string' ||
      typeof event.commentId !== 'string' ||
      (event.parentId !== null && typeof event.parentId !== 'string')
    )
      return;
    this.prune(this.seen);
    this.prune(this.own);
    if (this.seen.has(event.eventId)) return;
    this.remember(this.seen, event.eventId);
    if (event.parentId === null) {
      if (this.sections && !this.own.has(event.commentId))
        this.views.invalidateRoots(event.commentId);
    } else if (this.activeIds().includes(event.parentId)) {
      if (!this.own.has(event.commentId))
        this.views.invalidateReplies(event.parentId, event.commentId);
      this.queueCounts([event.parentId]);
    }
  }

  private remember(map: Map<string, number>, id: string) {
    this.prune(map);
    map.delete(id);
    map.set(id, Date.now());
    if (map.size > 1000) map.delete(map.keys().next().value!);
  }

  private prune(map: Map<string, number>) {
    const cutoff = Date.now() - 10 * 60_000;
    for (const [id, time] of map) {
      if (time >= cutoff) break;
      map.delete(id);
    }
  }

  private queueCounts(ids: string[]) {
    const active = new Set(this.watched.values());
    for (const id of ids)
      if (active.has(id) && this.dirtyCounts.size < 100) this.dirtyCounts.add(id);
    if (
      this.counting ||
      this.countTimer ||
      !this.dirtyCounts.size ||
      this.countFailures >= 3 ||
      this.destroyRef.destroyed
    )
      return;
    // Fixed window, not trailing debounce: a continuous burst cannot starve this request.
    this.countTimer = setTimeout(
      () => {
        this.countTimer = undefined;
        void this.flushCounts();
      },
      this.countFailures ? 1000 * 2 ** this.countFailures : 150,
    );
  }

  private async flushCounts() {
    const ids = [...this.dirtyCounts];
    this.dirtyCounts.clear();
    if (!ids.length || this.destroyRef.destroyed) return;
    this.counting = true;
    const snapshot = this.views.countSnapshot();
    try {
      const response = await firstValueFrom(this.api.getReplyCounts(ids).pipe(timeout(10000)));
      if (this.destroyRef.destroyed) return;
      const active = new Set(this.watched.values());
      for (const item of response.items) {
        // An invalidation during this request requires another snapshot, not arithmetic.
        if (active.has(item.id) && !this.dirtyCounts.has(item.id)) {
          if (
            this.views.current.branches.get(item.id)?.countRevision === snapshot.get(item.id)?.[0]
          )
            this.views.updateCount(item.id, item.replyCount);
          else this.dirtyCounts.add(item.id);
        }
      }
      this.countFailures = 0;
      this.countsUnavailable.set(false);
    } catch {
      this.countFailures++;
      this.countsUnavailable.set(true);
      for (const id of ids)
        if ([...this.watched.values()].includes(id) && this.dirtyCounts.size < 100)
          this.dirtyCounts.add(id);
    } finally {
      this.counting = false;
      this.queueCounts([]);
    }
  }
}
