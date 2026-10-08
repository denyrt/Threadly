import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import {
  Comment,
  CommentPage,
  CommentPreview,
  CommentReplies,
  CommentSortBy,
  CommentSortDirection,
  CreateCommentRequest,
  TextContentBlock,
} from './comment.models';

@Injectable({ providedIn: 'root' })
export class CommentsApi {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/comments';

  getPage(
    page: number,
    sortBy: CommentSortBy = 'date',
    sortDirection: CommentSortDirection = 'desc',
  ) {
    return this.http.get<CommentPage>(this.url, { params: { page, sortBy, sortDirection } });
  }

  getById(id: string) {
    return this.http.get<Comment>(`${this.url}/${encodeURIComponent(id)}`);
  }

  create(payload: CreateCommentRequest, files: readonly File[] = []) {
    if (files.length === 0) {
      return this.http.post<Comment>(this.url, payload);
    }
    const form = new FormData();
    form.append('username', payload.username);
    form.append('email', payload.email);
    form.append('captchaToken', payload.captchaToken);
    form.append('content', JSON.stringify(payload.content));
    if (payload.parentId) form.append('parentId', payload.parentId);
    for (const file of files) form.append('attachments', file, file.name);
    return this.http.post<Comment>(this.url, form);
  }

  getReplies(id: string, cursor: string | null = null) {
    return this.http.get<CommentReplies>(`${this.url}/${encodeURIComponent(id)}/replies`, {
      params: cursor === null ? {} : { cursor },
    });
  }

  preview(content: TextContentBlock[]) {
    return this.http.post<CommentPreview>(`${this.url}/preview`, { content });
  }
}
