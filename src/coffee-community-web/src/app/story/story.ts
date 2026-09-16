import { QueryInspector, InspectedQuery, queriesByLabel } from '../query-inspector/query-inspector';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap, tap } from 'rxjs';

interface DemoStatus {
  arcadeDbVersion: string;
  process: string;
  database: string;
  schema: string;
  seed: string;
  embedding: string;
  embeddingProvider: string;
  error?: string;
}

interface StoryRecord { slug: string; name: string; context?: string }
interface SeedStory {
  queries?: InspectedQuery[];
  profile: string;
  persona: StoryRecord & { role: string };
  counts: { type: string; count: number }[];
  connections: StoryRecord[];
  tastings: StoryRecord[];
  rematches: StoryRecord[];
}

@Component({
  selector: 'app-story',
  imports: [RouterLink, QueryInspector],
  templateUrl: './story.html',
  styleUrl: './story.scss',
})
export class Story implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly story = signal<SeedStory | null>(null);
  protected sectionQueries(...labels: string[]) { return queriesByLabel(this.story()?.queries, ...labels); }
  protected readonly storyLoading = signal(true);
  protected readonly storyError = signal(false);
  protected readonly resetError = signal(false);

  constructor() {
    this.route.queryParamMap.pipe(
      tap(() => { this.storyLoading.set(true); this.storyError.set(false); this.story.set(null); }),
      switchMap(params => this.http.get<SeedStory>('/api/demo/story', { params: { persona: params.get('persona') ?? 'maya' } }).pipe(
        catchError(() => { this.storyError.set(true); return of(null); }),
      )),
      takeUntilDestroyed(),
    ).subscribe(story => {
      this.story.set(story);
      if (story) this.selectedPersona.set(story.persona.slug);
      this.storyLoading.set(false);
    });
  }

  protected selectPersona(event: Event): void {
    const persona = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], { relativeTo: this.route, queryParams: { persona }, queryParamsHandling: 'merge' });
  }

  protected readonly status = signal<DemoStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly resetting = signal(false);
  protected readonly showResources = signal(false);
  protected readonly selectedPersona = signal('maya-chen');

  protected readonly experiences = [
    { number: '01', title: 'Coffee passport', detail: 'People, tastings, games, and reconnects', route: '/demo/passport/maya-chen', model: 'Graph' },
    { number: '02', title: 'Bean to cup', detail: 'Lot, roast, recipe, brewer, and reaction', route: '/demo/coffee/ethiopia-blueberry-bloom', model: 'Graph + documents' },
    { number: '03', title: 'Find my next cup', detail: 'Keyword, semantic, and social ranking', route: '/demo/discover', model: 'Search + vectors' },
    { number: '04', title: 'Live brew', detail: 'A pour-over curve against its recipe', route: '/demo/brews/blueberry-bloom-v60', model: 'Time-series' },
    { number: '05', title: 'Community pulse', detail: 'Counters, activity, and nearby tables', route: '/demo/pulse', model: 'Key/value + geo' },
    { number: '06', title: 'Query lab', detail: 'SQL, Cypher, plans, and transactions', route: '/demo/lab', model: 'Polyglot API' },
  ];

  ngOnInit(): void { this.refresh(); }

  protected refresh(): void {
    this.loading.set(true);
    this.http.get<DemoStatus>('/api/demo/status').subscribe({
      next: status => { this.status.set(status); this.loading.set(false); },
      error: () => { this.status.set(null); this.loading.set(false); },
    });
  }

  protected reset(): void {
    this.resetting.set(true);
    this.resetError.set(false);
    this.http.post('/api/demo/reset', {}).subscribe({
      next: () => {
        this.resetting.set(false);
        this.refresh();
        this.http.get<SeedStory>('/api/demo/story', { params: { persona: this.selectedPersona() } }).subscribe({
          next: story => { this.story.set(story); this.storyError.set(false); },
          error: () => { this.story.set(null); this.storyError.set(true); },
        });
      },
      error: () => { this.resetting.set(false); this.resetError.set(true); },
    });
  }
}
