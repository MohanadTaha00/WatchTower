import { el, plural } from './ui.js';

const SVG_NS = 'http://www.w3.org/2000/svg';
const WIDTH = 900;
const HEIGHT = 240;
const PAD = { top: 14, right: 14, bottom: 28, left: 58 };

const RANGE_LABELS = { '24h': 'last 24 hours', '7d': 'last 7 days', '30d': 'last 30 days' };
const WINDOW_LABELS = { '24h': 'Uptime 24h', '7d': 'Uptime 7d', '30d': 'Uptime 30d' };
const LABEL_EVERY = { '24h': 8, '7d': 8, '30d': 10 };

function svg(tag, attrs = {}, ...children) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (value !== undefined && value !== null) node.setAttribute(key, value);
  }
  for (const child of children) {
    if (child !== null && child !== undefined) node.append(child instanceof Node ? child : String(child));
  }
  return node;
}

export function formatMs(ms) {
  if (ms === null || ms === undefined) return '—';
  return ms >= 1000 ? `${(ms / 1000).toFixed(ms >= 10000 ? 0 : 1)} s` : `${ms} ms`;
}

export function formatPercent(value) {
  if (value === null || value === undefined) return '—';
  if (value === 100) return '100%';
  return `${value >= 99 ? value.toFixed(2) : value.toFixed(1)}%`;
}

function uptimeClass(value) {
  if (value === null || value === undefined) return '';
  if (value >= 99) return 'tile-good';
  if (value >= 95) return 'tile-warn';
  return 'tile-bad';
}

export function renderStatTiles(container, stats) {
  const tiles = stats.uptime.map((window) => el('div', {
    className: `tile ${uptimeClass(window.uptimePercent)} ${window.window === stats.range ? 'tile-active' : ''}`,
    title: window.checks ? `${window.upChecks} of ${plural(window.checks, 'check')} succeeded` : 'No checks in this period',
  },
  el('span', { className: 'tile-label' }, WINDOW_LABELS[window.window] ?? window.window),
  el('span', { className: 'tile-value' }, formatPercent(window.uptimePercent))));

  const rt = stats.responseTime;
  tiles.push(
    el('div', { className: 'tile', title: `Successful checks, ${RANGE_LABELS[stats.range]}` },
      el('span', { className: 'tile-label' }, 'Avg response'),
      el('span', { className: 'tile-value' }, formatMs(rt.averageMs))),
    el('div', { className: 'tile', title: `95% of successful checks were at least this fast, ${RANGE_LABELS[stats.range]}` },
      el('span', { className: 'tile-label' }, 'P95 response'),
      el('span', { className: 'tile-value' }, formatMs(rt.p95Ms))));

  container.replaceChildren(...tiles);
}

function niceStep(rough) {
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const residual = rough / magnitude;
  const nice = residual <= 1 ? 1 : residual <= 2 ? 2 : residual <= 5 ? 5 : 10;
  return nice * magnitude;
}

function axisLabel(date, range) {
  return range === '24h'
    ? date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : date.toLocaleDateString([], { month: 'short', day: 'numeric' });
}

function bucketTooltip(bucket, bucketSeconds) {
  const start = new Date(bucket.start);
  const end = new Date(start.getTime() + bucketSeconds * 1000);
  const period = `${start.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })} – ` +
    end.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  if (!bucket.checks) return `${period}\nNo checks`;
  const failed = bucket.checks - bucket.upChecks;
  const lines = [period, `${plural(bucket.checks, 'check')}${failed ? `, ${failed} failed` : ', all up'}`];
  if (bucket.averageResponseMs !== null) lines.push(`Avg ${formatMs(bucket.averageResponseMs)} · max ${formatMs(bucket.maxResponseMs)}`);
  return lines.join('\n');
}

export function renderResponseChart(container, stats) {
  const buckets = stats.buckets;
  const values = buckets.map((b) => b.averageResponseMs).filter((v) => v !== null);
  if (values.length === 0 && buckets.every((b) => b.checks === 0)) {
    container.replaceChildren(el('div', { className: 'empty' }, `No checks in the ${RANGE_LABELS[stats.range]}.`));
    return;
  }

  const plotW = WIDTH - PAD.left - PAD.right;
  const plotH = HEIGHT - PAD.top - PAD.bottom;
  const bucketW = plotW / buckets.length;
  const step = niceStep(Math.max(...values, 10) * 1.15 / 4);
  const yMax = step * 4;
  const x = (i) => PAD.left + bucketW * (i + 0.5);
  const y = (ms) => PAD.top + plotH - (ms / yMax) * plotH;
  const baseline = PAD.top + plotH;

  const chart = svg('svg', { viewBox: `0 0 ${WIDTH} ${HEIGHT}`, class: 'chart-svg', role: 'img',
    'aria-label': `Average response time, ${RANGE_LABELS[stats.range]}` });

  for (let i = 0; i <= 4; i++) {
    const value = step * i;
    chart.append(
      svg('line', { x1: PAD.left, x2: WIDTH - PAD.right, y1: y(value), y2: y(value), class: 'chart-grid' }),
      svg('text', { x: PAD.left - 8, y: y(value) + 4, class: 'chart-label', 'text-anchor': 'end' }, formatMs(value)));
  }

  const every = LABEL_EVERY[stats.range] ?? 8;
  buckets.forEach((bucket, i) => {
    if (i % every !== 0) return;
    const left = PAD.left + bucketW * i;
    chart.append(
      svg('line', { x1: left, x2: left, y1: PAD.top, y2: baseline, class: 'chart-grid chart-grid-v' }),
      svg('text', { x: left, y: HEIGHT - 8, class: 'chart-label', 'text-anchor': 'start' }, axisLabel(new Date(bucket.start), stats.range)));
  });

  // Split the line wherever a bucket has no successful checks, so gaps aren't drawn as flat lines.
  const segments = [];
  let current = [];
  buckets.forEach((bucket, i) => {
    if (bucket.averageResponseMs === null) {
      if (current.length) segments.push(current);
      current = [];
    } else {
      current.push([x(i), y(bucket.averageResponseMs)]);
    }
  });
  if (current.length) segments.push(current);

  for (const points of segments) {
    if (points.length === 1) {
      chart.append(svg('circle', { cx: points[0][0], cy: points[0][1], r: 3, class: 'chart-dot' }));
      continue;
    }
    const line = points.map(([px, py], i) => `${i ? 'L' : 'M'}${px.toFixed(1)},${py.toFixed(1)}`).join(' ');
    const area = `${line} L${points.at(-1)[0].toFixed(1)},${baseline} L${points[0][0].toFixed(1)},${baseline} Z`;
    chart.append(svg('path', { d: area, class: 'chart-area' }), svg('path', { d: line, class: 'chart-line' }));
  }

  buckets.forEach((bucket, i) => {
    const left = PAD.left + bucketW * i;
    if (bucket.checks > bucket.upChecks) {
      const severity = bucket.upChecks === 0 ? 'chart-fail-all' : 'chart-fail-some';
      chart.append(svg('rect', { x: left + 0.5, y: baseline - 5, width: Math.max(bucketW - 1, 1), height: 5, class: severity }));
    }
    chart.append(svg('rect', { x: left, y: PAD.top, width: bucketW, height: plotH, class: 'chart-hit' },
      svg('title', {}, bucketTooltip(bucket, stats.bucketSeconds))));
  });

  chart.append(svg('line', { x1: PAD.left, x2: WIDTH - PAD.right, y1: baseline, y2: baseline, class: 'chart-axis' }));
  container.replaceChildren(chart);
}
