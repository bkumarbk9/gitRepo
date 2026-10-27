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

import { Component, inject, signal } from '@angular/core';
import { JsonPipe } from '@angular/common';
import { DocumentRead } from '../../services/document-read';

@Component({
  imports: [JsonPipe],
  selector: 'app-document-uploader',
  styleUrl: './document-uploader.css',
  templateUrl: './document-uploader.html',
})
export class DocumentUploader {
  // Inject the functional service layer
  private documentService = inject(DocumentRead);

  // Signals notify zoneless change detection when values change
  selectedFile = signal<File | null>(null);
  extractedJson = signal<any>(null);
  isLoading = signal(false);
  errorMessage = signal('');

  // Bound to the native (change) input element event
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile.set(input.files[0]);
      this.errorMessage.set('');
    }
  }

  // Bound to the element (click) handler
  processDocument(): void {
    const file = this.selectedFile();
    if (!file) return;

    this.isLoading.set(true);
    this.extractedJson.set(null);
    this.errorMessage.set('');

    // Streams requests to the network and reacts to stream changes
    this.documentService.uploadAndConvert(file).subscribe({
      next: (response) => {
        this.extractedJson.set(response);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error(err);
        this.errorMessage.set('Backend extraction process failed.');
        this.isLoading.set(false);
      }
    });
  }

}
