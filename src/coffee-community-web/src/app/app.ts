import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { DemoNavigation } from './demo-navigation/demo-navigation';

@Component({
  imports: [RouterOutlet, DemoNavigation],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {}
