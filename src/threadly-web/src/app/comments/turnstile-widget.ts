import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  NgZone,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Turnstile, TurnstileApi } from './turnstile';

@Component({
  selector: 'app-turnstile-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #container></div>
    <p class="muted" role="status">{{ status() }}</p>
    @if (failed()) {
      <button class="button secondary" type="button" (click)="reset()">Retry verification</button>
    }
  `,
})
export class TurnstileWidget implements AfterViewInit {
  private readonly container = viewChild.required<ElementRef<HTMLElement>>('container');
  private readonly turnstile = inject(Turnstile);
  private readonly destroyRef = inject(DestroyRef);
  private readonly zone = inject(NgZone);
  private api?: TurnstileApi;
  private widget?: string;
  private loading = false;
  private generation = 0;
  private expiry?: ReturnType<typeof setTimeout>;
  readonly tokenChange = output<string | null>();
  readonly status = signal('Loading verification… You can continue writing.');
  readonly failed = signal(false);

  constructor() {
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.expiry);
      if (this.widget !== undefined) this.api?.remove(this.widget);
      this.widget = undefined;
    });
  }

  ngAfterViewInit() {
    void this.render();
  }

  reset() {
    if (this.destroyRef.destroyed || this.loading) return;
    this.invalidate('Please complete verification.', false);
    if (this.widget !== undefined) {
      try {
        this.api!.reset(this.widget);
      } catch {
        // Recover a broken widget without closing the composer or losing its draft/files.
        this.generation++;
        try {
          this.api?.remove(this.widget);
        } catch {
          // The provider may already have removed this instance.
        }
        this.widget = undefined;
        this.container().nativeElement.replaceChildren();
        void this.render();
      }
    } else {
      void this.render();
    }
  }

  private async render() {
    if (this.loading || this.widget !== undefined || this.destroyRef.destroyed) return;
    this.loading = true;
    const generation = ++this.generation;
    const active = () => !this.destroyRef.destroyed && generation === this.generation;
    this.status.set('Loading verification… You can continue writing.');
    this.failed.set(false);
    try {
      const { api, config } = await this.turnstile.load();
      if (this.destroyRef.destroyed) return;
      this.api = api;
      this.status.set('Please complete verification.');
      this.widget = api.render(this.container().nativeElement, {
        sitekey: config.siteKey,
        action: config.action,
        'response-field': false,
        'refresh-expired': 'manual',
        retry: 'never',
        language: 'en',
        size: 'flexible',
        callback: (token) =>
          this.zone.run(() => {
            if (!active()) return;
            clearTimeout(this.expiry);
            if (!token || token.length > 2048)
              return this.invalidate('Verification failed. Please retry.', true);
            this.tokenChange.emit(token);
            this.status.set('Verification complete.');
            this.failed.set(false);
            // Leave a small margin before Siteverify's five-minute token lifetime.
            this.expiry = setTimeout(
              () => this.invalidate('Verification expired. Please verify again.', true),
              290000,
            );
          }),
        'expired-callback': () => {
          if (active()) this.invalidate('Verification expired. Please verify again.', true);
        },
        'timeout-callback': () => {
          if (active()) this.invalidate('Verification timed out. Please retry.', true);
        },
        'error-callback': () => {
          if (active())
            this.invalidate('Verification could not load. Check your connection and retry.', true);
          return true;
        },
      });
    } catch {
      this.invalidate('Verification could not load. Check your connection and retry.', true);
    } finally {
      this.loading = false;
    }
  }

  private invalidate(message: string, failed: boolean) {
    this.zone.run(() => {
      if (this.destroyRef.destroyed) return;
      clearTimeout(this.expiry);
      this.tokenChange.emit(null);
      this.status.set(message);
      this.failed.set(failed);
    });
  }
}
