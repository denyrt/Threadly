import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Comment, CommentPage, CreateCommentRequest } from './comment.models';

@Injectable({ providedIn: 'root' })
export class CommentsApi {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/comments';

  getPage(page: number) {
    return this.http.get<CommentPage>(this.url, { params: { page } });
  }

  getById(id: string) {
    return this.http.get<Comment>(`${this.url}/${encodeURIComponent(id)}`);
  }

  create(payload: CreateCommentRequest) {
    return this.http.post<Comment>(this.url, payload);
  }
}
