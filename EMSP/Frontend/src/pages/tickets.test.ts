/**
 * The keys and tickets drawn, in a document of happy-dom, against a stand-in
 * EMSP: an account key made here - its signing request signed here, its file
 * saved, the list drawn anew and the passwords gone from the form; two
 * passwords that differ said without asking anybody; a ticket without its key
 * file or good for too long said without asking either; and whoever manages
 * the tickets shown everybody's, with no form.
 *
 * The passwords here are test values for a form in a document of happy-dom;
 * nothing is signed in and nothing leaves the process.
 */

import { asked, field, open, submit, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { AccountCertificate, AccountKeys, Tickets } from '../api/client.ts';

const { ticketsPage } = await import('./tickets.ts');


const ownKey = { subject: 'CN=GraphDefined Charging Ticket Issuer', fingerprint: 'AB'.repeat(32), notAfter: '2036-10-09T00:00:00Z',
                 pem: '-----BEGIN CERTIFICATE-----\nMIIBAgMEBQYHCAk=\n-----END CERTIFICATE-----\n', file: 'pki/tickets/ticket_issuer.cert.pem' };

function aKey(id: string, owner = 'alice'): AccountCertificate {
    return { id, owner, label: 'Phone', serialNumber: '01', notBefore: '2026-10-09T09:59:00Z', notAfter: '2028-10-08T09:59:00Z',
             issuedAt: '2026-10-09T10:00:00Z', revokedAt: null, revokedBy: null, status: 'valid',
             certificate: '-----BEGIN CERTIFICATE-----\nMIIBAgMEBQYHCAk=\n-----END CERTIFICATE-----\n' };
}

let keys:     AccountKeys;
let tickets:  Tickets;

function emsp({ method, path }: Asked): unknown {

    if (path === '/account-keys' && method === 'GET')
        return keys;

    if (path === '/tickets' && method === 'GET')
        return tickets;

    if (path === '/account-keys' && method === 'POST') {
        const made = aKey('CD'.repeat(32));
        keys = { ...keys, certificates: [ made, ...keys.certificates ] };
        return { message: 'An account certificate was issued, good until 2028-10-08.', accountCertificate: made,
                 certificate: made.certificate, ca: ownKey.pem, accountKeys: keys };
    }

    return undefined;

}

async function opened(permissions = [ 'tickets:run' ], everyone = false): Promise<HTMLElement> {

    keys    = { everyone, validityDays: 730, ca: ownKey, certificates: everyone ? [ aKey('EF'.repeat(32), 'bobby') ] : [] };
    tickets = { everyone, party: 'DE*GDF', maxValidityDays: 30, issuer: ownKey,
                tickets: everyone ? [ { id: '01'.repeat(16), owner: 'bobby', accountCertificate: 'EF'.repeat(32), notBefore: '2026-10-09T10:00:00Z',
                                        notAfter: '2026-10-10T10:00:00Z', maxKW: 22, maxMinutes: null, maxKWh: null,
                                        issuedAt: '2026-10-09T10:00:00Z', status: 'valid' } ] : [] };

    return open(ticketsPage, '/tickets', permissions, emsp, root => root.querySelector('#ticket-issuer') !== null);

}


describe('the keys and tickets', () => {

    it('make an account key here, save it, and empty the form', async () => {

        const root = await opened();

        field(root, '#key-form', 'label').value     = 'Phone';
        field(root, '#key-form', 'password').value  = 'test-only-key-file';
        field(root, '#key-form', 'password2').value = 'test-only-key-file';
        submit(root, '#key-form');

        // The key is wrapped with 100,000 rounds of PBKDF2.
        await until(() => /saved as a file/.test(root.querySelector('#key-note')!.textContent!), 'the key made was not said', 10_000);

        const sent = asked.find(one => one.method === 'POST')!.body as { csr: string; label: string };

        assert.match(sent.csr, /^-----BEGIN CERTIFICATE REQUEST-----/, 'the request was not signed here');
        assert.equal(sent.label, 'Phone');
        assert.equal(root.querySelectorAll('#key-list tbody tr').length, 1);
        assert.equal(field(root, '#key-form', 'password').value, '', 'the password stayed in the form');

    });

    it('say that two passwords differ, and ask nobody', async () => {

        const root = await opened();

        field(root, '#key-form', 'password').value  = 'test-only-key-file';
        field(root, '#key-form', 'password2').value = 'test-only-other';
        submit(root, '#key-form');

        await until(() => root.querySelector('#key-error')!.textContent !== '', 'nothing was said');

        assert.equal(root.querySelector('#key-error')!.textContent, 'The two passwords differ.');
        assert.equal(asked.filter(one => one.method === 'POST').length, 0);

    });

    it('say what a ticket lacks, and ask nobody', async () => {

        const root = await opened();

        field(root, '#ticket-form', 'ticketPassword').value  = 'test-only-ticket';
        field(root, '#ticket-form', 'ticketPassword2').value = 'test-only-ticket';
        field(root, '#ticket-form', 'hours').value           = '9999';
        submit(root, '#ticket-form');

        await until(() => root.querySelector('#ticket-error')!.textContent !== '', 'nothing was said');

        assert.match(root.querySelector('#ticket-error')!.textContent!, /720 hours at the most/);

        field(root, '#ticket-form', 'hours').value = '24';
        submit(root, '#ticket-form');

        await until(() => /Choose the file/.test(root.querySelector('#ticket-error')!.textContent!), 'the missing key file was not said');

        assert.equal(asked.filter(one => one.method === 'POST').length, 0);

    });

    it('show whoever manages them everybody\'s, and no form', async () => {

        const root = await opened([ 'tickets:edit' ], true);

        assert.equal(root.querySelector('#key-form'),    null);
        assert.equal(root.querySelector('#ticket-form'), null);
        assert.match(root.querySelector('#key-list')!.textContent!,    /bobby/);
        assert.match(root.querySelector('#ticket-list')!.textContent!, /bobby/);
        assert.match(root.querySelector('#ticket-list')!.textContent!, /22[.,]0 kW/);

    });

});
