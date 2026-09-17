import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
const lookup = {
  code: 'badge-0001',
  kind: 'badge',
  persistent: true,
  target: {
    slug: 'priya-nair',
    name: 'Priya Nair',
    type: 'Person',
    route: '/demo/passport/priya-nair',
  },
  queries: [],
};
const map = {
  latitude: 42.3314,
  longitude: -83.0458,
  radius: 100,
  area: { slug: 'pour-over-bar', name: 'Pour-over bar', boundary: 'POLYGON(...)' },
  results: [
    {
      slug: 'vendor-0',
      name: 'Great Lakes Coffee Table',
      coords: 'POINT(-83.0458 42.3314)',
      distanceMeters: 0,
      contained: true,
      areaSlug: 'pour-over-bar',
      available: true,
      coffees: [
        {
          slug: 'ethiopia-blueberry-bloom',
          name: 'Ethiopia Blueberry Bloom',
          route: '/demo/coffee/ethiopia-blueberry-bloom',
        },
      ],
    },
  ],
  queries: [],
};
const brew = {
  brew: { slug: 'blueberry-bloom-v60', name: 'Blueberry Bloom V60' },
  brewer: { slug: 'priya-nair', name: 'Priya Nair' },
  recipe: { slug: 'blueberry-v60', name: 'Blueberry V60' },
  runId: 'seed',
  status: 'complete',
  samples: [
    {
      second: 30,
      timestamp: 1789401630000,
      waterGrams: 120,
      flowRate: 12,
      temperatureC: 92.4,
      targetWaterGrams: 140,
      targetFlowRate: 4,
      deviation: -20,
    },
  ],
  anomaly: { second: 30, actual: 12, target: 4 },
  queries: [],
};
const pulse = {
  event: { slug: 'brew-connection-2026', name: 'Brew Connection at TechCon' },
  area: { slug: 'pour-over-bar', name: 'Pour-over bar' },
  bucketMinutes: 10,
  buckets: [{ timestamp: 1789401600000, count: 30, ratePerMinute: 3 }],
  sampleCount: 120,
  totalCount: 360,
  percentile95: 5,
  ratePerMinute: 3,
  downsampled: [],
  retention: { status: 'not-started', detail: 'Run the isolated retention example' },
  queries: [],
};
describe('Specialized demo routes', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
    }
  });
  it('resolves stable codes to linked community records and recovers from missing codes', async () => {
    const h = await RouterTestingHarness.create('/demo/lookup?code=badge-0001');
    http.expectOne((r) => r.url === '/api/demo/lookup').flush(lookup);
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Priya Nair');
    expect(h.routeNativeElement!.querySelector('a[href="/demo/passport/priya-nair"]')).toBeTruthy();
    await h.navigateByUrl('/demo/lookup?code=missing');
    http
      .expectOne((r) => r.url === '/api/demo/lookup')
      .flush({ message: 'Code not found' }, { status: 404, statusText: 'Not Found' });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.querySelector('[role="alert"]')!.textContent).toContain(
      'Code not found',
    );
    expect(h.routeNativeElement!.querySelector('input')).toBeTruthy();
  });
  it('renders actual distances, containment and linked coffees', async () => {
    const h = await RouterTestingHarness.create('/demo/map');
    http.expectOne((r) => r.url === '/api/demo/map').flush(map);
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Great Lakes Coffee Table');
    expect(h.routeNativeElement!.textContent).toContain('Inside selected area');
    expect(h.routeNativeElement!.querySelector('svg')).toBeTruthy();
  });
  it('shows brew targets, actual readings and the authored flow anomaly', async () => {
    const h = await RouterTestingHarness.create('/demo/brews/blueberry-bloom-v60');
    http.expectOne((r) => r.url === '/api/demo/brews/blueberry-bloom-v60').flush(brew);
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Blueberry Bloom V60');
    expect(h.routeNativeElement!.textContent).toContain('30-second pour spike');
    expect(h.routeNativeElement!.querySelectorAll('svg').length).toBe(2);
  });
  it('labels transient counters and renders event aggregates', async () => {
    const h = await RouterTestingHarness.create('/demo/pulse');
    http.expectOne((r) => r.url === '/api/demo/pulse').flush(pulse);
    http.expectOne('/api/demo/counter').flush({
      value: 0,
      delta: 0,
      transient: true,
      restartBehavior: 'Resets when ArcadeDB restarts',
      queries: [],
    });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('360');
    expect(h.routeNativeElement!.textContent).toContain('Resets when ArcadeDB restarts');
    const button = Array.from(h.routeNativeElement!.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Add one tasting',
    )!;
    button.click();
    http.expectOne('/api/demo/counter').flush({
      value: 1,
      delta: 1,
      transient: true,
      restartBehavior: 'Resets when ArcadeDB restarts',
      queries: [],
    });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Last increment: +1');
  });
  it('waits for the initial counter read before allowing increments', async () => {
    const h = await RouterTestingHarness.create('/demo/pulse');
    http.expectOne((r) => r.url === '/api/demo/pulse').flush(pulse);
    await h.fixture.whenStable();
    const button = Array.from(h.routeNativeElement!.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Adding…',
    )!;
    expect(button.disabled).toBe(true);
    http.expectOne('/api/demo/counter').flush({
      value: 7,
      delta: 0,
      transient: true,
      restartBehavior: 'Resets on restart',
      queries: [],
    });
    await h.fixture.whenStable();
    expect(button.disabled).toBe(false);
    expect(h.routeNativeElement!.textContent).toContain('7');
  });
  it('shows regrouping output and resets an out-of-range bucket selection', async () => {
    const tenMinuteBuckets = Array.from({ length: 12 }, (_, i) => ({
      timestamp: 1789401600000 + i * 600000,
      count: 30,
      ratePerMinute: 3,
    }));
    const h = await RouterTestingHarness.create('/demo/pulse?bucketMinutes=10');
    http
      .expectOne((r) => r.url === '/api/demo/pulse' && r.params.get('bucketMinutes') === '10')
      .flush({ ...pulse, buckets: tenMinuteBuckets });
    http.expectOne('/api/demo/counter').flush({
      value: 0,
      delta: 0,
      transient: true,
      restartBehavior: 'Resets on restart',
      queries: [],
    });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('12 × 10-minute buckets');
    h.routeNativeElement!.querySelectorAll<HTMLButtonElement>('.bars button')[11].click();

    const select = h.routeNativeElement!.querySelector<HTMLSelectElement>(
      'select[aria-label="Bucket size"]',
    )!;
    select.value = '60';
    select.dispatchEvent(new Event('input'));
    select.dispatchEvent(new Event('change'));
    h.fixture.detectChanges();
    Array.from(h.routeNativeElement!.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.trim() === 'Regroup samples')!
      .click();
    await h.fixture.whenStable();
    http
      .expectOne((r) => r.url === '/api/demo/pulse' && r.params.get('bucketMinutes') === '60')
      .flush({
        ...pulse,
        bucketMinutes: 60,
        buckets: [
          { timestamp: 1789401600000, count: 180, ratePerMinute: 3 },
          { timestamp: 1789405200000, count: 180, ratePerMinute: 3 },
        ],
      });
    http.expectOne('/api/demo/counter').flush({
      value: 0,
      delta: 0,
      transient: true,
      restartBehavior: 'Resets on restart',
      queries: [],
    });
    await h.fixture.whenStable();

    expect(h.routeNativeElement!.textContent).toContain('2 × 60-minute buckets');
    expect(h.routeNativeElement!.querySelectorAll('.bars button')).toHaveLength(2);
    expect(h.routeNativeElement!.querySelector('.bars button.selected')).toBe(
      h.routeNativeElement!.querySelector('.bars button'),
    );
    expect(h.routeNativeElement!.querySelector('.selected-bucket')!.textContent).toContain(
      '16:00–17:00',
    );
  });
  it('ignores replay results after navigating to a different brew', async () => {
    const h = await RouterTestingHarness.create('/demo/brews/blueberry-bloom-v60');
    http.expectOne((r) => r.url.startsWith('/api/demo/brews/')).flush(brew);
    await h.fixture.whenStable();
    Array.from(h.routeNativeElement!.querySelectorAll('button'))
      .find((b) => b.textContent?.trim() === 'Replay simulation')!
      .click();
    const replay = http.expectOne((r) => r.method === 'POST');
    await h.navigateByUrl('/demo/brews/brew-001');
    http
      .expectOne((r) => r.url === '/api/demo/brews/brew-001')
      .flush({ ...brew, brew: { slug: 'brew-001', name: 'Another brew' }, anomaly: null });
    replay.flush({ runId: 'late-run' });
    await h.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/demo/brews/brew-001');
    expect(h.routeNativeElement!.textContent).not.toContain('30-second pour spike');
  });
  it('does not label early replay measurements as the selected future second', async () => {
    const h = await RouterTestingHarness.create(
      '/demo/brews/blueberry-bloom-v60?runId=progressive',
    );
    http
      .expectOne((r) => r.url.startsWith('/api/demo/brews/'))
      .flush({
        ...brew,
        status: 'queued',
        samples: [{ ...brew.samples[0], second: 0, waterGrams: 0 }],
        anomaly: null,
      });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Waiting for sample at 30 seconds.');
    expect(h.routeNativeElement!.querySelector('.metrics')).toBeNull();
  });
});
