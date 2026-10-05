import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    flash_sale_surge: {
      executor: 'ramping-arrival-rate',
      startRate: 50,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 1000,
      stages: [
        { target: 500, duration: '10s' }, // Tăng vọt lên 500 req/s trong 10s
        { target: 1000, duration: '20s' }, // Duy trì 1000 req/s
        { target: 0, duration: '5s' },
      ],
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<300', 'p(99)<500'], // P95 < 300ms, P99 < 500ms
    http_req_failed: ['rate<0.01'], // Lỗi hệ thống 5xx < 1%
  },
};

const BASE_URL = __ENV.API_URL || 'http://localhost:8080';
const FLASH_SKU_ID = '71e6fd02-3410-4fac-a801-bf629ff1fc6a';

export default function () {
  const userId = `user-${__VU}-${__ITER}`;
  const idempotencyKey = `idemp-${userId}`;
  const randomOrderId = '00000000-0000-4000-8000-' + Math.floor(Math.random() * 1000000000000).toString().padStart(12, '0');

  const payload = JSON.stringify({
    orderId: randomOrderId,
    items: [
      {
        skuId: FLASH_SKU_ID,
        quantity: 1,
      },
    ],
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
      'X-Correlation-Id': `corr-${userId}`,
    },
  };

  const res = http.post(`${BASE_URL}/api/v1/inventory/reserve`, payload, params);

  check(res, {
    'status is 200 or 409 (Out of Stock)': (r) => r.status === 200 || r.status === 409,
    'no 500 server crash': (r) => r.status < 500,
  });

  sleep(0.1);
}
