import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Comment } from './comment.models';

@Component({
  selector: 'app-comment-card',
  imports: [DatePipe, RouterLink],
  templateUrl: './comment-card.html',
  styleUrl: './comment-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentCard {
  readonly comment = input.required<Comment>();
  readonly detail = input(false);
}
