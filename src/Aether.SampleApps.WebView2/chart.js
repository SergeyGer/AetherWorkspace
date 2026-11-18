(() => {
  'use strict';

  const allowedTickers = new Set(['NRST', 'VEGA', 'ORIN']);
  const messageType = 'aether.webview2.instrument';
  const schemaVersion = '1.0';
  const title = document.getElementById('chart-title');
  const meta = document.getElementById('chart-meta');
  const status = document.getElementById('js-status');
  const badge = document.getElementById('renderer-badge');
  const provenance = document.getElementById('provenance');
  const chart = document.getElementById('chart');
  const svgNs = 'http://www.w3.org/2000/svg';

  function post(message) {
    window.chrome.webview.postMessage(message);
  }

  function reject(reason) {
    status.textContent = 'Bridge rejected the message: ' + reason;
    badge.textContent = 'JS bridge rejected';
    post({ type: 'bridge-reject', reason: reason });
  }

  function drawChart(message) {
    const points = message.series;
    const values = points.map(point => Number(point.value));
    const min = Math.min(...values);
    const max = Math.max(...values);
    const span = Math.max(0.0001, max - min);
    const left = 62;
    const right = 930;
    const top = 40;
    const bottom = 360;
    const width = right - left;
    const height = bottom - top;
    const x = index => left + (width * index) / Math.max(1, points.length - 1);
    const y = value => bottom - (height * (value - min)) / span;

    chart.replaceChildren();

    const defs = document.createElementNS(svgNs, 'defs');
    defs.innerHTML = '<linearGradient id="area-gradient" x1="0" x2="0" y1="0" y2="1">'
      + '<stop offset="0" stop-color="#d9b05e" stop-opacity="0.32"></stop>'
      + '<stop offset="1" stop-color="#d9b05e" stop-opacity="0"></stop></linearGradient>';
    chart.appendChild(defs);

    for (let row = 0; row <= 4; row += 1) {
      const value = min + (span * row) / 4;
      const lineY = y(value);
      const grid = document.createElementNS(svgNs, 'line');
      grid.setAttribute('x1', left);
      grid.setAttribute('x2', right);
      grid.setAttribute('y1', lineY);
      grid.setAttribute('y2', lineY);
      grid.setAttribute('class', 'grid');
      chart.appendChild(grid);

      const label = document.createElementNS(svgNs, 'text');
      label.setAttribute('x', 8);
      label.setAttribute('y', lineY + 4);
      label.setAttribute('class', 'label');
      label.textContent = value.toFixed(2);
      chart.appendChild(label);
    }

    const areaPath = points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${x(index)} ${y(Number(point.value))}`).join(' ')
      + ` L ${x(points.length - 1)} ${bottom} L ${x(0)} ${bottom} Z`;
    const area = document.createElementNS(svgNs, 'path');
    area.setAttribute('d', areaPath);
    area.setAttribute('class', 'area');
    chart.appendChild(area);

    const path = points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${x(index)} ${y(Number(point.value))}`).join(' ');
    const line = document.createElementNS(svgNs, 'path');
    line.setAttribute('d', path);
    line.setAttribute('class', 'series');
    chart.appendChild(line);

    points.forEach((point, index) => {
      const dot = document.createElementNS(svgNs, 'circle');
      dot.setAttribute('cx', x(index));
      dot.setAttribute('cy', y(Number(point.value)));
      dot.setAttribute('r', 4.5);
      dot.setAttribute('class', 'dot');
      chart.appendChild(dot);
    });

    for (let index = 0; index < points.length; index += 3) {
      const label = document.createElementNS(svgNs, 'text');
      label.setAttribute('x', x(index));
      label.setAttribute('y', 402);
      label.setAttribute('class', 'label');
      label.setAttribute('text-anchor', 'middle');
      label.textContent = points[index].month;
      chart.appendChild(label);
    }

    const axis = document.createElementNS(svgNs, 'line');
    axis.setAttribute('x1', left);
    axis.setAttribute('x2', right);
    axis.setAttribute('y1', bottom);
    axis.setAttribute('y2', bottom);
    axis.setAttribute('class', 'axis');
    chart.appendChild(axis);
  }

  function accept(message) {
    const ticker = message.ticker;
    title.textContent = ticker + ' — ' + message.name;
    meta.textContent = 'Broker-approved typed context · classification ' + message.classification
      + ' · trace ' + message.traceId;
    badge.textContent = 'JS rendered ' + ticker;
    status.textContent = 'Series rendered locally: ' + message.series.length + ' synthetic points.';
    provenance.textContent = 'No remote scripts, no network fetch; rendered from .NET host context only.';
    drawChart(message);
    post({
      type: 'bridge-ack',
      ticker: ticker,
      traceId: message.traceId,
      classification: message.classification,
      points: message.series.length,
    });
  }

  window.chrome.webview.addEventListener('message', event => {
    const message = event.data;
    if (!message || message.type !== messageType) {
      reject('unsupported message type');
      return;
    }

    if (message.schemaVersion !== schemaVersion) {
      reject('unsupported schema version');
      return;
    }

    if (!allowedTickers.has(message.ticker)) {
      reject('ticker is not in the local allowlist');
      return;
    }

    if (!Array.isArray(message.series) || message.series.length < 2) {
      reject('series must contain at least two points');
      return;
    }

    if (message.series.some(point => typeof point.month !== 'string' || typeof point.value !== 'number')) {
      reject('series point shape is invalid');
      return;
    }

    accept(message);
  });

  post({ type: 'bridge-ready', renderer: 'local-webview2-js', schemaVersion: schemaVersion });
})();
