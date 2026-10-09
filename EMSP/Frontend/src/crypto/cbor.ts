// CBOR (RFC 8949) as much as a charging ticket needs: unsigned and negative
// integers, byte and text strings, arrays, maps, tags, floats, true, false
// and null. Written deterministically - the shortest form of every head and
// every float, the keys of a map sorted by their encoded bytes, shorter
// first - so that what the browser signs is what it, and the EMSP, would
// write again. Read leniently: what is checked is a
// signature over the bytes as they came, not their form.
//
// No DOM in here: this runs under Node as well, which is how it is tested.

import type { Bytes } from './pkcs';

/** A CBOR value as JavaScript holds it. Integers beyond 2^53 are not needed here, and refused. */
export type CBORValue = number | string | boolean | null | Bytes | CBORValue[] | CBORMap | Tagged;

/** A map, keyed by what CBOR keys a COSE header or a ticket with: integers and text. */
export type CBORMap = Map<number | string, CBORValue>;

/** A tagged value: COSE_Sign is tag 98. */
export class Tagged {

    readonly tag:    number;
    readonly value:  CBORValue;

    // Fields of its own rather than parameter properties: Node runs this
    // file by stripping its types, and those it cannot strip.
    constructor(tag: number, value: CBORValue) {
        this.tag   = tag;
        this.value = value;
    }

}


// ---------------------------------------------------------------------------
// Writing
// ---------------------------------------------------------------------------

/** A value as deterministic CBOR. */
export function encode(value: CBORValue): Bytes {
    const parts: Bytes[] = [];
    write(value, parts);
    return concat(parts);
}

function write(value: CBORValue, out: Bytes[]): void {

    if (value === null)               { out.push(new Uint8Array([ 0xf6 ])); return; }
    if (value === true)               { out.push(new Uint8Array([ 0xf5 ])); return; }
    if (value === false)              { out.push(new Uint8Array([ 0xf4 ])); return; }

    if (typeof value === 'number') {

        // An integer as one where it is exact in JavaScript; beyond 2^53 a
        // number is a float that happens to have no fraction, and is one.
        if (Number.isSafeInteger(value)) {
            out.push(value >= 0 ? head(0, value) : head(1, -1 - value));
            return;
        }

        out.push(float(value));
        return;

    }

    if (typeof value === 'string') {
        const utf8 = new TextEncoder().encode(value);
        out.push(head(3, utf8.length), utf8);
        return;
    }

    if (value instanceof Uint8Array) {
        out.push(head(2, value.length), new Uint8Array(value));
        return;
    }

    if (Array.isArray(value)) {
        out.push(head(4, value.length));
        for (const item of value)
            write(item, out);
        return;
    }

    if (value instanceof Tagged) {
        out.push(head(6, value.tag));
        write(value.value, out);
        return;
    }

    // A map: its pairs ordered by the bytes of their keys, shorter first.
    const pairs = [ ...value.entries() ].map(([ key, item ]) => ({ key: encode(key), item }));

    pairs.sort((a, b) => compareBytes(a.key, b.key));

    out.push(head(5, pairs.length));

    for (const { key, item } of pairs) {
        out.push(key);
        write(item, out);
    }

}

/**
 * A float in the fewest bytes that hold it exactly - half, single or double
 * precision - as RFC 8949's preferred serialization has it, and as .NET's
 * canonical CBOR writes it: 22.5 kW is f9 4da0, as the EMSP would write it.
 */
function float(value: number): Bytes {

    const half = toHalf(value);

    if (half !== null)
        return new Uint8Array([ 0xf9, half >> 8, half & 0xff ]);

    if (Math.fround(value) === value) {
        const bytes = new Uint8Array(5);
        bytes[0] = 0xfa;
        new DataView(bytes.buffer).setFloat32(1, value);
        return bytes;
    }

    const bytes = new Uint8Array(9);
    bytes[0] = 0xfb;
    new DataView(bytes.buffer).setFloat64(1, value);
    return bytes;

}

/** The half-precision bits of a value it holds exactly, or null. */
function toHalf(value: number): number | null {

    if (Number.isNaN(value))
        return 0x7e00;

    if (!Number.isFinite(value))
        return value > 0 ? 0x7c00 : 0xfc00;

    const sign      = value < 0 || Object.is(value, -0) ? 0x8000 : 0;
    const magnitude = Math.abs(value);

    if (magnitude === 0)
        return sign;

    let exponent = Math.floor(Math.log2(magnitude));

    // Subnormal: 2^-24 steps below 2^-14.
    if (exponent < -14) {
        const fraction = magnitude / 2 ** -24;
        return Number.isInteger(fraction) && fraction < 1024 ? sign | fraction : null;
    }

    if (exponent > 15)
        return null;

    // Math.log2 can land a hair off at a power of two.
    if (2 ** exponent > magnitude) exponent--;
    if (2 ** (exponent + 1) <= magnitude) exponent++;

    const fraction = (magnitude / 2 ** exponent - 1) * 1024;

    return Number.isInteger(fraction) ? sign | ((exponent + 15) << 10) | fraction : null;

}

/** The head of an item: its major type and its argument, in the fewest bytes. */
function head(major: number, argument: number): Bytes {

    const type = major << 5;

    if (argument < 24)          return new Uint8Array([ type | argument ]);
    if (argument < 0x100)       return new Uint8Array([ type | 24, argument ]);
    if (argument < 0x10000)     return new Uint8Array([ type | 25, argument >> 8, argument & 0xff ]);
    if (argument < 0x100000000) {
        const bytes = new Uint8Array(5);
        bytes[0] = type | 26;
        new DataView(bytes.buffer).setUint32(1, argument);
        return bytes;
    }

    const bytes = new Uint8Array(9);
    bytes[0] = type | 27;
    new DataView(bytes.buffer).setBigUint64(1, BigInt(argument));
    return bytes;

}

function compareBytes(a: Bytes, b: Bytes): number {
    if (a.length !== b.length)
        return a.length - b.length;
    for (let i = 0; i < a.length; i++)
        if (a[i] !== b[i])
            return a[i]! - b[i]!;
    return 0;
}

function concat(parts: Bytes[]): Bytes {
    const all = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
    let at = 0;
    for (const part of parts) {
        all.set(part, at);
        at += part.length;
    }
    return all;
}


// ---------------------------------------------------------------------------
// Reading
// ---------------------------------------------------------------------------

/** A value read from CBOR, which has to be all of the bytes. */
export function decode(bytes: Bytes): CBORValue {

    const reader = { bytes, at: 0, view: new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength) };
    const value  = read(reader);

    if (reader.at !== bytes.length)
        throw new SyntaxError('there is more after the CBOR value');

    return value;

}

interface Reader { bytes: Bytes; at: number; view: DataView }

function read(r: Reader): CBORValue {

    const initial = byte(r);
    const major   = initial >> 5;
    const info    = initial & 0x1f;

    if (major === 7) {
        switch (info) {
            case 20: return false;
            case 21: return true;
            case 22: return null;
            case 25: { const half = r.view.getUint16(take(r, 2)); return halfToNumber(half); }
            case 26: return r.view.getFloat32(take(r, 4));
            case 27: return r.view.getFloat64(take(r, 8));
            default: throw new SyntaxError(`simple value ${info} is not read here`);
        }
    }

    const argument = readArgument(r, info);

    switch (major) {
        case 0: return argument;
        case 1: return -1 - argument;
        case 2: return r.bytes.slice(take(r, argument), r.at);
        case 3: return new TextDecoder('utf-8', { fatal: true }).decode(r.bytes.subarray(take(r, argument), r.at));
        case 4: return Array.from({ length: argument }, () => read(r));
        case 5: {
            const map: CBORMap = new Map();
            for (let i = 0; i < argument; i++) {
                const key = read(r);
                if (typeof key !== 'number' && typeof key !== 'string')
                    throw new SyntaxError('a map key here is an integer or a text');
                map.set(key, read(r));
            }
            return map;
        }
        default: return new Tagged(argument, read(r));
    }

}

function readArgument(r: Reader, info: number): number {

    if (info < 24)  return info;
    if (info === 24) return r.view.getUint8 (take(r, 1));
    if (info === 25) return r.view.getUint16(take(r, 2));
    if (info === 26) return r.view.getUint32(take(r, 4));

    if (info === 27) {
        const big = r.view.getBigUint64(take(r, 8));
        if (big > BigInt(Number.MAX_SAFE_INTEGER))
            throw new RangeError('an integer beyond 2^53 is not read here');
        return Number(big);
    }

    throw new SyntaxError('indefinite lengths are not read here');

}

function byte(r: Reader): number {
    return r.view.getUint8(take(r, 1));
}

/** Where the next n bytes begin, the reader moved past them. */
function take(r: Reader, n: number): number {
    if (r.at + n > r.bytes.length)
        throw new SyntaxError('the CBOR ends too early');
    const at = r.at;
    r.at += n;
    return at;
}

function halfToNumber(half: number): number {
    const exponent = (half >> 10) & 0x1f;
    const fraction = half & 0x3ff;
    const sign     = half & 0x8000 ? -1 : 1;
    if (exponent === 0)  return sign * 2 ** -14 * (fraction / 1024);
    if (exponent === 31) return fraction ? NaN : sign * Infinity;
    return sign * 2 ** (exponent - 15) * (1 + fraction / 1024);
}
