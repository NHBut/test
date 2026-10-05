/**
 * E-Commerce Realistic Traffic Simulator
 * Generates continuous concurrent requests to API endpoints to populate Prometheus & Grafana metrics.
 */

const BASE_URL = process.env.API_URL || 'http://localhost:8080';
const DURATION_SECONDS = parseInt(process.env.DURATION || '60', 10);
const TARGET_RPS = parseInt(process.env.RPS || '40', 10);

const SKU_IPHONE = '71e6fd02-3410-4fac-a801-bf629ff1fc6a';
const CUSTOMER_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6';

const stats = {
  total: 0,
  s2xx: 0,
  s4xx: 0,
  s5xx: 0,
  lastTotal: 0,
};

function generateUUID() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

async function sendRequest() {
  const roll = Math.random();
  const correlationId = generateUUID();
  const idempotencyKey = generateUUID();

  try {
    let res;
    if (roll < 0.45) {
      // 45%: Browse products
      res = await fetch(`${BASE_URL}/api/v1/products`, {
        headers: { 'X-Correlation-Id': correlationId },
      });
    } else if (roll < 0.65) {
      // 20%: Health check probes
      const probe = Math.random() > 0.5 ? 'live' : 'ready';
      res = await fetch(`${BASE_URL}/health/${probe}`, {
        headers: { 'X-Correlation-Id': correlationId },
      });
    } else if (roll < 0.85) {
      // 20%: Flash-sale inventory reservation
      const qty = Math.random() < 0.8 ? 1 : 99999; // 99999 will trigger 409 OUT_OF_STOCK
      res = await fetch(`${BASE_URL}/api/v1/inventory/reserve`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'X-Correlation-Id': correlationId,
        },
        body: JSON.stringify({
          orderId: generateUUID(),
          items: [{ skuId: SKU_IPHONE, quantity: qty }],
          ttlMinutes: 15,
        }),
      });
    } else if (roll < 0.95) {
      // 10%: Create Order
      res = await fetch(`${BASE_URL}/api/v1/orders`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'X-Correlation-Id': correlationId,
        },
        body: JSON.stringify({
          customerId: CUSTOMER_ID,
          shippingAddressJson: JSON.stringify({ city: 'Hanoi', address: '123 Trang Tien' }),
          items: [
            {
              skuId: SKU_IPHONE,
              sku: 'IP15PM-256-NAT',
              productName: 'iPhone 15 Pro Max',
              variantName: 'Natural Titanium 256GB',
              unitPrice: 29990000,
              quantity: 1,
            },
          ],
          shippingFee: 30000,
          discountAmount: 0,
        }),
      });
    } else {
      // 5%: Request to non-existent endpoint (Simulate natural 404 client error)
      res = await fetch(`${BASE_URL}/api/v1/non-existent-page-${Date.now()}`);
    }

    stats.total++;
    if (res.status >= 200 && res.status < 300) stats.s2xx++;
    else if (res.status >= 400 && res.status < 500) stats.s4xx++;
    else if (res.status >= 500) stats.s5xx++;
  } catch (err) {
    stats.total++;
    stats.s5xx++;
  }
}

async function main() {
  console.log(`====================================================`);
  console.log(`🚀 Starting Traffic Simulator against ${BASE_URL}`);
  console.log(`⏱  Duration: ${DURATION_SECONDS}s | Target RPS: ~${TARGET_RPS}`);
  console.log(`📊 Open Grafana at http://localhost:3000 to watch live`);
  console.log(`====================================================\n`);

  const startTime = Date.now();
  const endTime = startTime + DURATION_SECONDS * 1000;

  // Reporter timer
  const reportInterval = setInterval(() => {
    const elapsed = Math.floor((Date.now() - startTime) / 1000);
    const rps = stats.total - stats.lastTotal;
    stats.lastTotal = stats.total;
    const remaining = Math.max(0, DURATION_SECONDS - elapsed);

    console.log(
      `[${elapsed}s/${DURATION_SECONDS}s] RPS: ${rps.toString().padStart(3)} | Total: ${stats.total.toString().padStart(5)} | 2xx: ${stats.s2xx} | 4xx: ${stats.s4xx} | 5xx: ${stats.s5xx} (Remaining: ${remaining}s)`
    );
  }, 1000);

  // Worker loop
  const intervalMs = Math.max(10, Math.floor(1000 / TARGET_RPS));
  const batchSize = Math.max(1, Math.floor(TARGET_RPS / (1000 / intervalMs)));

  while (Date.now() < endTime) {
    const promises = [];
    for (let i = 0; i < batchSize; i++) {
      promises.push(sendRequest());
    }
    await Promise.all(promises);
    await new Promise((r) => setTimeout(r, intervalMs));
  }

  clearInterval(reportInterval);

  console.log(`\n====================================================`);
  console.log(`✅ Simulation Completed!`);
  console.log(`📈 Total Requests Sent: ${stats.total}`);
  console.log(`   - 2xx (Success)     : ${stats.s2xx} (${((stats.s2xx / stats.total) * 100).toFixed(1)}%)`);
  console.log(`   - 4xx (Client/Stock): ${stats.s4xx} (${((stats.s4xx / stats.total) * 100).toFixed(1)}%)`);
  console.log(`   - 5xx (Server Error): ${stats.s5xx} (${((stats.s5xx / stats.total) * 100).toFixed(1)}%)`);
  console.log(`====================================================`);
}

main().catch(console.error);
