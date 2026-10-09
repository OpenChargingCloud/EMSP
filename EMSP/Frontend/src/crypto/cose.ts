// A charging ticket, in the browser: its payload in CBOR, and the request for
// it - a COSE_Sign (RFC 9052, tag 98) signed twice with ES256, once with the
// ticket's own key to show it is held, once with one of the driver's account
// keys to show whose it is. The EMSP takes the second signature off and puts
// its own in its place; see EMSP/Tickets on the server.
//
// No DOM in here: this runs under Node as well, which is how it is tested.

import { decode, encode, Tagged, type CBORMap, type CBORValue } from './cbor';
import type { Bytes } from './pkcs';

const subtle = globalThis.crypto.subtle;

/** The CBOR tag of a COSE_Sign. */
export const COSE_SIGN_TAG = 98;

/** ECDSA on P-256 with SHA-256. */
export const ES256 = -7;

/** What the body's protected header says the payload is. */
export const TICKET_CONTENT_TYPE = 'application/charging-ticket+cbor';

/** The key identifier of a ticket's own signature: the key in the payload. */
export const TICKET_KEY_ID: Bytes = new TextEncoder().encode('ticket');


/** What a charging ticket says. */
export interface TicketSpec {
    /** Sixteen random bytes. */
    id:           Bytes;
    /** The EMSP it is good with, as its party: "DE*GDF". */
    emsp:         string;
    /** The ticket's public key. */
    publicKey:    CryptoKey;
    notBefore:    Date;
    notAfter:     Date;
    maxKW?:       number;
    maxMinutes?:  number;
    maxKWh?:      number;
}

/** One signature of a COSE_Sign, as it came. */
export interface Signature {
    protected:  Bytes;
    kid:        Bytes | null;
    alg:        number | null;
    value:      Bytes;
}

/** A COSE_Sign, as it came. */
export interface SignMessage {
    protected:   Bytes;
    payload:     Bytes;
    signatures:  Signature[];
}


/** A fresh key pair on P-256 for one ticket, its private half exportable to be handed to the driver. */
export function generateTicketKey(): Promise<CryptoKeyPair> {
    return subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, [ 'sign', 'verify' ]);
}

/** Sixteen random bytes: a ticket's id. */
export function newTicketId(): Bytes {
    return globalThis.crypto.getRandomValues(new Uint8Array(16));
}

/** The payload of a ticket, as deterministic CBOR. */
export async function ticketPayload(spec: TicketSpec): Promise<Bytes> {

    const jwk     = await subtle.exportKey('jwk', spec.publicKey);

    const key: CBORMap = new Map<number | string, CBORValue>([
        [  1, 2 ],                          // kty: EC2
        [ -1, 1 ],                          // crv: P-256
        [ -2, fromBase64URL(jwk.x!) ],
        [ -3, fromBase64URL(jwk.y!) ]
    ]);

    const payload: CBORMap = new Map<number | string, CBORValue>([
        [ 'typ',  'ChargingTicket' ],
        [ 'v',    1 ],
        [ 'id',   spec.id ],
        [ 'emsp', spec.emsp ],
        [ 'key',  key ],
        [ 'nbf',  Math.floor(spec.notBefore.getTime() / 1000) ],
        [ 'exp',  Math.floor(spec.notAfter. getTime() / 1000) ]
    ]);

    const limits: CBORMap = new Map();

    if (spec.maxKW      !== undefined) limits.set('kW',      spec.maxKW);
    if (spec.maxMinutes !== undefined) limits.set('minutes', spec.maxMinutes);
    if (spec.maxKWh     !== undefined) limits.set('kWh',     spec.maxKWh);

    if (limits.size > 0)
        payload.set('limits', limits);

    return encode(payload);

}

/**
 * A ticket request: the payload signed with the ticket's own key and with
 * an account key, named by the SHA-256 of its certificate.
 */
export async function ticketRequest(payload:       Bytes,
                                    ticketKey:     CryptoKey,
                                    accountKey:    CryptoKey,
                                    accountKeyId:  Bytes): Promise<Bytes> {

    const body = encode(new Map<number | string, CBORValue>([ [ 3, TICKET_CONTENT_TYPE ] ]));

    return encode(new Tagged(COSE_SIGN_TAG, [
        body,
        new Map(),
        payload,
        [
            await sign(ticketKey,  TICKET_KEY_ID, body, payload),
            await sign(accountKey, accountKeyId,  body, payload)
        ]
    ]));

}

/** One ES256 COSE_Signature over a body and its payload. */
async function sign(key: CryptoKey, kid: Bytes, body: Bytes, payload: Bytes): Promise<CBORValue> {

    const header = encode(new Map<number | string, CBORValue>([ [ 1, ES256 ], [ 4, kid ] ]));
    const value  = new Uint8Array(await subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, key, toBeSigned(body, header, payload)));

    return [ header, new Map(), value ];

}

/** The Sig_structure a signature of a COSE_Sign is made over, the external data empty. */
export function toBeSigned(body: Bytes, header: Bytes, payload: Bytes): Bytes {
    return encode([ 'Signature', body, header, new Uint8Array(0), payload ]);
}

/** A COSE_Sign, read: a ticket as the EMSP handed it back. */
export function decodeSign(bytes: Bytes): SignMessage {

    let value = decode(bytes);

    if (value instanceof Tagged) {
        if (value.tag !== COSE_SIGN_TAG)
            throw new SyntaxError(`tagged ${value.tag}, and not as a COSE_Sign (98)`);
        value = value.value;
    }

    if (!Array.isArray(value) || value.length !== 4 || !(value[0] instanceof Uint8Array) || !(value[2] instanceof Uint8Array) || !Array.isArray(value[3]))
        throw new SyntaxError('no COSE_Sign: an array of the protected header, the unprotected one, the payload and the signatures');

    return {
        protected:   value[0],
        payload:     value[2],
        signatures:  value[3].map(signature => {

                         if (!Array.isArray(signature) || !(signature[0] instanceof Uint8Array) || !(signature[2] instanceof Uint8Array))
                             throw new SyntaxError('no COSE_Signature');

                         const header = signature[0].length === 0 ? new Map() : decode(signature[0]);
                         const kid    = header instanceof Map ? header.get(4) : null;
                         const alg    = header instanceof Map ? header.get(1) : null;

                         return {
                             protected:  signature[0],
                             kid:        kid instanceof Uint8Array ? new Uint8Array(kid) : null,
                             alg:        typeof alg === 'number' ? alg : null,
                             value:      signature[2]
                         };

                     })
    };

}

/** Whether a signature of a COSE_Sign is ES256 by the given public key. */
export async function verifies(key: CryptoKey, message: SignMessage, signature: Signature): Promise<boolean> {

    if (signature.alg !== ES256)
        return false;

    return subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, key, signature.value,
                         toBeSigned(message.protected, signature.protected, message.payload));

}


/** Base64url, as a JWK writes x and y, to bytes. */
function fromBase64URL(text: string): Bytes {
    const base64 = text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - text.length % 4) % 4);
    return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
}

/** Bytes as hex, upper case, as the EMSP writes ids and fingerprints. */
export function toHex(bytes: Bytes): string {
    return [ ...bytes ].map(b => b.toString(16).padStart(2, '0')).join('').toUpperCase();
}
