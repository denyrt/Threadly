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
import { Comment, CommentPreview, TextContentBlock, ValidationProblem } from './comment.models';
import { CommentBody } from './comment-body';
import { CommentsApi } from './comments-api';

@Component({
  selector: 'app-comment-composer',
  imports: [ReactiveFormsModule, CommentBody],
  templateUrl: './comment-composer.html',
  styleUrl: './comment-composer.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentComposer implements AfterViewInit {
  private readonly textInput = viewChild.required<ElementRef<HTMLTextAreaElement>>('textInput');
  private previewRevision = 0;
  private linkSelection = { start: 0, end: 0 };
  readonly previewResult = signal<CommentPreview | null>(null);
  readonly previewLoading = signal(false);
  readonly previewError = signal<string | null>(null);
  readonly linkOpen = signal(false);
  readonly linkError = signal<string | null>(null);
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
    this.form.controls.text.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.previewRevision++;
      this.previewResult.set(null);
      this.previewLoading.set(false);
      this.previewError.set(null);
      this.linkOpen.set(false);
    });
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
    const serverError =
      field === 'text'
        ? Object.entries(this.serverErrors()).find(([key]) =>
            key.toLowerCase().startsWith('content'),
          )?.[1][0]
        : this.serverErrors()[field]?.[0];
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

    const { username, email } = this.form.getRawValue();
    const payload = {
      username,
      email,
      content: this.content(),
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

  private content(): TextContentBlock[] {
    return [{ type: 'text', html: this.form.controls.text.value }];
  }

  preview() {
    if (this.submitting() || this.previewLoading()) return;
    const revision = ++this.previewRevision;
    this.previewLoading.set(true);
    this.previewResult.set(null);
    this.previewError.set(null);
    this.api
      .preview(this.content())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (revision !== this.previewRevision) return;
          this.previewLoading.set(false);
          this.previewResult.set(result);
        },
        error: (error: HttpErrorResponse) => {
          if (revision !== this.previewRevision) return;
          this.previewLoading.set(false);
          const problem = error.error as ValidationProblem | null;
          this.previewError.set(
            error.status === 400 && problem?.errors
              ? Object.values(problem.errors).flat().join(' ')
              : 'Preview could not be loaded. Your draft is saved here; please try again.',
          );
        },
      });
  }

  format(tag: 'i' | 'strong' | 'code') {
    if (this.submitting()) return;
    const input = this.textInput().nativeElement;
    const selected = input.value.slice(input.selectionStart, input.selectionEnd);
    const value = tag === 'code' ? this.escapeHtml(selected) : selected;
    this.insertMarkup(
      `<${tag}>${value}</${tag}>`,
      input.selectionStart,
      input.selectionEnd,
      selected ? undefined : tag.length + 2,
    );
  }

  openLink() {
    const input = this.textInput().nativeElement;
    this.linkSelection = { start: input.selectionStart, end: input.selectionEnd };
    this.linkError.set(null);
    this.linkOpen.set(true);
  }

  insertLink(address: string) {
    if (this.submitting()) return;
    try {
      const url = new URL(address);
      if (
        !/^https?:\/\//i.test(address) ||
        /[\s\\]/.test(address) ||
        Array.from(address).some(
          (character) => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127,
        ) ||
        !['http:', 'https:'].includes(url.protocol) ||
        !url.hostname
      )
        throw new Error();
    } catch {
      this.linkError.set('Enter an absolute http or https URL.');
      return;
    }
    const { start, end } = this.linkSelection;
    const selected = this.form.controls.text.value.slice(start, end);
    const opening = `<a href="${this.escapeHtml(address)}">`;
    this.insertMarkup(`${opening}${selected || this.escapeHtml(address)}</a>`, start, end);
    this.linkOpen.set(false);
  }

  private escapeHtml(value: string) {
    return value
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }

  private insertMarkup(markup: string, start: number, end: number, caretOffset?: number) {
    const input = this.textInput().nativeElement;
    input.focus();
    input.setSelectionRange(start, end);
    // Native insertText preserves the textarea's browser Undo/Redo history.
    const inserted = document.execCommand?.('insertText', false, markup);
    if (!inserted) {
      input.setRangeText(markup, start, end, 'end');
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    this.form.controls.text.setValue(input.value);
    const caret = start + (caretOffset ?? markup.length);
    input.setSelectionRange(caret, caret);
  }
}
