/**
 * The configuration drawn, in a document of happy-dom, against a stand-in
 * EMSP: its cards - cardViews.ts's, in a template of view.ts - stand as the
 * markup they are, the OCPI card names the party, and Reload draws them again
 * with what the EMSP says then.
 */

import { open, refused, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { configurationPage } = await import('./configuration.ts');


let uptime = '1 minute';
let broken = false;

function emsp({ path }: Asked): unknown {

    if (broken)
        return refused(500, 'the configuration file could not be read');

    if (path === '/status')
        return { service: 'EMSP', version: '1.0', hermod: null, timestamp: '2026-10-04T12:00:00Z',
                 startedAt: '2026-10-04T11:59:00Z', uptime, sessions: 1, log: { entries: 0, capacity: 100, lastId: 0, tags: [] },
                 partyId: 'DE*GDF' };

    if (path === '/configuration')
        return { EMSP: { name: 'GraphDefined EMSP' }, http: { port: 8080 }, web: {}, log: {}, time: {},
                 ocpi: { partyId: 'DE*GDF', versions: [ '2.1.1', '2.2.1' ] }, contracts: { moRoot: 'none' },
                 assemblies: [ { name: 'EMSP', version: '1.0', commit: 'abc1234' } ] };

    return undefined;

}


describe('the configuration', () => {

    it('draws its cards as markup, and again on Reload', async () => {

        uptime = '1 minute';
        broken = false;

        const root = await open(configurationPage, '/configuration', [ 'configuration:read' ],
                                emsp, root => root.querySelector('.cards') !== null);

        assert.equal(root.querySelectorAll('.cards > section.card').length, 8);
        assert.match(root.querySelector('.cards')!.textContent!, /OCPI - DE\*GDF/);
        assert.match(root.querySelector('.cards')!.textContent!, /Uptime\s+1 minute/);
        assert.match(root.querySelector('.cards')!.textContent!, /GraphDefined EMSP/);
        assert.doesNotMatch(root.textContent!, /<section/, 'a card was taken as text');

        uptime = '2 minutes';
        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => /Uptime\s+2 minutes/.test(root.textContent!), 'Reload did not draw what the EMSP says then');

        assert.equal(root.querySelectorAll('.cards > section.card').length, 8);

    });

    it('says why, where the configuration cannot be had', async () => {

        broken = true;

        const root = await open(configurationPage, '/configuration', [ 'configuration:read' ],
                                emsp, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /could not be loaded: .*could not be read/);
        assert.equal(root.querySelector('.cards'), null);

    });

});
