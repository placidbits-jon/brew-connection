import { Transactions } from './transactions/transactions';
import { EventTools } from './event-tools/event-tools';
import { Telemetry } from './telemetry/telemetry';
import { Documents } from './documents/documents';
import { Routes } from '@angular/router';
import { Discovery } from './discovery/discovery';
import { Story } from './story/story';
import { Lab } from './lab/lab';
import { Community } from './community/community';

export const routes: Routes = [
  { path: 'demo/transactions', component: Transactions, title: 'Transactions | Brew Connection' },
  {
    path: 'demo/lookup',
    component: EventTools,
    data: { mode: 'lookup' },
    title: 'Code lookup | Brew Connection',
  },
  {
    path: 'demo/map',
    component: EventTools,
    data: { mode: 'map' },
    title: 'Nearby coffee | Brew Connection',
  },
  {
    path: 'demo/brews/:brewSlug',
    component: Telemetry,
    data: { mode: 'brew' },
    title: 'Brew telemetry | Brew Connection',
  },
  {
    path: 'demo/pulse',
    component: Telemetry,
    data: { mode: 'pulse' },
    title: 'Event pulse | Brew Connection',
  },
  { path: 'demo/discover', component: Discovery, title: 'Discovery | Brew Connection' },
  {
    path: 'demo/recipes/:recipeSlug',
    component: Documents,
    data: { mode: 'recipes' },
    title: 'Recipe workshop | Brew Connection',
  },
  {
    path: 'demo/coffee/:roastBatchSlug',
    component: Documents,
    data: { mode: 'coffee' },
    title: 'Bean to cup | Brew Connection',
  },
  {
    path: 'demo/meet/:personSlug',
    component: Community,
    data: { mode: 'meet' },
    title: 'Meet someone | Brew Connection',
  },
  {
    path: 'demo/passport/:personSlug',
    component: Community,
    data: { mode: 'passport' },
    title: 'Coffee passport | Brew Connection',
  },
  {
    path: 'demo/network/:personSlug',
    component: Community,
    data: { mode: 'network' },
    title: 'My network | Brew Connection',
  },
  { path: 'demo/lab', component: Lab, title: 'Schema explorer | Brew Connection' },
  { path: 'demo/story', component: Story, title: 'Demo story | Brew Connection' },
  { path: 'demo/:feature', component: Story, title: 'Coming next | Brew Connection' },
  { path: 'demo/:feature/:slug', component: Story, title: 'Coming next | Brew Connection' },
  { path: '', pathMatch: 'full', redirectTo: 'demo/story' },
  { path: '**', redirectTo: 'demo/story' },
];
