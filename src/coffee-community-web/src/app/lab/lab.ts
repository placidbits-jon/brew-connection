import { HttpClient } from '@angular/common/http';
import { JsonPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

interface SchemaType { name: string; type: string; properties?: {name: string; type: string}[] }
interface SchemaIndex { name: string; type: string; unique?: boolean; typeName?: string; properties?: string[] }
interface Schema { types: SchemaType[]; indexes: SchemaIndex[] }

@Component({
  selector: 'app-lab',
  imports: [RouterLink, JsonPipe],
  templateUrl: './lab.html',
  styleUrl: './lab.scss',
})
export class Lab {
  private readonly http = inject(HttpClient);
  protected readonly view = inject(ActivatedRoute).snapshot.queryParamMap.get('view') ?? 'schema';
  protected readonly schema = signal<Schema | null>(null);
  protected readonly selected = signal<string | null>(null);
  protected readonly error = signal(false);
  protected readonly detail = computed(() => this.schema()?.types.find(type => type.name === this.selected()));
  protected readonly indexes = computed(() => this.schema()?.indexes.filter(index => index.typeName === this.selected() || index.name.startsWith(`${this.selected()}[`)) ?? []);

  constructor() { this.load(); }

  protected load(): void {
    this.error.set(false);
    this.http.get<Schema>('/api/demo/schema').subscribe({
      next: schema => this.schema.set(schema),
      error: () => this.error.set(true),
    });
  }
}
