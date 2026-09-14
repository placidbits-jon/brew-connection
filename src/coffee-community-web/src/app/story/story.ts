import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

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

@Component({
  selector: 'app-story',
  imports: [RouterLink],
  templateUrl: './story.html',
  styleUrl: './story.scss',
})
export class Story implements OnInit {
  private readonly http = inject(HttpClient);

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
    this.http.post('/api/demo/reset', {}).subscribe({
      next: () => { this.resetting.set(false); this.refresh(); },
      error: () => this.resetting.set(false),
    });
  }
}
