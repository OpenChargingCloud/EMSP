/**
 * A page of roaming data drawn, in a document of happy-dom, against a stand-in
 * EMSP: a row per item with its columns, the whole object behind JSON - and
 * what was opened stays open, with the same item, when Reload draws again
 * with one more pushed before it.
 */

import { open, refused, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { RoamingItem } from '../api/client.ts';

const { roamingDataPages } = await import('./roamingData.ts');


function session(id: string, kwh: number): RoamingItem {
    return { version: '2.2.1', country_code: 'DE', party_id: 'GEF', id, kwh, status: 'ACTIVE',
             start_date_time: '2026-10-04T10:00:00Z', last_updated: '2026-10-04T10:05:00Z' };
}

let sessions: RoamingItem[] = [];
let broken                  = false;

function emsp({ path }: Asked): unknown {

    if (path === '/ocpi/sessions')
        return broken ? refused(500, 'the sessions could not be read') : { kind: 'sessions', items: sessions };

    return undefined;

}

const rawOf = (root: HTMLElement, id: string) =>
    root.querySelector<HTMLTableRowElement>(`tr.raw[data-key="2.2.1/DE-GEF/${id}"]`);

const buttonOf = (root: HTMLElement, id: string) =>
    root.querySelector<HTMLButtonElement>(`button.show-raw[data-key="2.2.1/DE-GEF/${id}"]`);


describe('the charging sessions', () => {

    it('draw a row per session, and the whole of one behind JSON', async () => {

        sessions = [ session('S1', 7.5), session('S2', 11) ];
        broken   = false;

        const root = await open(roamingDataPages.sessions, '/roaming/sessions', [ 'ocpi:read' ],
                                emsp, root => root.querySelector('table') !== null);

        assert.equal(root.querySelectorAll('tbody tr.raw').length, 2);
        assert.match(root.querySelector('h2 .chip')!.textContent!, /^2$/);
        assert.equal(rawOf(root, 'S2')!.hidden, true, 'the whole of a session is shown before it is asked for');

        buttonOf(root, 'S2')!.click();

        await until(() => !rawOf(root, 'S2')!.hidden, 'JSON did not show the session');

        assert.match(rawOf(root, 'S2')!.textContent!, /"id": "S2"/);
        assert.doesNotMatch(rawOf(root, 'S2')!.textContent!, /"version"/, 'the version is a column, not part of the object');
        assert.equal(buttonOf(root, 'S2')!.textContent!.trim(), 'Hide');
        assert.equal(rawOf(root, 'S1')!.hidden, true, 'another session was opened with it');

    });

    it('keep what was opened, with the same session, when Reload draws one more before it', async () => {

        sessions = [ session('S1', 7.5), session('S2', 11) ];
        broken   = false;

        const root = await open(roamingDataPages.sessions, '/roaming/sessions', [ 'ocpi:read' ],
                                emsp, root => root.querySelector('table') !== null);

        buttonOf(root, 'S2')!.click();
        await until(() => !rawOf(root, 'S2')!.hidden, 'JSON did not show the session');

        const opened = rawOf(root, 'S2')!;

        sessions = [ session('S0', 3), session('S1', 7.5), session('S2', 12) ];
        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => root.querySelectorAll('tbody tr.raw').length === 3, 'Reload did not draw the new session');

        assert.ok(rawOf(root, 'S2') === opened, 'the opened row was drawn anew');
        assert.equal(rawOf(root, 'S2')!.hidden, false, 'what was opened was closed by Reload');
        assert.match(rawOf(root, 'S2')!.textContent!, /"kwh": 12/, 'the opened row does not show what the EMSP says now');
        assert.equal(rawOf(root, 'S0')!.hidden, true, 'the new session came in opened');
        assert.equal(rawOf(root, 'S1')!.hidden, true, 'the session now where the opened one was came in opened');

    });

    it('say why, where they cannot be had', async () => {

        broken = true;

        const root = await open(roamingDataPages.sessions, '/roaming/sessions', [ 'ocpi:read' ],
                                emsp, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /charging sessions could not be loaded: .*could not be read/);

    });

});
