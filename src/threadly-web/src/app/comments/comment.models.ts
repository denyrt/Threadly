export interface Comment {
  id: string;
  username: string;
  email: string;
  content: TextContentBlock[];
  createdAtUtc: string;
  parentId: string | null;
  replyCount: number;
  attachments: CommentAttachment[];
}

export interface CommentAttachment {
  id: string;
  fileName: string;
  contentType: 'image/jpeg' | 'image/png' | 'image/gif' | 'text/plain';
  size: number;
  width: number | null;
  height: number | null;
}

export interface CommentReplies {
  items: Comment[];
  nextCursor: string | null;
}

export interface CommentPage {
  items: Comment[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type CommentSortBy = 'date' | 'username' | 'email';
export type CommentSortDirection = 'asc' | 'desc';

export interface CommentFeedQuery {
  page: number;
  sortBy: CommentSortBy;
  sortDirection: CommentSortDirection;
}

export interface CreateCommentRequest {
  username: string;
  email: string;
  content: TextContentBlock[];
  parentId?: string;
}

export interface ValidationProblem {
  errors?: Record<string, string[]>;
}

export interface TextContentBlock {
  type: 'text';
  html: string;
}

export interface CommentPreview {
  content: TextContentBlock[];
  budgetUsed: number;
  budgetLimit: number;
}
