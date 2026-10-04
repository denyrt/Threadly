import { Routes } from '@angular/router';
import { CommentDetailPage } from './comments/comment-detail-page';
import { CommentsPage } from './comments/comments-page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'comments' },
  { path: 'comments', component: CommentsPage },
  { path: 'comments/:id', component: CommentDetailPage },
  { path: '**', redirectTo: 'comments' },
];
