/**
 * "/" drawn, in a document of happy-dom, against a stand-in EMSP: for nobody
 * signed in, a welcome with the way in for a driver and for root, asking
 * nobody anything; for somebody signed in, where they are going - an operator
 * to the configuration, a driver to what they charged.
 */

import { asked, open } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { auth }      = await import('../auth.ts');
const { homePage }  = await import('./home.ts');


/** Where "/" sends somebody with these permissions. */
function whereTo(permissions: string[]): string | null {

    auth.set({ username: 'alice', roles: [], permissions, mayReadTheLog: false } as never);

    let went: string | null = null;

    homePage.render({ root: document.createElement('div'), url: new URL('http://127.0.0.1/'), params: {},
                      navigate: (to: string) => { went = to; } } as never);

    return went;

}


describe('the welcome', () => {

    it('shows a driver the way to sign up and in, and root the way in, asking nobody', async () => {

        const root = await open(homePage, '/', null, () => undefined, root => root.querySelector('#welcome') !== null);

        assert.equal(root.querySelector('#to-signup')!.getAttribute('href'), '/signup');
        assert.equal(root.querySelector('#to-signin')!.getAttribute('href'), '/login');
        assert.equal(root.querySelector('#to-root')!.getAttribute('href'),   '/login');
        assert.match(root.querySelector('#for-operators')!.textContent!, /root/);
        assert.equal(asked.length, 0, 'the welcome asked the EMSP something');

    });

    it('sends an operator to the configuration, and a driver to what they charged', () => {

        assert.equal(whereTo([ 'configuration:read', 'tokens:edit' ]), '/configuration');
        assert.equal(whereTo([ 'contracts:run', 'tokens:run' ]),       '/charging');
        assert.equal(whereTo([ 'contracts:run' ]),                     '/charging');

    });

});
