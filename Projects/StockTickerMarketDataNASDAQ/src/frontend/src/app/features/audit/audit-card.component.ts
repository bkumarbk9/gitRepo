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


import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AuditCard } from '../../core/audit.model';

// One derivation card: each line shows the formula, the numbers plugged in, and the displayed result.
@Component({
  selector: 'app-audit-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="card">
      <h4>{{ card().name }}</h4>
      @for (step of card().steps; track step.label) {
        <div class="step" [attr.data-label]="step.label" [class.focused]="step.label === focused()">
          <div class="head">
            <span class="label">{{ step.label }}</span>
            <span class="value">{{ step.value }}</span>
          </div>
          <div class="formula">{{ step.formula }}</div>
          <div class="plugged">{{ step.substituted }}</div>
        </div>
      }
    </section>
  `,
  styles: `
    .card { border: 1px solid #ddd; border-radius: 8px; padding: 0.6rem 0.8rem; margin-bottom: 0.7rem; background: #fff; }
    h4 { margin: 0 0 0.4rem; font-size: 0.85rem; text-transform: uppercase; letter-spacing: 0.04em; color: #556; }
    .step { border-top: 1px solid #eee; padding: 0.45rem 0; border-radius: 4px; }
    .step.focused { background: #fff4c2; box-shadow: inset 3px 0 0 #e0a800; }
    .head { display: flex; justify-content: space-between; gap: 0.5rem; }
    .label { font-weight: 600; }
    .value { font-variant-numeric: tabular-nums; font-weight: 600; }
    .formula { color: #556; font-size: 0.82rem; margin-top: 0.15rem; }
    .plugged { font-family: monospace; font-size: 0.8rem; color: #222; word-break: break-word; }
  `,
})
export class AuditCardComponent {
  readonly card = input.required<AuditCard>();
  // The line the user clicked. It stays highlighted until another line is clicked or the drawer closes.
  readonly focused = input<string | null>(null);
}
