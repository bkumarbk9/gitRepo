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


// Shapes of GET /api/audit/{ticker}. The backend builds every number, formula and chart coordinate; the browser only
// displays them.

export interface AuditStep {
  label: string;
  formula: string;
  substituted: string;
  value: string;
}

export interface AuditCard {
  name: string;
  steps: AuditStep[];
}

export interface IntradaySnapshot {
  sessionNumber: number;
  historyDay: number;
  sessionMinute: number;
  secondsIntoMinute: number;
  last: number;
  bid: number;
  ask: number;
  bidSize: number;
  askSize: number;
  previousClose: number;
  dayOpen: number;
  high52w: number;
  low52w: number;
}

export interface IntradaySection {
  title: string;
  snapshot: IntradaySnapshot;
  cards: AuditCard[];
  csvUrl: string;
  note: string;
}

export interface HistogramBin {
  label: string;
  low: number;
  high: number;
  count: number;
  x: number;
  width: number;
  height: number;
}

export interface ChartPoint {
  date: string;
  close: number;
  x: number;
  y: number;
}

export interface ChartMarker {
  name: string;
  date: string;
  price: number;
  x: number;
  y: number;
}

export interface ChartLine {
  name: string;
  price: number;
  y: number;
}

export interface ReturnHistogram {
  bins: HistogramBin[];
  markerScale: number;
  varHistoricalX: number;
  varHistoricalPercent: number;
  varParametricX: number;
  varParametricPercent: number;
}

export interface PriceChart {
  points: ChartPoint[];
  peak: ChartMarker;
  trough: ChartMarker;
  high52w: ChartLine;
  low52w: ChartLine;
}

export interface HistoricalAudit {
  ticker: string;
  firstDate: string;
  lastDate: string;
  closes: number;
  returns: number;
  cards: AuditCard[];
  returnHistogram: ReturnHistogram;
  chart: PriceChart;
  universeMeanBeta: number;
  universeMeanBetaLabel: string;
  universeMeanBetaNote: string;
  excelChecks: string[];
}

export interface HistoricalSection {
  title: string;
  audit: HistoricalAudit;
  csvUrl: string;
}

export interface AuditPayload {
  ticker: string;
  intraday: IntradaySection;
  historical: HistoricalSection;
}
