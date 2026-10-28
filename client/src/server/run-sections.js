import { requireValue } from './errors.js';

export const allRunSections = Object.freeze(['exam', 'review', 'theory', 'practical']);

export function validateRunSections(value) {
  requireValue(Array.isArray(value) && value.length > 0 && value.length <= allRunSections.length
    && value.every(section => allRunSections.includes(section)) && new Set(value).size === value.length,
  'Chọn ít nhất một phần hợp lệ: kiểm tra, ôn luyện, lý thuyết hoặc thực hành.', 'runSections');
  return allRunSections.filter(section => value.includes(section));
}
