/**
 * CBOR as a charging ticket writes and reads it: the examples of RFC 8949,
 * appendix A, byte for byte; a map's keys in the order deterministic CBOR
 * puts them, whatever order they were set in; and everything read back as it
 * was written.
 */

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import { decode, encode, Tagged, type CBORValue } from './cbor.ts';

const hex = (bytes: Uint8Array) => [ ...bytes ].map(b => b.toString(16).padStart(2, '0')).join('');
const bytesOf = (text: string) => Uint8Array.from(text.match(/../g) ?? [], pair => parseInt(pair, 16));


describe('CBOR', () => {

    it('writes the examples of RFC 8949 as the RFC does', () => {

        const examples: [CBORValue, string][] = [
            [ 0,                         '00' ],
            [ 23,                        '17' ],
            [ 24,                        '1818' ],
            [ 100,                       '1864' ],
            [ 1000,                      '1903e8' ],
            [ 1000000,                   '1a000f4240' ],
            [ 1000000000000,             '1b000000e8d4a51000' ],
            [ -1,                        '20' ],
            [ -1000,                     '3903e7' ],
            [ 1.1,                       'fb3ff199999999999a' ],
            [ 1.5,                       'f93e00' ],
            [ 65504,                     '19ffe0' ],
            [ 100000.5,                  'fa47c35040' ],
            [ 22.5,                      'f94da0' ],
            [ 5.960464477539063e-8,      'f90001' ],
            [ 0.00006103515625,          'f90400' ],
            [ -4.5,                      'f9c480' ],
            [ 3.4028234663852886e+38,    'fa7f7fffff' ],
            [ 1.0e+300,                  'fb7e37e43c8800759c' ],
            [ false,                     'f4' ],
            [ true,                      'f5' ],
            [ null,                      'f6' ],
            [ 'a',                       '6161' ],
            [ 'IETF',                    '6449455446' ],
            [ 'ü',                  '62c3bc' ],
            [ bytesOf('01020304'),       '4401020304' ],
            [ [ 1, 2, 3 ],               '83010203' ],
            [ [ 1, [ 2, 3 ], [ 4, 5 ] ], '8301820203820405' ],
            [ new Map([ [ 1, 2 ], [ 3, 4 ] ]), 'a201020304' ],
            [ new Tagged(1, 1363896240), 'c11a514b67b0' ]
        ];

        for (const [ value, expected ] of examples)
            assert.equal(hex(encode(value)), expected, `${String(value)}`);

    });

    it('orders a map by the bytes of its keys, shorter first, whatever order they were set in', () => {

        const map = new Map<number | string, CBORValue>([ [ 'typ', 1 ], [ -3, 2 ], [ 'v', 3 ], [ 1, 4 ], [ 'id', 5 ], [ -1, 6 ] ]);

        // 1 (01), -1 (20), -3 (22), "v" (6176), "id" (626964), "typ" (63747970)
        assert.equal(hex(encode(map)), 'a6' + '0104' + '2006' + '2202' + '617603' + '62696405' + '6374797001');

    });

    it('reads back what it wrote', () => {

        const value = new Tagged(98, [ bytesOf('a10126'), new Map(), bytesOf('cafe'), [ [ bytesOf(''), new Map([ [ 4, bytesOf('01') ] ]), bytesOf('ff') ] ], 'text', -7, 22.5, null, true ]);

        assert.deepEqual(decode(encode(value)), value);

    });

    it('refuses what is not all CBOR, or ends too early', () => {

        assert.throws(() => decode(bytesOf('0000')), /more after/);
        assert.throws(() => decode(bytesOf('44010203')), /too early/);

    });

});
