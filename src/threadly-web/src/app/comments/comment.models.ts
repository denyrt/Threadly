export interface Comment {
  id: string;
  username: string;
  email: string;
  text: string;
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

export interface CreateCommentRequest {
  username: string;
  email: string;
  text: string;
  parentId?: string;
}

export interface ValidationProblem {
  errors?: Record<string, string[]>;
}
