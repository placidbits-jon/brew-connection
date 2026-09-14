import { DecimalPipe, JsonPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { form, FormField } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, of, startWith, Subject, switchMap, tap } from 'rxjs';

interface SearchResult {
  slug: string;
  type: string;
  name: string;
  description: string;
  keywordScore: number;
  vectorDistance: number | null;
  keywordContribution: number;
  vectorContribution: number;
  graphContribution: number;
  totalScore: number;
  available: boolean;
  explanations: string[];
}
interface Query {
  label: string;
  language: string;
  command: string;
  parameters: unknown;
  plan?: unknown;
}
interface DiscoveryResult {
  query: string;
  mode: string;
  persona: string;
  results: SearchResult[];
  queries: Query[];
  model: {
    provider: string;
    model?: string;
    dimensions: number;
    similarity?: string;
    ranking?: string;
    filtering?: string;
  };
}

@Component({
  selector: 'app-discovery',
  imports: [RouterLink, FormField, DecimalPipe, JsonPipe],
  templateUrl: './discovery.html',
  styleUrl: './discovery.scss',
})
export class Discovery {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private context = 0;
  protected readonly question = signal({ text: '' });
  protected readonly questionFields = form(this.question);
  protected readonly interpreting = signal(false);
  protected readonly interpretation = signal('');
  protected readonly helperError = signal('');
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly reload = new Subject<void>();
  protected readonly data = signal<DiscoveryResult | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly showQueries = signal(false);
  protected readonly mode = signal('keyword');
  protected readonly model = signal({
    query: 'blueberry',
    persona: 'maya-chen',
    type: 'all',
    syntax: 'plain',
    similarTo: 'ethiopia-blueberry-bloom',
    availableOnly: true,
  });
  protected readonly fields = form(this.model);
  protected readonly modes = [
    { value: 'keyword', label: 'Keyword' },
    { value: 'semantic', label: 'Semantic' },
    { value: 'hybrid', label: 'Hybrid' },
    { value: 'personalized', label: 'Personalized' },
  ];
  protected readonly explanation = computed(
    () =>
      ({
        keyword: 'Find matching words with full-text relevance.',
        semantic: 'Find related meanings with local model embeddings.',
        hybrid: 'Combine keyword relevance and semantic similarity.',
        personalized: 'Add evidence from your community connections.',
      })[this.mode()] ?? 'Choose a discovery mode.',
  );
  constructor() {
    combineLatest([this.route.queryParamMap, this.reload.pipe(startWith(undefined))])
      .pipe(
        tap(([params]) => {
          this.context++;
          this.interpreting.set(false);
          this.mode.set(params.get('mode') ?? 'keyword');
          this.model.set({
            query: params.get('query') ?? 'blueberry',
            persona: params.get('persona') ?? 'maya-chen',
            type: params.get('type') ?? 'all',
            syntax: params.get('syntax') ?? 'plain',
            similarTo: params.get('similarTo') ?? 'ethiopia-blueberry-bloom',
            availableOnly: params.get('availableOnly') !== 'false',
          });
          this.loading.set(true);
          this.error.set('');
          this.data.set(null);
          this.showQueries.set(false);
        }),
        switchMap(() =>
          this.http
            .get<DiscoveryResult>('/api/demo/discover', {
              params: { ...this.model(), mode: this.mode() },
            })
            .pipe(
              catchError((error) => {
                this.error.set(this.errorText(error));
                return of(null);
              }),
            ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((data) => {
        this.data.set(data);
        this.loading.set(false);
      });
  }
  protected search(event?: Event): void {
    event?.preventDefault();
    this.navigate(this.mode());
  }
  protected changeMode(mode: string): void {
    this.navigate(mode);
  }
  protected interpret(event: Event): void {
    event.preventDefault();
    if (this.interpreting()) return;
    const context = this.context;
    this.interpreting.set(true);
    this.helperError.set('');
    this.interpretation.set('');
    this.http
      .post<{ interpretedQuery: string }>('/api/demo/discover/ask', {
        text: this.question().text,
        persona: this.model().persona,
        type: this.model().type,
        availableOnly: this.model().availableOnly,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (answer) => {
          if (context !== this.context) return;
          this.interpreting.set(false);
          this.interpretation.set(
            `Local model search query: “${answer.interpretedQuery}”. You can edit it above.`,
          );
          this.model.update((value) => ({
            ...value,
            query: answer.interpretedQuery,
            syntax: 'plain',
          }));
          this.navigate('personalized');
        },
        error: (error) => {
          if (context !== this.context) return;
          this.interpreting.set(false);
          this.helperError.set(this.errorText(error));
        },
      });
  }
  private navigate(mode: string): void {
    const tree = this.router.createUrlTree([], {
      relativeTo: this.route,
      queryParams: { ...this.model(), mode },
    });
    if (this.router.serializeUrl(tree) === this.router.url) this.reload.next();
    else void this.router.navigateByUrl(tree);
  }
  private errorText(error: HttpErrorResponse): string {
    return (
      error.error?.message ??
      error.error?.detail ??
      'Discovery could not be loaded. Check service readiness and try again.'
    );
  }
}
