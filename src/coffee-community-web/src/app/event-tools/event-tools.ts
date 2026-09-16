import { DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { form, FormField } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, of, startWith, Subject, switchMap, tap } from 'rxjs';
import {
  InspectedQuery as Query,
  QueryInspector,
  queriesByLabel,
} from '../query-inspector/query-inspector';
interface Lookup {
  code: string;
  kind: string;
  persistent: boolean;
  target: { slug: string; name: string; type: string; route: string };
  queries: Query[];
}
interface Place {
  slug: string;
  name: string;
  coords: string;
  distanceMeters: number;
  contained: boolean;
  areaSlug: string;
  available: boolean;
  coffees: { slug: string; name: string; route: string }[];
}
interface MapResult {
  latitude: number;
  longitude: number;
  radius: number;
  distanceModel?: string;
  area: { slug: string; name: string; boundary: string };
  results: Place[];
  queries: Query[];
}
@Component({
  selector: 'app-event-tools',
  imports: [RouterLink, FormField, DecimalPipe, QueryInspector],
  templateUrl: './event-tools.html',
  styleUrl: './event-tools.scss',
})
export class EventTools {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly reload = new Subject<void>();
  protected readonly mode = signal('lookup');
  protected readonly lookup = signal<Lookup | null>(null);
  protected readonly map = signal<MapResult | null>(null);
  protected readonly error = signal('');
  protected readonly loading = signal(true);
  protected readonly selected = signal('');
  protected readonly model = signal({
    code: 'badge-0001',
    latitude: '42.3314',
    longitude: '-83.0458',
    radius: '100',
    area: 'pour-over-bar',
  });
  protected readonly fields = form(this.model);
  protected readonly lookupQueries = computed(() =>
    queriesByLabel(this.lookup()?.queries, 'Persistent exact key lookup'),
  );
  protected readonly mapQueries = computed(() =>
    queriesByLabel(
      this.map()?.queries,
      'Selected venue boundary',
      'Native indexed containment and distance in meters',
    ),
  );
  protected readonly vendorQueries = computed(() =>
    queriesByLabel(
      this.map()?.queries,
      'Native indexed containment and distance in meters',
      'Graph-linked coffees sold at nearby tables',
    ),
  );
  protected readonly points = computed(() => {
    const data = this.map();
    if (!data) return [];
    return data.results.map((p, i) => {
      const numbers = p.coords.match(/-?\d+(?:\.\d+)?/g)?.map(Number) ?? [
        data.longitude,
        data.latitude,
      ];
      const dx = (numbers[0] - data.longitude) * 111320 * Math.cos((data.latitude * Math.PI) / 180);
      const dy = (numbers[1] - data.latitude) * 111320;
      const scale = 210 / Math.max(data.radius, 1);
      return { ...p, x: 300 + dx * scale, y: 250 - dy * scale, index: i + 1 };
    });
  });
  constructor() {
    combineLatest([
      this.route.data,
      this.route.queryParamMap,
      this.reload.pipe(startWith(undefined)),
    ])
      .pipe(
        tap(([data, p]) => {
          this.mode.set(data['mode']);
          this.model.set({
            code: p.get('code') ?? 'badge-0001',
            latitude: p.get('latitude') ?? '42.3314',
            longitude: p.get('longitude') ?? '-83.0458',
            radius: p.get('radius') ?? '100',
            area: p.get('area') ?? 'pour-over-bar',
          });
          this.lookup.set(null);
          this.map.set(null);
          this.error.set('');
          this.selected.set('');
          this.loading.set(true);
        }),
        switchMap(() =>
          this.mode() === 'lookup'
            ? this.http
                .get<Lookup>('/api/demo/lookup', { params: { code: this.model().code } })
                .pipe(
                  tap((d) => this.lookup.set(d)),
                  catchError((e) => this.fail(e)),
                )
            : this.http
                .get<MapResult>('/api/demo/map', {
                  params: {
                    latitude: this.model().latitude,
                    longitude: this.model().longitude,
                    radius: this.model().radius,
                    area: this.model().area,
                  },
                })
                .pipe(
                  tap((d) => this.map.set(d)),
                  catchError((e) => this.fail(e)),
                ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.loading.set(false));
  }
  protected search(event: Event) {
    event.preventDefault();
    const queryParams =
      this.mode() === 'lookup'
        ? { code: this.model().code }
        : {
            latitude: this.model().latitude,
            longitude: this.model().longitude,
            radius: this.model().radius,
            area: this.model().area,
          };
    const tree = this.router.createUrlTree([], { relativeTo: this.route, queryParams });
    if (this.router.serializeUrl(tree) === this.router.url) this.reload.next();
    else void this.router.navigateByUrl(tree);
  }
  private fail(error: HttpErrorResponse) {
    this.error.set(
      error.error?.message ?? 'This view could not be loaded. Check the services and try again.',
    );
    return of(null);
  }
}
