import { QueryInspector, queriesByLabel } from '../query-inspector/query-inspector';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { form, FormField, required } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, of, Subject, switchMap, tap, startWith } from 'rxjs';

interface Person { slug: string; name: string }
interface Query { label: string; language: string; command: string; parameters: Record<string, unknown> }
interface Passport { person: Person; timeline: {kind: string; title: string; occurredAt: string; context: string; location?: string; slug: string}[]; queries: Query[] }
interface Network { person: Person; connections: Person[]; paths: {slugs: string[]; names: string[]}[]; sharedInterests: (Person & {interests: string[]})[]; rematches: (Person & {score: number; opponentScore: number; sessionSlug: string})[]; reconnects: (Person & {context?: string})[]; queries: Query[] }

@Component({
  selector: 'app-community', imports: [RouterLink, FormField, DatePipe, QueryInspector],
  templateUrl: './community.html', styleUrl: './community.scss',
})
export class Community {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  protected readonly mode = signal('passport');
  protected readonly slug = signal('');
  protected readonly passport = signal<Passport | null>(null);
  protected readonly network = signal<Network | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected readonly showRematches = signal(false);
  protected readonly showReconnects = signal(false);
  protected readonly mutationSection = signal('');
  protected sectionQueries(...labels: string[]) { return queriesByLabel(this.network()?.queries ?? this.passport()?.queries, ...labels); }
  protected readonly timelineQueries = computed(() => (this.passport()?.queries ?? []).filter(q => q.label.startsWith('Passport ')));
  protected savedQueries(...actions: string[]) { return actions.includes(this.mutationSection()) ? this.mutationQueries() : []; }
  protected readonly mutationQueries = signal<Query[]>([]);
  protected readonly meetings = computed(() => this.passport()?.timeline.filter(item => item.kind === 'MET') ?? []);
  protected readonly person = computed(() => this.network()?.person ?? this.passport()?.person);
  protected readonly model = signal({badgeCode: 'badge-0003', context: 'Coffee and conversation', location: 'Community coffee bar', brewSlug: 'blueberry-bloom-v60', targetSlug: 'priya-nair', pathTarget: 'luis-ortega', sessionSlug: 'demo-coffee-cards', gameSlug: 'coffee-cards', opponentSlug: 'priya-nair', winnerScore: 21, loserScore: 17});
  protected readonly fields = form(this.model, fields => { required(fields.badgeCode); required(fields.context); required(fields.location); required(fields.pathTarget); });

  constructor() {
    combineLatest([this.route.paramMap, this.route.queryParamMap, this.reload.pipe(startWith(undefined))]).pipe(
      tap(([params, query]) => {
        const slug = params.get('personSlug')!;
        if (slug !== this.slug() || this.mode() !== this.route.snapshot.data['mode']) { this.message.set(''); this.mutationQueries.set([]); this.mutationSection.set(''); this.showRematches.set(false); this.showReconnects.set(false); }
        this.slug.set(slug);
        this.mode.set(this.route.snapshot.data['mode']);
        this.model.update(value => ({...value, pathTarget: query.get('target') ?? 'luis-ortega'}));
        this.loading.set(true); this.error.set(''); this.passport.set(null); this.network.set(null);
      }),
      switchMap(() => this.http.get<Passport | Network>(`/api/demo/${this.mode() === 'network' ? 'network' : 'passport'}/${encodeURIComponent(this.slug())}`, {
        params: this.mode() === 'network' ? {target: this.model().pathTarget} : {},
      }).pipe(catchError(error => { this.error.set(this.errorText(error)); return of(null); }))),
      takeUntilDestroyed(),
    ).subscribe(data => {
      if (this.mode() === 'network') this.network.set(data as Network | null);
      else this.passport.set(data as Passport | null);
      this.loading.set(false);
    });
  }

  protected retry(): void { this.reload.next(); }
  protected findPath(event: Event): void {
    event.preventDefault();
    if ((this.route.snapshot.queryParamMap.get('target') ?? 'luis-ortega') === this.model().pathTarget) this.reload.next();
    else void this.router.navigate([], {relativeTo: this.route, queryParams: {target: this.model().pathTarget}});
  }
  protected save(action: string, event?: Event): void {
    event?.preventDefault();
    if (this.busy()) return;
    const values = this.model();
    const personSlug = this.slug();
    const bodies: Record<string, object> = {
      meet: {personSlug, badgeCode: values.badgeCode, context: values.context, location: values.location},
      tastings: {personSlug, brewSlug: values.brewSlug},
      loves: {personSlug, brewSlug: values.brewSlug},
      reconnects: {personSlug, targetSlug: values.targetSlug},
      'game-sessions': {personSlug, slug: values.sessionSlug, gameSlug: values.gameSlug, opponentSlug: values.opponentSlug},
      'game-results': {sessionSlug: values.sessionSlug, winnerSlug: personSlug, loserSlug: values.opponentSlug, winnerScore: values.winnerScore, loserScore: values.loserScore},
    };
    this.busy.set(true); this.error.set(''); this.message.set('');
    this.http.post<{message: string; queries: Query[]}>(`/api/demo/graph/${action}`, bodies[action]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { if (personSlug !== this.slug()) return; this.busy.set(false); this.message.set(result.message); this.mutationQueries.set(result.queries); this.mutationSection.set(action); this.reload.next(); },
      error: error => { this.busy.set(false); this.error.set(this.errorText(error)); },
    });
  }
  private errorText(error: HttpErrorResponse): string {
    return error.error?.detail ?? error.error?.error ?? error.error?.message ?? 'The community could not be updated or loaded. Check the connection and try again.';
  }
}
