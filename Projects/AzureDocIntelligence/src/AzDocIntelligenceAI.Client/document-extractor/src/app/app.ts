import { Component, signal } from '@angular/core';
import { DocumentUploader } from './components/document-uploader/document-uploader';


@Component({
  imports: [DocumentUploader],
  selector: 'app-root',
  styleUrl: './app.css',
  template: `<app-document-uploader></app-document-uploader>`
})
export class App {
  protected readonly title = signal('document-extractor');
}
