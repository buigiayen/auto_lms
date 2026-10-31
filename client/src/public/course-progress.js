const escape = value => String(value ?? '').replace(/[&<>"']/g,
  character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
const numberFormat = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 20, useGrouping: false });
const isPassed = value => value === 1 || value === true || value === '1';

function scoreNumber(value) {
  if (typeof value !== 'number' && (typeof value !== 'string' || !value.trim())) return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function* courseItems(items, groups = []) {
  for (const item of items) {
    const children = [item?.children, item?.course?.children]
      .find(value => Array.isArray(value) && value.length > 0);
    const name = typeof item?.name === 'string' ? item.name : item?.course?.name ?? '';
    if (children) {
      yield* courseItems(children, name ? [...groups, name] : groups);
    } else {
      yield { item, name, groups };
    }
  }
}

export function buildCourseProgress(response) {
  const criteria = new Set();
  const items = Array.isArray(response?.result) ? response.result : [];
  const columns = [...courseItems(items)].map(({ item, name, groups }) => {
    const course = item?.course ?? item;
    const expand = course?.__expand ?? course?._expand;
    const rubric = expand?.score_by_rubric ?? expand?.syllabus?.score_by_rubic;
    const entries = Array.isArray(rubric?.score_by_rubrik) ? rubric.score_by_rubrik : [];
    const scores = new Map();
    for (const entry of entries) {
      if (typeof entry?.name !== 'string' || !entry.name.trim()) continue;
      const name = entry.name.trim();
      criteria.add(name);
      if (!scores.has(name)) scores.set(name, {
        score: scoreNumber(entry.score),
        maxScore: scoreNumber(entry.max_score),
        passed: isPassed(entry.passed),
      });
    }
    return { name, groups, scores, passed: isPassed(rubric?.passed) };
  });
  return { columns, criteria: [...criteria] };
}

function columnHeader(column, rowspan = false) {
  return `<th scope="col"${rowspan ? ' rowspan="2"' : ''}${column.passed ? ` class="course-progress-completed" aria-label="${escape(column.name)} · Hoàn thành"` : ''}>${escape(column.name)}</th>`;
}

function columnHeaders(columns) {
  const grouped = columns.some(column => column.groups.length > 0);
  const corner = `<th class="course-progress-corner" scope="col" aria-label="Tiêu chí đánh giá"${grouped ? ' rowspan="2"' : ''}></th>`;
  if (!grouped)
    return `<tr>${corner}${columns.map(column => columnHeader(column)).join('')}</tr>`;
  const headings = [], children = [];
  for (let index = 0; index < columns.length;) {
    const column = columns[index];
    if (!column.groups.length) {
      headings.push(columnHeader(column, true));
      index++;
      continue;
    }
    const key = JSON.stringify(column.groups);
    let end = index + 1;
    while (end < columns.length && JSON.stringify(columns[end].groups) === key) end++;
    headings.push(`<th class="course-progress-group" scope="colgroup" colspan="${end - index}">${escape(column.groups.join(' › '))}</th>`);
    children.push(...columns.slice(index, end).map(child => columnHeader(child)));
    index = end;
  }
  return `<tr>${corner}${headings.join('')}</tr><tr>${children.join('')}</tr>`;
}

export function renderCourseProgress(response) {
  const { columns, criteria } = buildCourseProgress(response);
  if (!columns.length) return '<div class="course-progress-placeholder" role="status">Chương trình chưa có bài học.</div>';
  const rows = criteria.map(name => `<tr><th scope="row">${escape(name)}</th>${columns.map(column => {
    const value = column.scores.get(name);
    const passed = column.passed || value?.passed;
    if (!value || value.score === null || value.maxScore === null)
      return `<td class="course-progress-empty${column.passed ? ' course-progress-completed' : ''}" aria-label="${column.passed ? 'Bài học đã hoàn thành; chưa có dữ liệu điểm' : 'Chưa có dữ liệu'}"></td>`;
    const label = `${numberFormat.format(value.score)} / ${numberFormat.format(value.maxScore)}`;
    return `<td${column.passed ? ' class="course-progress-completed"' : ''}><span class="course-progress-score ${passed ? 'is-passed' : 'is-failed'}" aria-label="${escape(label)} · ${passed ? 'Đạt' : 'Chưa đạt'}">${escape(label)}</span></td>`;
  }).join('')}</tr>`).join('');
  return `<div class="course-progress-scroll" role="region" aria-label="Bảng tiến độ học tập theo bài" tabindex="0"><table class="course-progress-table"><caption>Điểm đạt / Điểm tối đa theo từng tiêu chí và bài học</caption><thead>${columnHeaders(columns)}</thead><tbody>${rows || `<tr><td colspan="${columns.length + 1}" class="course-progress-no-data">Chưa có dữ liệu đánh giá cho các bài học này.</td></tr>`}</tbody></table></div>`;
}
