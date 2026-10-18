import { TestBed } from '@angular/core/testing';
import { DocumentRead } from './document-read';

describe('DocumentRead', () => {
  let service: DocumentRead;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DocumentRead);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
