export interface Comment {
  id: string;
  username: string;
  email: string;
  text: string;
  createdAtUtc: string;
  parentId: string | null;
  replyCount: number;
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
