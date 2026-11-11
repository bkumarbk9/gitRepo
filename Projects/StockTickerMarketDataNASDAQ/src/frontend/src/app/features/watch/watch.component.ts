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

import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { UniverseApi } from '../../core/universe.api';
import { MathAuditDrawerComponent } from '../audit/math-audit-drawer.component';
import { FIELDS, FieldDef, FieldGroup, GROUPS, formatValue } from '../../core/market.model';
import { MarketService } from '../../core/market.service';
import { ViewportService } from '../../core/viewport.service';

// The fields the mobile card shows at the top, so they are not repeated in the groups below it.
const HEAD_CODES = new Set(['p', 'tier', 'reg']);

// The audit card line that explains each marked table value, so a click can scroll the drawer to it.
const AUDIT_LABEL_BY_CODE: Readonly<Record<string, string>> = {
  vol: 'Vol (ann.)', reg: 'Vol (ann.)', var: 'VaR 1-day 95% (parametric)', beta: 'Beta (vs simulated index)',
  shp: 'Sharpe (session)', mdd: 'Max drawdown (session)', obi: 'Buy pressure %', rvol: 'Rel. volume',
  slip: 'Slippage $100k', vwap: 'VWAP', vwd: 'vs VWAP', rsi: 'RSI(14)', atr: 'ATR(14)',
  hi52: '52w position', lo52: '52w position', pos52: '52w position',
};

// Tab 2: the stocks picked on the Universe tab, with live values as the server pushes them.
// Two layouts: a card per stock on a phone, with the fields in groups; a table on a desktop, grouped by column band.
@Component({
  selector: 'app-watch',
  imports: [MathAuditDrawerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (tickers().length === 0) {
      <p class="empty">{{ emptyText }}</p>
    } @else if (viewport.isMobile()) {
      <!-- Mobile: one card per stock. Price and risk regime at the top, then the fields in groups. -->
      <ul class="cards">
        @for (ticker of tickers(); track ticker) {
          <li class="card">
            <div class="card-head">
              <div>
                <span class="ticker">{{ ticker }}</span>
                <span class="tier">{{ value(ticker, 'tier') }}</span>
              </div>
              <span class="last" [class.up]="direction(ticker, 'p') > 0" [class.down]="direction(ticker, 'p') < 0">{{ value(ticker, 'p') }}</span>
            </div>
            <span class="regime" [attr.data-level]="level(ticker)">Risk: {{ value(ticker, 'reg') }}</span>
            @for (group of groups; track group) {
              <section class="seg seg-{{ group.toLowerCase() }}">
              <h3>{{ group }}</h3>
              <dl>
                @for (def of bodyFields(group); track def.code) {
                  <div class="cell">
                    <dt>{{ def.label }}</dt>
                    <dd (click)="clickCell(ticker, group, def.code)" [class.calc]="group !== 'Quote'" [attr.data-hint]="group !== 'Quote' ? 'Click for details' : null" [class.up]="direction(ticker, def.code) > 0" [class.down]="direction(ticker, def.code) < 0">{{ value(ticker, def.code) }}</dd>
                  </div>
                }
              </dl>
              </section>
            }
          </li>
        }
      </ul>
    } @else {
      <!-- Desktop: a table with one row per stock. The group names sit above their columns. -->
      <div class="scroll">
        <table>
          <thead>
            <tr>
              <th rowspan="2">Ticker</th>
              @for (group of groups; track group) {
                <th class="band seg-{{ group.toLowerCase() }}" [attr.colspan]="fieldsIn(group).length">{{ group }}</th>
              }
            </tr>
            <tr>
              @for (def of fields; track def.code) {
                <th class="num">{{ def.label }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (ticker of tickers(); track ticker) {
              <tr class="row">
                <td class="ticker">{{ ticker }}</td>
                @for (def of fields; track def.code) {
                  <td class="num seg-{{ def.group.toLowerCase() }}" (click)="clickCell(ticker, def.group, def.code)" [class.calc]="def.group !== 'Quote'" [attr.data-hint]="def.group !== 'Quote' ? 'Click for details' : null" [attr.data-level]="def.code === 'reg' ? level(ticker) : null" [class.up]="direction(ticker, def.code) > 0" [class.down]="direction(ticker, def.code) < 0">{{ value(ticker, def.code) }}</td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
    <app-math-audit-drawer [ticker]="auditTicker()" [focus]="auditFocus()" (closed)="closeAudit()" />
  `,
  styles: `
    .empty { color: #555; }
    /* Calculated values (Risk, Microstructure, Technicals) open the math audit, so they are highlighted on hover. */
    td.calc, dd.calc { cursor: pointer; position: relative; transition: background 0.12s, box-shadow 0.12s; }
    td.calc:hover, dd.calc:hover { background: #fff4c2 !important; box-shadow: inset 0 0 0 2px #e0a800; }
    td.calc[data-hint]:hover::after, dd.calc[data-hint]:hover::after {
      content: attr(data-hint); position: absolute; bottom: 100%; right: 0; margin-bottom: 2px; z-index: 5;
      background: #333; color: #fff; font-size: 0.7rem; padding: 0.1rem 0.4rem; border-radius: 4px;
      white-space: nowrap; pointer-events: none;
    }
    .ticker { font-family: monospace; font-weight: 600; }
    .tier { margin-left: 0.5rem; font-size: 0.8rem; color: #555; }

    .scroll { overflow-x: auto; }
    table { border-collapse: collapse; white-space: nowrap; font-size: 0.9rem; }
    th, td { padding: 0.3rem 0.5rem; border-bottom: 1px solid #e3e3e3; }
    th { background: #f5f5f5; text-align: left; }
    th.band { text-align: center; background: #e8eef8; }
    .num { text-align: right; font-variant-numeric: tabular-nums; }

    .cards { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.6rem; }
    .card { border: 1px solid #ddd; border-radius: 8px; padding: 0.7rem 0.8rem; background: #fff; }
    .card-head { display: flex; justify-content: space-between; align-items: baseline; gap: 0.5rem; }
    .last { font-size: 1.4rem; font-weight: 600; font-variant-numeric: tabular-nums; }
    /* One colour per segment: Quote, Risk, Microstructure, Technicals. */
    .seg-quote { --seg: #1f5fbf; --tint: #eef4fc; }
    .seg-risk { --seg: #b26a00; --tint: #fff7e8; }
    .seg-microstructure { --seg: #0f7f7a; --tint: #e9f8f6; }
    .seg-technicals { --seg: #6b3fb5; --tint: #f4effb; }
    th.band[class*='seg-'] { background: var(--tint); color: var(--seg); border-bottom: 3px solid var(--seg); }
    td[class*='seg-'] { background: var(--tint); }
    section[class*='seg-'] { border-left: 4px solid var(--seg); padding-left: 0.5rem; }
    section[class*='seg-'] h3 { color: var(--seg); }
    .up { color: #1a7f37; }
    .down { color: #c62828; }
    .regime { display: inline-block; margin: 0.4rem 0; padding: 0.1rem 0.5rem; border-radius: 4px; font-size: 0.8rem; background: #e6f4e6; color: #1d5e1d; }
    .regime[data-level='1'] { background: #fff3d6; color: #7a4b00; }
    .regime[data-level='2'] { background: #fde0e0; color: #8a1313; }
    td[data-level='1'] { background: #fff8e8; }
    td[data-level='2'] { background: #fdeeee; }
    h3 { font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.04em; color: #667; margin: 0.7rem 0 0.3rem; }
    dl { display: grid; grid-template-columns: 1fr 1fr; gap: 0.3rem 1rem; margin: 0; }
    .cell { display: flex; justify-content: space-between; gap: 0.5rem; }
    dt { color: #666; font-size: 0.8rem; }
    dd { margin: 0; font-variant-numeric: tabular-nums; font-size: 0.9rem; }
  `,
})
export class WatchComponent {
  protected readonly market = inject(MarketService);
  private readonly universeApi = inject(UniverseApi);
  // The Watch tab shows the picks. The Popular tab (same table) shows the popular list, streamed to this browser.
  private readonly popular = inject(ActivatedRoute).snapshot.data['source'] === 'popular';
  protected readonly tickers = computed(() => (this.popular ? this.market.view() : this.market.picks()));
  protected readonly emptyText = this.popular
    ? 'Loading the popular stocks…'
    : 'Nothing picked yet. Tick stocks on the Universe tab to see their prices here.';
  protected readonly viewport = inject(ViewportService);
  protected readonly fields = FIELDS;
  protected readonly groups = GROUPS;

  constructor() {
    if (this.popular) {
      this.universeApi.search('', 1, 200, 'popular').subscribe({
        next: (page) => this.market.setView(page.rows.map((r) => r.ticker)),
      });
      inject(DestroyRef).onDestroy(() => this.market.setView([]));
    }
  }

  // The ticker whose math audit is open. Null when the drawer is closed.
  protected readonly auditTicker = signal<string | null>(null);
  // The audit card to scroll to, when a marked value was clicked.
  protected readonly auditFocus = signal<string | null>(null);

  // Quote values are display only. Calculated values open the audit at their card.
  protected clickCell(ticker: string, group: FieldGroup, code: string): void {
    if (group === 'Quote') return;
    this.openAudit(ticker, code);
  }

  protected openAudit(ticker: string, code: string | null = null): void {
    this.auditFocus.set(code ? (AUDIT_LABEL_BY_CODE[code] ?? null) : null);
    this.auditTicker.set(ticker);
  }

  protected closeAudit(): void {
    this.auditTicker.set(null);
    this.auditFocus.set(null);
  }

  // Text for one value of one stock, or a dash before the first update.
  protected value(ticker: string, code: string): string {
    const def = FIELDS.find((f) => f.code === code);
    if (def === undefined) return '—';
    return formatValue(def, this.market.quotes().get(ticker)?.[code]);
  }

  // 1 when the field last rose, -1 when it last fell, 0 when it has not changed yet.
  protected direction(ticker: string, code: string): number {
    return this.market.directions().get(ticker)?.[code] ?? 0;
  }

  // The risk regime as 0, 1 or 2, used to colour the badge. Undefined until the server sends it.
  protected level(ticker: string): string | null {
    const value = this.market.quotes().get(ticker)?.['reg'];
    return value === undefined ? null : String(value);
  }

  protected fieldsIn(group: FieldGroup): FieldDef[] {
    return FIELDS.filter((f) => f.group === group);
  }

  protected bodyFields(group: FieldGroup): FieldDef[] {
    return this.fieldsIn(group).filter((f) => !HEAD_CODES.has(f.code));
  }
}
