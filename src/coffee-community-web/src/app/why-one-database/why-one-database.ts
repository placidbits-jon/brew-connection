import { Component, input } from '@angular/core';

@Component({
  selector: 'app-why-one-database',
  templateUrl: './why-one-database.html',
  styleUrl: './why-one-database.scss',
})
export class WhyOneDatabase {
  readonly models = input.required<string>();
  readonly proof = input.required<string>();
  readonly alternative = input.required<string>();
}
