/**
 * The RFID cards drawn, in a document of happy-dom, against a stand-in EMSP: a
 * driver enters a card and the form is emptied, blocks one and lets it charge
 * again, and is told why the EMSP refused; whoever may manage the tokens sees
 * every driver's, how many wait, and lets one in or turns it down with a
 * reason for its driver.
 */

import { asked, field, open, refused, said, submit, until, type Asked } from '../../test/controller.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

import type { Card, CardState, Cards } from '../api/client.ts';

const { cardsPage } = await import('./cards.ts');


function aCard(uid: string, state: CardState, owner = 'alice'): Card {
    return { uid, owner, label: null, state, requestedAt: '2026-10-09T10:00:00Z', decidedAt: null, decidedBy: null,
             reason: null, contractId: null, changedAt: null, changedBy: null };
}

let held: Cards;

/** Refuses every card entered, where told to. */
let refuseCards = false;

function withState(uid: string, state: CardState, more: Partial<Card> = {}): Cards {
    return { ...held, cards: held.cards.map(card => card.uid === uid ? { ...card, state, ...more } : card),
             waiting: held.cards.filter(card => card.uid !== uid && card.state === 'requested').length + (state === 'requested' ? 1 : 0) };
}

function emsp({ method, path, body }: Asked): unknown {

    if (path === '/cards' && method === 'GET')
        return held;

    if (path === '/cards' && method === 'POST') {

        if (refuseCards)
            return refused(400, 'The card 04A2B3C4D5E6F7 is somebody else\'s.');

        const { uid, label } = body as { uid: string; label?: string };
        const card = { ...aCard(uid.replaceAll(':', '').toUpperCase(), 'requested'), label: label ?? null };
        held = { ...held, cards: [ card, ...held.cards ] };
        return { message: `The card ${card.uid} was entered.`, card, cards: held };

    }

    const one = /^\/cards\/([^/]+)(?:\/(approve|reject|block|unblock))?$/.exec(path);

    if (one && method === 'POST') {
        const [ , uid, how ] = one;
        held = how === 'approve' ? withState(uid!, 'active')
             : how === 'reject'  ? withState(uid!, 'rejected', { reason: (body as { reason?: string }).reason ?? null })
             : how === 'block'   ? withState(uid!, 'blocked')
             :                     withState(uid!, 'active');
        return { message: 'done', cards: held };
    }

    if (one && method === 'DELETE') {
        held = { ...held, cards: held.cards.filter(card => card.uid !== one[1]) };
        return { message: 'removed', cards: held };
    }

    return undefined;

}

async function opened(cards: Card[], everyone: boolean, permissions: string[]): Promise<HTMLElement> {

    held        = { everyone, issuer: 'GraphDefined', waiting: cards.filter(card => card.state === 'requested').length, cards };
    refuseCards = false;

    return open(cardsPage, '/cards', permissions, emsp, root => root.querySelector('#card-list h2') !== null);

}

const rowOf    = (root: HTMLElement, uid: string) => root.querySelector<HTMLTableRowElement>(`#card-list tr[data-uid="${uid}"]`);
const buttonOf = (root: HTMLElement, uid: string, what: string) => rowOf(root, uid)?.querySelector<HTMLButtonElement>(`.card-${what}`) ?? null;


describe('a driver\'s cards', () => {

    it('enter a card, say so, and empty the form for the next', async () => {

        const root = await opened([], false, [ 'tokens:run', 'contracts:run' ]);

        field(root, '#card-form', 'uid').value   = '04:a2:b3:c4:d5:e6:f7';
        field(root, '#card-form', 'label').value = 'Blue keyring';
        submit(root, '#card-form');

        await until(() => rowOf(root, '04A2B3C4D5E6F7') !== null, 'the card entered was not listed');

        assert.deepEqual(asked.find(one => one.method === 'POST')!.body, { uid: '04:a2:b3:c4:d5:e6:f7', label: 'Blue keyring' });
        assert.match(rowOf(root, '04A2B3C4D5E6F7')!.textContent!, /waits to be let in/);
        assert.match(root.querySelector('#card-note')!.textContent!, /was entered/);
        assert.equal(field(root, '#card-form', 'uid').value, '', 'the form was not emptied');
        assert.equal(buttonOf(root, '04A2B3C4D5E6F7', 'approve'), null, 'a driver was offered to let their own card in');

    });

    it('say why the EMSP refused a card, and keep what was typed', async () => {

        const root = await opened([], false, [ 'tokens:run' ]);

        refuseCards = true;

        field(root, '#card-form', 'uid').value = '04A2B3C4D5E6F7';
        submit(root, '#card-form');

        await until(() => root.querySelector('#card-error')!.textContent !== '', 'the refusal was not said');

        assert.match(root.querySelector('#card-error')!.textContent!, /somebody else's/);
        assert.equal(field(root, '#card-form', 'uid').value, '04A2B3C4D5E6F7');

    });

    it('block a card that charges, asking first, and let it charge again', async () => {

        const root = await opened([ aCard('04A2B3C4D5E6F7', 'active') ], false, [ 'tokens:run' ]);

        buttonOf(root, '04A2B3C4D5E6F7', 'block')!.click();

        await until(() => buttonOf(root, '04A2B3C4D5E6F7', 'unblock') !== null, 'the card was not blocked');

        assert.match(said.join('\n'), /Block the card 04A2B3C4D5E6F7/);
        assert.ok(asked.some(one => one.method === 'POST' && one.path === '/cards/04A2B3C4D5E6F7/block'));

        buttonOf(root, '04A2B3C4D5E6F7', 'unblock')!.click();

        await until(() => buttonOf(root, '04A2B3C4D5E6F7', 'block') !== null, 'the card does not charge again');

    });

});


describe('every driver\'s cards', () => {

    it('say how many wait, and let one in', async () => {

        const root = await opened([ aCard('04A2B3C4D5E6F7', 'requested'), aCard('0A0B0C0D', 'active', 'bobby') ],
                                  true, [ 'tokens:read', 'tokens:edit' ]);

        assert.match(root.querySelector('#cards-waiting')!.textContent!, /One card waits/);
        assert.equal(root.querySelector('#card-form'), null, 'whoever only manages was offered to bring a card');
        assert.match(rowOf(root, '0A0B0C0D')!.textContent!, /bobby/);

        buttonOf(root, '04A2B3C4D5E6F7', 'approve')!.click();

        await until(() => /charges/.test(rowOf(root, '04A2B3C4D5E6F7')!.textContent!), 'the card was not let in');

        assert.equal(root.querySelector('#cards-waiting'), null, 'still said to wait');

    });

    it('turn one down with a reason for its driver, and ask nobody where the reason is not given', async () => {

        const root = await opened([ aCard('04A2B3C4D5E6F7', 'requested') ], true, [ 'tokens:edit' ]);

        window.prompt = () => null;
        buttonOf(root, '04A2B3C4D5E6F7', 'reject')!.click();
        await new Promise(resolve => setTimeout(resolve, 20));

        assert.equal(asked.filter(one => one.method === 'POST').length, 0, 'turned down although the question was cancelled');

        window.prompt = () => 'Not a card of ours.';
        buttonOf(root, '04A2B3C4D5E6F7', 'reject')!.click();

        await until(() => root.querySelector('.card-reason') !== null, 'the reason was not shown');

        assert.deepEqual(asked.find(one => one.method === 'POST')!.body, { reason: 'Not a card of ours.' });
        assert.equal(root.querySelector('.card-reason')!.textContent, 'Not a card of ours.');

    });

});
