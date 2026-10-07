import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AttachmentGallery } from './attachment-gallery';
import { Comment } from './comment.models';

@Component({
  selector: 'app-comment-card',
  imports: [DatePipe, RouterLink, AttachmentGallery],
  templateUrl: './comment-card.html',
  styleUrl: './comment-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentCard {
  readonly comment = input.required<Comment>();
  readonly detail = input(false);
  readonly images = computed(() =>
    this.comment().attachments.filter((attachment) => attachment.contentType !== 'text/plain'),
  );
  readonly textFiles = computed(() =>
    this.comment().attachments.filter((attachment) => attachment.contentType === 'text/plain'),
  );

  attachmentUrl(id: string) {
    return `/api/comments/${encodeURIComponent(this.comment().id)}/attachments/${encodeURIComponent(id)}`;
  }
}
