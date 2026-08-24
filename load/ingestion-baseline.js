import http from 'k6/http';
import { check } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://localhost:5254';
const vus = Number(__ENV.VUS || 10);
const duration = __ENV.DURATION || '30s';

export const options = {
    vus,
    duration,
};

export default function () {
    const event = {
        eventId: crypto.randomUUID(),
        type: 'ingestion.baseline',
        source: 'k6-ingestion-baseline',
        occurredAt: new Date().toISOString(),
        payload: {
            scenario: 'ingestion-baseline',
        },
    };

    const response = http.post(`${baseUrl}/api/events`, `${JSON.stringify(event)}\n`, {
        headers: {
            'Content-Type': 'application/x-ndjson',
        },
    });

    check(response, {
        'status is 202': (result) => result.status === 202,
    });
}
