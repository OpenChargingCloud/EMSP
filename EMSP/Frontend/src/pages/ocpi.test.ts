/**
 * The OCPI page drawn, in a document of happy-dom, against a stand-in EMSP:
 * who this EMSP is, its settings and what it holds as cards, one table of
 * endpoints per version, rows and links as markup and not as text - and
 * Reload draws again with what the EMSP says then.
 */

import { open, refused, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { OCPIConfiguration } from '../api/client.ts';

const { ocpiPage } = await import('./ocpi.ts');


let partners = 2;
let broken   = false;

function ocpi(): OCPIConfiguration {
    return {
        party:          { countryCode: 'DE', partyId: 'GDF', id: 'DE-GDF_EMSP', role: 'EMSP', name: 'GraphDefined EMSP', website: null },
        endpoints:      { base: 'http://127.0.0.1/ext', versions: 'http://127.0.0.1/ext/versions', externalURL: null,
                          byVersion: [ { version: '2.1.1', details: 'http://127.0.0.1/ext/versions/2.1.1', credentials: 'http://127.0.0.1/ext/v2.1.1/credentials',
                                         modules: { locations: 'http://127.0.0.1/ext/v2.1.1/emsp/locations' } },
                                       { version: '2.2.1', details: 'http://127.0.0.1/ext/versions/2.2.1', credentials: 'http://127.0.0.1/ext/v2.2.1/credentials',
                                         modules: { locations: 'http://127.0.0.1/ext/v2.2.1/emsp/locations', tokens: 'http://127.0.0.1/ext/v2.2.1/emsp/tokens' } } ] },
        versions:       [ '2.1.1', '2.2.1' ],
        knownVersions:  [ '2.1.1', '2.2.1', '2.3.0' ],
        settings:       { locationsAsOpenData: true, tariffsAsOpenData: false, allowDowngrades: false, logRequests: true, logPayloads: false },
        counts:         { partners, tokens: 3, locations: 4, tariffs: 0, sessions: 1, cdrs: 5 },
        directory:      'D:\\EMSP\\ocpi',
        file:           'D:\\EMSP\\emsp.json'
    };
}

function emsp({ path }: Asked): unknown {

    if (path === '/configuration/ocpi')
        return broken ? refused(500, 'the OCPI library is not there') : ocpi();

    return undefined;

}

const heldValue = (root: HTMLElement, key: string) =>
    [...root.querySelectorAll('.kv')].find(kv => kv.querySelector('.k')?.textContent === key)?.querySelector('.v')?.textContent?.trim();


describe('the OCPI page', () => {

    it('draws who this EMSP is, a table per version, and again on Reload', async () => {

        partners = 2;
        broken   = false;

        const root = await open(ocpiPage, '/configuration/ocpi', [ 'ocpi:read' ],
                                emsp, root => root.querySelector('.cards') !== null);

        assert.equal(heldValue(root, 'Party'), 'DE-GDF_EMSP');
        assert.equal(heldValue(root, 'Versions'), '2.1.1, 2.2.1');
        assert.equal(heldValue(root, 'Locations as open data'), 'yes');
        assert.equal(heldValue(root, 'Roaming partners'), '2');

        assert.equal(root.querySelectorAll('.versions table.records').length, 2, 'not one table per version');
        assert.equal(root.querySelectorAll('.versions table.records')[1]!.querySelectorAll('tbody tr').length, 4,
                     'details, credentials and two modules are not four rows');
        assert.ok(root.querySelector('a[href$="/configuration/ocpi/partners"]'), 'the partners are not linked');
        assert.doesNotMatch(root.textContent!, /<tr|<a /, 'a row or a link was taken as text');

        partners = 3;
        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => heldValue(root, 'Roaming partners') === '3', 'Reload did not draw what the EMSP says then');

    });

    it('says why, where the OCPI side cannot be had', async () => {

        broken = true;

        const root = await open(ocpiPage, '/configuration/ocpi', [ 'ocpi:read' ],
                                emsp, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /could not be loaded: .*not there/);

    });

});
