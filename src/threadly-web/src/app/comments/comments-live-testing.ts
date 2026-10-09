import { inject, signal } from '@angular/core';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { Comment, CommentCreated } from './comment.models';
import { CommentViewState } from './comment-view-state';
import { CommentsLive } from './comments-live';

// Existing REST/composer specs opt out of network delivery. Live specs use the real service below.
export const provideQuietCommentsLive = () => ({
  provide: CommentsLive,
  useFactory: () => {
    const views = inject(CommentViewState);
    return {
      status: signal('unavailable'),
      limited: signal(false),
      countsUnavailable: signal(false),
      enter: () => () => undefined,
      watch: () => () => undefined,
      retry: () => undefined,
      syncCounts: () => undefined,
      confirmCreated: (comment: Comment) => views.confirmCreated(comment),
    };
  },
});

export class TestCommentsConnection {
  starts = 0;
  registrations = 0;
  readonly invocations: { roots: boolean; ids: string[] }[] = [];
  state = HubConnectionState.Disconnected;
  readonly handlers = new Map<string, (event: CommentCreated) => void>();
  reconnecting = () => undefined as void;
  reconnected = () => undefined as void;
  closed = () => undefined as void;
  async start() {
    this.starts++;
    this.state = HubConnectionState.Connected;
  }
  async stop() {
    this.state = HubConnectionState.Disconnected;
  }
  async invoke(_method: string, roots: boolean, ids: string[]) {
    this.invocations.push({ roots, ids });
  }
  on(name: string, handler: (event: CommentCreated) => void) {
    this.registrations++;
    this.handlers.set(name, handler);
  }
  onreconnecting(handler: () => void) {
    this.reconnecting = handler;
  }
  onreconnected(handler: () => void) {
    this.reconnected = handler;
  }
  onclose(handler: () => void) {
    this.closed = handler;
  }
  asConnection() {
    return this as unknown as HubConnection;
  }
  emit(commentId: string, parentId: string | null = null, eventId = `event-${commentId}`) {
    this.handlers.get('CommentCreated')?.({
      eventId,
      commentId,
      parentId,
      createdAtUtc: '2026-10-09T12:00:00Z',
    });
  }
  disconnect() {
    this.state = HubConnectionState.Reconnecting;
    this.reconnecting();
  }
  reconnect() {
    this.state = HubConnectionState.Connected;
    this.reconnected();
  }
}
