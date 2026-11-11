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

import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MAX_PICKS, MarketService } from '../../core/market.service';
import { UniverseRow } from '../../core/market.model';
import { UniverseApi } from '../../core/universe.api';
import { ViewportService } from '../../core/viewport.service';

// Tab 1: the whole stock universe, searchable and paged. Ticking a stock picks it for the Watch tab.
// Two layouts: cards on a phone, a table on a desktop.
@Component({
  selector: 'app-universe',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toolbar">
      <input
        type="search"
        placeholder="Search ticker or company"
        [ngModel]="query()"
        (ngModelChange)="onQuery($event)"
        aria-label="Search the universe"
      />
      <span class="count">{{ total() }} stocks · {{ market.picks().length }}/{{ maxPicks }} picked</span>
    </div>

    @if (message()) {
      <p class="notice">{{ message() }}</p>
    }

    @if (viewport.isMobile()) {
      <!-- Mobile: one card per stock, with a large tap target for the pick. -->
      <ul class="cards">
        @for (row of rows(); track row.ticker) {
          <li class="card" [class.picked]="market.isPicked(row.ticker)">
            <label>
              <input
                type="checkbox"
                [checked]="market.isPicked(row.ticker)"
                (change)="toggle(row)"
                [attr.aria-label]="'Watch ' + row.ticker"
              />
              <span class="card-text">
                <span class="ticker">{{ row.ticker }}</span>
                <span class="name">{{ row.name }}</span>
              </span>
            </label>
          </li>
        }
      </ul>
    } @else {
      <!-- Desktop: a table with the pick box in the first column. -->
      <table>
        <thead>
          <tr>
            <th class="pick-col">Watch</th>
            <th>Ticker</th>
            <th>Company</th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.ticker) {
            <tr [class.picked]="market.isPicked(row.ticker)">
              <td class="pick-col">
                <input
                  type="checkbox"
                  [checked]="market.isPicked(row.ticker)"
                  (change)="toggle(row)"
                  [attr.aria-label]="'Watch ' + row.ticker"
                />
              </td>
              <td class="ticker">{{ row.ticker }}</td>
              <td>{{ row.name }}</td>
            </tr>
          }
        </tbody>
      </table>
    }

    <div class="pager">
      <button type="button" (click)="go(page() - 1)" [disabled]="page() <= 1">Previous</button>
      <span>Page {{ page() }} of {{ pageCount() }}</span>
      <button type="button" (click)="go(page() + 1)" [disabled]="page() >= pageCount()">Next</button>
    </div>
  `,
  styles: `
    .toolbar { display: flex; gap: 1rem; align-items: center; margin-bottom: 0.75rem; }
    input[type='search'] { flex: 1; min-width: 0; padding: 0.5rem 0.6rem; font: inherit; }
    .count { color: #555; white-space: nowrap; }
    .ticker { font-family: monospace; font-weight: 600; }
    .notice { color: #8a4b00; background: #fff6e5; padding: 0.4rem 0.6rem; }

    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 0.3rem 0.5rem; border-bottom: 1px solid #e3e3e3; }
    th { background: #f5f5f5; position: sticky; top: 0; }
    .pick-col { width: 4.5rem; text-align: center; }
    tr.picked { background: #eef6ff; }

    .cards { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.5rem; }
    .card { border: 1px solid #ddd; border-radius: 8px; background: #fff; }
    .card.picked { background: #eef6ff; border-color: #1f5fbf; }
    .card label { display: flex; gap: 0.75rem; align-items: center; padding: 0.75rem; min-height: 3rem; cursor: pointer; }
    .card input[type='checkbox'] { width: 1.5rem; height: 1.5rem; flex: none; }
    .card-text { display: grid; gap: 0.15rem; min-width: 0; }
    .name { color: #555; font-size: 0.9rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

    .pager { display: flex; gap: 1rem; align-items: center; justify-content: center; margin-top: 0.75rem; flex-wrap: wrap; }
    .pager button { min-height: 2.5rem; min-width: 6rem; font: inherit; }
  `,
})
export class UniverseComponent implements OnInit {
  protected readonly market = inject(MarketService);
  protected readonly viewport = inject(ViewportService);
  private readonly api = inject(UniverseApi);
  // 'all' for the Universe tab, 'popular' for the Popular tab. Set by the route.
  private readonly set = (inject(ActivatedRoute).snapshot.data['set'] ?? 'all') as 'all' | 'popular';

  protected readonly maxPicks = MAX_PICKS;
  protected readonly query = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(50);
  protected readonly total = signal(0);
  protected readonly rows = signal<UniverseRow[]>([]);
  protected readonly message = signal<string | null>(null);

  private debounce: ReturnType<typeof setTimeout> | null = null;

  protected readonly pageCount = () => Math.max(1, Math.ceil(this.total() / this.pageSize()));

  ngOnInit(): void {
    this.load();
  }

  protected onQuery(text: string): void {
    this.query.set(text);
    if (this.debounce !== null) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => {
      this.page.set(1);
      this.load();
    }, 250);
  }

  protected go(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected toggle(row: UniverseRow): void {
    const accepted = this.market.togglePick(row.ticker);
    this.message.set(accepted ? null : `You can watch at most ${MAX_PICKS} stocks. Untick one first.`);
  }

  private load(): void {
    this.api.search(this.query(), this.page(), this.pageSize(), this.set).subscribe({
      next: (res) => {
        this.total.set(res.total);
        this.rows.set(res.rows);
      },
      error: () => this.message.set('Could not load the stock list. Is the server running?'),
    });
  }
}
