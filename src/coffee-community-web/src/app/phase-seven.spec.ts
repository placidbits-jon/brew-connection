import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
const snapshot = {
  counts: { Brew: 100, Note: 2, BREWED: 100, USED_BATCH: 100, USED_RECIPE: 100, TASTED: 40 },
  brew: [],
  note: [],
};
const transaction = {
  outcome: 'ready',
  operationId: 'phase7-tasting-commit',
  brewSlug: 'transaction-blueberry-v60',
  before: snapshot,
  after: snapshot,
  staged: null,
  steps: [],
  queries: [],
  sql: [],
  cypher: [],
  sameRecord: false,
};
const catalog = {
  examples: [
    {
      id: 'sql-brew',
      label: 'SQL brew',
      language: 'sql',
      command: 'SELECT @rid AS rid, name FROM Brew WHERE slug=:slug',
      parameters: { slug: 'blueberry-bloom-v60' },
      description: 'Read a seeded brew.',
      planStatus: 'available',
    },
    {
      id: 'cypher-brew',
      label: 'Cypher brew',
      language: 'cypher',
      command: 'MATCH (b:Brew) RETURN b.name',
      parameters: {},
      description: 'Read through the graph.',
      planStatus: 'unavailable',
    },
  ],
  limitations: [],
};
const result = {
  ...catalog.examples[0],
  records: [{ rid: '#1:2', name: 'Blueberry Bloom V60' }],
  recordCount: 1,
  executionMs: 2.3,
  plan: { status: 'available', command: 'EXPLAIN SELECT ...', text: 'FETCH FROM INDEX Brew[slug]' },
  emptyMessage: null,
};
describe('Transactions and curated query lab', () => {
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
  function button(h: RouterTestingHarness, text: string) {
    return Array.from(h.routeNativeElement!.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === text,
    )!;
  }
  it('shows real rollback counts after the controlled halfway failure', async () => {
    const h = await RouterTestingHarness.create('/demo/transactions?scenario=rollback');
    http.expectOne('/api/demo/transactions').flush(transaction);
    await h.fixture.whenStable();
    button(h, 'Trigger halfway failure').click();
    const req = http.expectOne('/api/demo/transactions/run');
    expect(req.request.body).toEqual({ scenario: 'rollback' });
    req.flush({
      ...transaction,
      outcome: 'rolled-back',
      staged: { ...snapshot, counts: { ...snapshot.counts, Brew: 101 } },
      steps: ['Inserted brew', 'Controlled failure', 'Rolled back'],
    });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Rolled back');
    expect(h.routeNativeElement!.textContent).toContain('101');
    expect(h.routeNativeElement!.textContent).toContain('All graph and document counts unchanged');
  });
  it('renders fixed query parameters, records, timing and actual plans', async () => {
    const h = await RouterTestingHarness.create('/demo/lab?view=queries&example=sql-brew');
    http.expectOne('/api/demo/lab').flush(catalog);
    await h.fixture.whenStable();
    button(h, 'Run query').click();
    const req = http.expectOne('/api/demo/lab/run');
    expect(req.request.body).toEqual({ id: 'sql-brew' });
    req.flush(result);
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Blueberry Bloom V60');
    expect(h.routeNativeElement!.textContent).toContain('FETCH FROM INDEX');
    expect(h.routeNativeElement!.querySelector('textarea')).toBeNull();
  });
  it('does not display an old query response after choosing another example', async () => {
    const h = await RouterTestingHarness.create('/demo/lab?example=sql-brew');
    http.expectOne('/api/demo/lab').flush(catalog);
    await h.fixture.whenStable();
    button(h, 'Run query').click();
    const old = http.expectOne('/api/demo/lab/run');
    await h.navigateByUrl('/demo/lab?example=cypher-brew');
    http.expectOne('/api/demo/lab').flush(catalog);
    old.flush(result);
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).not.toContain('Blueberry Bloom V60');
    expect(h.routeNativeElement!.textContent).toContain('Cypher brew');
  });
  it('does not apply an old transaction receipt after changing scenarios', async () => {
    const h = await RouterTestingHarness.create('/demo/transactions?scenario=commit');
    http.expectOne('/api/demo/transactions').flush(transaction);
    await h.fixture.whenStable();
    button(h, 'Run successful tasting').click();
    const prior = http.expectOne('/api/demo/transactions/run');
    await h.navigateByUrl('/demo/transactions?scenario=rollback');
    http.expectOne('/api/demo/transactions').flush(transaction);
    prior.flush({ ...transaction, outcome: 'committed', steps: ['Old commit completed'] });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).not.toContain('Old commit completed');
    expect(button(h, 'Trigger halfway failure')).toBeTruthy();
  });
  it('keeps the catalog usable after a query fails', async () => {
    const h = await RouterTestingHarness.create('/demo/lab?example=sql-brew');
    http.expectOne('/api/demo/lab').flush(catalog);
    await h.fixture.whenStable();
    button(h, 'Run query').click();
    http
      .expectOne('/api/demo/lab/run')
      .flush({ message: 'Database read unavailable' }, { status: 503, statusText: 'Unavailable' });
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.querySelector('[role="alert"]')!.textContent).toContain(
      'Database read unavailable',
    );
    expect(button(h, 'Run query').disabled).toBe(false);
    button(h, 'Run query').click();
    http.expectOne('/api/demo/lab/run').flush(result);
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).toContain('Blueberry Bloom V60');
  });
});
