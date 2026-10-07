import { HttpErrorResponse } from '@angular/common/http';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  input,
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
  readonly parent = input<Comment | null>(null);
  readonly cancelled = output<void>();
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly serverErrors = signal<Record<string, string[]>>({});
  readonly files = signal<File[]>([]);
  readonly fileErrors = computed(() =>
    this.files().map((file, index) => {
      const extension = file.name.split('.').pop()?.toLowerCase();
      if (!extension || !['jpg', 'jpeg', 'png', 'gif', 'txt'].includes(extension)) {
        return 'Use JPG, PNG, GIF or TXT files.';
      }
      if (!file.size) return 'The file is empty.';
      if (file.size > (extension === 'txt' ? 100 * 1024 : 2 * 1024 * 1024)) {
        return extension === 'txt'
          ? 'TXT must be at most 100 KiB.'
          : 'Images must be at most 2 MiB.';
      }
      if (file.name.length > 128) return 'Use a file name of at most 128 characters.';
      return this.serverErrors()[`attachments[${index}]`]?.[0] ?? null;
    }),
  );
  readonly attachmentsError = computed(() =>
    this.files().length > 10
      ? 'Choose at most 10 attachments.'
      : (this.serverErrors()['attachments']?.[0] ?? null),
  );
  readonly hasFileErrors = computed(() => this.fileErrors().some(Boolean));

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
      this.serverErrors.update((errors) =>
        Object.fromEntries(Object.entries(errors).filter(([key]) => key.startsWith('attachments'))),
      );
    });
  }

  ngAfterViewInit() {
    this.usernameInput().nativeElement.focus();
  }

  fieldId(name: string) {
    return this.parent() ? `reply-${this.parent()!.id}-${name}` : name;
  }

  selectFiles(event: Event) {
    const input = event.target as HTMLInputElement;
    if (!this.submitting()) {
      this.files.update((files) => [...files, ...Array.from(input.files ?? [])]);
      this.clearAttachmentErrors();
    }
    input.value = '';
  }

  removeFile(index: number) {
    if (this.submitting()) return;
    this.files.update((files) => files.filter((_, position) => position !== index));
    this.clearAttachmentErrors();
  }

  private clearAttachmentErrors() {
    this.serverErrors.update((errors) =>
      Object.fromEntries(Object.entries(errors).filter(([key]) => !key.startsWith('attachments'))),
    );
    this.error.set(null);
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
    if (this.form.invalid || this.attachmentsError() || this.fileErrors().some(Boolean)) {
      this.form.markAllAsTouched();
      this.error.set('Please check the highlighted fields and attachments.');
      return;
    }

    const payload = {
      ...this.form.getRawValue(),
      ...(this.parent() ? { parentId: this.parent()!.id } : {}),
    };
    this.submitting.set(true);
    this.error.set(null);
    this.serverErrors.set({});
    this.form.disable({ emitEvent: false });

    this.api
      .create(payload, this.files())
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
            this.error.set(
              problem.errors['parentId']?.[0] ?? 'Please check the highlighted fields.',
            );
          } else if (error.status === 413) {
            this.error.set(
              'The upload is too large. Use up to 10 images of 2 MiB each, or TXT files of 100 KiB.',
            );
          } else if (error.status === 429) {
            this.error.set(
              'Uploads are busy. Your files are still selected; please try again shortly.',
            );
          } else {
            this.error.set('Your comment could not be posted. Please try again.');
          }
        },
      });
  }
}
