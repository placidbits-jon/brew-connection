import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, of, switchMap, tap } from 'rxjs';
import { InspectedQuery, QueryInspector, queriesByLabel } from '../query-inspector/query-inspector';

interface Person { slug: string; name: string; role: string }
interface GameDrink { slug: string; name: string; roastBatchSlug: string; coffeeName: string; note: string }
interface GameHistory {
  sessionSlug: string;
  gameSlug: string;
  gameName: string;
  category: string;
  playedAt: string | null;
  opponent: { slug: string; name: string };
  drink: GameDrink | null;
  opponentDrink: GameDrink | null;
  outcome: 'Win' | 'Loss' | 'In progress';
  score: number | null;
  opponentScore: number | null;
}
interface GameCatalogItem {
  slug: string;
  name: string;
  category: string;
  mechanics: string[];
  playerRange: string;
  playMinutes: number;
  sessions: number;
  players: number;
  playedByPersona: boolean;
}
interface GameJourney {
  person: Person;
  stats: { games: number; sessions: number; wins: number };
  history: GameHistory[];
  tableMatches: { slug: string; name: string; games: string[] }[];
  catalog: GameCatalogItem[];
  queries: InspectedQuery[];
}

@Component({
  selector: 'app-games',
  imports: [DatePipe, RouterLink, QueryInspector],
  templateUrl: './games.html',
  styleUrl: './games.scss',
})
export class Games {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly journey = signal<GameJourney | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  constructor() {
    this.route.paramMap.pipe(
      tap(() => { this.loading.set(true); this.error.set(''); this.journey.set(null); }),
      switchMap(params => this.http.get<GameJourney>(`/api/demo/games/${encodeURIComponent(params.get('personSlug') ?? 'maya-chen')}`).pipe(
        catchError((error: HttpErrorResponse) => {
          this.error.set(error.error?.message ?? 'The game journey could not be loaded.');
          return of(null);
        }),
      )),
      takeUntilDestroyed(),
    ).subscribe(journey => { this.journey.set(journey); this.loading.set(false); });
  }

  protected changePersona(event: Event): void {
    void this.router.navigate(['/demo/games', (event.target as HTMLSelectElement).value]);
  }

  protected sectionQueries(...labels: string[]): InspectedQuery[] {
    return queriesByLabel(this.journey()?.queries, ...labels);
  }
}
