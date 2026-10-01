import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 100,
  duration: '30s',
  thresholds: {
    http_req_duration: ['p(95)<100'],
  },
};

const BASE_URL = __ENV.API_URL || 'http://localhost:8080';

export default function () {
  const res = http.get(`${BASE_URL}/api/v1/products?page=1&pageSize=20`);

  check(res, {
    'status is 200': (r) => r.status === 200,
    'response has items': (r) => r.body.length > 0,
  });

  sleep(0.5);
}
