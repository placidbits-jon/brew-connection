import { QueryInspector, queriesByLabel } from '../query-inspector/query-inspector';
import { JsonPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, combineLatest, of, startWith, Subject, switchMap, tap } from 'rxjs';
interface Snapshot {
  counts: Record<string, number>;
  brew: unknown[];
  note: unknown[];
}
interface Receipt {
  outcome: string;
  operationId: string;
  brewSlug: string;
  before: Snapshot;
  after: Snapshot;
  staged: Snapshot | null;
  steps: string[];
  queries: { label: string; language: string; command: string; parameters: unknown }[];
  sql: unknown[];
  cypher: unknown[];
  sameRecord: boolean;
}
@Component({
  selector: 'app-transactions',
  imports: [RouterLink, JsonPipe, QueryInspector],
  templateUrl: './transactions.html',
  styleUrl: './transactions.scss',
})
export class Transactions {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private context = 0;
  protected readonly scenario = signal('commit');
  protected readonly data = signal<Receipt | null>(null);
  protected readonly error = signal('');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly ran = signal(false);
  protected sectionQueries(...labels: string[]) { return queriesByLabel(this.data()?.queries, ...labels); }
  protected readonly countQueries = computed(() => (this.data()?.queries ?? []).filter(q => q.label.startsWith('Count durable ') || q.label === 'Optional replay document type'));
  protected readonly writeQueries = computed(() => (this.data()?.queries ?? []).filter(q => q.label.startsWith('Create ') || q.label.startsWith('Resolve ')));
  protected readonly snapshotQueries = computed(() => (this.data()?.queries ?? []).filter(q => q.label.startsWith('Verify ') || q.label === 'Linked tasting note' || q.label === 'SQL brew identity'));
  protected readonly sqlQuery = computed(() => this.sectionQueries('SQL brew identity').slice(-1));
  protected readonly rows = computed(() => {
    const d = this.data();
    if (!d) return [];
    return Object.keys(d.before.counts)
      .filter(
        (k) =>
          ['Brew', 'Note', 'BREWED', 'USED_BATCH', 'USED_RECIPE', 'TASTED'].includes(k) ||
          d.before.counts[k] !== d.after.counts[k],
      )
      .map((type) => ({
        type,
        before: d.before.counts[type],
        staged: d.staged?.counts[type],
        after: d.after.counts[type],
        delta: d.after.counts[type] - d.before.counts[type],
      }));
  });
  protected readonly unchanged = computed(() => {
    const d = this.data();
    return !!d && Object.entries(d.before.counts).every(([k, v]) => d.after.counts[k] === v);
  });
  constructor() {
    combineLatest([this.route.queryParamMap, this.reload.pipe(startWith(undefined))])
      .pipe(
        tap(([p]) => {
          this.context++;
          this.scenario.set(p.get('scenario') ?? 'commit');
          this.data.set(null);
          this.error.set('');
          this.loading.set(true);
          this.busy.set(false);
          this.ran.set(false);
        }),
        switchMap(() =>
          this.http.get<Receipt>('/api/demo/transactions').pipe(
            catchError((e) => {
              this.error.set(this.message(e));
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((d) => {
        this.data.set(d);
        this.loading.set(false);
      });
  }
  protected run() {
    if (this.busy()) return;
    const context = this.context;
    this.busy.set(true);
    this.error.set('');
    this.http
      .post<Receipt>('/api/demo/transactions/run', { scenario: this.scenario() })
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (d) => {
          if (context !== this.context) return;
          this.data.set(d);
          this.ran.set(true);
          this.busy.set(false);
        },
        error: (e) => {
          if (context !== this.context) return;
          this.busy.set(false);
          this.error.set(this.message(e));
        },
      });
  }
  protected refresh() {
    this.reload.next();
  }
  private message(e: HttpErrorResponse) {
    return (
      e.error?.message ??
      'The transaction outcome could not be confirmed. Refresh stored state before retrying.'
    );
  }
}
