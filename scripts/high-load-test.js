/**
 * High-Throughput Multi-Worker Load Generator for E-Commerce API
 * Utilizes Node.js cluster module with persistent HTTP Keep-Alive connections
 * to achieve 1,000 - 3,000+ RPS on local multicore machines.
 */

const cluster = require('cluster');
const http = require('http');
const os = require('os');

const NUM_WORKERS = Math.min(8, Math.max(2, Math.floor(os.cpus().length / 2)));
const DURATION_SECONDS = parseInt(process.env.DURATION || '30', 10);
const CONCURRENCY_PER_WORKER = parseInt(process.env.CONCURRENCY || '50', 10);

const HOST = process.env.API_HOST || 'localhost';
const PORT = parseInt(process.env.API_PORT || '8080', 10);

const SKU_IPHONE = '71e6fd02-3410-4fac-a801-bf629ff1fc6a';

if (cluster.isPrimary) {
  console.log(`====================================================`);
  console.log(`🔥 HIGH-PERFORMANCE MULTI-WORKER LOAD GENERATOR`);
  console.log(`💻 CPU Cores: ${os.cpus().length} | Active Workers: ${NUM_WORKERS}`);
  console.log(`🚀 Concurrency: ${NUM_WORKERS * CONCURRENCY_PER_WORKER} in-flight requests`);
  console.log(`⏱  Duration: ${DURATION_SECONDS}s`);
  console.log(`📊 Watch live on Grafana: http://localhost:3000`);
  console.log(`====================================================\n`);

  let totalRequests = 0;
  let s2xx = 0;
  let s4xx = 0;
  let s5xx = 0;
  let lastTotal = 0;

  const workers = [];
  for (let i = 0; i < NUM_WORKERS; i++) {
    const worker = cluster.fork();
    workers.push(worker);

    worker.on('message', (msg) => {
      totalRequests += msg.total || 0;
      s2xx += msg.s2xx || 0;
      s4xx += msg.s4xx || 0;
      s5xx += msg.s5xx || 0;
    });
  }

  const startTime = Date.now();
  const endTime = startTime + DURATION_SECONDS * 1000;

  const reportTimer = setInterval(() => {
    const elapsed = Math.floor((Date.now() - startTime) / 1000);
    const rps = totalRequests - lastTotal;
    lastTotal = totalRequests;
    const remaining = Math.max(0, DURATION_SECONDS - elapsed);

    console.log(
      `[${elapsed.toString().padStart(2)}s/${DURATION_SECONDS}s] ` +
      `⚡ RPS: ${rps.toLocaleString().padStart(6)} | ` +
      `Total: ${totalRequests.toLocaleString().padStart(7)} | ` +
      `2xx: ${s2xx.toLocaleString().padStart(6)} | ` +
      `4xx: ${s4xx.toLocaleString().padStart(5)} | ` +
      `5xx: ${s5xx.toString().padStart(3)} | ` +
      `Remaining: ${remaining}s`
    );

    if (Date.now() >= endTime) {
      clearInterval(reportTimer);
      console.log(`\n====================================================`);
      console.log(`🏁 Benchmark Finished!`);
      console.log(`⚡ Peak Sustained RPS : ${Math.round(totalRequests / Math.max(1, elapsed)).toLocaleString()} req/s`);
      console.log(`📈 Total Requests     : ${totalRequests.toLocaleString()}`);
      console.log(`   - 2xx (Success)    : ${s2xx.toLocaleString()} (${((s2xx / Math.max(1, totalRequests)) * 100).toFixed(1)}%)`);
      console.log(`   - 4xx (Client/Stock): ${s4xx.toLocaleString()} (${((s4xx / Math.max(1, totalRequests)) * 100).toFixed(1)}%)`);
      console.log(`   - 5xx (Server Error): ${s5xx.toLocaleString()} (${((s5xx / Math.max(1, totalRequests)) * 100).toFixed(1)}%)`);
      console.log(`====================================================`);

      for (const w of workers) {
        w.kill();
      }
      process.exit(0);
    }
  }, 1000);

} else {
  // Worker Process
  const agent = new http.Agent({
    keepAlive: true,
    maxSockets: CONCURRENCY_PER_WORKER * 2,
    keepAliveMsecs: 30000,
  });

  let wTotal = 0;
  let w2xx = 0;
  let w4xx = 0;
  let w5xx = 0;

  // Send stats every 250ms
  setInterval(() => {
    if (wTotal > 0) {
      process.send({ total: wTotal, s2xx: w2xx, s4xx: w4xx, s5xx: w5xx });
      wTotal = 0;
      w2xx = 0;
      w4xx = 0;
      w5xx = 0;
    }
  }, 250);

  function executeRequest() {
    const roll = Math.random();
    let method = 'GET';
    let path = '/api/v1/products';
    let body = null;

    if (roll < 0.40) {
      // 40% Product catalog read
      path = '/api/v1/products';
    } else if (roll < 0.70) {
      // 30% Fast health probe read
      path = '/health/live';
    } else if (roll < 0.95) {
      // 25% Flash sale reservation
      method = 'POST';
      path = '/api/v1/inventory/reserve';
      body = JSON.stringify({
        orderId: '00000000-0000-0000-0000-' + Math.floor(Math.random() * 1000000000000).toString().padStart(12, '0'),
        items: [{ skuId: SKU_IPHONE, quantity: Math.random() < 0.8 ? 1 : 99999 }],
        ttlMinutes: 15,
      });
    } else {
      // 5% 404 test
      path = '/api/v1/unknown-' + Math.floor(Math.random() * 10000);
    }

    const options = {
      hostname: HOST,
      port: PORT,
      path: path,
      method: method,
      agent: agent,
      headers: {
        'Connection': 'keep-alive',
      },
    };

    if (body) {
      options.headers['Content-Type'] = 'application/json';
      options.headers['Content-Length'] = Buffer.byteLength(body);
      options.headers['Idempotency-Key'] = 'bulk-' + Math.random().toString(36).substring(2);
    }

    const req = http.request(options, (res) => {
      res.resume(); // Discard body quickly to maximize throughput
      wTotal++;
      if (res.statusCode >= 200 && res.statusCode < 300) w2xx++;
      else if (res.statusCode >= 400 && res.statusCode < 500) w4xx++;
      else if (res.statusCode >= 500) w5xx++;

      setImmediate(executeRequest); // Loop immediately
    });

    req.on('error', () => {
      wTotal++;
      w5xx++;
      setTimeout(executeRequest, 10);
    });

    if (body) req.write(body);
    req.end();
  }

  // Start concurrent loops
  for (let i = 0; i < CONCURRENCY_PER_WORKER; i++) {
    executeRequest();
  }
}
