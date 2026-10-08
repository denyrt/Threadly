import { provideTestTurnstile } from './turnstile-testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CommentComposer } from './comment-composer';

describe('Comment content editor', () => {
  let fixture: ComponentFixture<CommentComposer>;
  let http: HttpTestingController;
  const element = () => fixture.nativeElement as HTMLElement;
  const textarea = () => element().querySelector('textarea')!;
  const button = (text: string) =>
    Array.from(element().querySelectorAll('button')).find(
      (value) => value.textContent?.trim() === text,
    )!;
  const draft = (value: string) => {
    textarea().value = value;
    textarea().dispatchEvent(new Event('input', { bubbles: true }));
  };
  const render = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideTestTurnstile(), provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(CommentComposer);
    http = TestBed.inject(HttpTestingController);
    await render();
  });
  afterEach(() => http.verify());

  it.each([
    { label: 'Italic', tag: 'i' },
    { label: 'Bold', tag: 'strong' },
  ])('wraps selection with $tag and inserts a pair at the caret', async ({ label, tag }) => {
    draft('one two');
    textarea().setSelectionRange(4, 7);
    button(label).click();
    expect(textarea().value).toBe(`one <${tag}>two</${tag}>`);
    expect(button(label).type).toBe('button');
    draft('');
    textarea().setSelectionRange(0, 0);
    button(label).click();
    expect(textarea().value).toBe(`<${tag}></${tag}>`);
    expect(textarea().selectionStart).toBe(tag.length + 2);
    http.expectNone((request) => request.method === 'POST');
  });

  it('escapes selected source for code and counts markup using UTF-16 length', async () => {
    draft('<x> & 🧵');
    textarea().setSelectionRange(0, textarea().value.length);
    button('Code').click();
    expect(textarea().value).toBe('<code>&lt;x&gt; &amp; 🧵</code>');
    await render();
    expect(element().textContent).toContain(`Message budget: ${textarea().value.length} / 2000`);
    expect(element().textContent).toContain('Includes formatting markup.');
  });

  it('validates link addresses and preserves the selection while entering a URL', async () => {
    draft('my link here');
    textarea().setSelectionRange(3, 7);
    button('Link').click();
    await render();
    const url = element().querySelector<HTMLInputElement>('#link-url')!;
    url.value = 'javascript:alert(1)';
    button('Insert link').click();
    await render();
    expect(element().textContent).toContain('absolute http or https');
    expect(textarea().value).toBe('my link here');
    url.value = 'https://example.com/?a=1&b=2';
    button('Insert link').click();
    await render();
    expect(textarea().value).toBe('my <a href="https://example.com/?a=1&amp;b=2">link</a> here');
    expect(textarea().selectionStart).toBe(textarea().value.length - 5);
  });

  it('previews only content, preserves draft and files, and renders safe normalized HTML', async () => {
    draft('<STRONG>Привіт</STRONG>\n<code>&lt;x&gt;&amp;</code>');
    fixture.componentInstance.files.set([new File(['notes'], 'notes.txt')]);
    button('Preview').click();
    await render();
    expect(button('Loading preview…').disabled).toBe(true);
    const request = http.expectOne('/api/comments/preview');
    expect(request.request.body).toEqual({ content: [{ type: 'text', html: textarea().value }] });
    const html = '<strong>Привіт</strong>\n<code>&lt;x&gt;&amp;</code>';
    request.flush({
      content: [{ type: 'text', html }],
      budgetUsed: textarea().value.length,
      budgetLimit: 2000,
    });
    await render();
    expect(element().querySelector('.preview strong')?.textContent).toBe('Привіт');
    expect(element().querySelector('.preview code')?.textContent).toBe('<x>&');
    expect(textarea().value).toContain('<STRONG>');
    expect(fixture.componentInstance.files()).toHaveLength(1);
    draft('changed');
    await render();
    expect(element().querySelector('.preview')).toBeNull();
  });

  it('ignores stale success and error responses for changed drafts', async () => {
    draft('first');
    button('Preview').click();
    const first = http.expectOne('/api/comments/preview');
    draft('second');
    await render();
    button('Preview').click();
    const second = http.expectOne('/api/comments/preview');
    second.flush({ content: [{ type: 'text', html: 'second' }], budgetUsed: 6, budgetLimit: 2000 });
    first.flush({ content: [{ type: 'text', html: 'first' }], budgetUsed: 5, budgetLimit: 2000 });
    await render();
    expect(element().querySelector('.preview .text')?.textContent).toBe('second');
    button('Preview').click();
    const obsolete = http.expectOne('/api/comments/preview');
    draft('third');
    obsolete.flush({}, { status: 503, statusText: 'Unavailable' });
    await render();
    expect(element().querySelector('.preview')).toBeNull();
    expect(fixture.componentInstance.previewError()).toBeNull();
  });

  it('keeps identity, content, files and reply context after preview and create errors', async () => {
    const parent = {
      id: 'parent',
      username: 'Parent',
      email: 'parent@example.com',
      content: [],
      createdAtUtc: '',
      parentId: null,
      replyCount: 0,
      attachments: [],
    };
    fixture.componentRef.setInput('parent', parent);
    fixture.componentInstance.form.setValue({
      username: 'Reader',
      email: 'reader@example.com',
      text: '<i>draft',
    });
    const file = new File(['notes'], 'notes.txt');
    fixture.componentInstance.files.set([file]);
    await render();
    button('Preview').click();
    http
      .expectOne('/api/comments/preview')
      .flush(
        { errors: { content: ['All HTML tags must be closed.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await render();
    expect(element().textContent).toContain('All HTML tags must be closed.');
    button('Preview').click();
    http.expectOne('/api/comments/preview').error(new ProgressEvent('network error'));
    await render();
    expect(element().textContent).toContain('Preview could not be loaded');
    fixture.componentInstance.submit();
    const request = http.expectOne('/api/comments');
    expect((request.request.body as FormData).get('parentId')).toBe('parent');
    request.flush(
      { errors: { content: ['All HTML tags must be closed.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await render();
    expect(fixture.componentInstance.form.getRawValue()).toEqual({
      username: 'Reader',
      email: 'reader@example.com',
      text: '<i>draft',
    });
    expect(fixture.componentInstance.files()).toEqual([file]);
    expect(fixture.componentInstance.parent()).toBe(parent);
    expect(element().querySelector('[id$="text-error"]')?.textContent).toContain('must be closed');
  });
});
