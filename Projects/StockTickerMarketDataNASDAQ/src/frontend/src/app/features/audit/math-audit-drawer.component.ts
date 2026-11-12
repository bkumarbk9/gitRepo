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

import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, effect, inject, input, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { AuditApi } from '../../core/audit.api';
import { AuditPayload } from '../../core/audit.model';
import { ViewportService } from '../../core/viewport.service';
import { AuditCardComponent } from './audit-card.component';
import { environment } from '../../../environments/environment';

// Chart box sizes, in SVG units. The backend gives every position as 0..1 of the box; these only stretch them.
const HIST_W = 600;
const HIST_H = 180;
const PRICE_W = 600;
const PRICE_H = 220;

// Slide-over drawer opened from a table row. On a phone it covers the screen; on a desktop it sits at the right edge.
@Component({
  selector: 'app-math-audit-drawer',
  imports: [AuditCardComponent, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (ticker(); as t) {
      <div class="scrim" (click)="close()"></div>
      <aside class="drawer" [class.mobile]="viewport.isMobile()" role="dialog" [attr.aria-label]="'Math audit for ' + t">
        <header>
          <div>
            <h2>{{ t }} · Math audit</h2>
            <span class="badge">Simulated Data — Live Math Audit</span>
            @if (payload(); as p) {
              <span class="pill">Universe mean beta = {{ p.historical.audit.universeMeanBetaLabel }}</span>
            }
          </div>
          <button type="button" class="close" (click)="close()" aria-label="Close audit">Close</button>
        </header>

        @if (error(); as message) {
          <p class="error">{{ message }}</p>
        } @else if (payload(); as p) {
          <!-- Section 1: the live session, matching the table. -->
          <section data-section="intraday">
            <h3>{{ p.intraday.title }}</h3>
            <p class="note">{{ p.intraday.note }}</p>
            <p class="snapshot">
              Session {{ p.intraday.snapshot.sessionNumber }} · minute {{ p.intraday.snapshot.sessionMinute }} ·
              last {{ p.intraday.snapshot.last | number: '1.2-4' }} · bid {{ p.intraday.snapshot.bid | number: '1.2-4' }} ·
              ask {{ p.intraday.snapshot.ask | number: '1.2-4' }}
            </p>
            @for (card of p.intraday.cards; track card.name) {
              <app-audit-card [card]="card" [focused]="focus()" />
            }
            <a class="csv" [href]="apiBase + p.intraday.csvUrl" download>Download intraday CSV</a>
          </section>

          <!-- Section 2: the 504-day history baseline. -->
          <section>
            <h3>{{ p.historical.title }}</h3>
            <p class="note">
              {{ p.historical.audit.firstDate }} to {{ p.historical.audit.lastDate }} · {{ p.historical.audit.closes }} closes ·
              {{ p.historical.audit.returns }} daily returns
            </p>
            @for (card of p.historical.audit.cards; track card.name) {
              <app-audit-card [card]="card" />
            }

            <h4 class="chart-title">Return distribution (30 bins)</h4>
            <svg [attr.viewBox]="'0 0 ' + histW + ' ' + histH" class="chart" role="img" aria-label="Histogram of daily returns">
              @for (b of p.historical.audit.returnHistogram.bins; track b.label) {
                <rect class="bar"
                      [attr.x]="b.x * histW + 1" [attr.y]="(1 - b.height) * histH"
                      [attr.width]="b.width * histW - 2" [attr.height]="b.height * histH">
                  <title>{{ b.label }}: {{ b.count }} days</title>
                </rect>
              }
              <line class="marker var-hist"
                    [attr.x1]="p.historical.audit.returnHistogram.varHistoricalX * histW"
                    [attr.x2]="p.historical.audit.returnHistogram.varHistoricalX * histW" y1="0" [attr.y2]="histH" />
              <line class="marker var-param"
                    [attr.x1]="p.historical.audit.returnHistogram.varParametricX * histW"
                    [attr.x2]="p.historical.audit.returnHistogram.varParametricX * histW" y1="0" [attr.y2]="histH" />
            </svg>
            <p class="legend">
              <span class="red">Red: historical 95% VaR {{ p.historical.audit.returnHistogram.varHistoricalPercent | number: '1.2-2' }}%</span>
              · <span class="blue">Blue: parametric 95% VaR {{ p.historical.audit.returnHistogram.varParametricPercent | number: '1.2-2' }}%</span>
            </p>

            <h4 class="chart-title">2-year close with drawdown and 52-week range</h4>
            <svg [attr.viewBox]="'0 0 ' + priceW + ' ' + priceH" class="chart" role="img" aria-label="Price history">
              <rect class="drawdown"
                    [attr.x]="shadeX(p) * priceW" [attr.width]="shadeWidth(p) * priceW" y="0" [attr.height]="priceH" />
              <polyline class="price" [attr.points]="pricePoints(p)" />
              <line class="range high" x1="0" [attr.x2]="priceW"
                    [attr.y1]="(1 - p.historical.audit.chart.high52w.y) * priceH"
                    [attr.y2]="(1 - p.historical.audit.chart.high52w.y) * priceH" />
              <line class="range low" x1="0" [attr.x2]="priceW"
                    [attr.y1]="(1 - p.historical.audit.chart.low52w.y) * priceH"
                    [attr.y2]="(1 - p.historical.audit.chart.low52w.y) * priceH" />
              <circle class="dd-point"
                      [attr.cx]="p.historical.audit.chart.peak.x * priceW"
                      [attr.cy]="(1 - p.historical.audit.chart.peak.y) * priceH" r="4">
                <title>{{ p.historical.audit.chart.peak.name }} {{ p.historical.audit.chart.peak.date }} at {{ p.historical.audit.chart.peak.price | number: '1.2-4' }}</title>
              </circle>
              <circle class="dd-point"
                      [attr.cx]="p.historical.audit.chart.trough.x * priceW"
                      [attr.cy]="(1 - p.historical.audit.chart.trough.y) * priceH" r="4">
                <title>{{ p.historical.audit.chart.trough.name }} {{ p.historical.audit.chart.trough.date }} at {{ p.historical.audit.chart.trough.price | number: '1.2-4' }}</title>
              </circle>
            </svg>
            <p class="legend">Shaded: peak to trough. Dashed: 52-week high (green) and low (red). {{ p.historical.audit.universeMeanBetaNote }}</p>

            <details class="excel">
              <summary>Check in Excel (open the CSV)</summary>
              <ul>
                @for (f of p.historical.audit.excelChecks; track f) {
                  <li><code>{{ f }}</code></li>
                }
              </ul>
            </details>
            <a class="csv" [href]="apiBase + p.historical.csvUrl" download>Download 504-day CSV</a>
          </section>
        } @else {
          <p class="muted">Loading the audit…</p>
        }
      </aside>
    }
  `,
  styles: `
    .scrim { position: fixed; inset: 0; background: rgba(0, 0, 0, 0.3); z-index: 10; }
    .drawer { box-sizing: border-box; position: fixed; top: 0; right: 0; bottom: 0; width: min(640px, 100vw); background: #fff; z-index: 11;
              overflow-y: auto; padding: 0.9rem 1rem 2rem; box-shadow: -4px 0 16px rgba(0, 0, 0, 0.2); }
    .drawer.mobile { left: 0; width: 100vw; }
    header { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 0.5rem 0.75rem; align-items: flex-start; margin-bottom: 0.6rem;
             position: sticky; top: -0.9rem; background: #fff; z-index: 2; padding: 0.9rem 0 0.5rem; }
    h2 { margin: 0 0 0.3rem; font-size: 1.1rem; }
    h3 { margin: 0.9rem 0 0.3rem; font-size: 1rem; }
    .chart-title { margin: 0.9rem 0 0.3rem; font-size: 0.9rem; }
    .badge { display: inline-block; background: #fff3d6; color: #7a4b00; border-radius: 4px; padding: 0.1rem 0.45rem; font-size: 0.78rem; }
    .pill { display: inline-block; background: #e6f4e6; color: #1d5e1d; border-radius: 999px; padding: 0.1rem 0.6rem; font-size: 0.78rem; margin-left: 0.3rem; }
    header > div { min-width: 0; flex: 1 1 12rem; }
    .close { flex: none; min-height: 2.4rem; min-width: 4rem; }
    .note, .snapshot, .legend, .muted { color: #555; font-size: 0.85rem; }
    .error { color: #a40000; }
    .chart { width: 100%; height: auto; background: #fbfbfb; border: 1px solid #eee; border-radius: 6px; }
    .bar { fill: #7aa7d9; }
    .marker { stroke-width: 2; stroke-dasharray: 4 3; }
    .var-hist { stroke: #c62828; }
    .var-param { stroke: #1f5fbf; }
    .red { color: #c62828; }
    .blue { color: #1f5fbf; }
    .drawdown { fill: rgba(198, 40, 40, 0.15); }
    .price { fill: none; stroke: #1f5fbf; stroke-width: 1.5; }
    .range { stroke-dasharray: 5 4; stroke-width: 1; }
    .range.high { stroke: #1a7f37; }
    .range.low { stroke: #c62828; }
    .dd-point { fill: #c62828; }
    .csv { display: inline-block; margin-top: 0.6rem; padding: 0.4rem 0.7rem; border: 1px solid #1f5fbf; border-radius: 6px; color: #1f5fbf; text-decoration: none; }
    .excel code { font-size: 0.8rem; }
  `,
})
export class MathAuditDrawerComponent {
  // The ticker to audit. Null hides the drawer.
  readonly ticker = input<string | null>(null);
  readonly closed = output<void>();

  // The audit card to scroll to, by its label. Null shows the top of the drawer.
  readonly focus = input<string | null>(null);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly viewport = inject(ViewportService);
  // Prefix for the CSV links, which the server builds as /api paths.
  protected readonly apiBase = environment.apiBase;
  private readonly api = inject(AuditApi);
  protected readonly payload = signal<AuditPayload | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly histW = HIST_W;
  protected readonly histH = HIST_H;
  protected readonly priceW = PRICE_W;
  protected readonly priceH = PRICE_H;

  private subscription: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subscription?.unsubscribe());
    effect(() => {
      const label = this.focus();
      if (!label || !this.payload()) return;
      setTimeout(() => this.scrollToStep(label), 60);
    });
    effect(() => {
      const t = this.ticker();
      this.subscription?.unsubscribe();
      this.payload.set(null);
      this.error.set(null);
      if (!t) return;
      this.subscription = this.api.get(t).subscribe({
        next: (p) => this.payload.set(p),
        error: () => this.error.set(`No audit for ${t}. Is the server running, and is the ticker in the history file?`),
      });
    });
  }

  // Scrolls to the derivation card for the clicked value. The card is highlighted by the template.
  private scrollToStep(label: string): void {
    const el = this.host.nativeElement.querySelector<HTMLElement>(`[data-section="intraday"] [data-label="${CSS.escape(label)}"]`);
    if (!el) return;
    el.scrollIntoView({ behavior: "smooth", block: "center" });
  }

  close(): void {
    this.closed.emit();
  }

  // Left edge of the shaded peak-to-trough region, as a fraction of the chart width.
  protected shadeX(p: AuditPayload): number {
    const c = p.historical.audit.chart;
    return Math.min(c.peak.x, c.trough.x);
  }

  // Width of the shaded region, as a fraction of the chart width.
  protected shadeWidth(p: AuditPayload): number {
    const c = p.historical.audit.chart;
    return Math.abs(c.trough.x - c.peak.x);
  }

  // The polyline points, in SVG units.
  protected pricePoints(p: AuditPayload): string {
    return p.historical.audit.chart.points
      .map((q) => `${(q.x * PRICE_W).toFixed(2)},${((1 - q.y) * PRICE_H).toFixed(2)}`)
      .join(' ');
  }
}
