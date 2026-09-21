// The driver's side of a contract certificate, in the browser and nowhere
// else: a key pair that never leaves it, a PKCS#10 request signed with that
// key, and - once the EMSP answered - a PKCS#12 that bundles the key with the
// certificate and the sub-CAs, encrypted with a password the driver chose,
// for the vehicle to import.
//
// Everything here is DER written by hand. The three structures are small and
// fixed - one curve, one hash, one cipher - and a library that can encode all
// of PKCS would be ten times this file for the same bytes. No DOM in here:
// this runs under Node as well, which is how it is tested.
//
// References:
//   RFC 2986  PKCS #10: certification request
//   RFC 5480  elliptic curve keys in PKIX (the SPKI, ecdsa-with-SHA256)
//   RFC 8018  PKCS #5: PBES2, PBKDF2
//   RFC 7292  PKCS #12: PFX, safe bags, MAC and its key derivation
//   RFC 3279  ECDSA-Sig-Value

const subtle = globalThis.crypto.subtle;

// WebCrypto takes views over an ArrayBuffer and not over a SharedArrayBuffer,
// and TypeScript tells the two apart: everything here is the former.
export type Bytes = Uint8Array<ArrayBuffer>;

// ---------------------------------------------------------------------------
// DER
// ---------------------------------------------------------------------------

const oids = {
    commonName:            '2.5.4.3',
    ecPublicKey:           '1.2.840.10045.2.1',
    ecdsaWithSHA256:       '1.2.840.10045.4.3.2',
    data:                  '1.2.840.113549.1.7.1',
    pbes2:                 '1.2.840.113549.1.5.13',
    pbkdf2:                '1.2.840.113549.1.5.12',
    hmacWithSHA256:        '1.2.840.113549.2.9',
    aes256CBC:             '2.16.840.1.101.3.4.1.42',
    sha256:                '2.16.840.1.101.3.4.2.1',
    friendlyName:          '1.2.840.113549.1.9.20',
    localKeyId:            '1.2.840.113549.1.9.21',
    x509Certificate:       '1.2.840.113549.1.9.22.1',
    pkcs8ShroudedKeyBag:   '1.2.840.113549.1.12.10.1.2',
    certBag:               '1.2.840.113549.1.12.10.1.3'
} as const;

function concat(...parts: Bytes[]): Bytes {

    const out = new Uint8Array(parts.reduce((length, part) => length + part.length, 0));

    let offset = 0;

    for (const part of parts) {
        out.set(part, offset);
        offset += part.length;
    }

    return out;

}

function length(n: number): Bytes {

    if (n < 0x80)
        return Uint8Array.of(n);

    const bytes: number[] = [];

    for (let rest = n; rest > 0; rest = Math.floor(rest / 256))
        bytes.unshift(rest & 0xff);

    return Uint8Array.of(0x80 | bytes.length, ...bytes);

}

function tlv(tag: number, content: Bytes): Bytes {
    return concat(Uint8Array.of(tag), length(content.length), content);
}

const sequence     = (...items: Bytes[]): Bytes => tlv(0x30, concat(...items));
const set          = (...items: Bytes[]): Bytes => tlv(0x31, concat(...items));
const octetString  = (bytes: Bytes):     Bytes => tlv(0x04, bytes);
const nullValue    = ():                      Bytes => Uint8Array.of(0x05, 0x00);

/** [n] EXPLICIT, constructed. */
const explicit     = (n: number, content: Bytes): Bytes => tlv(0xa0 | n, content);

/** A BIT STRING with no unused bits. */
function bitString(bytes: Bytes): Bytes {
    return tlv(0x03, concat(Uint8Array.of(0), bytes));
}

/** A non-negative INTEGER, from a number or from big-endian bytes. */
function integer(value: number | Bytes): Bytes {

    let bytes: Bytes;

    if (typeof value === 'number') {

        if (!Number.isInteger(value) || value < 0)
            throw new Error('Only non-negative integers are encoded here.');

        const digits: number[] = [];

        for (let rest = value; rest > 0; rest = Math.floor(rest / 256))
            digits.unshift(rest & 0xff);

        bytes = Uint8Array.from(digits.length > 0 ? digits : [0]);

    }
    else {

        let start = 0;

        while (start < value.length - 1 && value[start] === 0)
            start++;

        bytes = value.slice(start);

    }

    // Two's complement: a leading 1 bit would make it negative.
    if ((bytes[0] ?? 0) & 0x80)
        bytes = concat(Uint8Array.of(0), bytes);

    return tlv(0x02, bytes);

}

function oid(text: string): Bytes {

    const arcs  = text.split('.').map(Number);
    const bytes = [40 * (arcs[0] ?? 0) + (arcs[1] ?? 0)];

    for (const arc of arcs.slice(2)) {

        const chunk: number[] = [arc & 0x7f];

        for (let rest = Math.floor(arc / 128); rest > 0; rest = Math.floor(rest / 128))
            chunk.unshift(0x80 | (rest & 0x7f));

        bytes.push(...chunk);

    }

    return tlv(0x06, Uint8Array.from(bytes));

}

const utf8String = (text: string): Bytes => tlv(0x0c, new TextEncoder().encode(text));

/** UCS-2, big-endian: what PKCS#12 wants for a friendly name. */
function bmpString(text: string): Bytes {

    const bytes = new Uint8Array(text.length * 2);

    for (let i = 0; i < text.length; i++) {
        const unit = text.charCodeAt(i);
        bytes[2 * i]     = unit >> 8;
        bytes[2 * i + 1] = unit & 0xff;
    }

    return tlv(0x1e, bytes);

}

/** AlgorithmIdentifier ::= SEQUENCE { algorithm OID, parameters ANY OPTIONAL } */
const algorithm = (id: string, parameters?: Bytes): Bytes =>
    parameters ? sequence(oid(id), parameters) : sequence(oid(id));

/** ContentInfo of type data: the OCTET STRING of whatever is inside. */
const dataContent = (content: Bytes): Bytes =>
    sequence(oid(oids.data), explicit(0, octetString(content)));


// ---------------------------------------------------------------------------
// PEM
// ---------------------------------------------------------------------------

function base64(bytes: Bytes): string {

    let binary = '';

    for (const byte of bytes)
        binary += String.fromCharCode(byte);

    return btoa(binary);

}

function fromBase64(text: string): Bytes {
    return Uint8Array.from(atob(text), character => character.charCodeAt(0));
}

/** One PEM block. */
export function toPEM(label: string, der: Bytes): string {

    const lines = base64(der).match(/.{1,64}/g) ?? [];

    return `-----BEGIN ${label}-----\n${lines.join('\n')}\n-----END ${label}-----\n`;

}

/** Every block of the given label in a PEM text, as DER, in order. */
export function fromPEM(pem: string, label = 'CERTIFICATE'): Bytes[] {

    const pattern = new RegExp(`-----BEGIN ${label}-----([\\s\\S]*?)-----END ${label}-----`, 'g');
    const blocks: Bytes[] = [];

    for (const match of pem.matchAll(pattern))
        blocks.push(fromBase64((match[1] ?? '').replace(/\s+/g, '')));

    return blocks;

}


// ---------------------------------------------------------------------------
// The key and the request
// ---------------------------------------------------------------------------

/**
 * A fresh ECDSA key pair on P-256: the one curve ISO 15118-2 puts a contract
 * on, and the one a vehicle signs its authorization with.
 */
export function generateContractKey(): Promise<CryptoKeyPair> {
    return subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
}

/**
 * A PKCS#10 certificate signing request for the given key, signed with it,
 * as PEM.
 *
 * The subject is a courtesy: the EMSP makes the certificate out to an eMAID
 * of its own choosing and takes nothing from here but the key and the proof
 * that its private half was at hand.
 */
export async function createCSR(keys: CryptoKeyPair, commonName: string): Promise<string> {

    const spki = new Uint8Array(await subtle.exportKey('spki', keys.publicKey));

    const info = sequence(
                     integer(0),
                     sequence(set(sequence(oid(oids.commonName), utf8String(commonName)))),
                     spki,
                     // attributes [0] IMPLICIT SET OF Attribute: none, but the
                     // tag has to be there.
                     explicit(0, new Uint8Array(0))
                 );

    const raw = new Uint8Array(await subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, keys.privateKey, info));

    return toPEM('CERTIFICATE REQUEST',
                 sequence(info, algorithm(oids.ecdsaWithSHA256), bitString(ecdsaSignature(raw))));

}

/** WebCrypto hands back r || s; X.509 wants SEQUENCE { INTEGER r, INTEGER s }. */
function ecdsaSignature(raw: Bytes): Bytes {

    const half = raw.length / 2;

    return sequence(integer(raw.slice(0, half)), integer(raw.slice(half)));

}


// ---------------------------------------------------------------------------
// PKCS#12
// ---------------------------------------------------------------------------

export interface PKCS12Options {
    /** The driver's private key. */
    privateKey:     CryptoKey;
    /** The contract certificate, DER. */
    certificate:    Bytes;
    /** The sub-CAs the certificate chains through, DER, the signer first. */
    chain:          Bytes[];
    /** What the key and the certificate are encrypted and sealed with. */
    password:       string;
    /** How the bundle names the key and the certificate, e.g. the eMAID. */
    friendlyName:   string;
}

/**
 * A PKCS#12 with the private key - shrouded with PBES2, AES-256-CBC and
 * PBKDF2-HMAC-SHA256 - the certificate and its chain, and a MAC over all of
 * it: what a vehicle's certificate store imports as a contract.
 */
export async function buildPKCS12(options: PKCS12Options): Promise<Bytes> {

    const pkcs8       = new Uint8Array(await subtle.exportKey('pkcs8', options.privateKey));
    const localKeyId  = new Uint8Array(await subtle.digest('SHA-256', options.certificate)).slice(0, 20);

    const attributes  = set(
                            sequence(oid(oids.friendlyName), set(bmpString(options.friendlyName))),
                            sequence(oid(oids.localKeyId),   set(octetString(localKeyId)))
                        );

    // The key, shrouded.
    const keyBag      = sequence(
                            oid(oids.pkcs8ShroudedKeyBag),
                            explicit(0, await shroud(pkcs8, options.password)),
                            attributes
                        );

    // The certificate and, without attributes, the sub-CAs.
    const certBag     = (der: Bytes, withAttributes: boolean): Bytes =>
                            sequence(
                                oid(oids.certBag),
                                explicit(0, sequence(oid(oids.x509Certificate), explicit(0, octetString(der)))),
                                ...(withAttributes ? [attributes] : [])
                            );

    const authSafe    = sequence(
                            dataContent(sequence(keyBag)),
                            dataContent(sequence(certBag(options.certificate, true), ...options.chain.map(der => certBag(der, false))))
                        );

    return sequence(integer(3), dataContent(authSafe), await macData(authSafe, options.password));

}

/** EncryptedPrivateKeyInfo with PBES2: PBKDF2-HMAC-SHA256 and AES-256-CBC. */
async function shroud(pkcs8: Bytes, password: string): Promise<Bytes> {

    const salt        = globalThis.crypto.getRandomValues(new Uint8Array(16));
    const iv          = globalThis.crypto.getRandomValues(new Uint8Array(16));
    const iterations  = 100_000;

    const baseKey     = await subtle.importKey('raw', new TextEncoder().encode(password), 'PBKDF2', false, ['deriveKey']);
    const key         = await subtle.deriveKey(
                                  { name: 'PBKDF2', salt, iterations, hash: 'SHA-256' },
                                  baseKey,
                                  { name: 'AES-CBC', length: 256 },
                                  false,
                                  ['encrypt']
                              );

    const encrypted   = new Uint8Array(await subtle.encrypt({ name: 'AES-CBC', iv }, key, pkcs8));

    return sequence(
               algorithm(oids.pbes2, sequence(
                   algorithm(oids.pbkdf2, sequence(octetString(salt), integer(iterations), algorithm(oids.hmacWithSHA256, nullValue()))),
                   algorithm(oids.aes256CBC, octetString(iv))
               )),
               octetString(encrypted)
           );

}

/** MacData over the authenticated safe: HMAC-SHA256, its key from the PKCS#12 KDF. */
async function macData(authSafe: Bytes, password: string): Promise<Bytes> {

    const salt        = globalThis.crypto.getRandomValues(new Uint8Array(8));
    const iterations  = 2048;

    const macKey      = await pkcs12KeyDerivation(password, salt, iterations, 3, 32);
    const key         = await subtle.importKey('raw', macKey, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    const mac         = new Uint8Array(await subtle.sign('HMAC', key, authSafe));

    return sequence(
               sequence(algorithm(oids.sha256, nullValue()), octetString(mac)),
               octetString(salt),
               integer(iterations)
           );

}

/**
 * The key derivation of RFC 7292, appendix B, with SHA-256: the one thing in
 * PKCS#12 that is neither PBKDF2 nor anything WebCrypto knows.
 *
 * Only as much of it as a 32-byte MAC key needs: one block of output, so the
 * loop that would feed the next block back into the input never runs.
 */
async function pkcs12KeyDerivation(password:    string,
                                   salt:        Bytes,
                                   iterations:  number,
                                   id:          number,
                                   bytes:       number): Promise<Bytes> {

    const v = 64;   // the block size of SHA-256, in bytes

    // The password as a BMPString: UTF-16BE, and two zero bytes at the end.
    const encoded = new Uint8Array((password.length + 1) * 2);

    for (let i = 0; i < password.length; i++) {
        const unit = password.charCodeAt(i);
        encoded[2 * i]     = unit >> 8;
        encoded[2 * i + 1] = unit & 0xff;
    }

    const repeat = (source: Bytes): Bytes => {

        if (source.length === 0)
            return source;

        const out = new Uint8Array(Math.ceil(source.length / v) * v);

        for (let i = 0; i < out.length; i++)
            out[i] = source[i % source.length] ?? 0;

        return out;

    };

    const D = new Uint8Array(v).fill(id);
    const I = concat(repeat(salt), repeat(encoded));

    let A = new Uint8Array(await subtle.digest('SHA-256', concat(D, I)));

    for (let i = 1; i < iterations; i++)
        A = new Uint8Array(await subtle.digest('SHA-256', A));

    if (bytes > A.length)
        throw new Error(`The PKCS#12 key derivation here yields ${A.length} bytes, and ${bytes} were asked for.`);

    return A.slice(0, bytes);

}
