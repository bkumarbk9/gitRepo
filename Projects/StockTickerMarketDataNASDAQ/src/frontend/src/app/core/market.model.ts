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

// One common stock from GET /api/universe.
export interface UniverseRow {
  ticker: string;
  name: string;
}

export interface UniversePage {
  total: number;
  page: number;
  pageSize: number;
  rows: UniverseRow[];
}

// GET /api/status. Holds no account details.
export interface ServiceStatus {
  feed: string;
  universe: number;
  priced: number;
  clients: number;
  picked: number;
  session: number;
  sessionMinute: number;
}

export type FieldGroup = 'Quote' | 'Risk' | 'Microstructure' | 'Technicals';
export type FieldKind = 'price' | 'size' | 'pct' | 'signed' | 'num' | 'ratio' | 'bps' | 'score' | 'tier' | 'regime';

export interface FieldDef {
  code: string;
  label: string;
  kind: FieldKind;
  group: FieldGroup;
}

// Every field the server sends, in display order within its group. Codes match the server's wire codes.
export const FIELDS: readonly FieldDef[] = [
  { code: 'p', label: 'Last', kind: 'price', group: 'Quote' },
  { code: 'b', label: 'Bid', kind: 'price', group: 'Quote' },
  { code: 'a', label: 'Ask', kind: 'price', group: 'Quote' },
  { code: 'bs', label: 'Bid size', kind: 'size', group: 'Quote' },
  { code: 'as', label: 'Ask size', kind: 'size', group: 'Quote' },
  { code: 'ls', label: 'Last size', kind: 'size', group: 'Quote' },
  { code: 'o', label: 'Open', kind: 'price', group: 'Quote' },
  { code: 'h', label: 'High', kind: 'price', group: 'Quote' },
  { code: 'l', label: 'Low', kind: 'price', group: 'Quote' },
  { code: 'c', label: 'Prev close', kind: 'price', group: 'Quote' },
  { code: 'v', label: 'Volume', kind: 'size', group: 'Quote' },
  { code: 'tier', label: 'Tier', kind: 'tier', group: 'Quote' },

  { code: 'reg', label: 'Risk', kind: 'regime', group: 'Risk' },
  { code: 'vol', label: 'Vol (ann.)', kind: 'pct', group: 'Risk' },
  { code: 'var', label: 'VaR 1-day 95%', kind: 'pct', group: 'Risk' },
  { code: 'beta', label: 'Beta', kind: 'num', group: 'Risk' },
  { code: 'shp', label: 'Sharpe', kind: 'num', group: 'Risk' },
  { code: 'mdd', label: 'Max drawdown', kind: 'pct', group: 'Risk' },

  { code: 'obi', label: 'Buy pressure %', kind: 'score', group: 'Microstructure' },
  { code: 'rvol', label: 'Rel. volume', kind: 'ratio', group: 'Microstructure' },
  { code: 'slip', label: 'Slippage $100k', kind: 'bps', group: 'Microstructure' },
  { code: 'vwap', label: 'VWAP', kind: 'price', group: 'Microstructure' },
  { code: 'vwd', label: 'vs VWAP', kind: 'signed', group: 'Microstructure' },

  { code: 'rsi', label: 'RSI(14)', kind: 'score', group: 'Technicals' },
  { code: 'atr', label: 'ATR(14)', kind: 'price', group: 'Technicals' },
  { code: 'hi52', label: '52w high', kind: 'price', group: 'Technicals' },
  { code: 'lo52', label: '52w low', kind: 'price', group: 'Technicals' },
  { code: 'pos52', label: '52w position', kind: 'pct', group: 'Technicals' },
];

export const GROUPS: readonly FieldGroup[] = ['Quote', 'Risk', 'Microstructure', 'Technicals'];

export const TIER_LABELS = ['Large cap', 'Mid cap', 'Small cap', 'Penny'] as const;
export const REGIME_LABELS = ['Low', 'Elevated', 'High risk'] as const;

// The server's protobuf enum name for each field, and the short code the labels and FIELDS table use.
export const WIRE_CODES: Readonly<Record<string, string>> = {
  BID_PRICE: 'b',
  ASK_PRICE: 'a',
  LAST_PRICE: 'p',
  HIGH: 'h',
  LOW: 'l',
  OPEN: 'o',
  PREVIOUS_CLOSE: 'c',
  BID_SIZE: 'bs',
  ASK_SIZE: 'as',
  LAST_SIZE: 'ls',
  VOLUME: 'v',
  REALIZED_VOL_PERCENT: 'vol',
  VAR95_PERCENT: 'var',
  BETA: 'beta',
  SHARPE: 'shp',
  MAX_DRAWDOWN_PERCENT: 'mdd',
  RISK_REGIME: 'reg',
  BUY_PRESSURE_PERCENT: 'obi',
  RELATIVE_VOLUME: 'rvol',
  SLIPPAGE_BPS_100K: 'slip',
  VWAP: 'vwap',
  VWAP_DISTANCE_PERCENT: 'vwd',
  RSI14: 'rsi',
  ATR14: 'atr',
  HIGH52: 'hi52',
  LOW52: 'lo52',
  POSITION52_PERCENT: 'pos52',
  TIER: 'tier',
};

const PRICE_2 = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const PRICE_4 = new Intl.NumberFormat('en-US', { minimumFractionDigits: 4, maximumFractionDigits: 4 });
const SIZE = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });

// The text shown for one value. Undefined means the server has not sent it yet, so it shows a dash.
export function formatValue(def: FieldDef, value: number | undefined): string {
  if (value === undefined) return '—';
  switch (def.kind) {
    case 'price':
      return (value < 1 ? PRICE_4 : PRICE_2).format(value);
    case 'size':
      return SIZE.format(value);
    case 'pct':
      return `${value.toFixed(2)}%`;
    case 'signed':
      return `${value > 0 ? '+' : ''}${value.toFixed(2)}%`;
    case 'num':
      return value.toFixed(2);
    case 'ratio':
      return `${value.toFixed(2)}×`;
    case 'bps':
      return `${value.toFixed(1)} bps`;
    case 'score':
      return value.toFixed(1);
    case 'tier':
      return TIER_LABELS[value] ?? '—';
    case 'regime':
      return REGIME_LABELS[value] ?? '—';
  }
}
