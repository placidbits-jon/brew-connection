import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

const result = {
  query: 'blueberry floral fruit',
  mode: 'keyword',
  persona: 'maya-chen',
  results: [
    {
      slug: 'ethiopia-blueberry-bloom',
      type: 'RoastBatch',
      name: 'Ethiopia Blueberry Bloom',
      description: 'Blueberry and jasmine',
      keywordScore: 2.4,
      vectorDistance: null,
      keywordContribution: 1,
      vectorContribution: 0,
      graphContribution: 0,
      totalScore: 1,
      available: true,
      explanations: ['BM25 keyword match'],
    },
  ],
  queries: [
    {
      label: 'Full-text index',
      language: 'sql',
      command: 'SELECT FROM RoastBatch WHERE SEARCH_INDEX(...)',
      parameters: { query: 'blueberry floral fruit' },
    },
  ],
  model: { provider: 'embeddinggemma', dimensions: 768 },
};

describe('Discovery modes', () => {
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
  async function load() {
    const harness = await RouterTestingHarness.create(
      '/demo/discover?query=blueberry%20floral%20fruit&mode=keyword&persona=maya-chen',
    );
    http.expectOne((r) => r.url === '/api/demo/discover').flush(result);
    await harness.fixture.whenStable();
    return harness;
  }
  function button(h: RouterTestingHarness, label: string) {
    return Array.from(h.routeNativeElement!.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === label,
    )!;
  }
  it('reconstructs query and renders keyword evidence, scores and actual query metadata', async () => {
    const h = await load();
    expect(h.routeNativeElement!.textContent).not.toContain('Why one database?');
    expect(h.routeNativeElement!.textContent).toContain('Ethiopia Blueberry Bloom');
    expect(h.routeNativeElement!.textContent).toContain('BM25 keyword match');
    button(h, 'Inspect queries').click();
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain(result.queries[0].command);
  });
  it('retains the same query when switching to semantic mode', async () => {
    const h = await load();
    button(h, 'Semantic').click();
    await h.fixture.whenStable();
    const request = http.expectOne(
      (r) => r.url === '/api/demo/discover' && r.params.get('mode') === 'semantic',
    );
    expect(request.request.params.get('query')).toBe('blueberry floral fruit');
    request.flush({
      ...result,
      mode: 'semantic',
      results: [
        {
          ...result.results[0],
          name: 'Summer Orchard',
          vectorDistance: 0.23,
          keywordContribution: 0,
          vectorContribution: 0.77,
          explanations: ['Cosine similarity'],
        },
      ],
    });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Summer Orchard');
    expect(TestBed.inject(Router).url).toContain('mode=semantic');
  });
  it('keeps controls usable after an invalid query and reloads successfully', async () => {
    const h = await RouterTestingHarness.create('/demo/discover?query=test');
    http
      .expectOne((r) => r.url === '/api/demo/discover')
      .flush({ message: 'Query is invalid' }, { status: 400, statusText: 'Bad Request' });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.querySelector('[role="alert"]')!.textContent).toContain(
      'Query is invalid',
    );
    button(h, 'Search').click();
    await h.fixture.whenStable();
    http.expectOne((r) => r.url === '/api/demo/discover').flush(result);
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Ethiopia Blueberry Bloom');
  });
  it('uses the model rewrite as a visible editable query for deterministic retrieval', async () => {
    const h = await load();
    const input = h.routeNativeElement!.querySelector(
      'input[aria-label="Your coffee preference"]',
    ) as HTMLInputElement;
    input.value = 'I want a bright cup';
    input.dispatchEvent(new Event('input'));
    button(h, 'Find my next coffee').click();
    http.expectOne('/api/demo/discover/ask').flush({ interpretedQuery: 'bright floral' });
    await h.fixture.whenStable();
    const request = http.expectOne(
      (r) => r.url === '/api/demo/discover' && r.params.get('mode') === 'personalized',
    );
    expect(request.request.params.get('query')).toBe('bright floral');
    request.flush({ ...result, query: 'bright floral', mode: 'personalized' });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Why one database?');
    expect(h.routeNativeElement!.textContent).toContain('copied social and inventory signals');
    expect(h.routeNativeElement!.textContent).toContain(
      'Local model search query: “bright floral”',
    );
  });
  it('ignores a late helper response after switching search modes', async () => {
    const h = await load();
    button(h, 'Find my next coffee').click();
    const helper = http.expectOne('/api/demo/discover/ask');
    button(h, 'Semantic').click();
    await h.fixture.whenStable();
    http.expectOne((r) => r.url === '/api/demo/discover').flush({ ...result, mode: 'semantic' });
    helper.flush({ interpretedQuery: 'stale question' });
    await h.fixture.whenStable();
    expect(TestBed.inject(Router).url).toContain('mode=semantic');
    expect(h.routeNativeElement!.textContent).not.toContain('stale question');
  });
});
