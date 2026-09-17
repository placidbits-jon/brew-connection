import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)],
    })
      .compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the routed story screen', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
  });

  it('should expose every demo page in the shared navigation', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        'app-demo-navigation nav[aria-label="Demo pages"] a',
      ),
    );

    expect(links.slice(1).map((link) => link.textContent?.trim())).toEqual([
      'Passport',
      'Meet someone',
      'My network',
      'Games',
      'Recipes',
      'Bean to cup',
      'Discover',
      'Live brew',
      'Event pulse',
      'Code lookup',
      'Nearby coffee',
      'Transactions',
      'Query lab',
    ]);
  });
});
