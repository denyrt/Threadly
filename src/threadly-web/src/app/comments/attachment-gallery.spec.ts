import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AttachmentGallery } from './attachment-gallery';
import { CommentAttachment } from './comment.models';

describe('Attachment gallery', () => {
  let fixture: ComponentFixture<AttachmentGallery>;
  let dialog: HTMLDialogElement;
  const images: CommentAttachment[] = Array.from({ length: 10 }, (_, index) => ({
    id: `image-${index + 1}`,
    fileName: `photo-${index + 1}.png`,
    contentType: 'image/png',
    size: 1200,
    width: 320,
    height: 200,
  }));
  const element = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) =>
    element().querySelector(`[aria-label="${label}"]`) as HTMLButtonElement;
  const currentImage = () => element().querySelector('.full-image') as HTMLImageElement;

  beforeEach(async () => {
    fixture = TestBed.createComponent(AttachmentGallery);
    fixture.componentRef.setInput('commentId', 'root');
    fixture.componentRef.setInput('images', images);
    await fixture.whenStable();
    dialog = element().querySelector('dialog')!;
    // jsdom does not implement native dialog focus management; cover it in the browser.
    dialog.showModal = vi.fn(() => dialog.setAttribute('open', ''));
    dialog.close = vi.fn(() => {
      dialog.removeAttribute('open');
      dialog.dispatchEvent(new Event('close'));
    });
  });

  async function open(index = 0) {
    (element().querySelectorAll('.thumbnail')[index] as HTMLButtonElement).click();
    await fixture.whenStable();
  }

  it('keeps ten images compact and opens the selected preview', async () => {
    expect(element().querySelectorAll('.thumbnail')).toHaveLength(4);
    expect(element().querySelector('.more-images')?.textContent).toBe('+6');
    expect(element().querySelectorAll('img')).toHaveLength(4);
    await open(3);
    expect(dialog.showModal).toHaveBeenCalledOnce();
    expect(dialog.open).toBe(true);
    expect(currentImage().getAttribute('src')).toBe('/api/comments/root/attachments/image-4');
    expect(element().querySelector('.image-counter')?.textContent?.trim()).toBe('4 / 10');
  });

  it('reaches hidden images and wraps around using buttons and arrow keys', async () => {
    await open(3);
    for (let index = 4; index < 10; index++) {
      button('Next image').click();
      await fixture.whenStable();
      expect(currentImage().alt).toBe(`photo-${index + 1}.png`);
    }
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await fixture.whenStable();
    expect(currentImage().alt).toBe('photo-1.png');
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    await fixture.whenStable();
    expect(currentImage().alt).toBe('photo-10.png');
    button('Previous image').click();
    await fixture.whenStable();
    expect(currentImage().alt).toBe('photo-9.png');
  });

  it('clears the selected image on native close and can reopen a different preview', async () => {
    await open();
    button('Close image viewer').click();
    await fixture.whenStable();
    expect(dialog.open).toBe(false);
    expect(currentImage()).toBeNull();
    await open(2);
    expect(currentImage().alt).toBe('photo-3.png');
  });

  it('does not show extra-image or navigation controls for one image', async () => {
    fixture.componentRef.setInput('images', images.slice(0, 1));
    await fixture.whenStable();
    expect(element().querySelector('.more-images')).toBeNull();
    await open();
    expect(button('Previous image')).toBeNull();
    expect(button('Next image')).toBeNull();
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await fixture.whenStable();
    expect(currentImage().alt).toBe('photo-1.png');
  });

  it('shows loading and image errors, then resets the state when navigating', async () => {
    await open();
    expect(element().querySelector('[role="status"]')?.textContent).toContain('Loading');
    currentImage().dispatchEvent(new Event('error'));
    await fixture.whenStable();
    expect(element().querySelector('[role="status"]')?.textContent).toContain(
      'could not be loaded',
    );
    button('Next image').click();
    await fixture.whenStable();
    expect(element().querySelector('[role="status"]')?.textContent).toContain('Loading');
    currentImage().dispatchEvent(new Event('load'));
    await fixture.whenStable();
    expect(currentImage().classList.contains('loaded')).toBe(true);
    expect(element().querySelector('[role="status"]')).toBeNull();
  });

  it('uses the reply identifier in image URLs', async () => {
    fixture.componentRef.setInput('commentId', 'reply');
    await fixture.whenStable();
    await open();
    expect(currentImage().getAttribute('src')).toBe('/api/comments/reply/attachments/image-1');
  });

  it('closes on a backdrop click but keeps clicks inside the viewer open', async () => {
    await open();
    vi.spyOn(dialog, 'getBoundingClientRect').mockReturnValue(new DOMRect(100, 100, 480, 400));
    dialog.dispatchEvent(new MouseEvent('click', { clientX: 120, clientY: 120 }));
    currentImage().dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(dialog.close).not.toHaveBeenCalled();
    dialog.dispatchEvent(new MouseEvent('click', { clientX: 50, clientY: 50 }));
    await fixture.whenStable();
    expect(dialog.open).toBe(false);
  });
});
