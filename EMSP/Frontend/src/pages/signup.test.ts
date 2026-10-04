/**
 * The sign-up drawn, in a document of happy-dom, against a stand-in EMSP: for
 * nobody signed in, its form - listened to from the template now - says when
 * the two passwords differ without asking anybody, says in its own words why
 * the EMSP refused, and keeps what was typed while it does.
 */

import { asked, field, open, refused, submit, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { signUpPage } = await import('./signup.ts');


let answer: (asked: Asked) => unknown = () => undefined;

function emsp(one: Asked): unknown {
    return answer(one);
}

async function signUpForm(): Promise<HTMLElement> {
    return open(signUpPage, '/signup', null, emsp, root => root.querySelector('#signup-form') !== null);
}

function fill(root: HTMLElement, password2: string): void {
    field(root, '#signup-form', 'username').value     = 'driver1';
    field(root, '#signup-form', 'email').value        = 'driver1@example.org';
    field(root, '#signup-form', 'displayName').value  = 'Driver One';
    field(root, '#signup-form', 'password').value     = 'test-only-1';
    field(root, '#signup-form', 'password2').value    = password2;
}


describe('the sign-up', () => {

    it('says that the two passwords differ, and asks nobody', async () => {

        answer = () => undefined;

        const root = await signUpForm();

        fill(root, 'test-only-2');
        submit(root, '#signup-form');

        await until(() => root.querySelector('#form-error')!.textContent !== '', 'nothing was said');

        assert.equal(root.querySelector('#form-error')!.textContent, 'The two passwords differ.');
        assert.equal(asked.length, 0, 'the EMSP was asked although the passwords differ');

    });

    it('says why the EMSP refused, and keeps what was typed', async () => {

        answer = ({ method, path }) =>
            method === 'POST' && path === '/ext/auth/signup'
                ? refused(409, 'The username is taken.')
                : undefined;

        const root     = await signUpForm();
        const username = field(root, '#signup-form', 'username');

        fill(root, 'test-only-1');
        submit(root, '#signup-form');

        await until(() => root.querySelector('#form-error')!.textContent !== '', 'the refusal was not said');

        assert.match(root.querySelector('#form-error')!.textContent!, /The username is taken/);
        assert.ok(field(root, '#signup-form', 'username') === username, 'the username field was drawn anew');
        assert.equal(username.value, 'driver1');
        assert.equal(root.querySelector<HTMLButtonElement>('#signup-form button[type="submit"]')!.disabled, false,
                     'the button stays switched off after a refusal');

    });

    it('signs up, and asks who is signed in then', async () => {

        answer = ({ method, path }) =>
            method === 'POST' && path === '/ext/auth/signup' ? {} :
            method === 'GET'  && path === '/auth/me'          ? { username: 'driver1', roles: [ 'driver' ], permissions: [ 'contracts:run' ], mayReadTheLog: false } :
            undefined;

        const root = await signUpForm();

        fill(root, 'test-only-1');
        submit(root, '#signup-form');

        await until(() => asked.some(one => one.path === '/auth/me'), 'the EMSP was not asked who is signed in');

        const post = asked.find(one => one.method === 'POST')!;

        assert.deepEqual(post.body, { username: 'driver1', email: 'driver1@example.org', password: 'test-only-1', displayName: 'Driver One' });
        assert.equal(root.querySelector('#form-error')!.textContent, '');

    });

});
