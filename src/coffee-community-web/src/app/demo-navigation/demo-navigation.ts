import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

interface DemoDestination {
  label: string;
  route: string;
}

@Component({
  selector: 'app-demo-navigation',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './demo-navigation.html',
  styleUrl: './demo-navigation.scss',
})
export class DemoNavigation {
  protected readonly destinations: DemoDestination[] = [
    { label: 'Passport', route: '/demo/passport/maya-chen' },
    { label: 'Meet someone', route: '/demo/meet/maya-chen' },
    { label: 'My network', route: '/demo/network/maya-chen' },
    { label: 'Games', route: '/demo/games/maya-chen' },
    { label: 'Recipes', route: '/demo/recipes/blueberry-v60' },
    { label: 'Bean to cup', route: '/demo/coffee/ethiopia-blueberry-bloom' },
    { label: 'Discover', route: '/demo/discover' },
    { label: 'Live brew', route: '/demo/brews/blueberry-bloom-v60' },
    { label: 'Event pulse', route: '/demo/pulse' },
    { label: 'Code lookup', route: '/demo/lookup' },
    { label: 'Nearby coffee', route: '/demo/map' },
    { label: 'Transactions', route: '/demo/transactions' },
    { label: 'Query lab', route: '/demo/lab' },
  ];
}
