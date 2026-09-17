import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';

describe('Game lounge', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('shows the persona journey, table matches, catalog, and executed queries', async () => {
    const harness = await RouterTestingHarness.create('/demo/games/maya-chen');
    const request = http.expectOne('/api/demo/games/maya-chen');
    request.flush({
      person: { slug: 'maya-chen', name: 'Maya Chen', role: 'Curious taster' },
      stats: { games: 4, sessions: 4, wins: 2 },
      history: [{ sessionSlug: 'maya-priya-wingspan', gameSlug: 'wingspan', gameName: 'Wingspan', category: 'Strategy', playedAt: '2026-09-14T16:25:00Z', opponent: { slug: 'priya-nair', name: 'Priya Nair' }, drink: { slug: 'blueberry-bloom-v60', name: "Priya's Blueberry Bloom V60", roastBatchSlug: 'ethiopia-blueberry-bloom', coffeeName: 'Ethiopia Blueberry Bloom', note: 'Blueberry and jasmine' }, opponentDrink: { slug: 'brew-003', name: "Priya's Honey Stonefruit V60", roastBatchSlug: 'roast-batch-0003', coffeeName: "Priya's Honey Stonefruit", note: 'Honeyed stone fruit' }, outcome: 'Win', score: 91, opponentScore: 84 }],
      tableMatches: [{ slug: 'priya-nair', name: 'Priya Nair', games: ['Bid Whist', 'Wingspan'] }],
      catalog: [
        { slug: 'wingspan', name: 'Wingspan', category: 'Strategy', mechanics: ['engine building'], playerRange: '1–5', playMinutes: 70, sessions: 1, players: 2, playedByPersona: true },
        { slug: 'monopoly', name: 'Monopoly', category: 'Classic board game', mechanics: ['trading'], playerRange: '2–8', playMinutes: 120, sessions: 1, players: 2, playedByPersona: false },
      ],
      queries: [{ label: 'Persona game graph', language: 'sql', command: "MATCH .outE('PLAYED_IN')", parameters: { slug: 'maya-chen' } }, { label: 'Opponent coffee provenance', language: 'sql', command: "SELECT out('USED_BATCH') FROM Brew", parameters: { slugs: ['blueberry-bloom-v60', 'brew-003'] } }],
    });
    await harness.fixture.whenStable();

    const page = harness.routeNativeElement!;
    expect(page.textContent).toContain('Maya Chen’s game record');
    expect(page.textContent).toContain('Wingspan');
    expect(page.textContent).toContain('vs. Priya Nair');
    expect(page.textContent).toContain('Priya Nair’s cup');
    expect(page.textContent).toContain("Priya's Honey Stonefruit V60");
    expect(page.textContent).toContain('Honeyed stone fruit');
    expect(page.querySelector<HTMLAnchorElement>('.opponent-cup a')?.getAttribute('href')).toContain('/demo/coffee/roast-batch-0003');
    expect(page.textContent).toContain('Monopoly');
    expect(page.querySelectorAll('.game-grid article')).toHaveLength(2);
    const inspect = Array.from(page.querySelectorAll('button')).find(button => button.textContent?.includes('Inspect queries'))!;
    inspect.click();
    await harness.fixture.whenStable();
    expect(page.querySelector('[role="region"]')?.textContent).toContain('PLAYED_IN');
  });

  it('loads another persona from the selector', async () => {
    const harness = await RouterTestingHarness.create('/demo/games/maya-chen');
    http.expectOne('/api/demo/games/maya-chen').flush({ person: { slug: 'maya-chen', name: 'Maya Chen', role: '' }, stats: { games: 0, sessions: 0, wins: 0 }, history: [], tableMatches: [], catalog: [], queries: [] });
    await harness.fixture.whenStable();
    await harness.navigateByUrl('/demo/games/priya-nair');
    http.expectOne('/api/demo/games/priya-nair').flush({ person: { slug: 'priya-nair', name: 'Priya Nair', role: '' }, stats: { games: 0, sessions: 0, wins: 0 }, history: [], tableMatches: [], catalog: [], queries: [] });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Priya Nair’s game record');
  });
});
