import { Routes } from '@angular/router';
import { Story } from './story/story';
import { Lab } from './lab/lab';

export const routes: Routes = [
  { path: 'demo/lab', component: Lab, title: 'Schema explorer | Brew Connection' },
  { path: 'demo/story', component: Story, title: 'Demo story | Brew Connection' },
  { path: 'demo/:feature', component: Story, title: 'Coming next | Brew Connection' },
  { path: 'demo/:feature/:slug', component: Story, title: 'Coming next | Brew Connection' },
  { path: '', pathMatch: 'full', redirectTo: 'demo/story' },
  { path: '**', redirectTo: 'demo/story' },
];
