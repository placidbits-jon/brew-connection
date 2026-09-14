import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

describe('Phase two slide checkpoints', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { try { http.verify(); } finally { TestBed.resetTestingModule(); } });

  it('loads the schema slide as a real schema explorer', async () => {
    const harness = await RouterTestingHarness.create('/demo/lab?view=schema');
    http.expectOne('/api/demo/schema').flush({ types: [{ name: 'Person', type: 'vertex', properties: [{name: 'slug', type: 'STRING'}] }], indexes: [{ name: 'Person[slug]', type: 'LSM_TREE', unique: true }] });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.textContent).toContain('Schema explorer');
    const button = Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(button => button.textContent?.includes('Person'))!;
    button.click();
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.textContent).toContain('Person[slug]');
    expect(harness.routeNativeElement?.textContent).toContain('STRING');
  });

  it('reconstructs Maya from the persona slide link and renders seeded relationships', async () => {
    const harness = await RouterTestingHarness.create('/demo/story?persona=maya');
    http.expectOne('/api/demo/status').flush({});
    http.expectOne('/api/demo/story?persona=maya').flush({ profile: 'story', persona: {slug:'maya-chen', name:'Maya Chen', role:'attendee'}, counts:[{type:'Person', count:40}], connections:[{slug:'priya-nair',name:'Priya Nair',context:'pour-over table'}], tastings:[{slug:'blueberry-bloom-v60',name:'Blueberry Bloom V60'}], rematches:[{slug:'alex-rivera',name:'Alex Rivera'}] });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.querySelector('select')?.value).toBe('maya-chen');
    expect(harness.routeNativeElement?.textContent).toContain('Priya Nair');
    expect(harness.routeNativeElement?.textContent).toContain('Blueberry Bloom V60');
    expect(harness.routeNativeElement?.textContent).toContain('40');
  });
});
