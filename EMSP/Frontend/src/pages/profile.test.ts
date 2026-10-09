/**
 * A driver's profile drawn, in a document of happy-dom, against a stand-in
 * EMSP: the details saved as the whole account with what changed, the password
 * changed at the HTTPExt API - two new ones that differ said without asking
 * anybody - and the account deleted only with the username typed again, and
 * the driver signed out then.
 */

import { asked, field, open, refused, submit, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Account } from '../api/client.ts';

const { auth }         = await import('../auth.ts');
const { profilePage }  = await import('./profile.ts');


let account: Account;

/** Refuses a password change, where told to. */
let wrongPassword = false;

function emsp({ method, path, body }: Asked): unknown {

    if (path === '/ext/users/alice' && method === 'GET')
        return account;

    if (path === '/ext/users/alice' && method === 'SET') {
        account = body as Account;
        return account;
    }

    if (path === '/ext/users/alice/password' && method === 'SET')
        return wrongPassword ? refused(403, 'The current password is wrong!') : {};

    if (path === '/me/delete' && method === 'POST')
        return { message: 'Your account was deleted.' };

    return undefined;

}

async function opened(): Promise<HTMLElement> {

    account       = { '@id': 'alice', '@context': 'https://opendata.social/contexts/users/user', name: { en: 'Alice' },
                      email: 'alice@example.org', language: 'en', somethingElse: 42 };
    wrongPassword = false;

    return open(profilePage, '/profile', [ 'contracts:run', 'tokens:run' ], emsp, root => root.querySelector('#details-form') !== null);

}


describe('a driver\'s profile', () => {

    it('saves the whole account with what changed, and what it does not show as it came', async () => {

        const root = await opened();

        field(root, '#details-form', 'name').value        = 'Alice Driver';
        field(root, '#details-form', 'mobilePhone').value = '+49 170 1234567';
        submit(root, '#details-form');

        await until(() => root.querySelector('#details-note')!.textContent === 'Saved.', 'it was not said to be saved');

        const set = asked.find(one => one.method === 'SET')!.body as Account;

        assert.deepEqual(set.name, { en: 'Alice Driver' });
        assert.equal(set.mobilePhone,    '+49 170 1234567');
        assert.equal(set.email,          'alice@example.org');
        assert.equal(set['somethingElse'], 42, 'what the page does not show was not sent back');

    });

    it('says that two new passwords differ without asking anybody, and what the EMSP refused', async () => {

        const root = await opened();

        field(root, '#password-form', 'current').value   = 'test-only-old';
        field(root, '#password-form', 'password').value  = 'test-only-new';
        field(root, '#password-form', 'password2').value = 'test-only-other';
        submit(root, '#password-form');

        await until(() => root.querySelector('#password-error')!.textContent !== '', 'nothing was said');

        assert.equal(root.querySelector('#password-error')!.textContent, 'The two new passwords differ.');
        assert.equal(asked.filter(one => one.method === 'SET').length, 0);

        wrongPassword = true;
        field(root, '#password-form', 'password2').value = 'test-only-new';
        submit(root, '#password-form');

        await until(() => /wrong/.test(root.querySelector('#password-error')!.textContent!), 'the refusal was not said');

        assert.deepEqual(asked.find(one => one.method === 'SET')!.body, { currentPassword: 'test-only-old', newPassword: 'test-only-new' });

    });

    it('deletes the account only with the username typed again, and signs the driver out', async () => {

        const root = await opened();

        field(root, '#delete-form', 'confirm').value = 'bob';
        submit(root, '#delete-form');

        await until(() => root.querySelector('#delete-error')!.textContent !== '', 'nothing was said');

        assert.equal(asked.filter(one => one.path === '/me/delete').length, 0, 'deleted with somebody else\'s name');

        field(root, '#delete-form', 'confirm').value = 'alice';
        submit(root, '#delete-form');

        await until(() => auth.user === null, 'the driver is still signed in');

        assert.deepEqual(asked.find(one => one.path === '/me/delete')!.body, { username: 'alice' });

    });

});
