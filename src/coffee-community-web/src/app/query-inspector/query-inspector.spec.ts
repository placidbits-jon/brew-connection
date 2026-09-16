import { TestBed } from '@angular/core/testing';
import { QueryInspector } from './query-inspector';

describe('Section query inspector', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('can close an open inspector when its section loses query evidence', async () => {
    const fixture = TestBed.createComponent(QueryInspector);
    fixture.componentRef.setInput('title', 'Community memory');
    fixture.componentRef.setInput('queries', [{label:'Notes', language:'sql', command:'SELECT FROM Note', parameters:{slug:'cup'}}]);
    await fixture.whenStable();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click(); await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('SELECT FROM Note');
    fixture.componentRef.setInput('queries', []);
    await fixture.whenStable();
    expect(button.disabled).toBe(false);
    expect(fixture.nativeElement.textContent).not.toContain('SELECT FROM Note');
    button.click(); await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=region]')).toBeNull();
    expect(button.disabled).toBe(true);
  });
});
