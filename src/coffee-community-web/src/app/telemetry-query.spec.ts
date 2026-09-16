import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from './app.routes';

const query = (label: string, command = label) => ({
  label,
  language: 'sql',
  command,
  parameters: { source: label },
});
const brewLabels = [
  'Stable brew',
  'Replay identity',
  'Brewer from graph',
  'Recipe from graph',
  'Pinned recipe revision',
  'Native time-series tag and time range',
  'Native time-series query plan',
];
const pulseLabels = [
  'Native time buckets',
  'Native percentile',
  'Native query-time downsampling',
  'Native water rate',
  'Event tag link',
  'Area tag link',
  'Native time-series query plan',
  'Retention example availability',
  'Native retention result',
];
const counterLabel = 'ArcadeDB Redis commands over HTTP; server RAM only';
const retention = {
  status: 'waiting',
  beforeCount: 2,
  afterCount: 2,
  retentionDays: 1,
  detail: 'Waiting for maintenance',
};
const pulse = {
  event: { slug: 'event', name: 'Event' },
  area: { slug: 'area', name: 'Area' },
  bucketMinutes: 10,
  buckets: [{ timestamp: 1789401600000, count: 30, ratePerMinute: 3 }],
  sampleCount: 120,
  totalCount: 360,
  percentile95: 5,
  ratePerMinute: 3,
  downsampled: [],
  retention: { ...retention, status: 'not-started' },
  indexPlan: 'EVENT NATIVE PLAN',
  queries: pulseLabels.map((label) => query(label)),
};
const counter = (value: number, command: string) => ({
  value,
  delta: command.startsWith('INCR') ? 1 : 0,
  transient: true,
  restartBehavior: 'Resets on restart',
  queries: [query('Stable event record'), { ...query(counterLabel, command), language: 'redis' }],
});

describe('Telemetry and event section query evidence', () => {
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
      vi.useRealTimers();
    }
  });
  async function inspect(h: RouterTestingHarness, title: string) {
    (
      h.routeNativeElement!.querySelector(
        `[aria-label="Inspect ${title} queries"]`,
      ) as HTMLButtonElement
    ).click();
    await h.fixture.whenStable();
    return h.routeNativeElement!.querySelector(`[role="region"][aria-label="${title} queries"]`)!;
  }
  function labels(region: Element) {
    return Array.from(region.querySelectorAll('article h4:first-child')).map((el) =>
      el.firstChild!.textContent!.trim(),
    );
  }
  function click(h: RouterTestingHarness, text: string) {
    Array.from(h.routeNativeElement!.querySelectorAll('button'))
      .find((b) => b.textContent!.trim() === text)!
      .click();
  }
  async function loadPulse() {
    const h = await RouterTestingHarness.create('/demo/pulse');
    http.expectOne((r) => r.url === '/api/demo/pulse').flush(pulse);
    http.expectOne('/api/demo/counter').flush(counter(7, 'GET tasting-count'));
    await h.fixture.whenStable();
    return h;
  }
  it('isolates brew context, charts, selected sample and derived anomaly evidence', async () => {
    const h = await RouterTestingHarness.create('/demo/brews/blueberry-bloom-v60');
    http
      .expectOne((r) => r.url.startsWith('/api/demo/brews/'))
      .flush({
        brew: { slug: 'blueberry-bloom-v60', name: 'Brew' },
        brewer: { slug: 'person', name: 'Person' },
        recipe: { slug: 'recipe', name: 'Recipe' },
        runId: 'seed',
        status: 'complete',
        sampleCount: 1,
        samples: [
          {
            second: 30,
            timestamp: 1789401630000,
            waterGrams: 120,
            flowRate: 12,
            temperatureC: 92,
            targetWaterGrams: 140,
            targetFlowRate: 4,
            deviation: 8,
          },
        ],
        anomaly: { second: 30, actual: 12, target: 4 },
        indexPlan: 'BREW NATIVE PLAN',
        queries: [...brewLabels.map((label) => query(label)), query('Unrelated statement')],
      });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Why one database?');
    expect(h.routeNativeElement!.textContent).toContain('without copying the target');
    const expected: Record<string, string[]> = {
      'Brew context': brewLabels.slice(0, 6),
      'Water weight': [
        'Pinned recipe revision',
        'Native time-series tag and time range',
        'Native time-series query plan',
      ],
      'Pour rate': [
        'Stable brew',
        'Native time-series tag and time range',
        'Native time-series query plan',
      ],
      'Selected sample': [
        'Stable brew',
        'Pinned recipe revision',
        'Native time-series tag and time range',
      ],
      'Pour anomaly': ['Stable brew', 'Native time-series tag and time range'],
    };
    for (const [title, wanted] of Object.entries(expected)) {
      const region = await inspect(h, title);
      expect(labels(region)).toEqual(wanted);
      expect(region.textContent!.includes('BREW NATIVE PLAN')).toBe(
        ['Water weight', 'Pour rate'].includes(title),
      );
    }
  });
  it('isolates counter, event context, aggregate, bucket, rate, downsampling and retention statements', async () => {
    const h = await loadPulse();
    const expected: Record<string, string[]> = {
      'Live counter': [counterLabel],
      'Event context': ['Event tag link', 'Area tag link'],
      'Event metrics': ['Native percentile'],
      'Event buckets': ['Native time buckets', 'Native time-series query plan'],
      'Native water rate': ['Native water rate'],
      Downsampling: ['Native query-time downsampling'],
      Retention: ['Retention example availability', 'Native retention result'],
    };
    for (const [title, wanted] of Object.entries(expected)) {
      const region = await inspect(h, title);
      expect(labels(region)).toEqual(wanted);
      expect(region.textContent!.includes('EVENT NATIVE PLAN')).toBe(title === 'Event buckets');
    }
  });
  it('replaces GET evidence with the increment supplying the latest counter value', async () => {
    const h = await loadPulse();
    const region = await inspect(h, 'Live counter');
    expect(region.textContent).toContain('GET tasting-count');
    click(h, 'Add one tasting');
    http.expectOne('/api/demo/counter').flush(counter(8, 'INCR tasting-count'));
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.querySelector('.counter-value')!.textContent).toBe('8');
    expect(region.textContent).toContain('INCR tasting-count');
    expect(region.textContent).not.toContain('GET tasting-count');
  });
  it('replaces retention action evidence on the next poll along with its status', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const h = await loadPulse();
    const region = await inspect(h, 'Retention');
    click(h, 'Run retention example');
    http.expectOne('/api/demo/pulse/retention').flush({
      retention,
      queries: [
        query('Telemetry lifecycle', 'CREATE TIMESERIES TYPE DemoRetention'),
        query('Native retention result', 'ACTION COUNT'),
      ],
    });
    await h.fixture.whenStable();
    expect(region.textContent).toContain('CREATE TIMESERIES TYPE DemoRetention');
    expect(region.textContent).toContain('ACTION COUNT');
    expect((await inspect(h, 'Event metrics')).textContent).not.toContain('ACTION COUNT');
    await vi.advanceTimersByTimeAsync(5000);
    http
      .expectOne((r) => r.url === '/api/demo/pulse')
      .flush({
        ...pulse,
        retention: { ...retention, status: 'complete', afterCount: 1 },
        queries: pulse.queries.map((q) =>
          q.label === 'Native retention result' ? query(q.label, 'LATEST COUNT') : q,
        ),
      });
    await h.fixture.whenStable();
    expect(region.textContent).toContain('LATEST COUNT');
    expect(region.textContent).not.toContain('ACTION COUNT');
    expect(region.textContent).not.toContain('CREATE TIMESERIES TYPE');
    expect(h.routeNativeElement!.textContent).toContain('2 inserted → 1 remaining');
  });
  it('shows only the executed exact lookup with its returned plan', async () => {
    const h = await RouterTestingHarness.create('/demo/lookup?code=badge-0001');
    http
      .expectOne((r) => r.url === '/api/demo/lookup')
      .flush({
        code: 'badge-0001',
        kind: 'badge',
        persistent: true,
        target: { slug: 'person', name: 'Person', type: 'Person', route: '/demo/passport/person' },
        queries: [
          { ...query('Persistent exact key lookup'), plan: 'KEY INDEX' },
          query('Unrelated statement'),
        ],
      });
    await h.fixture.whenStable();
    const region = await inspect(h, 'Code lookup');
    expect(labels(region)).toEqual(['Persistent exact key lookup']);
    expect(region.textContent).toContain('KEY INDEX');
  });
  it('shares the spatial read between map and vendors while keeping coffee joins out of the map', async () => {
    const h = await RouterTestingHarness.create('/demo/map');
    http
      .expectOne((r) => r.url === '/api/demo/map')
      .flush({
        latitude: 42,
        longitude: -83,
        radius: 100,
        area: { slug: 'area', name: 'Area', boundary: 'POLYGON(...)' },
        results: [],
        queries: [
          query('Selected venue boundary'),
          { ...query('Native indexed containment and distance in meters'), plan: 'SPATIAL INDEX' },
          query('Graph-linked coffees sold at nearby tables'),
          query('Unrelated statement'),
        ],
      });
    await h.fixture.whenStable();
    const map = await inspect(h, 'Nearby map');
    const vendors = await inspect(h, 'Vendor results');
    expect(labels(map)).toEqual([
      'Selected venue boundary',
      'Native indexed containment and distance in meters',
    ]);
    expect(labels(vendors)).toEqual([
      'Native indexed containment and distance in meters',
      'Graph-linked coffees sold at nearby tables',
    ]);
    expect(map.textContent).toContain('SPATIAL INDEX');
    expect(vendors.textContent).toContain('SPATIAL INDEX');
  });
});
