import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  catchError,
  combineLatest,
  EMPTY,
  finalize,
  map,
  of,
  startWith,
  Subject,
  switchMap,
  tap,
} from 'rxjs';
import { CommentThread } from './comment-thread';
import { CommentViewState } from './comment-view-state';
import { CommentComposer } from './comment-composer';
import {
  CommentFeedQuery,
  CommentPage,
  CommentSortBy,
  CommentSortDirection,
} from './comment.models';
import { CommentsApi } from './comments-api';
import { CommentsLive } from './comments-live';
import { CommentsLiveStatus } from './comments-live-status';

@Component({
  selector: 'app-comments-page',
  imports: [CommentThread, CommentComposer, CommentsLiveStatus],
  templateUrl: './comments-page.html',
  styleUrl: './comments-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentsPage implements OnInit {
  private readonly api = inject(CommentsApi);
  readonly views = inject(CommentViewState);
  private readonly live = inject(CommentsLive);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<number>();
  private refreshSequence = 0;
  private handledRefresh = 0;
  private renderedView: object | null = null;
  private readonly composeButton =
    viewChild.required<ElementRef<HTMLButtonElement>>('composeButton');

  readonly page = signal(1);
  readonly sortBy = signal<CommentSortBy>('date');
  readonly sortDirection = signal<CommentSortDirection>('desc');
  readonly viewVersion = signal(0);
  readonly result = signal<CommentPage | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly showComposer = signal(false);
  readonly notice = signal<string | null>(null);
  readonly totalPages = computed(() => {
    const result = this.result();
    return result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;
  });

  ngOnInit() {
    combineLatest([this.route.queryParamMap, this.refresh.pipe(startWith(0))])
      .pipe(
        switchMap(([params, refreshSequence]) => {
          const manual = refreshSequence !== this.handledRefresh;
          this.handledRefresh = refreshSequence;
          const rawPage = params.get('page');
          const page = Number(rawPage ?? 1);
          const validPage =
            rawPage === null ||
            (params.getAll('page').length === 1 &&
              /^\d+$/.test(rawPage) &&
              Number.isInteger(page) &&
              page > 0 &&
              page <= 2147483647);
          const sortBy = params.get('sortBy');
          const validSortBy =
            sortBy === null ||
            (params.getAll('sortBy').length === 1 &&
              (sortBy === 'date' || sortBy === 'username' || sortBy === 'email'));
          const sortDirection = params.get('sortDirection');
          const validSortDirection =
            sortDirection === null ||
            (params.getAll('sortDirection').length === 1 &&
              (sortDirection === 'asc' || sortDirection === 'desc'));
          const query: CommentFeedQuery = {
            page: validPage ? page : 1,
            sortBy: validSortBy && sortBy !== null ? (sortBy as CommentSortBy) : 'date',
            sortDirection:
              validSortDirection && sortDirection !== null
                ? (sortDirection as CommentSortDirection)
                : 'desc',
          };
          if (!validPage || !validSortBy || !validSortDirection) {
            void this.router.navigate(['/comments'], {
              queryParams: query,
              queryParamsHandling: 'merge',
              replaceUrl: true,
            });
            return EMPTY;
          }

          this.page.set(query.page);
          this.sortBy.set(query.sortBy);
          this.sortDirection.set(query.sortDirection);
          const view = this.views.selectFeed(query);
          // Recreate cards across history entries, even if they contain the same comment IDs.
          const navigation = this.renderedView !== view;
          if (navigation) {
            this.viewVersion.update((version) => version + 1);
            this.result.set(null);
            this.renderedView = view;
          }
          this.loading.set(true);
          this.error.set(null);
          const version = view.feedChanges.version();
          const rootsVersion = this.views.roots.version();
          const cached = !manual && view.page !== null;
          const counts = this.views.countSnapshot();

          return (
            cached ? of(view.page) : this.api.getPage(query.page, query.sortBy, query.sortDirection)
          ).pipe(
            catchError(() => {
              this.error.set('Comments could not be loaded. Please try again.');
              return of(view.page);
            }),
            map((result) => {
              if (!cached && !navigation && result && this.views.composers() > 0) {
                this.error.set('Finish or close open composers, then refresh comments again.');
                return view.page;
              }
              return result;
            }),
            tap((result) => {
              if (result && !this.error()) {
                view.page = result;
                if (!cached) {
                  this.views.applyCountSnapshot(result.items, counts);
                  view.feedChanges.acknowledge(version);
                  this.views.roots.acknowledge(rootsVersion);
                  this.live.syncCounts();
                }
              }
              if (navigation) this.views.restoreScroll();
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.result.set(result);
      });
  }

  goToPage(page: number) {
    this.notice.set(null);
    void this.router.navigate(['/comments'], {
      queryParams: { page, sortBy: this.sortBy(), sortDirection: this.sortDirection() },
    });
  }

  changeSortBy(value: string) {
    if (value !== 'date' && value !== 'username' && value !== 'email') return;
    this.changeSort(value, this.sortDirection());
  }

  changeSortDirection(value: string) {
    if (value !== 'asc' && value !== 'desc') return;
    this.changeSort(this.sortBy(), value);
  }

  private changeSort(sortBy: CommentSortBy, sortDirection: CommentSortDirection) {
    this.notice.set(null);
    void this.router.navigate(['/comments'], { queryParams: { page: 1, sortBy, sortDirection } });
  }

  retry() {
    if (this.views.composers() > 0 || this.loading()) return;
    this.refresh.next(++this.refreshSequence);
  }

  closeComposer() {
    this.showComposer.set(false);
    this.composeButton().nativeElement.focus();
  }

  onCreated() {
    this.closeComposer();
    this.notice.set('Comment posted.');
    this.views.invalidateFeedPages();
    // Another open composer must keep its text, File objects and CAPTCHA widget.
    if (this.views.composers() > 0) return;
    if (this.page() === 1) {
      this.refresh.next(++this.refreshSequence);
    } else {
      void this.router.navigate(['/comments'], {
        queryParams: { page: 1, sortBy: this.sortBy(), sortDirection: this.sortDirection() },
      });
    }
  }
}
