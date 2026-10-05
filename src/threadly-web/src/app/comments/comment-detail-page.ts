import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, finalize, of, startWith, Subject, switchMap } from 'rxjs';
import { CommentCard } from './comment-card';
import { Comment } from './comment.models';
import { CommentsApi } from './comments-api';

@Component({
  selector: 'app-comment-detail-page',
  imports: [CommentCard, RouterLink],
  templateUrl: './comment-detail-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentDetailPage implements OnInit {
  private readonly api = inject(CommentsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<void>();

  readonly comment = signal<Comment | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly notFound = signal(false);

  ngOnInit() {
    combineLatest([this.route.paramMap, this.refresh.pipe(startWith(undefined))])
      .pipe(
        switchMap(([params]) => {
          this.loading.set(true);
          this.error.set(null);
          this.notFound.set(false);

          return this.api.getById(params.get('id') ?? '').pipe(
            catchError((error: HttpErrorResponse) => {
              this.notFound.set(error.status === 404);
              this.error.set(
                error.status === 404
                  ? 'This comment could not be found.'
                  : 'The comment could not be loaded. Please try again.',
              );
              return of(null);
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((comment) => this.comment.set(comment));
  }

  retry() {
    this.refresh.next();
  }
}
