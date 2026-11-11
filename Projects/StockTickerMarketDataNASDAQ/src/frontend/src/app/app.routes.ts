// Copyright (C) 2026 Bharath K A (bkumarbk9)
// SPDX-License-Identifier: AGPL-3.0-only
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License version 3
// as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

import { Routes } from '@angular/router';
import { UniverseComponent } from './features/universe/universe.component';
import { WatchComponent } from './features/watch/watch.component';
import { PICKS_KEY } from './core/market.service';

// The tab opened by default: Watch when the user already has picks, otherwise Popular.
function defaultTab(): string {
  try {
    const saved: unknown = JSON.parse(localStorage.getItem(PICKS_KEY) ?? '[]');
    return Array.isArray(saved) && saved.length > 0 ? '/watch' : '/popular';
  } catch {
    return '/popular';
  }
}

export const routes: Routes = [
  { path: 'universe', component: UniverseComponent, data: { set: 'all' } },
  { path: 'popular', component: WatchComponent, data: { source: 'popular' } },
  { path: 'watch', component: WatchComponent },
  { path: '', pathMatch: 'full', redirectTo: defaultTab },
  { path: '**', redirectTo: defaultTab },
];
