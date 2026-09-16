import { QueryInspector, queriesByLabel, InspectedQuery } from '../query-inspector/query-inspector';
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
type Query = InspectedQuery;
interface RecipeDetail { recipe: RecordData; currentRevision: Revision; revisions: Revision[]; notes: Note[]; queries: Query[] }
interface CoffeeDetail { batch: RecordData; lot: RecordData; roaster: RecordData; vendor: RecordData; roastProfile: RecordData; brews: {brew: RecordData; brewer: RecordData; recipe: RecordData; revision: Revision; reactions: {person: RecordData; kind: string; reaction: RecordData}[]}[]; notes: Note[]; queries: Query[] }
interface GraphNode { id: string; label: string; kind: string; record: unknown; x: number; y: number }
interface GraphEdge { from: GraphNode; to: GraphNode; label: string }
const defaults = () => ({steps: [{atSeconds: 0, waterGrams: 60, action: 'Bloom'}, {atSeconds: 45, waterGrams: 180, action: 'Slow spiral pour'}, {atSeconds: 90, waterGrams: 300, action: 'Finish pour'}], equipment: {brewer: 'V60', filter: 'paper', grinder: 'hand grinder'}, grind: {clicks: 20}, temperatureC: 93, coffeeGrams: 20, waterGrams: 300, commentary: 'A sweeter finish'});

@Component({selector: 'app-documents', imports: [RouterLink, JsonPipe, FormField, QueryInspector], templateUrl: './documents.html', styleUrl: './documents.scss'})
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
  protected readonly selectedNode = signal<GraphNode | null>(null);
  protected readonly comparison = signal('');
  protected readonly previous = computed(() => this.recipe()?.revisions.find(r => r.slug === this.comparison()));
  protected readonly recipeQueries = computed(() => this.creating() ? [] : queriesByLabel(this.recipe()?.queries, 'Recipe vertex and current document link', 'Immutable recipe history', 'Current revision link'));
  protected readonly historyQueries = computed(() => queriesByLabel(this.recipe()?.queries, 'Immutable recipe history', 'Current revision link'));
  protected readonly provenanceQueries = computed(() => {
    const entry = this.coffee()?.brews.find(b => b.brew.slug === this.selectedBrew());
    return queriesByLabel(this.coffee()?.queries, 'Resolve RoastBatch', 'Follow FROM_LOT', 'Follow ROASTED', 'Follow SELLS', 'Nested roast profile document', 'Brews using this batch', 'Follow BREWED', 'Follow USED_RECIPE', 'Historical revision pinned on USED_RECIPE', 'Attendee TASTED reactions', 'Attendee LOVED reactions', 'Resolve Person').filter(query => {
      const slug = (query.parameters as {slug?: string})?.slug;
      if (query.label === 'Resolve Person') return entry?.reactions.some(r => r.person.slug === slug);
      const brewLabels = ['Follow BREWED', 'Follow USED_RECIPE', 'Historical revision pinned on USED_RECIPE', 'Attendee TASTED reactions', 'Attendee LOVED reactions'];
      return !brewLabels.includes(query.label) || slug === this.selectedBrew();
    });
  });
  protected readonly documentQueries = computed(() => {
    const node = this.selectedNode();
    if (!node) return [];
    const labels: Record<string, string[]> = {
      batch: ['Resolve RoastBatch'], lot: ['Follow FROM_LOT'], roaster: ['Follow ROASTED'], vendor: ['Follow SELLS'],
      profile: ['Nested roast profile document'], brew: ['Brews using this batch'], brewer: ['Follow BREWED'],
      recipe: ['Follow USED_RECIPE'], revision: ['Historical revision pinned on USED_RECIPE'],
      reaction: ['Resolve Person', `Attendee ${node.kind.split(' · ')[0]} reactions`],
    };
    return queriesByLabel(this.provenanceQueries(), ...(labels[node.id.split('-')[0]] ?? [])).filter(query => query.label !== 'Resolve Person' || (query.parameters as {slug?: string})?.slug === (node.record as RecordData).slug);
  });
  private readonly notesResult = signal<{context: string; notes: Note[]; queries: Query[]} | null>(null);
  private readonly notesContext = computed(() => JSON.stringify([this.persona(), this.noteModel().subjectType, this.noteModel().subjectSlug]));
  protected readonly notes = computed(() => this.notesResult()?.context === this.notesContext() ? this.notesResult()!.notes : []);
  protected readonly notesQueries = computed(() => this.notesResult()?.context === this.notesContext() ? this.notesResult()!.queries : []);
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
        this.cancelNotes.next(); this.noteModel.update(m => ({...m, body: ''})); this.loading.set(true); this.error.set(''); this.recipe.set(null); this.coffee.set(null); this.notesResult.set(null); this.selectedNode.set(null);
        if (previousSlug !== this.slug()) this.noteModel.update(m => ({...m, subjectType: this.mode() === 'recipes' ? 'Recipe' : 'RoastBatch', subjectSlug: this.slug()}));
      }),
      switchMap(() => this.http.get<RecipeDetail | CoffeeDetail>(`/api/demo/${this.mode()}/${encodeURIComponent(this.slug())}`, {params: {persona: this.persona()}}).pipe(catchError(error => {this.error.set(this.errorText(error)); return of(null);}))), takeUntilDestroyed(),
    ).subscribe(data => { if (data && 'recipe' in data) this.acceptRecipe(data); else if (data) {this.coffee.set(data); if (!data.brews.some(b => b.brew.slug === this.selectedBrew())) this.selectedBrew.set(data.brews[0]?.brew.slug ?? ''); this.acceptPageNotes(data); this.selectedNode.set(this.graph().nodes.find(n => n.id === 'batch') ?? null);} this.loading.set(false); if (data && (this.noteModel().subjectSlug !== this.slug() || this.noteModel().subjectType !== (this.mode() === 'recipes' ? 'Recipe' : 'RoastBatch'))) this.loadNotes(); });
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
    this.cancelNotes.next(); this.error.set(''); this.notesResult.set(null);
    const context = this.notesContext();
    this.http.get<{notes: Note[]; queries?: Query[]}>('/api/demo/notes', {params: {persona: this.persona(), subjectType: this.noteModel().subjectType, subjectSlug: this.noteModel().subjectSlug}}).pipe(takeUntil(this.cancelNotes), takeUntilDestroyed(this.destroyRef)).subscribe({next: data => {if (context === this.notesContext()) this.notesResult.set({context, notes: data.notes, queries: queriesByLabel(data.queries, 'Notes visible to selected demo persona')});}, error: error => {if (context === this.notesContext()) this.error.set(this.errorText(error));}});
  }
  protected saveNote(event: Event): void {
    event.preventDefault(); if (this.busy() || this.noteFields().invalid()) return;
    const epoch = this.contextEpoch;
    this.busy.set(true); this.error.set(''); this.message.set('');
    this.http.post('/api/demo/notes', {...this.noteModel(), personSlug: this.persona(), slug: this.noteKey}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next: () => {if (epoch !== this.contextEpoch) return; this.busy.set(false); this.message.set('Note saved.'); this.noteKey = crypto.randomUUID(); this.noteModel.update(m => ({...m, body: ''})); this.loadNotes();}, error: error => {if (epoch !== this.contextEpoch) return; this.busy.set(false); this.error.set(this.errorText(error));}});
  }
  private acceptPageNotes(data: {notes: Note[]; queries: Query[]}): void {
    const type = this.mode() === 'recipes' ? 'Recipe' : 'RoastBatch';
    if (this.noteModel().subjectType === type && this.noteModel().subjectSlug === this.slug()) this.notesResult.set({context: this.notesContext(), notes: data.notes, queries: queriesByLabel(data.queries, 'Notes visible to selected demo persona')});
  }
  private acceptRecipe(data: RecipeDetail): void {this.recipe.set(data); this.acceptPageNotes(data); this.comparison.set(data.revisions.find(r => r.revision < data.currentRevision.revision)?.slug ?? ''); const {steps,equipment,grind,temperatureC,coffeeGrams,waterGrams,commentary} = data.currentRevision; this.model.set({slug: data.recipe.slug, name: data.recipe.name ?? '', steps: structuredClone(steps), equipment: {...equipment}, grind: {...grind}, temperatureC, coffeeGrams, waterGrams, commentary});}
  private errorText(error: HttpErrorResponse): string {return error.error?.detail ?? error.error?.error ?? error.error?.message ?? 'Unable to load or save. Check the connection and try again.';}
}
