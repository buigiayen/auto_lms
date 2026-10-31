import test from 'node:test';
import assert from 'node:assert/strict';
import { buildCourseProgress, renderCourseProgress } from '../src/public/course-progress.js';

const course = (name, scores, passed) => ({ name, course: { __expand: { score_by_rubric: { score_by_rubrik: scores, passed } } } });
// Relevant scoring fields from the supplied response; no account or staff data.
const sample = { result: [
  course('Đạo đức người lái xe', [
    { name: 'Tiến độ môn học', score: 14, max_score: 0, passed: 1 },
    { name: 'Xem giáo trình', score: 11, max_score: 11, passed: 1 },
    { name: 'Ôn luyện', score: 2, max_score: 2, passed: 1 },
    { name: 'Kiểm tra', score: 1, max_score: 1, passed: 1 },
    { name: 'Điểm kiểm tra', score: 80, passed: 1 },
  ]),
  course('Kỹ thuật lái xe ô tô', [
    { name: 'Kiểm tra', score: 0, max_score: 1, passed: 0 },
    { name: 'Xem giáo trình', score: 13, max_score: 13, passed: 1 },
    { name: 'Tiến độ môn học', score: 19, max_score: 0, passed: 1 },
  ]),
  course('Cấu tạo sửa chữa', [
    { name: 'Tiến độ môn học', score: 5.98, max_score: 0, passed: 1 },
    { name: 'Xem giáo trình', score: 5.48, max_score: 7, passed: 0 },
    { name: 'Ôn luyện', score: 0.5, max_score: 0.5, passed: 1 },
  ]),
  { name: 'Pháp luật giao thông đường bộ', course: null, children: [course('Bài con', [])] },
  course('Mô phỏng các tình huống giao thông', [
    { name: 'Ôn luyện', score: 0, max_score: 4, passed: 0 },
  ]),
] };

test('progress matrix expands child columns and aligns criteria by name across courses', () => {
  const { columns, criteria } = buildCourseProgress(sample);
  assert.deepEqual(columns.map(column => column.name), [
    'Đạo đức người lái xe', 'Kỹ thuật lái xe ô tô', 'Cấu tạo sửa chữa', 'Bài con', 'Mô phỏng các tình huống giao thông',
  ]);
  assert.deepEqual(columns[3].groups, ['Pháp luật giao thông đường bộ']);
  assert.deepEqual(criteria, ['Tiến độ môn học', 'Xem giáo trình', 'Ôn luyện', 'Kiểm tra', 'Điểm kiểm tra']);
  assert.deepEqual(columns[1].scores.get('Kiểm tra'), { score: 0, maxScore: 1, passed: false });
  assert.deepEqual(columns[2].scores.get('Ôn luyện'), { score: 0.5, maxScore: 0.5, passed: true });
  assert.equal(columns[3].scores.size, 0, 'a child without scores remains a blank column');
  assert.equal(columns[4].scores.get('Tiến độ môn học'), undefined);
  const html = renderCourseProgress(sample);
  const rows = [...html.matchAll(/<tr><th scope="row">([^<]*)<\/th>(.*?)<\/tr>/g)];
  assert.equal(rows.length, criteria.length);
  for (const [, name, cells] of rows) {
    const values = [...cells.matchAll(/<td\b[^>]*>(.*?)<\/td>/g)].map(match => match[1]);
    assert.equal(values.length, 5, name);
    assert.equal(values[3], '', 'missing child course data stays blank in every row');
    if (name === 'Kiểm tra') {
      assert.match(values[0], /is-passed.*1 \/ 1/);
      assert.match(values[1], /is-failed.*0 \/ 1/);
      assert.equal(values[2], '');
    }
    if (name === 'Điểm kiểm tra') assert.ok(values.every(value => value === ''), 'missing max_score is not invented');
  }
  assert.match(html, /14 \/ 0/, 'zero denominators from LMS are preserved');
  assert.match(html, /5,48 \/ 7/);
  assert.match(html, /0,5 \/ 0,5/);
});

test('group children replace the parent course and expose a shared group heading', () => {
  const response = { result: [
    course('Bài ngoài nhóm', [{ name: 'Ôn luyện', score: 1, max_score: 2, passed: 0 }]),
    { ...course('Pháp luật giao thông đường bộ', [{ name: 'Điểm cha', score: 99, max_score: 100, passed: 1 }]), children: [
      course('Phần 1. Luật giao thông', [{ name: 'Ôn luyện', score: 2, max_score: 2, passed: 1 }]),
      course('Phần 2. Báo hiệu đường bộ', [{ name: 'Ôn luyện', score: 0, max_score: 3, passed: 0 }]),
      course('Phần 3. Xử lý tình huống', [{ name: 'Kiểm tra', score: 0.5, max_score: 1, passed: 0 }]),
    ] },
    course('Bài cuối', []),
  ] };
  const original = JSON.stringify(response);
  const { columns, criteria } = buildCourseProgress(response);
  assert.deepEqual(columns.map(column => column.name), [
    'Bài ngoài nhóm', 'Phần 1. Luật giao thông', 'Phần 2. Báo hiệu đường bộ', 'Phần 3. Xử lý tình huống', 'Bài cuối',
  ]);
  assert.deepEqual(criteria, ['Ôn luyện', 'Kiểm tra'], 'parent rubric data is replaced by child data');
  assert.deepEqual(columns.slice(1, 4).map(column => column.groups), Array(3).fill(['Pháp luật giao thông đường bộ']));
  assert.equal(columns[1].scores.get('Ôn luyện').score, 2);
  assert.equal(columns[2].scores.get('Ôn luyện').passed, false);
  assert.equal(columns[3].scores.get('Ôn luyện'), undefined);
  const html = renderCourseProgress(response);
  assert.match(html, /scope="colgroup" colspan="3">Pháp luật giao thông đường bộ<\/th>/);
  assert.match(html, /scope="col" rowspan="2">Bài ngoài nhóm<\/th>/);
  assert.match(html, /scope="col" rowspan="2">Bài cuối<\/th>/);
  assert.match(html, /<th scope="col">Phần 2\. Báo hiệu đường bộ<\/th>/);
  assert.ok(!html.includes('Điểm cha'));
  assert.equal(JSON.stringify(response), original, 'rendering does not mutate the LMS response');
});

test('nested groups, repeated child names and course.children keep their own course scores', () => {
  const score = (value, passed = 0) => [{ name: 'Tiến độ', score: value, max_score: 10, passed }];
  const rawCourse = course('Bài từ course.children', score(4)).course;
  const response = { result: [
    { name: 'Nhóm A', children: [{ name: 'Nhóm con', children: [course('Bài trùng tên', score(2))] }] },
    { name: 'Nhóm B', children: [course('Bài trùng tên', score(3))] },
    { name: 'Nhóm C', course: { children: [{ name: 'Bài từ course.children', ...rawCourse }] } },
    { ...course('Bài không có con', score(10, 1)), children: [] },
    { name: 'Nhóm rỗng', children: [] },
  ] };
  const { columns, criteria } = buildCourseProgress(response);
  assert.equal(columns.length, 5);
  assert.deepEqual(criteria, ['Tiến độ']);
  assert.deepEqual(columns.map(column => column.groups), [['Nhóm A', 'Nhóm con'], ['Nhóm B'], ['Nhóm C'], [], []]);
  assert.deepEqual(columns.slice(0, 4).map(column => column.scores.get('Tiến độ').score), [2, 3, 4, 10]);
  assert.equal(columns[4].scores.size, 0);
  assert.match(renderCourseProgress(response), /Nhóm A › Nhóm con/);
});

test('cell status follows passed independently of score; incomplete and invalid scores stay blank', () => {
  const html = renderCourseProgress({ result: [course('Bài 1', [
    { name: 'Đạt', score: '0', max_score: '10', passed: '1' },
    { name: 'Chưa đạt', score: 10, max_score: 10, passed: 0 },
    { name: 'Boolean', score: 1, max_score: 1, passed: true },
    ...[null, '', 'invalid', false, [], Infinity].map((score, index) => ({ name: `Thiếu ${index}`, score, max_score: 10, passed: 1 })),
    { name: 'Thiếu điểm tối đa', score: 3, max_score: null, passed: 0 },
    { score: 3, max_score: 5, passed: 0 }, null,
  ])] });
  assert.equal((html.match(/class="course-progress-score is-passed"/g) ?? []).length, 2);
  assert.equal((html.match(/class="course-progress-score is-failed"/g) ?? []).length, 1);
  assert.equal((html.match(/class="course-progress-empty"/g) ?? []).length, 7);
  assert.match(html, /0 \/ 10 · Đạt/);
  assert.match(html, /10 \/ 10 · Chưa đạt/);
});

test('a completed course makes its entire column green regardless of individual criteria', () => {
  for (const passed of [1, true, '1']) {
    const response = { result: [
      course('Bài hoàn thành', [
        { name: 'Chưa đạt riêng', score: 0, max_score: 10, passed: 0 },
        { name: 'Không có trạng thái', score: 2, max_score: 10 },
        { name: 'Thiếu điểm tối đa', score: 80, passed: 0 },
        { name: 'Thiếu điểm', score: null, max_score: 10, passed: 0 },
      ], passed),
      course('Bài chưa hoàn thành', [
        { name: 'Chưa đạt riêng', score: 0, max_score: 10, passed: 0 },
        { name: 'Chỉ có ở bài khác', score: 1, max_score: 1, passed: 1 },
      ], 0),
    ] };
    assert.equal(buildCourseProgress(response).columns[0].passed, true);
    const html = renderCourseProgress(response);
    assert.match(html, /<th scope="col" class="course-progress-completed"[^>]*>Bài hoàn thành<\/th>/);
    const rows = [...html.matchAll(/<tr><th scope="row">([^<]*)<\/th>(.*?)<\/tr>/g)];
    assert.equal(rows.length, 5);
    for (const [, name, cells] of rows) {
      const [completed, pending] = [...cells.matchAll(/<td\b([^>]*)>(.*?)<\/td>/g)];
      assert.ok(completed[1].includes('course-progress-completed'), name);
      assert.ok(!completed[2].includes('is-failed'), 'criterion failure cannot override course completion');
      assert.ok(!pending[1].includes('course-progress-completed'), 'completion does not leak into other columns');
      if (name === 'Chưa đạt riêng') {
        assert.match(completed[2], /is-passed.*0 \/ 10/);
        assert.match(pending[2], /is-failed.*0 \/ 10/);
      }
      if (['Thiếu điểm tối đa', 'Thiếu điểm', 'Chỉ có ở bài khác'].includes(name))
        assert.equal(completed[2], '', 'completed columns keep missing values blank');
    }
  }
});

test('course completion supports the alternate rubric path and remains specific to each child', () => {
  const childScores = [{ name: 'Kiểm tra', score: 0, max_score: 1, passed: 0 }];
  const response = { result: [
    { ...course('Nhóm đã đạt', [], 1), children: [
      course('Bài con đã đạt', childScores, 1),
      course('Bài con chưa đạt', childScores, 0),
    ] },
    { name: 'Bài dùng đường dẫn khác', course: { _expand: { syllabus: { score_by_rubic: {
      passed: 1, score_by_rubrik: childScores,
    } } } } },
  ] };
  const { columns } = buildCourseProgress(response);
  assert.deepEqual(columns.map(column => column.passed), [true, false, true]);
  const html = renderCourseProgress(response);
  assert.match(html, /<th scope="col" class="course-progress-completed"[^>]*>Bài con đã đạt<\/th>/);
  assert.match(html, /<th scope="col">Bài con chưa đạt<\/th>/);
  assert.match(html, /<th scope="col" rowspan="2" class="course-progress-completed"[^>]*>Bài dùng đường dẫn khác<\/th>/);
  assert.equal((html.match(/course-progress-score is-passed/g) ?? []).length, 2);
  assert.equal((html.match(/course-progress-score is-failed/g) ?? []).length, 1);
  for (const passed of [0, '0', false, undefined, null, 'true', 2]) {
    const pending = { result: [course('Chưa đạt', childScores, passed)] };
    assert.equal(buildCourseProgress(pending).columns[0].passed, false);
    assert.ok(!renderCourseProgress(pending).includes('course-progress-completed'));
  }
});

test('progress matrix handles empty responses, incomplete structures and the supplied alternate path', () => {
  for (const response of [null, {}, { result: null }, { result: {} }, { result: [] }]) {
    assert.deepEqual(buildCourseProgress(response), { columns: [], criteria: [] });
    assert.match(renderCourseProgress(response), /Chương trình chưa có bài học/);
  }
  const incomplete = renderCourseProgress({ result: [null, { name: 'Bài trống' }, { name: 'Bài khác', course: {} }] });
  assert.match(incomplete, /Bài trống/);
  assert.match(incomplete, /Bài khác/);
  assert.match(incomplete, /Chưa có dữ liệu đánh giá/);
  assert.match(incomplete, /colspan="4"/);
  const alternate = { result: [{ name: 'Bài 1', course: { _expand: { syllabus: { score_by_rubic: {
    score_by_rubrik: [{ name: 'Tiến độ môn học', score: 2, max_score: 3, passed: 0 }],
  } } } } }] };
  assert.match(renderCourseProgress(alternate), /2 \/ 3 · Chưa đạt/);
});

test('course names and rubric labels are escaped instead of executing LMS markup', () => {
  const html = renderCourseProgress({ result: [course('<img src=x onerror="alert(1)">', [
    { name: '<script>alert(2)</script>', score: 1, max_score: 2, passed: 0 },
  ])] });
  assert.ok(!html.includes('<img') && !html.includes('<script>'));
  assert.ok(html.includes('&lt;img src=x onerror=&quot;alert(1)&quot;&gt;'));
  assert.ok(html.includes('&lt;script&gt;alert(2)&lt;/script&gt;'));
  const grouped = renderCourseProgress({ result: [{ name: '<img src=x>', children: [
    course('<script>child()</script>', [{ name: 'Điểm', score: 1, max_score: 2, passed: 0 }]),
  ] }] });
  assert.ok(!grouped.includes('<img') && !grouped.includes('<script>'));
  assert.ok(grouped.includes('&lt;img src=x&gt;') && grouped.includes('&lt;script&gt;child()&lt;/script&gt;'));
});
