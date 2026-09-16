import { JsonPipe } from '@angular/common';
import { Component, input, signal } from '@angular/core';

export interface InspectedQuery {
  label: string;
  language: string;
  command: string;
  parameters: unknown;
  plan?: string | null;
}

/** Select actual API-returned statements; never reconstruct SQL in the UI. */
export function queriesByLabel(queries: readonly InspectedQuery[] | undefined, ...labels: string[]): InspectedQuery[] {
  return (queries ?? []).filter(query => labels.includes(query.label));
}

@Component({
  selector: 'app-query-inspector',
  imports: [JsonPipe],
  template: `
    <button type="button" [disabled]="!queries().length && !open()" [attr.aria-expanded]="open()"
      [attr.aria-label]="(open() ? 'Hide ' : 'Inspect ') + title() + ' queries'"
      (click)="open.set(!open())">{{ open() ? 'Hide queries' : 'Inspect queries' }}</button>
    @if (open()) {
      <section role="region" [attr.aria-label]="title() + ' queries'">
        <h3>{{ title() }} · executed queries</h3>
        @if (description()) { <p>{{ description() }}</p> }
        @for (query of queries(); track $index) {
          <article><h4>{{ query.label }} <small>{{ query.language }}</small></h4>
            <pre>{{ query.command }}</pre><h4>Parameters</h4><pre>{{ query.parameters | json }}</pre>
            @if (query.plan) { <h4>Execution plan</h4><pre>{{ query.plan }}</pre> }
          </article>
        } @empty { <p>No statements returned for this section.</p> }
      </section>
    }
  `,
  styles: `
    :host { display: block; margin: .75rem 0; min-width: 0; }
    button { border: 1px solid #796956; border-radius: .4rem; padding: .45rem .7rem; background: #25221e; color: #f4dcc0; cursor: pointer; font: inherit; font-size: .85rem; }
    button:disabled { opacity: .5; cursor: default; }
    button:focus-visible { outline: 2px solid #edbe88; outline-offset: 3px; }
    section { margin-top: .6rem; padding: 1rem; border: 1px solid #796956; border-radius: .5rem; background: #181716; color: #f5eee5; max-height: 32rem; overflow: auto; }
    h3 { margin: 0 0 1rem; font-size: 1rem; } h4 { font-size: .85rem; margin: .7rem 0; }
    small { color: #d5b693; } article + article { border-top: 1px solid #554b3f; padding-top: .5rem; }
    pre { white-space: pre-wrap; overflow-wrap: anywhere; font-size: .8rem; line-height: 1.5; }
    p { font-size: .85rem; }
  `,
})
export class QueryInspector {
  readonly title = input.required<string>();
  readonly queries = input<readonly InspectedQuery[]>([]);
  readonly description = input('');
  protected readonly open = signal(false);
}
