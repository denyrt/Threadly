export interface Comment {
  id: string;
  username: string;
  email: string;
  text: string;
  createdAtUtc: string;
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
}

export interface ValidationProblem {
  errors?: Record<string, string[]>;
}
