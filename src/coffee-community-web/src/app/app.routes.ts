import { Routes } from '@angular/router';
import { Story } from './story/story';

export const routes: Routes = [
  { path: 'demo/story', component: Story, title: 'Demo story | Brew Connection' },
  { path: 'demo/:feature', component: Story, title: 'Coming next | Brew Connection' },
  { path: 'demo/:feature/:slug', component: Story, title: 'Coming next | Brew Connection' },
  { path: '', pathMatch: 'full', redirectTo: 'demo/story' },
  { path: '**', redirectTo: 'demo/story' },
];
