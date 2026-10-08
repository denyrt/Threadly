import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Turnstile, TurnstileApi } from './turnstile';
import { CommentComposer } from './comment-composer';
import { provideTestTurnstile, TestTurnstile } from './turnstile-testing';

describe('Turnstile loader', () => {
  let http: HttpTestingController;
  const turnstileWindow = window as Window & { turnstile?: TurnstileApi };
  const scripts = () =>
    Array.from(
      document.querySelectorAll<HTMLScriptElement>('script[src*="challenges.cloudflare.com"]'),
    );
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    scripts().forEach((script) => script.remove());
    delete turnstileWindow.turnstile;
    vi.useRealTimers();
  });

  it('shares a single script and runtime configuration across concurrent composers', async () => {
    const service = TestBed.inject(Turnstile);
    const first = service.load();
    const second = service.load();
    expect(scripts()).toHaveLength(1);
    http
      .expectOne('/api/captcha/config')
      .flush({ siteKey: 'public-key', action: 'comment_create' });
    turnstileWindow.turnstile = new TestTurnstile().api;
    scripts()[0].dispatchEvent(new Event('load'));
    expect((await first).api).toBe((await second).api);
    expect((await service.load()).config.siteKey).toBe('public-key');
    expect(scripts()).toHaveLength(1);
  });

  it.each(['error', 'timeout'])(
    'recovers after script %s without leaving duplicate scripts',
    async (failure) => {
      vi.useFakeTimers();
      const service = TestBed.inject(Turnstile);
      const load = service.load();
      const rejected = expect(load).rejects.toThrow('script unavailable');
      http
        .expectOne('/api/captcha/config')
        .flush({ siteKey: 'public-key', action: 'comment_create' });
      if (failure === 'timeout') await vi.advanceTimersByTimeAsync(15001);
      else scripts()[0].dispatchEvent(new Event('error'));
      await rejected;
      expect(scripts()).toHaveLength(0);
      const retry = service.load();
      expect(scripts()).toHaveLength(1);
      turnstileWindow.turnstile = new TestTurnstile().api;
      scripts()[0].dispatchEvent(new Event('load'));
      await retry;
    },
  );
});

describe('Composer verification', () => {
  let http: HttpTestingController;
  let turnstile: TestTurnstile;
  const draft = { username: 'Reader', email: 'reader@example.com', text: 'Draft text' };
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTestTurnstile()],
    });
    http = TestBed.inject(HttpTestingController);
    turnstile = TestBed.inject(Turnstile) as unknown as TestTurnstile;
    turnstile.autoComplete = false;
  });
  afterEach(() => http.verify());
  const widget = () => Array.from(turnstile.widgets.values())[0];
  async function composer() {
    const fixture = TestBed.createComponent(CommentComposer);
    fixture.componentInstance.form.setValue(draft);
    await fixture.whenStable();
    return fixture;
  }

  it('isolates tokens and destroys only the closed composer widget', async () => {
    const first = await composer();
    const second = await composer();
    const widgets = Array.from(turnstile.widgets.values());
    expect(widgets).toHaveLength(2);
    widgets[0].callback('first-token');
    widgets[1].callback('second-token');
    expect(first.componentInstance.captchaToken()).toBe('first-token');
    expect(second.componentInstance.captchaToken()).toBe('second-token');
    await first.whenStable();
    expect(turnstile.widgets.size).toBe(2);
    first.destroy();
    expect(turnstile.widgets.size).toBe(1);
    widgets[0].callback('late-token');
    expect(second.componentInstance.captchaToken()).toBe('second-token');
  });

  it('requires a current token, handles expiry and lets preview run independently', async () => {
    const fixture = await composer();
    fixture.componentInstance.submit();
    http.expectNone('/api/comments');
    fixture.componentInstance.preview();
    const preview = http.expectOne('/api/comments/preview');
    expect(preview.request.body).toEqual({ content: [{ type: 'text', html: draft.text }] });
    preview.flush({ content: [], budgetUsed: 10, budgetLimit: 2000 });
    widget().callback('token');
    widget()['expired-callback']();
    await fixture.whenStable();
    expect(fixture.componentInstance.captchaToken()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Verification expired');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
        'button[type="submit"]',
      )!.disabled,
    ).toBe(true);
    const reset = vi.spyOn(turnstile.api, 'reset');
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find((button) => button.textContent?.includes('Retry verification'))!
      .click();
    expect(reset).toHaveBeenCalledOnce();
    expect(turnstile.widgets.size).toBe(1);
    widget().callback('new-token');
    expect(fixture.componentInstance.captchaToken()).toBe('new-token');
  });

  it('keeps valid verification after local validation errors', async () => {
    const fixture = await composer();
    widget().callback('token');
    const reset = vi.spyOn(turnstile.api, 'reset');
    fixture.componentInstance.form.controls.email.setValue('invalid');
    fixture.componentInstance.submit();
    http.expectNone('/api/comments');
    expect(reset).not.toHaveBeenCalled();
    expect(fixture.componentInstance.captchaToken()).toBe('token');
  });

  it.each([400, 429, 503, 0])(
    'preserves draft/files and consumes the token after HTTP %s',
    async (status) => {
      const fixture = await composer();
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
      const file = new File(['text'], 'notes.txt');
      fixture.componentInstance.files.set([file]);
      widget().callback('token');
      fixture.componentInstance.submit();
      const request = http.expectOne('/api/comments');
      expect((request.request.body as FormData).get('captchaToken')).toBe('token');
      fixture.componentInstance.submit();
      http.expectNone('/api/comments');
      if (status === 0) request.error(new ProgressEvent('network error'));
      else
        request.flush(
          { errors: { captchaToken: ['Verify again.'] } },
          { status, statusText: 'Failed' },
        );
      expect(fixture.componentInstance.form.getRawValue()).toEqual(draft);
      expect(fixture.componentInstance.files()[0]).toBe(file);
      expect(fixture.componentInstance.parent()).toBe(parent);
      expect(fixture.componentInstance.captchaToken()).toBeNull();
      fixture.componentInstance.form.controls.text.setValue('Edited');
      if (status === 400)
        expect(fixture.componentInstance.serverErrors()['captchaToken']).toEqual(['Verify again.']);
      if (status === 429)
        expect(fixture.componentInstance.error()).toContain('Too many publication');
      if (status === 503)
        expect(fixture.componentInstance.error()).toContain('temporarily unavailable');
      if (status === 0) expect(fixture.componentInstance.error()).toContain('may have been posted');
      http.expectNone('/api/comments');
      widget().callback('fresh-token');
      expect(fixture.componentInstance.serverErrors()['captchaToken']).toBeUndefined();
      fixture.componentInstance.submit();
      const retry = http.expectOne('/api/comments');
      expect((retry.request.body as FormData).get('captchaToken')).toBe('fresh-token');
      expect(((retry.request.body as FormData).get('attachments') as File).name).toBe(file.name);
      retry.flush({}, { status: 503, statusText: 'Unavailable' });
    },
  );

  it('invalidates a spent token after the server rejects attachments', async () => {
    const fixture = await composer();
    widget().callback('token');
    fixture.componentInstance.submit();
    const request = http.expectOne('/api/comments');
    expect(request.request.body.captchaToken).toBe('token');
    request.flush(
      { errors: { 'attachments[0]': ['Invalid image.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(fixture.componentInstance.captchaToken()).toBeNull();
  });

  it('reports widget errors and retries failed initialization without duplicate rendering', async () => {
    const load = vi.spyOn(turnstile, 'load').mockRejectedValueOnce(new Error('Script failed'));
    const fixture = await composer();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Verification could not load',
    );
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find((button) => button.textContent?.includes('Retry verification'))!
      .click();
    await fixture.whenStable();
    expect(load).toHaveBeenCalledTimes(2);
    expect(turnstile.widgets.size).toBe(1);
    widget().callback('token');
    widget()['error-callback']();
    expect(fixture.componentInstance.captchaToken()).toBeNull();
  });

  it('recreates a broken widget while preserving the draft and ignoring old callbacks', async () => {
    const fixture = await composer();
    const oldWidget = widget();
    oldWidget.callback('token');
    const file = new File(['notes'], 'notes.txt');
    fixture.componentInstance.files.set([file]);
    vi.spyOn(turnstile.api, 'reset').mockImplementationOnce(() => {
      throw new Error('Missing instance');
    });
    fixture.componentInstance.submit();
    http.expectOne('/api/comments').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(turnstile.widgets.size).toBe(1);
    expect(widget()).not.toBe(oldWidget);
    oldWidget.callback('obsolete-token');
    expect(fixture.componentInstance.captchaToken()).toBeNull();
    expect(fixture.componentInstance.form.getRawValue()).toEqual(draft);
    expect(fixture.componentInstance.files()[0]).toBe(file);
    widget().callback('fresh-token');
    expect(fixture.componentInstance.captchaToken()).toBe('fresh-token');
  });

  it('does not render when the composer closes while the script loads', async () => {
    let complete!: (value: Awaited<ReturnType<TestTurnstile['load']>>) => void;
    vi.spyOn(turnstile, 'load').mockReturnValue(
      new Promise((resolve) => {
        complete = resolve;
      }),
    );
    const fixture = await composer();
    fixture.destroy();
    complete({ api: turnstile.api, config: { siteKey: 'key', action: 'comment_create' } });
    await Promise.resolve();
    expect(turnstile.widgets.size).toBe(0);
  });
});
