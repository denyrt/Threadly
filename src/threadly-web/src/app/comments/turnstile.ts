import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, shareReplay, timeout } from 'rxjs';

export interface TurnstileRenderOptions {
  sitekey: string;
  action: string;
  callback: (token: string) => void;
  'expired-callback': () => void;
  'error-callback': () => boolean;
  'timeout-callback': () => void;
  'response-field': false;
  'refresh-expired': 'manual';
  retry: 'never';
  language: 'en';
  size: 'flexible';
}

export interface TurnstileApi {
  render(container: HTMLElement, options: TurnstileRenderOptions): string;
  reset(widget: string): void;
  remove(widget: string): void;
}

@Injectable({ providedIn: 'root' })
export class Turnstile {
  private readonly document = inject(DOCUMENT);
  private readonly configuration = inject(HttpClient)
    .get<{ siteKey: string; action: string }>('/api/captcha/config')
    .pipe(timeout(10000), shareReplay({ bufferSize: 1, refCount: false }));
  private script?: Promise<TurnstileApi>;

  async load() {
    const [api, config] = await Promise.all([
      this.loadScript(),
      firstValueFrom(this.configuration),
    ]);
    if (!config.siteKey || !config.action)
      throw new Error('Verification configuration unavailable.');
    return { api, config };
  }

  private loadScript(): Promise<TurnstileApi> {
    if (this.script) return this.script;
    const window = this.document.defaultView as (Window & { turnstile?: TurnstileApi }) | null;
    if (window?.turnstile) return Promise.resolve(window.turnstile);
    this.script = new Promise<TurnstileApi>((resolve, reject) => {
      const script = this.document.createElement('script');
      script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
      script.async = true;
      script.defer = true;
      const timer = setTimeout(() => failed(), 15000);
      const cleanup = () => {
        clearTimeout(timer);
        script.onload = null;
        script.onerror = null;
      };
      const failed = () => {
        cleanup();
        script.remove();
        this.script = undefined;
        reject(new Error('Verification script unavailable.'));
      };
      script.onload = () => {
        if (!window?.turnstile) return failed();
        cleanup();
        resolve(window.turnstile);
      };
      script.onerror = failed;
      this.document.head.appendChild(script);
    });
    return this.script;
  }
}
