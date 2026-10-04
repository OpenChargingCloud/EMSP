/**
 * The tokens drawn, in a document of happy-dom, against a stand-in EMSP: a
 * token half typed - and its focus - outlives another being taken away, a
 * removal the EMSP refused leaves the list as the EMSP has it, a token issued
 * empties the form and says so, Reload empties it as well, and whoever may
 * only look is shown no form.
 */

import { asked, field, open, refused, said, submit, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Token, Tokens } from '../api/client.ts';

const { tokensPage } = await import('./tokens.ts');


function aToken(uid: string, version = '2.2.1'): Token {
    return { version, uid, status: 'ALLOWED', type: 'RFID', contract_id: `DE-GDF-${uid}`, issuer: 'GraphDefined',
             valid: true, whitelist: 'ALLOWED', last_updated: '2026-10-04T10:00:00Z' };
}

let held: Tokens;

/** Refuses every removal where told to. */
let refuseRemovals = false;

function emsp({ method, path, body }: Asked): unknown {

    if (path === '/ocpi/tokens' && method === 'GET')
        return held;

    if (path === '/ocpi/tokens' && method === 'POST') {
        const spec = body as { version: string; uid: string };
        held = { ...held, tokens: [ ...held.tokens, aToken(spec.uid, spec.version) ] };
        return { message: `The token '${spec.uid}' was issued on OCPI ${spec.version}.`, uid: spec.uid, version: spec.version, tokens: held };
    }

    const one = /^\/ocpi\/tokens\/([^/]+)\/([^/]+)$/.exec(path);

    if (one && method === 'DELETE') {

        if (refuseRemovals)
            return refused(500, 'the token file could not be written');

        held = { ...held, tokens: held.tokens.filter(token => !(token.version === one[1] && token.uid === one[2])) };
        return held;

    }

    return undefined;

}

async function opened(permissions = [ 'tokens:read', 'tokens:edit' ]): Promise<HTMLElement> {

    held = { tokens: [ aToken('CARD1'), aToken('CARD2') ], versions: [ '2.1.1', '2.2.1' ],
             types: [ 'RFID', 'APP_USER', 'OTHER' ], whitelists: [ 'ALWAYS', 'ALLOWED', 'ALLOWED_OFFLINE', 'NEVER' ],
             issuer: 'GraphDefined', partyId: 'DE-GDF' };
    refuseRemovals = false;

    return open(tokensPage, '/configuration/ocpi/tokens', permissions,
                emsp, root => root.querySelector('#token-list table') !== null);

}

const removeOf = (root: HTMLElement, uid: string) => root.querySelector<HTMLButtonElement>(`.token-remove[data-uid="${uid}"]`);
const rowsOf   = (root: HTMLElement) => root.querySelectorAll('#token-list tbody tr').length;


describe('the tokens', () => {

    it('keep a token half typed, and its focus, while another is taken away', async () => {

        const root   = await opened();
        const uid    = field(root, '#token-form', 'uid');
        const visual = field(root, '#token-form', 'visualNumber');

        uid.value    = 'CARD3';
        visual.value = 'No. 3';
        visual.focus();

        removeOf(root, 'CARD1')!.click();

        await until(() => rowsOf(root) === 1, 'the token taken away was still drawn');

        assert.equal(said.length, 1, 'taking a token away did not ask first');
        assert.ok(field(root, '#token-form', 'visualNumber') === visual, 'the field was made anew');
        assert.equal(uid.value,    'CARD3');
        assert.equal(visual.value, 'No. 3');
        assert.ok(document.activeElement === visual, 'the focus went');

    });

    it('stay as the EMSP has them when it refused to take one away, and keep what is typed', async () => {

        const root = await opened();
        const uid  = field(root, '#token-form', 'uid');

        uid.value      = 'CARD3';
        refuseRemovals = true;

        removeOf(root, 'CARD1')!.click();

        await until(() => said.length === 2 && asked.filter(one => one.method === 'GET').length === 2,
                    'the refusal was not said, or the tokens not read again');

        assert.match(said[1]!, /could not be written/);
        assert.equal(rowsOf(root), 2, 'the token the EMSP kept is gone from the list');
        assert.ok(removeOf(root, 'CARD1'), 'the token the EMSP kept is gone from the list');
        assert.ok(field(root, '#token-form', 'uid') === uid, 'the field was made anew');
        assert.equal(uid.value, 'CARD3');

    });

    it('empty the form once a token is issued, and say so', async () => {

        const root = await opened();

        field(root, '#token-form', 'uid').value          = 'CARD3';
        field(root, '#token-form', 'visualNumber').value = 'No. 3';
        field<HTMLSelectElement>(root, '#token-form', 'version').value   = '2.1.1';
        field<HTMLSelectElement>(root, '#token-form', 'whitelist').value = 'NEVER';

        submit(root, '#token-form');

        await until(() => removeOf(root, 'CARD3') !== null, 'the token issued was not drawn');

        const post = asked.find(one => one.method === 'POST')!.body as Record<string, unknown>;

        assert.equal(post['uid'],          'CARD3');
        assert.equal(post['version'],      '2.1.1');
        assert.equal(post['whitelist'],    'NEVER');
        assert.equal(post['visualNumber'], 'No. 3');

        assert.match(root.querySelector('#token-note')!.textContent!, /CARD3' was issued on OCPI 2\.1\.1/);
        assert.equal(field(root, '#token-form', 'uid').value,                              '', 'the token issued is still in the form');
        assert.equal(field(root, '#token-form', 'visualNumber').value,                     '');
        assert.equal(field<HTMLSelectElement>(root, '#token-form', 'version').value,       '2.2.1', 'the version is not the newest again');
        assert.equal(field<HTMLSelectElement>(root, '#token-form', 'whitelist').value,     'ALLOWED');

    });

    it('are read anew on Reload, and the form emptied with them', async () => {

        const root = await opened();
        const uid  = field(root, '#token-form', 'uid');

        uid.value = 'CARD3';
        held      = { ...held, tokens: [ ...held.tokens, aToken('CARD9') ] };

        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => removeOf(root, 'CARD9') !== null, 'Reload did not draw what the EMSP says then');

        assert.equal(uid.value, '', 'Reload left what was typed');

    });

    it('are only looked at by whoever may not change them', async () => {

        const root = await opened([ 'tokens:read' ]);

        assert.equal(root.querySelector('#token-form'), null, 'the form is shown to whoever may not issue');
        assert.match(root.querySelector('.notice')!.textContent!, /look at the tokens/);
        assert.equal(removeOf(root, 'CARD1')!.disabled, true, 'Remove is offered to whoever may not take a token away');

    });

});
