/**
 * The contracts drawn, in a document of happy-dom, against a stand-in EMSP:
 * two passwords typed for the next contract - and the focus - outlive another
 * being revoked, a revocation the EMSP refused leaves the list as the EMSP
 * has it, a contract the EMSP would not make says why and keeps the form, the
 * MO root shown stays shown, Reload empties the form, and a driver may revoke
 * only what is theirs.
 *
 * The passwords here are test values for a form in a document of happy-dom;
 * nothing is signed in and nothing leaves the process.
 */

import { asked, field, open, refused, said, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Contract, Contracts } from '../api/client.ts';

const { contractsPage } = await import('./contracts.ts');


function aContract(emaId: string, owner = 'alice'): Contract {
    return { emaId, emaIdCompact: emaId.replace(/-/g, ''), owner, serialNumber: '01', thumbprint: 'ab',
             notBefore: '2026-10-01T00:00:00Z', notAfter: '2027-10-01T00:00:00Z', issuedAt: '2026-10-01T00:00:00Z',
             revokedAt: null, revokedBy: null, status: 'valid', file: `${emaId}.pem` };
}

let held: Contracts;

/** Refuses every revocation where told to. */
let refuseRevocations = false;

/** Makes a contract where told to, and refuses to otherwise. */
let issues = false;

/** A certificate as far as this page looks at one: some bytes, as PEM. */
const someCertificate = '-----BEGIN CERTIFICATE-----\nMIIBAgMEBQYHCAk=\n-----END CERTIFICATE-----';

function emsp({ method, path }: Asked): unknown {

    if (path === '/contracts' && method === 'GET')
        return held;

    if (path === '/contracts' && method === 'POST') {

        if (!issues)
            return refused(503, 'the MO sub-CA is not there');

        const made = aContract('DE-GDF-C3');

        held = { ...held, contracts: [ ...held.contracts, made ] };

        return { message: 'The contract DE-GDF-C3 was issued.', contract: made, certificate: someCertificate,
                 chain: '', moRoot: held.moRoot.pem, contracts: held };

    }

    const one = /^\/contracts\/([^/]+)\/revoke$/.exec(path);

    if (one && method === 'POST') {

        if (refuseRevocations)
            return refused(500, 'the contract file could not be written');

        const emaId = decodeURIComponent(one[1]!);

        held = { ...held, contracts: held.contracts.map(contract => contract.emaId === emaId
                                                                        ? { ...contract, status: 'revoked', revokedAt: '2026-10-04T12:00:00Z', revokedBy: 'alice' }
                                                                        : contract) };
        return { message: `The contract ${emaId} was revoked.`, contract: held.contracts[0], contracts: held };

    }

    return undefined;

}

async function opened(permissions = [ 'contracts:run', 'contracts:edit' ], everyone = true): Promise<HTMLElement> {

    held = { party: 'DE-GDF', issuer: 'GraphDefined', validityDays: 365, signUp: true, everyone,
             moRoot: { subject: 'CN=MO root', fingerprint: 'AB:CD', notAfter: '2030-01-01T00:00:00Z',
                       pem: '-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----', file: 'mo-root.pem' },
             chain: '', directory: 'contracts',
             contracts: [ aContract('DE-GDF-C1'), aContract('DE-GDF-C2', 'bob') ] };
    refuseRevocations = false;
    issues            = false;

    return open(contractsPage, '/contracts', permissions,
                emsp, root => root.querySelector('#contract-list') !== null);

}

const revokeOf = (root: HTMLElement, emaId: string) => root.querySelector<HTMLButtonElement>(`.contract-revoke[data-emaid="${emaId}"]`);
const rowOf    = (root: HTMLElement, emaId: string) => revokeOf(root, emaId)?.closest('tr') ?? null;


describe('the contracts', () => {

    it('keep two passwords typed for the next contract, and the focus, while another is revoked', async () => {

        const root      = await opened();
        const password  = field(root, '#contract-form', 'password');
        const password2 = field(root, '#contract-form', 'password2');

        password.value  = 'test-only-1';
        password2.value = 'test-only-1';
        password2.focus();

        revokeOf(root, 'DE-GDF-C1')!.click();

        await until(() => rowOf(root, 'DE-GDF-C1')?.classList.contains('dimmed') === true, 'the revoked contract was not drawn so');

        assert.equal(said.length, 1, 'revoking did not ask first');
        assert.ok(field(root, '#contract-form', 'password2') === password2, 'the field was made anew');
        assert.equal(password.value,  'test-only-1');
        assert.equal(password2.value, 'test-only-1');
        assert.ok(document.activeElement === password2, 'the focus went');

    });

    it('stay as the EMSP has them when it refused to revoke one, and keep what is typed', async () => {

        const root     = await opened();
        const password = field(root, '#contract-form', 'password');

        password.value    = 'test-only-1';
        refuseRevocations = true;

        revokeOf(root, 'DE-GDF-C1')!.click();

        await until(() => said.length === 2 && asked.filter(one => one.method === 'GET').length === 2,
                    'the refusal was not said, or the contracts not read again');

        assert.match(said[1]!, /could not be written/);
        assert.equal(rowOf(root, 'DE-GDF-C1')!.classList.contains('dimmed'), false, 'the contract the EMSP kept is drawn revoked');
        assert.ok(field(root, '#contract-form', 'password') === password, 'the field was made anew');
        assert.equal(password.value, 'test-only-1');

    });

    it('say why the EMSP would not make a contract, and keep the form', async () => {

        const root     = await opened();
        const password = field(root, '#contract-form', 'password');

        password.value                                         = 'test-only-1';
        field(root, '#contract-form', 'password2').value       = 'test-only-1';

        root.querySelector<HTMLFormElement>('#contract-form')!.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));

        await until(() => root.querySelector('#contract-error')!.textContent !== '', 'the refusal was not said');

        assert.match(root.querySelector('#contract-error')!.textContent!, /sub-CA is not there/);
        assert.equal(root.querySelector<HTMLElement>('#contract-done')!.hidden, true, 'a contract is said to be made');
        assert.equal(root.querySelector<HTMLButtonElement>('#contract-form button[type="submit"]')!.disabled, false);
        assert.ok(field(root, '#contract-form', 'password') === password, 'the field was made anew');
        assert.equal(password.value, 'test-only-1');

    });

    it('make a contract: the request signed here, the form emptied, and what to do with it said', async () => {

        const root = await opened();

        issues = true;

        field(root, '#contract-form', 'password').value  = 'test-only-1';
        field(root, '#contract-form', 'password2').value = 'test-only-1';

        root.querySelector<HTMLFormElement>('#contract-form')!.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));

        // The key is wrapped with 100,000 rounds of PBKDF2 - more than the
        // second until() waits by default while every test file runs at once.
        await until(() => !root.querySelector<HTMLElement>('#contract-done')!.hidden, 'the contract made was not said', 10_000);

        const csr = (asked.find(one => one.method === 'POST')!.body as { csr: string }).csr;

        assert.match(csr, /^-----BEGIN CERTIFICATE REQUEST-----/, 'the request was not signed here');
        assert.match(root.querySelector('#contract-done')!.textContent!, /Your contract is DE-GDF-C3/);
        assert.match(root.querySelector('#contract-note')!.textContent!, /DE-GDF-C3 was issued/);
        assert.ok(revokeOf(root, 'DE-GDF-C3'), 'the contract made is not in the list');
        assert.equal(field(root, '#contract-form', 'password').value,  '', 'the password stayed in the form');
        assert.equal(field(root, '#contract-form', 'password2').value, '', 'the password stayed in the form');
        assert.equal(root.querySelector<HTMLButtonElement>('#contract-form button[type="submit"]')!.disabled, false,
                     'the button stays switched off for the next contract');

        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => root.querySelector<HTMLElement>('#contract-done')!.hidden === true, 'Reload still says the contract made');

    });

    it('say that the two passwords differ, and ask nobody', async () => {

        const root = await opened();

        field(root, '#contract-form', 'password').value  = 'test-only-1';
        field(root, '#contract-form', 'password2').value = 'test-only-2';

        root.querySelector<HTMLFormElement>('#contract-form')!.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));

        await until(() => root.querySelector('#contract-error')!.textContent !== '', 'nothing was said');

        assert.equal(root.querySelector('#contract-error')!.textContent, 'The two passwords differ.');
        assert.equal(asked.filter(one => one.method === 'POST').length, 0);

    });

    it('keep the MO root shown while a contract is revoked', async () => {

        const root = await opened();

        assert.equal(root.querySelector<HTMLElement>('#root-pem')!.hidden, true, 'the PEM is shown before it is asked for');

        root.querySelector<HTMLButtonElement>('#show-root')!.click();
        await until(() => !root.querySelector<HTMLElement>('#root-pem')!.hidden, 'Show did not show the PEM');

        revokeOf(root, 'DE-GDF-C1')!.click();
        await until(() => rowOf(root, 'DE-GDF-C1')?.classList.contains('dimmed') === true, 'the revoked contract was not drawn so');

        assert.equal(root.querySelector<HTMLElement>('#root-pem')!.hidden, false, 'revoking hid the MO root');

    });

    it('are read anew on Reload, and the form emptied with them', async () => {

        const root     = await opened();
        const password = field(root, '#contract-form', 'password');

        password.value = 'test-only-1';
        held           = { ...held, contracts: [ ...held.contracts, aContract('DE-GDF-C9') ] };

        root.querySelector<HTMLButtonElement>('#reload')!.click();

        await until(() => revokeOf(root, 'DE-GDF-C9') !== null, 'Reload did not draw what the EMSP says then');

        assert.equal(password.value, '', 'Reload left what was typed');

    });

    it('let a driver revoke only their own', async () => {

        const root = await opened([ 'contracts:run' ], false);

        assert.equal(revokeOf(root, 'DE-GDF-C1')!.disabled, false, 'a driver cannot revoke their own contract');
        assert.equal(revokeOf(root, 'DE-GDF-C2')!.disabled, true,  'a driver may revoke somebody else\'s contract');

    });

});
