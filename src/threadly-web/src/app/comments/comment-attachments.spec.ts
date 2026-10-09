import { provideQuietCommentsLive } from './comments-live-testing';
import { provideTestTurnstile } from './turnstile-testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CommentComposer } from './comment-composer';
import { CommentCard } from './comment-card';
import { Comment } from './comment.models';

describe('Comment attachments', () => {
  let fixture: ComponentFixture<CommentComposer>;
  let http: HttpTestingController;
  const comment: Comment = {
    id: 'root',
    username: 'Reader',
    email: 'reader@example.com',
    content: [{ type: 'text', html: 'A comment' }],
    createdAtUtc: '2026-10-06T12:00:00Z',
    parentId: null,
    replyCount: 0,
    attachments: [],
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideTestTurnstile(),
        provideQuietCommentsLive(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CommentComposer);
    fixture.componentInstance.form.setValue({
      username: comment.username,
      email: comment.email,
      text: comment.content[0].html,
    });
    await fixture.whenStable();
  });
  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  async function select(files: File[]) {
    const input = element().querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }
  const file = (name = 'notes.txt', bytes = 'hello') =>
    new File([bytes], name, { type: 'text/plain' });
  function submit() {
    element()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  }

  it('adds files across selections, removes one and allows choosing it again', async () => {
    const first = file();
    await select([first]);
    await select([file('second.txt')]);
    expect(element().querySelectorAll('.selected-files li')).toHaveLength(2);
    (element().querySelector('[aria-label="Remove notes.txt"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(fixture.componentInstance.files().map((value) => value.name)).toEqual(['second.txt']);
    await select([first]);
    expect(fixture.componentInstance.files().map((value) => value.name)).toEqual([
      'second.txt',
      'notes.txt',
    ]);
  });

  it.each([
    ['bad.svg', 'data', 'JPG, PNG, GIF or TXT'],
    ['empty.txt', '', 'empty'],
    ['large.txt', 'x'.repeat(102401), '100 KiB'],
    ['large.jpg', 'x'.repeat(2097153), '2 MiB'],
  ])('blocks %s before posting and shows a file error', async (name, bytes, message) => {
    await select([file(name, bytes)]);
    submit();
    await fixture.whenStable();
    http.expectNone((request) => request.method === 'POST');
    expect(element().querySelector('.selected-files')?.textContent).toContain(message);
    expect(element().querySelector('input[type="file"]')?.getAttribute('aria-invalid')).toBe(
      'true',
    );
  });

  it('blocks eleven files and can submit after removing the extra file', async () => {
    await select(Array.from({ length: 11 }, (_, index) => file(`${index}.txt`)));
    submit();
    await fixture.whenStable();
    http.expectNone((request) => request.method === 'POST');
    expect(element().textContent).toContain('at most 10');
    fixture.componentInstance.removeFile(10);
    submit();
    const request = http.expectOne({ method: 'POST', url: '/api/comments' });
    expect((request.request.body as FormData).getAll('attachments')).toHaveLength(10);
    request.flush(comment);
  });

  it.each([false, true])('posts the selected files as multipart for a reply: %s', async (reply) => {
    if (reply) fixture.componentRef.setInput('parent', comment);
    await fixture.whenStable();
    await select([file(), file('photo.png')]);
    const created = vi.fn();
    fixture.componentInstance.created.subscribe(created);
    submit();
    await fixture.whenStable();
    const request = http.expectOne({ method: 'POST', url: '/api/comments' });
    const body = request.request.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect(body.get('username')).toBe(comment.username);
    expect(body.get('email')).toBe(comment.email);
    expect(body.get('content')).toBe(JSON.stringify(comment.content));
    expect(body.get('parentId')).toBe(reply ? comment.id : null);
    expect(body.getAll('attachments').map((value) => (value as File).name)).toEqual([
      'notes.txt',
      'photo.png',
    ]);
    expect((element().querySelector('input[type="file"]') as HTMLInputElement).disabled).toBe(true);
    fixture.componentInstance.removeFile(0);
    expect(fixture.componentInstance.files()).toHaveLength(2);
    submit();
    http.expectNone((value) => value.method === 'POST');
    request.flush(comment);
    expect(created).toHaveBeenCalledWith(comment);
  });

  it('keeps server file errors and the draft until the selection is corrected', async () => {
    await select([file('fake.png')]);
    submit();
    http
      .expectOne('/api/comments')
      .flush(
        { errors: { 'attachments[0]': ['The image content does not match its file extension.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    fixture.componentInstance.form.controls.text.setValue('Edited comment');
    await fixture.whenStable();
    expect(element().querySelector('.selected-files')?.textContent).toContain('does not match');
    expect(fixture.componentInstance.files()).toHaveLength(1);
    fixture.componentInstance.removeFile(0);
    await select([file()]);
    submit();
    const retry = http.expectOne('/api/comments');
    expect((retry.request.body as FormData).get('content')).toBe(
      JSON.stringify([{ type: 'text', html: 'Edited comment' }]),
    );
    retry.flush(comment);
  });

  it.each([413, 429, 500])(
    'preserves files and an editable draft after HTTP %s',
    async (status) => {
      await select([file()]);
      submit();
      http.expectOne('/api/comments').flush({}, { status, statusText: 'Failed' });
      await fixture.whenStable();
      expect(fixture.componentInstance.files()).toHaveLength(1);
      expect(fixture.componentInstance.form.controls.text.value).toBe(comment.content[0].html);
      expect(fixture.componentInstance.form.enabled).toBe(true);
      expect(element().querySelector('.error-message')?.textContent).toBeTruthy();
      expect((element().querySelector('input[type="file"]') as HTMLInputElement).disabled).toBe(
        false,
      );
    },
  );

  it('renders images and TXT download links in a comment card', async () => {
    const card = TestBed.createComponent(CommentCard);
    card.componentRef.setInput('comment', {
      ...comment,
      attachments: [
        {
          id: 'image',
          fileName: 'photo.png',
          contentType: 'image/png',
          size: 1200,
          width: 320,
          height: 160,
        },
        {
          id: 'text',
          fileName: 'notes.txt',
          contentType: 'text/plain',
          size: 50,
          width: null,
          height: null,
        },
      ],
    });
    await card.whenStable();
    const view = card.nativeElement as HTMLElement;
    expect(view.querySelector('img')?.getAttribute('src')).toBe(
      '/api/comments/root/attachments/image',
    );
    expect(view.querySelector('img')?.getAttribute('alt')).toBe('photo.png');
    expect(view.querySelector('.thumbnail')?.getAttribute('aria-haspopup')).toBe('dialog');
    expect(view.querySelector('.text-attachment')?.getAttribute('href')).toBe(
      '/api/comments/root/attachments/text',
    );
    expect(view.querySelector('.text-attachment')?.getAttribute('download')).toBe('notes.txt');
  });
});
