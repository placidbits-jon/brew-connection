import { QueryInspector, InspectedQuery } from '../query-inspector/query-inspector';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DecimalPipe, JsonPipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, of, startWith, Subject, switchMap, tap } from 'rxjs';
interface SchemaType {
  name: string;
  type: string;
  properties?: { name: string; type: string }[];
}
interface SchemaIndex {
  name: string;
  type: string;
  unique?: boolean;
  typeName?: string;
  properties?: string[];
}
interface Schema {
  queries?: InspectedQuery[];
  types: SchemaType[];
  indexes: SchemaIndex[];
}
interface Example {
  id: string;
  label: string;
  language: string;
  command: string;
  parameters: unknown;
  planStatus: string;
  description: string;
}
interface Catalog {
  examples: Example[];
  limitations: string[];
}
interface Execution {
  id: string;
  label: string;
  language: string;
  command: string;
  parameters: unknown;
  records: Record<string, unknown>[];
  recordCount: number;
  executionMs: number;
  plan: { status: string; command?: string; text?: string };
  emptyMessage?: string;
  checks?: Execution[];
  limitations?: string[];
}
@Component({
  selector: 'app-lab',
  imports: [RouterLink, JsonPipe, DecimalPipe, QueryInspector],
  templateUrl: './lab.html',
  styleUrl: './lab.scss',
})
export class Lab {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private context = 0;
  protected readonly view = signal('queries');
  protected readonly selectedId = signal('sql-brew');
  protected readonly schema = signal<Schema | null>(null);
  protected readonly selected = signal<string | null>(null);
  protected readonly schemaError = signal(false);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly catalog = signal<Catalog | null>(null);
  protected readonly execution = signal<Execution | null>(null);
  protected readonly detail = computed(() =>
    this.schema()?.types.find((t) => t.name === this.selected()),
  );
  protected readonly indexes = computed(
    () =>
      this.schema()?.indexes.filter(
        (i) => i.typeName === this.selected() || i.name.startsWith(`${this.selected()}[`),
      ) ?? [],
  );
  protected readonly example = computed(() =>
    this.catalog()?.examples.find((e) => e.id === this.selectedId()),
  );
  protected readonly columns = computed(() =>
    Array.from(new Set((this.execution()?.records ?? []).flatMap((row) => Object.keys(row)))),
  );
  constructor() {
    combineLatest([this.route.queryParamMap, this.reload.pipe(startWith(undefined))])
      .pipe(
        tap(([p]) => {
          this.context++;
          this.view.set(p.get('view') ?? 'queries');
          this.selectedId.set(
            p.get('example') ??
              (this.view() === 'compatibility' ? 'compatibility-summary' : 'sql-brew'),
          );
          this.schema.set(null);
          this.catalog.set(null);
          this.execution.set(null);
          this.selected.set(null);
          this.error.set('');
          this.schemaError.set(false);
          this.busy.set(false);
        }),
        switchMap(() =>
          this.view() === 'schema'
            ? this.http.get<Schema>('/api/demo/schema').pipe(
                tap((s) => this.schema.set(s)),
                catchError(() => {
                  this.schemaError.set(true);
                  return of(null);
                }),
              )
            : this.http.get<Catalog>('/api/demo/lab').pipe(
                tap((c) => this.catalog.set(c)),
                catchError((e) => {
                  this.error.set(this.message(e));
                  return of(null);
                }),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe();
  }
  protected load() {
    this.reload.next();
  }
  protected choose(id: string) {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        view: id === 'compatibility-summary' ? 'compatibility' : 'queries',
        example: id,
      },
    });
  }
  protected run() {
    if (this.busy() || !this.example()) return;
    const context = this.context;
    this.busy.set(true);
    this.error.set('');
    this.execution.set(null);
    this.http
      .post<Execution>('/api/demo/lab/run', { id: this.selectedId() })
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (r) => {
          if (context !== this.context) return;
          this.execution.set(r);
          this.busy.set(false);
        },
        error: (e) => {
          if (context !== this.context) return;
          this.error.set(this.message(e));
          this.busy.set(false);
        },
      });
  }
  protected cell(row: Record<string, unknown>, key: string): string {
    const value = row[key];
    return value === null || value === undefined
      ? '—'
      : typeof value === 'object'
        ? JSON.stringify(value)
        : String(value);
  }
  private message(e: HttpErrorResponse) {
    return e.error?.message ?? 'The query could not be run. Check the services and try again.';
  }
}
