export class AppError extends Error {
  constructor(status, code, message, details = {}) {
    super(message);
    Object.assign(this, { status, code, details });
  }
}

export function requireValue(condition, message, field) {
  if (!condition) throw new AppError(400, 'VALIDATION_ERROR', message,
    field ? { fields: { [field]: message } } : {});
}

export function pageParams(query, defaultSize = 20, maximum = 100) {
  const page = Number(query.get('page') ?? 1);
  const pageSize = Number(query.get('pageSize') ?? defaultSize);
  requireValue(Number.isInteger(page) && page > 0 && page <= 1_000_000, 'Trang không hợp lệ.', 'page');
  requireValue(Number.isInteger(pageSize) && pageSize > 0 && pageSize <= maximum, `Số dòng mỗi trang phải từ 1–${maximum}.`, 'pageSize');
  return { page, pageSize, offset: (page - 1) * pageSize };
}
