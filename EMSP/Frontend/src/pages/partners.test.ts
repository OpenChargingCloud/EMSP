/**
 * The roaming partners drawn, in a document of happy-dom, against a stand-in
 * EMSP: a partner half typed - their token and versions URL too - and its
 * focus outlive another being removed, a registration says it is under way
 * and then what came of it, one the partner refused says so with the list the
 * EMSP sends along, a partner added empties the form and says its token once,
 * the tokens are dotted out until shown, Reload empties the form, and whoever
 * may only look is shown no form.
 */

import { asked, change, field, open, refused, said, submit, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Partner, Partners } from '../api/client.ts';

const { partnersPage } = await import('./partners.ts');


function aPartner(partyId: string, canRegister = false): Partner {
    return { version: '2.2.1', id: `DE-${partyId}_CPO`, countryCode: 'DE', partyId, role: 'CPO', name: `CPO ${partyId}`,
             website: null, status: 'ENABLED', ourToken: `our-token-of-${partyId}`, hasOurToken: true, ourTokenStatus: 'ALLOWED',
             theirToken: canRegister ? `their-token-of-${partyId}` : null, hasTheirToken: canRegister,
             theirVersionsURL: canRegister ? `https://${partyId.toLowerCase()}.example.org/ocpi/versions` : null,
             remoteStatus: null, selectedVersion: null, canRegister, registered: false,
             created: '2026-10-01T00:00:00Z', lastUpdated: '2026-10-01T00:00:00Z' };
}

let held: Partners;

/** Refuses every removal where told to. */
let refuseRemovals = false;

/** How a registration is answered: at once, held back until let go, or refused by the partner. */
let registration: 'answered' | 'held' | 'refused' = 'answered';
let letGo: () => void = () => undefined;

function emsp({ method, path, body }: Asked): unknown {

    if (path === '/ocpi/partners' && method === 'GET')
        return held;

    if (path === '/ocpi/partners' && method === 'POST') {
        const spec = body as { version: string; countryCode: string; partyId: string };
        const id   = `${spec.countryCode}-${spec.partyId}_CPO`;
        held = { ...held, partners: [ ...held.partners, { ...aPartner(spec.partyId), id, version: spec.version } ] };
        return { message: `'${id}' was added.`, id, version: spec.version, ourToken: 'made-up-for-them', partners: held };
    }

    const register = /^\/ocpi\/partners\/([^/]+)\/([^/]+)\/register$/.exec(path);

    if (register && method === 'POST') {

        const id = decodeURIComponent(register[2]!);

        if (registration === 'refused')
            return new Response(JSON.stringify({ ok: false, message: `'${id}' did not take the credentials.`, partners: held }),
                                { status: 502, headers: { 'Content-Type': 'application/json' } });

        const registered = () => {
            held = { ...held, partners: held.partners.map(partner => partner.id === id ? { ...partner, registered: true } : partner) };
            return { ok: true, message: `Registered with '${id}'.`, partners: held };
        };

        return registration === 'held'
                   ? new Promise(resolve => { letGo = () => resolve(registered()); })
                   : registered();

    }

    const one = /^\/ocpi\/partners\/([^/]+)\/([^/]+)$/.exec(path);

    if (one && method === 'DELETE') {

        if (refuseRemovals)
            return refused(500, 'the partners\' file could not be written');

        const id = decodeURIComponent(one[2]!);

        held = { ...held, partners: held.partners.filter(partner => partner.id !== id) };
        return held;

    }

    return undefined;

}

async function opened(permissions = [ 'partners:read', 'partners:edit' ]): Promise<HTMLElement> {

    held = { partners: [ aPartner('AAA'), aPartner('BBB', true) ], versions: [ '2.1.1', '2.2.1' ],
             roles: [ 'CPO', 'HUB' ], ourVersionsURL: 'http://127.0.0.1/ext/versions' };
    refuseRemovals = false;
    registration   = 'answered';

    return open(partnersPage, '/configuration/ocpi/partners', permissions,
                emsp, root => root.querySelector('#partner-list table') !== null);

}

const removeOf   = (root: HTMLElement, id: string) => root.querySelector<HTMLButtonElement>(`.partner-remove[data-id="${id}"]`);
const registerOf = (root: HTMLElement, id: string) => root.querySelector<HTMLButtonElement>(`.partner-register[data-id="${id}"]`);


describe('the roaming partners', () => {

    it('keep a partner half typed, their token too, and its focus, while another is removed', async () => {

        const root       = await opened();
        const startHere  = field(root, '#partner-form', 'startHere');
        const name       = field(root, '#partner-form', 'name');

        name.value = 'Example Charging';

        change(startHere, true);

        await until(() => !root.querySelector<HTMLElement>('#start-here')!.hidden, 'their token and URL were not shown');

        const theirToken = field(root, '#partner-form', 'theirToken');

        assert.equal(theirToken.disabled, false, 'their token is shown and switched off');
        assert.ok(document.activeElement === theirToken, 'ticking the box did not go on to their token');

        theirToken.value = 'handed-out-by-them';

        removeOf(root, 'DE-AAA_CPO')!.click();

        await until(() => removeOf(root, 'DE-AAA_CPO') === null, 'the partner removed was still drawn');

        assert.ok(field(root, '#partner-form', 'theirToken') === theirToken, 'the field was made anew');
        assert.equal(theirToken.value,                                       'handed-out-by-them');
        assert.equal(name.value,                                             'Example Charging');
        assert.equal(root.querySelector<HTMLElement>('#start-here')!.hidden, false, 'removing a partner hid their token');
        assert.ok(document.activeElement === theirToken, 'the focus went');

    });

    it('say a registration is under way, and then what came of it', async () => {

        const root = await opened();

        registration = 'held';
        registerOf(root, 'DE-BBB_CPO')!.click();

        await until(() => registerOf(root, 'DE-BBB_CPO')!.disabled, 'Register was not switched off while it went on');

        assert.equal(registerOf(root, 'DE-BBB_CPO')!.textContent!.trim(), 'Registering ...');

        letGo();

        await until(() => /Registered with 'DE-BBB_CPO'/.test(root.querySelector('#partner-news')!.textContent!),
                    'what came of the registration was not said');

        assert.equal(registerOf(root, 'DE-BBB_CPO')!.disabled, false);
        assert.equal(registerOf(root, 'DE-BBB_CPO')!.textContent!.trim(), 'Register again');
        assert.match(root.querySelector('#partner-list')!.textContent!, /registered/);

    });

    it('say that the partner refused a registration, with the list the EMSP sent along', async () => {

        const root = await opened();

        registration = 'refused';
        registerOf(root, 'DE-BBB_CPO')!.click();

        await until(() => root.querySelector('#partner-news .notice.warn') !== null, 'the refusal was not said');

        assert.match(root.querySelector('#partner-news')!.textContent!, /did not take the credentials/);
        assert.equal(said.length, 0, 'a refusal with its own sentence went to an alert');
        assert.equal(registerOf(root, 'DE-BBB_CPO')!.textContent!.trim(), 'Register');

    });

    it('stay as the EMSP has them when it refused to remove one, and keep what is typed', async () => {

        const root = await opened();
        const name = field(root, '#partner-form', 'name');

        name.value     = 'Example Charging';
        refuseRemovals = true;

        removeOf(root, 'DE-AAA_CPO')!.click();

        await until(() => said.length === 2 && asked.filter(one => one.method === 'GET').length === 2,
                    'the refusal was not said, or the partners not read again');

        assert.match(said[1]!, /could not be written/);
        assert.ok(removeOf(root, 'DE-AAA_CPO'), 'the partner the EMSP kept is gone from the list');
        assert.ok(field(root, '#partner-form', 'name') === name, 'the field was made anew');
        assert.equal(name.value, 'Example Charging');

    });

    it('empty the form once a partner is added, and say its token once', async () => {

        const root = await opened();

        field(root, '#partner-form', 'countryCode').value = 'de';
        field(root, '#partner-form', 'partyId').value     = 'ccc';
        field(root, '#partner-form', 'name').value        = 'Example Charging';
        change(field(root, '#partner-form', 'startHere'), true);

        await until(() => !field(root, '#partner-form', 'theirToken').disabled, 'their token was not switched on');

        field(root, '#partner-form', 'theirToken').value  = 'handed-out-by-them';
        field(root, '#partner-form', 'versionsURL').value = 'https://ccc.example.org/ocpi/versions';

        submit(root, '#partner-form');

        await until(() => removeOf(root, 'DE-CCC_CPO') !== null, 'the partner added was not drawn');

        assert.deepEqual(asked.find(one => one.method === 'POST')?.body,
                         { version: '2.2.1', role: 'CPO', countryCode: 'DE', partyId: 'CCC', name: 'Example Charging',
                           theirToken: 'handed-out-by-them', versionsURL: 'https://ccc.example.org/ocpi/versions' });

        assert.equal(root.querySelector('#partner-news code.password')?.textContent, 'made-up-for-them');
        assert.equal(field(root, '#partner-form', 'name').value,          '', 'the partner added is still in the form');
        assert.equal(field(root, '#partner-form', 'startHere').checked,   false);
        assert.equal(root.querySelector<HTMLElement>('#start-here')!.hidden, true, 'their token is still shown');
        assert.equal(field(root, '#partner-form', 'theirToken').disabled, true, 'their token is hidden and asked for');

    });

    it('dot their tokens out until they are shown', async () => {

        const root = await opened();

        assert.doesNotMatch(root.querySelector('#partner-list')!.textContent!, /our-token-of-AAA/);

        root.querySelector<HTMLButtonElement>('#reveal')!.click();

        await until(() => /our-token-of-AAA/.test(root.querySelector('#partner-list')!.textContent!), 'Show did not show the tokens');

        assert.equal(root.querySelector<HTMLButtonElement>('#reveal')!.textContent!.trim(), 'Hide the tokens');

    });

    it('are read anew on Reload, and the form emptied with them', async () => {

        const root = await opened();
        const name = field(root, '#partner-form', 'name');

        name.value = 'Example Charging';
        change(field(root, '#partner-form', 'startHere'), true);

        held = { ...held, partners: [ ...held.partners, aPartner('ZZZ') ] };

        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => removeOf(root, 'DE-ZZZ_CPO') !== null, 'Reload did not draw what the EMSP says then');

        assert.equal(name.value, '', 'Reload left what was typed');
        assert.equal(root.querySelector<HTMLElement>('#start-here')!.hidden, true, 'Reload left their token shown');

    });

    it('are only looked at by whoever may not change them', async () => {

        const root = await opened([ 'partners:read' ]);

        assert.equal(root.querySelector('#partner-form'), null, 'the form is shown to whoever may not add a partner');
        assert.equal(registerOf(root, 'DE-BBB_CPO'), null, 'Register is offered to whoever may not register');
        assert.equal(removeOf(root, 'DE-AAA_CPO')!.disabled, true, 'Remove is offered to whoever may not remove');

    });

});
