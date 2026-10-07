import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TextContentBlock } from './comment.models';

@Component({
  selector: 'app-comment-body',
  template: `@for (block of content(); track $index) {
    <div class="text" [innerHTML]="block.html"></div>
  }`,
  styles: `
    .text {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      margin: 0.75rem 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentBody {
  readonly content = input.required<readonly TextContentBlock[]>();
}
