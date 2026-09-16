import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { form, FormField } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  catchError,
  combineLatest,
  exhaustMap,
  of,
  startWith,
  Subject,
  switchMap,
  tap,
  takeWhile,
  timer,
} from 'rxjs';
import {
  InspectedQuery as Query,
  QueryInspector,
  queriesByLabel,
} from '../query-inspector/query-inspector';
interface RecordLink {
  slug: string;
  name: string;
}
interface Sample {
  second: number;
  timestamp: number;
  waterGrams: number;
  flowRate: number;
  temperatureC: number;
  targetWaterGrams: number;
  targetFlowRate: number;
  deviation: number;
  waterDeviation?: number;
}
interface Brew {
  brew: RecordLink;
  brewer: RecordLink;
  recipe: RecordLink;
  recipeRevision?: { revision: number };
  runId: string;
  status: string;
  sampleCount: number;
  samples: Sample[];
  anomaly: { name?: string; second: number; actual: number; target: number } | null;
  targetDescription?: string;
  indexDescription?: string;
  indexPlan?: string;
  queries: Query[];
}
interface Bucket {
  timestamp: number;
  count: number;
  ratePerMinute: number;
  averageCount?: number;
}
interface Retention {
  status: string;
  beforeCount: number;
  afterCount: number;
  retentionDays: number;
  detail: string;
}
interface Pulse {
  event: RecordLink;
  area: RecordLink;
  bucketMinutes: number;
  buckets: Bucket[];
  sampleCount: number;
  totalCount: number;
  percentile95: number;
  ratePerMinute: number;
  waterGramsPerSecond?: number;
  downsampled: Bucket[];
  retention: Retention;
  rateDescription?: string;
  downsamplingDescription?: string;
  indexDescription?: string;
  indexPlan?: string;
  queries: Query[];
}
interface Counter {
  value: number;
  delta: number;
  transient: boolean;
  restartBehavior: string;
  queries: Query[];
}
@Component({
  selector: 'app-telemetry',
  imports: [RouterLink, FormField, DatePipe, DecimalPipe, QueryInspector],
  templateUrl: './telemetry.html',
  styleUrl: './telemetry.scss',
})
export class Telemetry {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private context = 0;
  private replayRunId: string | null = null;
  protected readonly mode = signal('brew');
  protected readonly brew = signal<Brew | null>(null);
  protected readonly pulse = signal<Pulse | null>(null);
  protected readonly counter = signal<Counter | null>(null);
  protected readonly error = signal('');
  protected readonly actionError = signal('');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly counterBusy = signal(false);
  protected readonly second = signal(30);
  protected readonly bucket = signal(0);
  protected readonly model = signal({ bucketMinutes: '10' });
  protected readonly fields = form(this.model);
  protected readonly sample = computed(() =>
    this.brew()?.samples.find((s) => s.second === this.second()),
  );
  protected readonly retentionQueries = signal<Query[]>([]);
  protected readonly counterQueries = computed(() =>
    queriesByLabel(this.counter()?.queries, 'ArcadeDB Redis commands over HTTP; server RAM only'),
  );
  protected readonly brewContextQueries = computed(() =>
    queriesByLabel(
      this.brew()?.queries,
      'Stable brew',
      'Replay identity',
      'Brewer from graph',
      'Recipe from graph',
      'Pinned recipe revision',
      'Native time-series tag and time range',
    ),
  );
  protected readonly waterQueries = computed(() =>
    this.brewQueries(
      'Native time-series tag and time range',
      'Pinned recipe revision',
      'Native time-series query plan',
    ),
  );
  protected readonly flowQueries = computed(() =>
    this.brewQueries(
      'Native time-series tag and time range',
      'Stable brew',
      'Native time-series query plan',
    ),
  );
  protected readonly sampleQueries = computed(() =>
    this.brewQueries(
      'Native time-series tag and time range',
      'Stable brew',
      'Pinned recipe revision',
    ),
  );
  protected readonly anomalyQueries = computed(() =>
    this.brewQueries('Native time-series tag and time range', 'Stable brew'),
  );
  protected readonly eventContextQueries = computed(() =>
    queriesByLabel(this.pulse()?.queries, 'Event tag link', 'Area tag link'),
  );
  protected readonly eventMetricQueries = computed(() =>
    queriesByLabel(this.pulse()?.queries, 'Native percentile'),
  );
  protected readonly bucketQueries = computed(() =>
    queriesByLabel(
      this.pulse()?.queries,
      'Native time buckets',
      'Native time-series query plan',
    ).map((query) =>
      query.label === 'Native time-series query plan'
        ? { ...query, plan: this.pulse()?.indexPlan }
        : query,
    ),
  );
  protected readonly rateQueries = computed(() =>
    queriesByLabel(this.pulse()?.queries, 'Native water rate'),
  );
  protected readonly downsamplingQueries = computed(() =>
    queriesByLabel(this.pulse()?.queries, 'Native query-time downsampling'),
  );
  private brewQueries(...labels: string[]): Query[] {
    return queriesByLabel(this.brew()?.queries, ...labels).map((query) =>
      query.label === 'Native time-series query plan'
        ? { ...query, plan: this.brew()?.indexPlan }
        : query,
    );
  }
  protected readonly maxBucket = computed(() =>
    Math.max(1, ...(this.pulse()?.buckets.map((b) => b.count) ?? [])),
  );
  constructor() {
    combineLatest([
      this.route.data,
      this.route.paramMap,
      this.route.queryParamMap,
      this.reload.pipe(startWith(undefined)),
    ])
      .pipe(
        tap(([d, , q]) => {
          this.context++;
          this.replayRunId = null;
          this.mode.set(d['mode']);
          this.model.set({ bucketMinutes: q.get('bucketMinutes') ?? '10' });
          this.brew.set(null);
          this.pulse.set(null);
          this.retentionQueries.set([]);
          this.counter.set(null);
          this.error.set('');
          this.actionError.set('');
          this.loading.set(true);
          this.busy.set(false);
          this.counterBusy.set(false);
          if (d['mode'] === 'pulse') this.loadCounter();
          else this.counter.set(null);
        }),
        switchMap(([, p, q]) => {
          const context = this.context;
          if (this.mode() === 'brew')
            return timer(1500, 1500).pipe(
              startWith(0),
              exhaustMap(() =>
                this.http.get<Brew>(
                  '/api/demo/brews/' + encodeURIComponent(p.get('brewSlug') ?? ''),
                  { params: { runId: q.get('runId') ?? 'seed' } },
                ),
              ),
              takeWhile((d) => d.status !== 'complete', true),
              tap((d) => {
                this.brew.set(d);
                this.loading.set(false);
              }),
              catchError((e) => this.fail(e)),
            );
          return timer(5000, 5000).pipe(
            startWith(0),
            exhaustMap(() =>
              this.http.get<Pulse>('/api/demo/pulse', {
                params: { bucketMinutes: q.get('bucketMinutes') ?? '10' },
              }),
            ),
            tap((d) => {
              if (context === this.context) {
                this.pulse.set(d);
                this.retentionQueries.set(
                  queriesByLabel(
                    d.queries,
                    'Retention example availability',
                    'Native retention result',
                  ),
                );
                this.loading.set(false);
              }
            }),
            catchError((e) => this.fail(e)),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe();
  }
  protected line(
    field: 'waterGrams' | 'targetWaterGrams' | 'flowRate' | 'targetFlowRate',
    max: number,
  ): string {
    return (
      this.brew()
        ?.samples.map((s) => `${48 + (s.second * 700) / 59},${220 - (s[field] * 180) / max}`)
        .join(' ') ?? ''
    );
  }
  protected chooseSecond(event: Event) {
    this.second.set(Number((event.target as HTMLInputElement).value));
  }
  protected replay() {
    if (this.busy()) return;
    const data = this.brew();
    if (!data) return;
    const context = this.context;
    this.busy.set(true);
    this.actionError.set('');
    const runId = (this.replayRunId ??= 'rehearsal-' + crypto.randomUUID());
    this.http
      .post<{ runId: string }>(
        '/api/demo/brews/' + encodeURIComponent(data.brew.slug) + '/replay',
        { runId },
      )
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (r) => {
          if (context !== this.context) return;
          this.busy.set(false);
          void this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { runId: r.runId },
          });
        },
        error: (e) => this.actionFailed(context, e),
      });
  }
  protected changeBuckets(event: Event) {
    event.preventDefault();
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { bucketMinutes: this.model().bucketMinutes },
    });
  }
  protected addTasting() {
    if (this.counterBusy()) return;
    const context = this.context;
    this.counterBusy.set(true);
    this.actionError.set('');
    this.http
      .post<Counter>('/api/demo/counter', {})
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (d) => {
          if (context !== this.context) return;
          this.counter.set(d);
          this.counterBusy.set(false);
        },
        error: (e) => {
          if (context === this.context) {
            this.counterBusy.set(false);
            this.actionFailed(context, e);
          }
        },
      });
  }
  protected retention() {
    if (this.busy()) return;
    const context = this.context;
    this.busy.set(true);
    this.actionError.set('');
    this.http
      .post<{ retention: Retention; queries: Query[] }>('/api/demo/pulse/retention', {})
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (d) => {
          if (context !== this.context) return;
          this.busy.set(false);
          this.retentionQueries.set(d.queries);
          this.pulse.update((p) => (p ? { ...p, retention: d.retention } : p));
        },
        error: (e) => this.actionFailed(context, e),
      });
  }
  protected refresh() {
    this.reload.next();
  }
  private loadCounter() {
    this.counterBusy.set(true);
    const context = this.context;
    this.http
      .get<Counter>('/api/demo/counter')
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (d) => {
          if (context === this.context) {
            this.counter.set(d);
            this.counterBusy.set(false);
          }
        },
        error: (e) => this.actionFailed(context, e),
      });
  }
  private actionFailed(context: number, e: HttpErrorResponse) {
    if (context !== this.context) return;
    this.busy.set(false);
    this.counterBusy.set(false);
    this.actionError.set(
      e.error?.message ?? 'The action could not be confirmed. Refresh before retrying.',
    );
  }
  private fail(e: HttpErrorResponse) {
    this.loading.set(false);
    this.error.set(
      e.error?.message ?? 'Telemetry could not be loaded. Check the services and try again.',
    );
    return of(null);
  }
}
