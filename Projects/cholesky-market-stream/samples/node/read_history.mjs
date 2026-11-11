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

// Reads data/market_history.bin (or a path given as the first argument) using the layout in DATA_FORMAT.md.
//   node samples/node/read_history.mjs [path]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const path = process.argv[2] ?? join(here, '..', '..', 'data', 'market_history.bin');

const file = readFileSync(path);
const view = new DataView(file.buffer, file.byteOffset, file.byteLength);

// Header, little-endian (bytes 0..31).
const magic = file.toString('latin1', 0, 8);
if (magic !== 'MKTSIM01') throw new Error(`bad magic: ${magic}`);
const numStocks = view.getUint32(8, true);
const numDays = view.getUint32(12, true);
const startEpochSec = view.getBigInt64(16, true);
const seed = view.getBigUint64(24, true);

// Symbol table: 16-byte null-padded ASCII fields, starting at byte 32.
const symbols = [];
for (let i = 0; i < numStocks; i++) {
  const start = 32 + 16 * i;
  const end = file.indexOf(0, start);
  symbols.push(file.toString('latin1', start, end > start && end < start + 16 ? end : start + 16));
}

// Price matrix: float32 little-endian, row-major, starting at byte 32 + 16 * N.
const matrixOffset = 32 + 16 * numStocks;
const expectedSize = matrixOffset + 4 * numStocks * numDays;
if (file.byteLength !== expectedSize) throw new Error(`size ${file.byteLength}, expected ${expectedSize}`);

// Float32Array needs a byte offset that is a multiple of 4. Copy into an aligned buffer if it is not.
let matrixBytes = file.subarray(matrixOffset);
if (matrixBytes.byteOffset % 4 !== 0) matrixBytes = Buffer.from(matrixBytes);
const prices = new Float32Array(matrixBytes.buffer, matrixBytes.byteOffset, numStocks * numDays);

// The stock's row is a zero-copy view of the matrix.
const row = (stock) => prices.subarray(stock * numDays, (stock + 1) * numDays);

console.log(`stocks=${numStocks} days=${numDays} start=${startEpochSec} seed=${seed}`);
for (let i = 0; i < Math.min(3, numStocks); i++) {
  const r = row(i);
  console.log(`${symbols[i].padEnd(6)} day0=${r[0].toFixed(4)} day1=${r[1].toFixed(4)} last=${r[numDays - 1].toFixed(4)}`);
}
