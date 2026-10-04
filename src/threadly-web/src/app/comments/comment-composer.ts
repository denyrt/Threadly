import { HttpErrorResponse } from '@angular/common/http';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { Comment, ValidationProblem } from './comment.models';
import { CommentsApi } from './comments-api';

@Component({
  selector: 'app-comment-composer',
  imports: [ReactiveFormsModule],
  templateUrl: './comment-composer.html',
  styleUrl: './comment-composer.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentComposer implements AfterViewInit {
  private readonly api = inject(CommentsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly usernameInput =
    viewChild.required<ElementRef<HTMLInputElement>>('usernameInput');

  readonly created = output<Comment>();
  readonly cancelled = output<void>();
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly serverErrors = signal<Record<string, string[]>>({});

  readonly form = this.formBuilder.group({
    username: [
      '',
      [Validators.required, Validators.maxLength(32), Validators.pattern(/^[a-zA-Z0-9]+$/)],
    ],
    email: ['', [Validators.required, Validators.maxLength(254), Validators.email]],
    text: ['', [Validators.required, Validators.maxLength(2000), Validators.pattern(/\S/)]],
  });

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.serverErrors.set({});
    });
  }

  ngAfterViewInit() {
    this.usernameInput().nativeElement.focus();
  }

  fieldError(field: 'username' | 'email' | 'text'): string | null {
    const serverError = this.serverErrors()[field]?.[0];
    if (serverError) {
      return serverError;
    }

    const control = this.form.controls[field];
    if (!control.touched || !control.invalid) {
      return null;
    }

    if (control.hasError('required') || (field === 'text' && control.hasError('pattern'))) {
      return 'This field is required.';
    }
    if (control.hasError('maxlength')) {
      return `Use at most ${control.getError('maxlength').requiredLength} characters.`;
    }
    return field === 'username'
      ? 'Use only ASCII letters and digits.'
      : 'Enter a valid email address.';
  }

  submit() {
    if (this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const payload = this.form.getRawValue();
    this.submitting.set(true);
    this.error.set(null);
    this.serverErrors.set({});
    this.form.disable({ emitEvent: false });

    this.api
      .create(payload)
      .pipe(
        finalize(() => {
          this.submitting.set(false);
          this.form.enable({ emitEvent: false });
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (comment) => this.created.emit(comment),
        error: (error: HttpErrorResponse) => {
          const problem = error.error as ValidationProblem | null;
          if (error.status === 400 && problem?.errors) {
            this.serverErrors.set(problem.errors);
            this.error.set('Please check the highlighted fields.');
          } else {
            this.error.set('Your comment could not be posted. Please try again.');
          }
        },
      });
  }
}
