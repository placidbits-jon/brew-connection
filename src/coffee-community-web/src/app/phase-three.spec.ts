import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

const query = {label: 'Passport MET', language: 'sql', command: 'SELECT expand(outE()) FROM Person WHERE slug = :personSlug', parameters: {personSlug: 'maya-chen'}};
const passport = {person: {slug: 'maya-chen', name: 'Maya Chen'}, timeline: [{kind:'MET',title:'Met Priya Nair',occurredAt:'2026-09-14T12:00:00Z',context:'Coffee bar',slug:'priya-nair'}], queries: [query]};

describe('Community graph routes', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()]});
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { try { http.verify(); } finally { TestBed.resetTestingModule(); } });
  function button(harness: RouterTestingHarness, label: string): HTMLButtonElement {
    return Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(button => button.textContent?.trim() === label)!;
  }
  async function loadPassport(mode = 'passport') {
    const harness = await RouterTestingHarness.create(`/demo/${mode}/maya-chen`);
    http.expectOne('/api/demo/passport/maya-chen').flush(passport);
    await harness.fixture.whenStable();
    return harness;
  }

  it('renders timeline and exposes actual API language, query, and parameters', async () => {
    const harness = await loadPassport();
    expect(harness.routeNativeElement!.textContent).toContain('Met Priya Nair');
    button(harness, 'Inspect queries').click();
    await harness.fixture.whenStable();
    const drawer = harness.routeNativeElement!.querySelector('[role=region]')!;
    expect(drawer.textContent).toContain(query.command);
    expect(drawer.textContent).toContain('sql');
    expect(drawer.textContent).toContain('maya-chen');
  });

  it('switches between the three authored passport personas', async () => {
    const harness = await loadPassport();
    const selector = harness.routeNativeElement!.querySelector<HTMLSelectElement>(
      'select[aria-label="Explore passport as"]',
    )!;
    expect(Array.from(selector.options).map(option => option.value)).toEqual([
      'maya-chen',
      'priya-nair',
      'luis-ortega',
    ]);

    selector.value = 'priya-nair';
    selector.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    http.expectOne('/api/demo/passport/priya-nair').flush({
      person: {slug: 'priya-nair', name: 'Priya Nair'},
      timeline: [],
      queries: [],
    });
    await harness.fixture.whenStable();

    expect(harness.routeNativeElement!.textContent).toContain('Priya Nair’s coffee passport');
    expect(harness.routeNativeElement!.querySelector<HTMLSelectElement>(
      'select[aria-label="Explore passport as"]',
    )!.value).toBe('priya-nair');
  });

  it('records the badge fields once, refreshes the timeline and retains mutation queries', async () => {
    const harness = await loadPassport('meet');
    harness.routeNativeElement!.querySelector('form')!.dispatchEvent(new Event('submit', {cancelable:true}));
    const request = http.expectOne('/api/demo/graph/meet');
    expect(request.request.body).toEqual({personSlug:'maya-chen',badgeCode:'badge-0003',context:'Coffee and conversation',location:'Community coffee bar'});
    request.flush({message:'Meeting saved',queries:[{...query,label:'Create meeting',command:'CREATE EDGE MET'}]});
    http.expectOne('/api/demo/passport/maya-chen').flush({...passport, timeline: [{kind:'MET',title:'Met Attendee 0003',occurredAt:'2026-09-14T12:00:00Z',context:'Coffee and conversation',location:'Community coffee bar',slug:'attendee-0003'}]});
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Meeting saved');
    const meetings = harness.routeNativeElement!.querySelector('[aria-label="Recent meetings"]')!;
    expect(meetings.textContent).toContain('Met Attendee 0003');
    expect(meetings.textContent).toContain('Coffee and conversation');
    expect(meetings.textContent).toContain('Community coffee bar');
    button(harness, 'Inspect queries').click();
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('[role=region]')!.textContent).toContain('CREATE EDGE MET');
  });

  it.each([
    ['Log tasting','tastings',{personSlug:'maya-chen',brewSlug:'blueberry-bloom-v60'}],
    ['Love this cup','loves',{personSlug:'maya-chen',brewSlug:'blueberry-bloom-v60'}],
    ['Save reconnect','reconnects',{personSlug:'maya-chen',targetSlug:'priya-nair'}],
    ['Start game','game-sessions',{personSlug:'maya-chen',slug:'demo-coffee-cards',gameSlug:'coffee-cards',opponentSlug:'priya-nair'}],
    ['Record result','game-results',{sessionSlug:'demo-coffee-cards',winnerSlug:'maya-chen',loserSlug:'priya-nair',winnerScore:21,loserScore:17}],
  ])('saves %s and reloads the persisted passport', async (label, endpoint, body) => {
    const harness = await loadPassport();
    button(harness, label as string).click();
    const request = http.expectOne(`/api/demo/graph/${endpoint}`);
    expect(request.request.body).toEqual(body);
    request.flush({message:'Saved',queries:[]});
    http.expectOne('/api/demo/passport/maya-chen').flush(passport);
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Saved');
  });

  it('keeps mutation errors visible and allows correction without claiming success', async () => {
    const harness = await loadPassport();
    button(harness,'Record result').click();
    http.expectOne('/api/demo/graph/game-results').flush({detail:'Start the game before recording a result.'},{status:404,statusText:'Not Found'});
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('[role="alert"]')!.textContent).toContain('Start the game');
    expect(button(harness,'Record result').disabled).toBe(false);
    expect(harness.routeNativeElement!.textContent).toContain('Met Priya Nair');
  });

  it('reconstructs a network deep link with paths, losses and reconnect targets', async () => {
    const harness = await RouterTestingHarness.create('/demo/network/maya-chen?target=luis-ortega');
    http.expectOne('/api/demo/network/maya-chen?target=luis-ortega').flush({person:passport.person,connections:[{slug:'priya-nair',name:'Priya Nair'}],paths:[{slugs:['maya-chen','priya-nair','luis-ortega'],names:['Maya Chen','Priya Nair','Luis Ortega']}],sharedInterests:[{slug:'priya-nair',name:'Priya Nair',interests:['Pour-over']}],rematches:[{slug:'alex-rivera',name:'Alex Rivera',score:21,opponentScore:17,sessionSlug:'seed-game'}],reconnects:[{slug:'priya-nair',name:'Priya Nair'}],queries:[query]});
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('.path')!.textContent).toContain('Luis Ortega');
    expect(harness.routeNativeElement!.textContent).toContain('2 connections away');
    expect(harness.routeNativeElement!.textContent).toContain('Pour-over');
    expect(harness.routeNativeElement!.querySelector('#rematches')!.textContent).not.toContain('Alex Rivera');
    button(harness, 'Show rematch candidates').click();
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('#rematches')!.textContent).toContain('Alex Rivera');
    expect(harness.routeNativeElement!.querySelector('#rematches')!.textContent).toContain('Your score 17 · Their score 21');
    expect(harness.routeNativeElement!.querySelector('#rematches')!.textContent).toContain('seed-game');
    harness.routeNativeElement!.querySelector('.path-form')!.dispatchEvent(new Event('submit', {cancelable:true}));
    http.expectOne('/api/demo/network/maya-chen?target=luis-ortega').flush({person:passport.person,connections:[],paths:[],sharedInterests:[],rematches:[],reconnects:[{slug:'priya-nair',name:'Priya Nair'}],queries:[]});
    await harness.fixture.whenStable();
    button(harness, 'Show reconnect targets').click();
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('#reconnects')!.textContent).toContain('Priya Nair');
  });

  it('toggles network results and inspects only that section', async () => {
    const h = await RouterTestingHarness.create('/demo/network/maya-chen');
    const rematchQuery = {...query, label:'Who beat me', command:'MATCH (winner)-[:BEAT_IN_GAME]->(person)'};
    http.expectOne('/api/demo/network/maya-chen?target=luis-ortega').flush({person:passport.person, connections:[], paths:[], sharedInterests:[], rematches:[{name:'Luis Ortega',score:10,opponentScore:7,sessionSlug:'game'}],reconnects:[], queries:[rematchQuery,{...query,label:'People to reconnect with',command:'MATCH (person)-[:WANTS_TO_RECONNECT]->(other)'}]});
    await h.fixture.whenStable();
    const panel = h.routeNativeElement!.querySelector('#rematches')!;
    expect(panel.textContent).not.toContain('Luis Ortega');
    button(h,'Show rematch candidates').click(); await h.fixture.whenStable();
    expect(panel.textContent).toContain('Luis Ortega');
    button(h,'Hide rematch candidates').click(); await h.fixture.whenStable();
    expect(panel.textContent).not.toContain('Luis Ortega');
    (panel.querySelector('app-query-inspector button') as HTMLButtonElement).click(); await h.fixture.whenStable();
    expect(panel.textContent).toContain(rematchQuery.command);
    expect(panel.textContent).not.toContain('WANTS_TO_RECONNECT');
    expect(panel.querySelector('app-query-inspector button')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('keeps the shortest-path form available to correct an unknown target', async () => {
    const harness = await RouterTestingHarness.create('/demo/network/maya-chen?target=unknown');
    http.expectOne('/api/demo/network/maya-chen?target=unknown').flush({detail:'Person not found'},{status:404,statusText:'Not Found'});
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Person not found');
    const input = harness.routeNativeElement!.querySelector('.path-form input') as HTMLInputElement;
    input.value = 'luis-ortega';
    input.dispatchEvent(new Event('input'));
    harness.routeNativeElement!.querySelector('.path-form')!.dispatchEvent(new Event('submit', {cancelable:true}));
    await harness.fixture.whenStable();
    http.expectOne('/api/demo/network/maya-chen?target=luis-ortega').flush({person:passport.person,connections:[],paths:[{slugs:['maya-chen','priya-nair','luis-ortega'],names:['Maya Chen','Priya Nair','Luis Ortega']}],sharedInterests:[],rematches:[],reconnects:[],queries:[]});
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('[role="alert"]')).toBeNull();
    expect(harness.routeNativeElement!.querySelector('.path')!.textContent).toContain('Luis Ortega');
  });

  it('recovers from a failed deep-link request with reload', async () => {
    const harness = await RouterTestingHarness.create('/demo/passport/maya-chen');
    http.expectOne('/api/demo/passport/maya-chen').flush({detail:'Database unavailable'},{status:503,statusText:'Unavailable'});
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Database unavailable');
    button(harness,'Reload community').click();
    http.expectOne('/api/demo/passport/maya-chen').flush(passport);
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.querySelector('[role="alert"]')).toBeNull();
    expect(harness.routeNativeElement!.textContent).toContain('Met Priya Nair');
  });
});
