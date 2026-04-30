export interface JsPhiResponse {
  code: number;
  mime: string;
  data: Buffer;
}

export function init(
  files: string[],
  image_format: string,
  cldb_data: Buffer
): void;

export function reset(): void;

export function call_router(path: string): JsPhiResponse;

export function get_image_formats(): string[];