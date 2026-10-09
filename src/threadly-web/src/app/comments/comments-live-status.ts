import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { CommentsLive } from './comments-live';

@Component({
  selector: 'app-comments-live-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="muted" role="status">
      @if (live.limited()) {
        Live updates cover up to 100 visible comments. Refresh other comments manually.
      } @else if (live.status() === 'unavailable' || live.countsUnavailable()) {
        Live updates unavailable. You can still refresh manually.
        <button class="button quiet" type="button" (click)="live.retry()">
          Retry live updates
        </button>
      } @else if (live.status() === 'connected') {
        Live updates connected.
      } @else {
        {{
          live.status() === 'reconnecting'
            ? 'Reconnecting live updates…'
            : 'Connecting live updates…'
        }}
      }
    </p>
  `,
})
export class CommentsLiveStatus {
  readonly live = inject(CommentsLive);
  constructor() {
    inject(DestroyRef).onDestroy(this.live.enter());
  }
}
