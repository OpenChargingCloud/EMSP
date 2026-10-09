/**
 * A ticket request as the browser makes one: a COSE_Sign tagged 98, its
 * payload the ticket - its id, the EMSP, its key as a COSE_Key, from when
 * until when, its limits - and two ES256 signatures over it, the ticket key's
 * named "ticket" and the account key's named by its certificate, each
 * verified here with WebCrypto as the EMSP verifies them. And a key file
 * written and opened again, and not opened with another password.
 */

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import { decode, type CBORMap } from './cbor.ts';
import { decodeSign, generateTicketKey, newTicketId, TICKET_KEY_ID, ticketPayload, ticketRequest, verifies } from './cose.ts';
import { encryptedPrivateKeyPEM, openEncryptedPrivateKey } from './pkcs.ts';

const subtle = globalThis.crypto.subtle;


describe('a ticket request', () => {

    it('is the ticket, signed by its own key and by the account key', async () => {

        const ticketKey   = await generateTicketKey();
        const accountKey  = await subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, [ 'sign', 'verify' ]);
        const accountId   = new Uint8Array(32).fill(7);
        const id          = newTicketId();

        const payload     = await ticketPayload({
                                id, emsp: 'DE*GDF', publicKey: ticketKey.publicKey,
                                notBefore: new Date('2026-10-09T10:00:00Z'), notAfter: new Date('2026-10-10T10:00:00Z'),
                                maxKW: 22, maxMinutes: 120, maxKWh: 40.5
                            });

        const request     = await ticketRequest(payload, ticketKey.privateKey, accountKey.privateKey, accountId);
        const message     = decodeSign(request);
        const ticket      = decode(message.payload) as CBORMap;
        const key         = ticket.get('key') as CBORMap;
        const jwk         = await subtle.exportKey('jwk', ticketKey.publicKey);

        assert.equal(request[0], 0xd8);
        assert.equal(request[1], 98, 'not tagged as a COSE_Sign');
        assert.equal(ticket.get('typ'),  'ChargingTicket');
        assert.equal(ticket.get('v'),    1);
        assert.deepEqual(ticket.get('id'), id);
        assert.equal(ticket.get('emsp'), 'DE*GDF');
        assert.equal(ticket.get('nbf'),  Date.parse('2026-10-09T10:00:00Z') / 1000);
        assert.equal(ticket.get('exp'),  Date.parse('2026-10-10T10:00:00Z') / 1000);
        assert.deepEqual(ticket.get('limits'), new Map<string, number>([ [ 'kW', 22 ], [ 'minutes', 120 ], [ 'kWh', 40.5 ] ]));
        assert.equal(key.get(1), 2);
        assert.equal(key.get(-1), 1);
        assert.equal(Buffer.from(key.get(-2) as Uint8Array).toString('base64url'), jwk.x);
        assert.equal(Buffer.from(key.get(-3) as Uint8Array).toString('base64url'), jwk.y);

        assert.equal(message.signatures.length, 2);
        assert.deepEqual(message.signatures[0]!.kid, TICKET_KEY_ID);
        assert.deepEqual(message.signatures[1]!.kid, accountId);
        assert.ok(await verifies(ticketKey.publicKey,  message, message.signatures[0]!), 'the ticket key\'s signature does not verify');
        assert.ok(await verifies(accountKey.publicKey, message, message.signatures[1]!), 'the account key\'s signature does not verify');
        assert.ok(!(await verifies(accountKey.publicKey, message, message.signatures[0]!)), 'a signature verifies with the wrong key');

    });

    it('leaves out the limits it is not given', async () => {

        const ticketKey = await generateTicketKey();
        const payload   = await ticketPayload({ id: newTicketId(), emsp: 'DE*GDF', publicKey: ticketKey.publicKey,
                                                notBefore: new Date(), notAfter: new Date(Date.now() + 3_600_000) });

        assert.equal((decode(payload) as CBORMap).has('limits'), false);

    });

});


describe('a key file', () => {

    it('opens with its password, and the key in it signs as the key that was saved', async () => {

        const pair    = await subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, [ 'sign', 'verify' ]);
        const pem     = await encryptedPrivateKeyPEM(pair.privateKey, 'test-only-key-file');
        const opened  = await openEncryptedPrivateKey(pem, 'test-only-key-file');
        const data    = new TextEncoder().encode('a ticket');
        const signed  = await subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, opened, data);

        assert.match(pem, /^-----BEGIN ENCRYPTED PRIVATE KEY-----/);
        assert.ok(await subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, pair.publicKey, signed, data));

    });

    it('says so where the password is another', async () => {

        const pair = await subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, [ 'sign', 'verify' ]);
        const pem  = await encryptedPrivateKeyPEM(pair.privateKey, 'test-only-key-file');

        await assert.rejects(openEncryptedPrivateKey(pem, 'test-only-other'), /password does not open the key/);

    });

});
