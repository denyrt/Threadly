import { Turnstile, TurnstileApi, TurnstileRenderOptions } from './turnstile';

// Used only by specs. Production always loads the real widget and runtime configuration.
export class TestTurnstile {
  readonly widgets = new Map<string, TurnstileRenderOptions>();
  autoComplete = true;
  private sequence = 0;
  readonly api: TurnstileApi = {
    render: (_element, options) => {
      const id = String(++this.sequence);
      this.widgets.set(id, options);
      if (this.autoComplete) options.callback('test-token');
      return id;
    },
    reset: (id) => {
      if (this.autoComplete) this.widgets.get(id)?.callback('test-token');
    },
    remove: (id) => {
      this.widgets.delete(id);
    },
  };
  async load() {
    return { api: this.api, config: { siteKey: 'test-site-key', action: 'comment_create' } };
  }
}

export const provideTestTurnstile = () => ({ provide: Turnstile, useClass: TestTurnstile });
