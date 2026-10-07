import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { CommentAttachment } from './comment.models';

@Component({
  selector: 'app-attachment-gallery',
  templateUrl: './attachment-gallery.html',
  styleUrl: './attachment-gallery.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttachmentGallery {
  readonly commentId = input.required<string>();
  readonly images = input.required<readonly CommentAttachment[]>();
  readonly previews = computed(() => this.images().slice(0, 4));
  readonly remainingCount = computed(() => this.images().length - this.previews().length);
  readonly selectedIndex = signal<number | null>(null);
  readonly selectedImage = computed(() => {
    const index = this.selectedIndex();
    return index === null ? null : (this.images()[index] ?? null);
  });
  readonly imageStatus = signal<'loading' | 'loaded' | 'error'>('loading');
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('viewer');

  attachmentUrl(id: string) {
    return `/api/comments/${encodeURIComponent(this.commentId())}/attachments/${encodeURIComponent(id)}`;
  }

  open(index: number) {
    this.imageStatus.set('loading');
    this.selectedIndex.set(index);
    this.dialog().nativeElement.showModal();
  }

  close() {
    this.dialog().nativeElement.close();
  }

  onClose() {
    this.selectedIndex.set(null);
  }

  move(direction: number) {
    const index = this.selectedIndex();
    if (index === null || this.images().length < 2) return;
    this.imageStatus.set('loading');
    this.selectedIndex.set((index + direction + this.images().length) % this.images().length);
  }

  onKeydown(event: KeyboardEvent) {
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault();
      this.move(event.key === 'ArrowLeft' ? -1 : 1);
    }
  }

  onBackdropClick(event: MouseEvent) {
    if (event.target !== event.currentTarget) return;
    const rect = this.dialog().nativeElement.getBoundingClientRect();
    if (
      event.clientX < rect.left ||
      event.clientX > rect.right ||
      event.clientY < rect.top ||
      event.clientY > rect.bottom
    ) {
      this.close();
    }
  }
}
