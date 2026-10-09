/**
 * What a driver charged, drawn in a document of happy-dom against a stand-in
 * EMSP: what goes on now apart, every charge detail record and every session
 * a row, with what it cost - and a driver with nothing to charge with yet
 * told where to get something.
 */

import { open, refused, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Charged, Charging } from '../api/client.ts';

const { chargingPage } = await import('./charging.ts');


function charged(kind: 'session' | 'cdr', id: string, status: string | null, cost: number | null): Charged {
    return { kind, version: '2.2.1', id, party: 'DE*G22', status, start: '2026-10-09T08:00:00Z',
             end: kind === 'cdr' ? '2026-10-09T09:00:00Z' : null, kWh: 7.5, cost, currency: 'EUR',
             location: 'LOC0001', address: 'Biberweg 18, Jena', evse: 'DE*G22*E0001', token: '04A2B3C4D5E6F7' };
}

let held: Charging | Response;

function emsp({ path }: Asked): unknown {
    return path === '/charging' ? held : undefined;
}

async function opened(): Promise<HTMLElement> {
    return open(chargingPage, '/charging', [ 'contracts:run', 'tokens:run' ], emsp,
                root => root.querySelector('#sessions, .error-box') !== null);
}


describe('what a driver charged', () => {

    it('shows what goes on now apart, and every record and session a row', async () => {

        held = { sessions: [ charged('session', 'S1', 'ACTIVE', null), charged('session', 'S0', 'COMPLETED', 3.75) ],
                 cdrs:     [ charged('cdr', 'CDR1', null, 4) ], cards: 1, contracts: 0 };

        const root = await open(chargingPage, '/charging', [ 'tokens:run' ], emsp, root => root.querySelector('#sessions') !== null);

        assert.equal(root.querySelectorAll('#charging-now tbody tr').length,   1);
        assert.equal(root.querySelector('#charging-now tbody tr')!.getAttribute('data-id'), 'S1');
        assert.equal(root.querySelectorAll('#sessions tbody tr').length,       2);
        assert.equal(root.querySelectorAll('#charge-records tbody tr').length, 1);
        assert.match(root.querySelector('#charge-records tbody tr')!.textContent!, /4[.,]00 EUR/);
        assert.match(root.querySelector('#charge-records tbody tr')!.textContent!, /Biberweg 18, Jena/);
        assert.equal(root.querySelector('#nothing-to-charge-with'), null);

    });

    it('tells a driver with nothing to charge with where to get something', async () => {

        held = { sessions: [], cdrs: [], cards: 0, contracts: 0 };

        const root = await opened();

        assert.ok(root.querySelector('#nothing-to-charge-with a[href="/cards"]'),     'no way to the cards');
        assert.ok(root.querySelector('#nothing-to-charge-with a[href="/contracts"]'), 'no way to the contracts');
        assert.equal(root.querySelector('#charging-now'), null, 'nothing goes on, and it was shown as going on');

    });

    it('says why, where what was charged could not be read', async () => {

        held = refused(500, 'the sessions could not be read');

        const root = await opened();

        assert.match(root.querySelector('.error-box')!.textContent!, /could not be read/);

    });

});
