import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';

const query = (label: string, slug = 'blueberry-v60', persona = 'maya-chen') => ({label, language: 'sql', command: `returned statement: ${label}`, parameters: {slug, persona}});
const revision = {slug: 'blueberry-v60-v1', revision: 1, steps: [{atSeconds: 0, waterGrams: 60, action: 'Bloom'}], equipment: {brewer: 'V60', filter: 'paper', grinder: 'hand grinder'}, grind: {clicks: 20}, temperatureC: 93, coffeeGrams: 20, waterGrams: 300, commentary: 'Sweet'};
const recipe = {recipe: {slug: 'blueberry-v60', name: 'Blueberry V60'}, currentRevision: revision, revisions: [revision], notes: [], queries: ['Recipe vertex and current document link', 'Immutable recipe history', 'Current revision link', 'Notes visible to selected demo persona', 'Insert immutable revision'].map(label => query(label))};

describe('Documents section query inspectors', () => {
  let http: HttpTestingController;
  beforeEach(() => {TestBed.configureTestingModule({providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()]}); http = TestBed.inject(HttpTestingController);});
  afterEach(() => {try {http.verify({ignoreCancelled: true});} finally {TestBed.resetTestingModule();}});
  const button = (h: RouterTestingHarness, label: string) => h.routeNativeElement!.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
  const region = (h: RouterTestingHarness, title: string) => h.routeNativeElement!.querySelector(`[role="region"][aria-label="${title} queries"]`)!.textContent!;
  const field = (h: RouterTestingHarness, label: string) => Array.from(h.routeNativeElement!.querySelectorAll('label')).find(l => l.firstChild?.textContent?.trim() === label)!.querySelector('input,select') as HTMLInputElement;
  const set = (element: HTMLInputElement, value: string) => {element.value = value; element.dispatchEvent(new Event('input')); element.dispatchEvent(new Event('change'));};
  const openMemory = (h: RouterTestingHarness) => h.routeNativeElement!.querySelector<HTMLButtonElement>('button[aria-controls="memory-panel"]')!.click();
  const loadNotes = (h: RouterTestingHarness) => Array.from(h.routeNativeElement!.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Load subject notes')!.click();
  async function load() {const h = await RouterTestingHarness.create('/demo/recipes/blueberry-v60'); http.expectOne('/api/demo/recipes/blueberry-v60?persona=maya-chen').flush(recipe); await h.fixture.whenStable(); return h;}

  it('separates editor, history and notes retrieval evidence from writes', async () => {
    const h = await load();
    expect(h.routeNativeElement!.querySelector('#memory-panel')).toBeNull();
    openMemory(h); await h.fixture.whenStable();
    expect(h.routeNativeElement!.querySelector('button[aria-controls="memory-panel"]')!.getAttribute('aria-expanded')).toBe('true');
    for (const title of ['Recipe', 'Revision history', 'Community memory']) button(h, `Inspect ${title} queries`).click();
    await h.fixture.whenStable();
    expect(region(h, 'Recipe')).toContain('Recipe vertex and current document link');
    expect(region(h, 'Recipe')).toContain('Immutable recipe history');
    expect(region(h, 'Recipe')).not.toContain('Notes visible');
    expect(region(h, 'Revision history')).toContain('Current revision link');
    expect(region(h, 'Revision history')).not.toContain('Recipe vertex');
    expect(region(h, 'Community memory')).toContain('Notes visible to selected demo persona');
    expect(region(h, 'Community memory')).not.toContain('Immutable recipe history');
    expect(h.routeNativeElement!.textContent).not.toContain('Insert immutable revision');
    expect(h.routeNativeElement!.querySelector('footer button, .drawer')).toBeNull();
  });

  it('clears stale evidence on subject edits and retains new GET response parameters', async () => {
    const h = await load(); openMemory(h); await h.fixture.whenStable(); button(h, 'Inspect Community memory queries').click(); await h.fixture.whenStable();
    set(field(h, 'About'), 'Brew'); set(field(h, 'Subject slug'), 'new-cup'); await h.fixture.whenStable();
    expect(region(h, 'Community memory')).not.toContain('blueberry-v60');
    loadNotes(h);
    const pending = http.expectOne('/api/demo/notes?persona=maya-chen&subjectType=Brew&subjectSlug=new-cup');
    set(field(h, 'Subject slug'), 'other-cup');
    pending.flush({notes: [{slug: 'stale-note', body: 'Wrong cup'}], queries: [query('Notes visible to selected demo persona', 'new-cup')]});
    await h.fixture.whenStable();
    expect(h.routeNativeElement!.textContent).not.toContain('Wrong cup');
    expect(region(h, 'Community memory')).not.toContain('new-cup');
    loadNotes(h);
    http.expectOne('/api/demo/notes?persona=maya-chen&subjectType=Brew&subjectSlug=other-cup').flush({notes: [], queries: [query('Notes visible to selected demo persona', 'other-cup'), query('Resolve Person')]});
    await h.fixture.whenStable();
    expect(region(h, 'Community memory')).toContain('other-cup');
    expect(region(h, 'Community memory')).not.toContain('Resolve Person');
    button(h, 'Inspect Recipe queries').click(); await h.fixture.whenStable();
    expect(region(h, 'Recipe')).not.toContain('other-cup');
  });

  it('cancels old persona notes and shows evidence for the selected persona', async () => {
    const h = await load(); openMemory(h); await h.fixture.whenStable(); set(field(h, 'About'), 'Brew'); set(field(h, 'Subject slug'), 'new-cup'); loadNotes(h);
    const stale = http.expectOne('/api/demo/notes?persona=maya-chen&subjectType=Brew&subjectSlug=new-cup');
    await h.navigateByUrl('/demo/recipes/blueberry-v60?persona=priya-nair'); expect(stale.cancelled).toBe(true);
    http.expectOne('/api/demo/recipes/blueberry-v60?persona=priya-nair').flush(recipe);
    http.expectOne('/api/demo/notes?persona=priya-nair&subjectType=Brew&subjectSlug=new-cup').flush({notes: [], queries: [query('Notes visible to selected demo persona', 'new-cup', 'priya-nair')]});
    await h.fixture.whenStable(); button(h, 'Inspect Community memory queries').click(); await h.fixture.whenStable();
    expect(region(h, 'Community memory')).toContain('priya-nair');
    expect(region(h, 'Community memory')).not.toContain('maya-chen');
  });

  it('shows one Cypher read for the provenance graph and the relevant document read', async () => {
    const h = await RouterTestingHarness.create('/demo/coffee/batch');
    const brew = (slug: string) => ({brew: {slug}, brewer: {slug: 'brewer'}, recipe: recipe.recipe, revision, reactions: []});
    http.expectOne('/api/demo/coffee/batch?persona=maya-chen').flush({
      batch: {slug: 'batch'}, lot: {slug: 'lot'}, roaster: {slug: 'roaster'}, vendor: {slug: 'vendor'}, roastProfile: {slug: 'profile'},
      brews: [brew('first-cup'), brew('second-cup')], notes: [],
      queries: [{...query('Single Cypher provenance graph', 'batch'), language: 'cypher', command: 'MATCH (batch:RoastBatch) OPTIONAL MATCH (brew)-[:USED_BATCH]->(batch) RETURN batch, brew'}, query('Nested roast profile document', 'batch'), query('Historical revisions pinned on USED_RECIPE', 'batch'), query('Notes visible to selected demo persona', 'batch')],
    });
    await h.fixture.whenStable(); button(h, 'Inspect Provenance graph queries').click();
    Array.from(h.routeNativeElement!.querySelectorAll<HTMLButtonElement>('.graph-node')).find(node => node.textContent!.includes('Pinned revision'))!.click();
    await h.fixture.whenStable(); button(h, 'Inspect Selected document queries').click(); await h.fixture.whenStable();
    expect(region(h, 'Provenance graph')).toContain('Single Cypher provenance graph');
    expect(region(h, 'Provenance graph')).toContain('cypher');
    expect(region(h, 'Provenance graph')).toContain('OPTIONAL MATCH');
    expect(region(h, 'Provenance graph')).not.toContain('Notes visible');
    expect(region(h, 'Selected document')).toContain('Historical revisions pinned on USED_RECIPE');
    expect(region(h, 'Selected document')).not.toContain('Single Cypher provenance graph');
    set(field(h, 'Trace a brew'), 'second-cup'); await h.fixture.whenStable();
    expect(region(h, 'Provenance graph')).toContain('Single Cypher provenance graph');
    expect(region(h, 'Selected document')).toContain('Single Cypher provenance graph');
    expect(region(h, 'Selected document')).not.toContain('Historical revision');
  });
});
