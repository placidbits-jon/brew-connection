import { JsonPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { form, FormField, required, min, applyEach } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { combineLatest, Subject, startWith, switchMap, tap, catchError, of, takeUntil } from 'rxjs';

interface RecordData { slug: string; name?: string; [key: string]: unknown }
interface Revision extends RecordData { revision: number; steps: {atSeconds: number; waterGrams: number; action: string}[]; equipment: {brewer: string; filter: string; grinder: string}; grind: {clicks: number}; temperatureC: number; coffeeGrams: number; waterGrams: number; commentary: string }
interface Note extends RecordData { body: string; visibility: string; ownerSlug: string; subjectType: string; subjectSlug: string }
interface Query { label: string; language: string; command: string; parameters: unknown }
interface RecipeDetail { recipe: RecordData; currentRevision: Revision; revisions: Revision[]; notes: Note[]; queries: Query[] }
interface CoffeeDetail { batch: RecordData; lot: RecordData; roaster: RecordData; vendor: RecordData; roastProfile: RecordData; brews: {brew: RecordData; brewer: RecordData; recipe: RecordData; revision: Revision; reactions: {person: RecordData; kind: string; reaction: RecordData}[]}[]; notes: Note[]; queries: Query[] }
interface GraphNode { id: string; label: string; kind: string; record: unknown; x: number; y: number }
interface GraphEdge { from: GraphNode; to: GraphNode; label: string }
const defaults = () => ({steps: [{atSeconds: 0, waterGrams: 60, action: 'Bloom'}, {atSeconds: 45, waterGrams: 180, action: 'Slow spiral pour'}, {atSeconds: 90, waterGrams: 300, action: 'Finish pour'}], equipment: {brewer: 'V60', filter: 'paper', grinder: 'hand grinder'}, grind: {clicks: 20}, temperatureC: 93, coffeeGrams: 20, waterGrams: 300, commentary: 'A sweeter finish'});

@Component({selector: 'app-documents', imports: [RouterLink, JsonPipe, FormField], templateUrl: './documents.html', styleUrl: './documents.scss'})
export class Documents {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();
  private contextEpoch = 0;
  protected readonly mode = signal('recipes');
  protected readonly slug = signal('');
  protected readonly persona = signal('maya-chen');
  protected readonly recipe = signal<RecipeDetail | null>(null);
  protected readonly coffee = signal<CoffeeDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly message = signal('');
  protected readonly creating = signal(false);
  protected readonly showQueries = signal(false);
  protected readonly selectedNode = signal<GraphNode | null>(null);
  protected readonly comparison = signal('');
  protected readonly previous = computed(() => this.recipe()?.revisions.find(r => r.slug === this.comparison()));
  protected readonly queries = computed(() => this.recipe()?.queries ?? this.coffee()?.queries ?? []);
  protected readonly notes = signal<Note[]>([]);
  protected readonly model = signal({slug: 'story-recipe', name: 'Story pour-over', ...defaults()});
  protected readonly fields = form(this.model, f => {required(f.slug); required(f.name); min(f.coffeeGrams, 1); min(f.waterGrams, 1); min(f.temperatureC, 1); min(f.grind.clicks, 1); required(f.equipment.brewer); required(f.commentary); applyEach(f.steps, step => {required(step.action); min(step.atSeconds, 0); min(step.waterGrams, 0);});});
  protected readonly noteModel = signal({subjectType: 'Recipe', subjectSlug: 'blueberry-v60', visibility: 'private', body: ''});
  protected readonly noteFields = form(this.noteModel, f => {required(f.subjectSlug); required(f.body);});
  private noteKey = crypto.randomUUID();
  private readonly cancelNotes = new Subject<void>();
  protected readonly selectedBrew = signal('blueberry-bloom-v60');
  protected readonly graph = computed(() => {
    const data = this.coffee(); const nodes: GraphNode[] = []; const edges: GraphEdge[] = [];
    if (!data) return {nodes, edges, height: 500};
    const add = (id: string, kind: string, record: RecordData, x: number, y: number) => {const node = {id, kind, record, label: record?.name ?? record?.slug ?? kind, x, y}; nodes.push(node); return node;};
    const link = (from: GraphNode, to: GraphNode, label: string) => edges.push({from, to, label});
    const lot = add('lot', 'Coffee lot', data.lot, 110, 60), batch = add('batch', 'Roast batch', data.batch, 430, 160);
    link(batch, lot, 'FROM_LOT'); link(add('roaster', 'Roaster', data.roaster, 430, 60), batch, 'ROASTED'); link(add('vendor', 'Vendor', data.vendor, 750, 60), batch, 'SELLS');
    link(batch, add('profile', 'RoastProfile document', data.roastProfile, 750, 160), 'profile');
    data.brews.filter(entry => entry.brew.slug === this.selectedBrew()).forEach((entry, i) => {
      const y = 300 + i * 300, brew = add('brew-' + i, 'Brew', entry.brew, 430, y), recipe = add('recipe-' + i, 'Recipe', entry.recipe, 750, y);
      link(brew, batch, 'USED_BATCH'); link(add('brewer-' + i, 'Brewer', entry.brewer, 110, y), brew, 'BREWED'); link(brew, recipe, 'USED_RECIPE');
      link(brew, add('revision-' + i, 'Pinned revision ' + entry.revision.revision, entry.revision, 750, y + 90), 'USED_RECIPE.revision');
      entry.reactions.forEach((reaction, j) => link(add(`reaction-${i}-${j}`, `${reaction.kind} · ${reaction.person.name ?? reaction.person.slug}`, {...reaction.person, reaction: reaction.reaction}, 180 + j * 230, y + 175), brew, reaction.kind));
    });
    return {nodes, edges, height: 560};
  });
  constructor() {
    combineLatest([this.route.paramMap, this.route.queryParamMap, this.reload.pipe(startWith(undefined))]).pipe(
      tap(([params, query]) => {
        this.contextEpoch++; this.noteKey = crypto.randomUUID(); this.busy.set(false); this.message.set('');
        const previousSlug = this.slug();
        this.mode.set(this.route.snapshot.data['mode']); this.slug.set(params.get('recipeSlug') ?? params.get('roastBatchSlug') ?? ''); this.persona.set(query.get('persona') ?? 'maya-chen');
        this.cancelNotes.next(); this.noteModel.update(m => ({...m, body: ''})); this.loading.set(true); this.error.set(''); this.recipe.set(null); this.coffee.set(null); this.notes.set([]); this.selectedNode.set(null);
        if (previousSlug !== this.slug()) this.noteModel.update(m => ({...m, subjectType: this.mode() === 'recipes' ? 'Recipe' : 'RoastBatch', subjectSlug: this.slug()}));
      }),
      switchMap(() => this.http.get<RecipeDetail | CoffeeDetail>(`/api/demo/${this.mode()}/${encodeURIComponent(this.slug())}`, {params: {persona: this.persona()}}).pipe(catchError(error => {this.error.set(this.errorText(error)); return of(null);}))), takeUntilDestroyed(),
    ).subscribe(data => { if (data && 'recipe' in data) this.acceptRecipe(data); else if (data) {this.coffee.set(data); if (!data.brews.some(b => b.brew.slug === this.selectedBrew())) this.selectedBrew.set(data.brews[0]?.brew.slug ?? ''); this.notes.set(data.notes); this.selectedNode.set(this.graph().nodes.find(n => n.id === 'batch') ?? null);} this.loading.set(false); if (data && (this.noteModel().subjectSlug !== this.slug() || this.noteModel().subjectType !== (this.mode() === 'recipes' ? 'Recipe' : 'RoastBatch'))) this.loadNotes(); });
  }
  protected chooseBrew(event: Event): void {this.selectedBrew.set((event.target as HTMLSelectElement).value); this.selectedNode.set(this.graph().nodes.find(n => n.id === 'brew-0') ?? null);}
  protected retry(): void {this.reload.next();}
  protected changePersona(event: Event): void {this.message.set(''); void this.router.navigate([], {relativeTo: this.route, queryParams: {persona: (event.target as HTMLSelectElement).value}, queryParamsHandling: 'merge'});}
  protected compare(event: Event): void {this.comparison.set((event.target as HTMLSelectElement).value);}
  protected startCreate(): void {this.creating.set(true); this.model.set({slug: 'story-recipe', name: 'Story pour-over', ...defaults()});}
  protected cancelCreate(): void {this.creating.set(false); if (this.recipe()) this.acceptRecipe(this.recipe()!);}
  protected addStep(): void {this.model.update(m => ({...m, steps: [...m.steps, {atSeconds: (m.steps.at(-1)?.atSeconds ?? 0) + 45, waterGrams: m.waterGrams, action: 'Final pour'}]}));}
  protected removeStep(index: number): void {this.model.update(m => ({...m, steps: m.steps.filter((_, i) => i !== index)}));}
  protected publish(event: Event): void {
    event.preventDefault(); if (this.busy() || this.fields().invalid()) return;
    const epoch = this.contextEpoch;
    const {slug, name, ...revision} = this.model(), creating = this.creating();
    const body = creating ? {slug, name, personSlug: this.persona(), revision} : {personSlug: this.persona(), expectedRevision: this.recipe()!.currentRevision.revision, revision};
    this.busy.set(true); this.error.set(''); this.message.set('');
    this.http.post<RecipeDetail>(creating ? '/api/demo/recipes' : `/api/demo/recipes/${encodeURIComponent(this.slug())}/revisions`, body).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next: data => {if (epoch !== this.contextEpoch) return; this.busy.set(false); this.creating.set(false); this.acceptRecipe(data); this.message.set(`Revision ${data.currentRevision.revision} published. Existing brews keep their pinned revision.`); if (creating) void this.router.navigate(['/demo/recipes', data.recipe.slug], {queryParams: {persona: this.persona()}});}, error: error => {if (epoch !== this.contextEpoch) return; this.busy.set(false); this.error.set(this.errorText(error));}});
  }
  protected loadNotes(): void {
    this.cancelNotes.next(); this.error.set(''); this.notes.set([]);
    this.http.get<{notes: Note[]}>('/api/demo/notes', {params: {persona: this.persona(), subjectType: this.noteModel().subjectType, subjectSlug: this.noteModel().subjectSlug}}).pipe(takeUntil(this.cancelNotes), takeUntilDestroyed(this.destroyRef)).subscribe({next: data => this.notes.set(data.notes), error: error => this.error.set(this.errorText(error))});
  }
  protected saveNote(event: Event): void {
    event.preventDefault(); if (this.busy() || this.noteFields().invalid()) return;
    const epoch = this.contextEpoch;
    this.busy.set(true); this.error.set(''); this.message.set('');
    this.http.post('/api/demo/notes', {...this.noteModel(), personSlug: this.persona(), slug: this.noteKey}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next: () => {if (epoch !== this.contextEpoch) return; this.busy.set(false); this.message.set('Note saved.'); this.noteKey = crypto.randomUUID(); this.noteModel.update(m => ({...m, body: ''})); this.loadNotes();}, error: error => {if (epoch !== this.contextEpoch) return; this.busy.set(false); this.error.set(this.errorText(error));}});
  }
  private acceptRecipe(data: RecipeDetail): void {this.recipe.set(data); this.notes.set(data.notes); this.comparison.set(data.revisions.find(r => r.revision < data.currentRevision.revision)?.slug ?? ''); const {steps,equipment,grind,temperatureC,coffeeGrams,waterGrams,commentary} = data.currentRevision; this.model.set({slug: data.recipe.slug, name: data.recipe.name ?? '', steps: structuredClone(steps), equipment: {...equipment}, grind: {...grind}, temperatureC, coffeeGrams, waterGrams, commentary});}
  private errorText(error: HttpErrorResponse): string {return error.error?.detail ?? error.error?.error ?? error.error?.message ?? 'Unable to load or save. Check the connection and try again.';}
}
