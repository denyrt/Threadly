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
import { catchError, combineLatest, finalize, of, startWith, Subject, switchMap } from 'rxjs';
import { CommentThread } from './comment-thread';
import { CommentViewState } from './comment-view-state';
import { CommentComposer } from './comment-composer';
import { CommentPage } from './comment.models';
import { CommentsApi } from './comments-api';

@Component({
  selector: 'app-comments-page',
  imports: [CommentThread, CommentComposer],
  templateUrl: './comments-page.html',
  styleUrl: './comments-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentsPage implements OnInit {
  private readonly api = inject(CommentsApi);
  private readonly views = inject(CommentViewState);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<void>();
  private readonly composeButton =
    viewChild.required<ElementRef<HTMLButtonElement>>('composeButton');

  readonly page = signal(1);
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
    combineLatest([this.route.queryParamMap, this.refresh.pipe(startWith(undefined))])
      .pipe(
        switchMap(([params]) => {
          const page = Number(params.get('page') ?? 1);
          this.page.set(Number.isInteger(page) && page > 0 && page <= 2147483647 ? page : 1);
          this.loading.set(true);
          this.error.set(null);

          return (
            this.views.current.page ? of(this.views.current.page) : this.api.getPage(this.page())
          ).pipe(
            catchError(() => {
              this.error.set('Comments could not be loaded. Please try again.');
              return of(null);
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.views.current.page = result;
        this.result.set(result);
        this.views.restoreScroll();
      });
  }

  goToPage(page: number) {
    this.notice.set(null);
    void this.router.navigate(['/comments'], { queryParams: { page } });
  }

  retry() {
    this.views.current.page = null;
    this.refresh.next();
  }

  closeComposer() {
    this.showComposer.set(false);
    this.composeButton().nativeElement.focus();
  }

  onCreated() {
    this.closeComposer();
    this.notice.set('Comment posted.');
    if (this.page() === 1) {
      this.views.current.page = null;
      this.refresh.next();
    } else {
      void this.router.navigate(['/comments'], { queryParams: { page: 1 } });
    }
  }
}
