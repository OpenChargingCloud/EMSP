import { api, type Card, type CardChange, type Cards } from '../api/client';
import { auth } from '../auth';
import { toTheMinute } from '../time';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage, field, formatTimestamp } from '@node/ui';
import { anyFormTypedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * The RFID cards: what a driver brings, and - for whoever may manage the
 * tokens - every driver's, to let in or turn down.
 *
 * A card a driver enters opens nothing until the operator lets it in; then it
 * is a token on every OCPI version this EMSP speaks. Its driver may block it -
 * lost, say - and let it charge again, and take it away.
 */
export const cardsPage: Page = {

    title: 'RFID cards',

    render({ root }) {

        const mayBring   = auth.can('tokens', 'run');
        const mayManage  = auth.can('tokens', 'edit');

        const content = shell(root, {
            active:    '/cards',
            title:     'RFID cards',
            subtitle:  mayManage
                           ? 'The cards the drivers brought: let them in, or turn them down.'
                           : 'Your RFID cards: what you hold up to a charging station.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        let cancelled = false;
        let store: Cards | null = null;


        /**
         * The whole page, whenever the cards changed: a draw changes only what
         * differs, so a card half typed into the form - and the focus - stays.
         */
        function draw(): void {

            if (store === null)
                return;

            render(content, html`
                <div class="cards">
                    ${mayBring ? addCard() : nothing}
                    <section class="card wide" id="card-list">${list(store)}</section>
                </div>
            `);

        }


        function addCard(): TemplateResult {

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-plus"></i> Bring a card</h2>

                    <p class="hint">
                        The UID is what a card reader shows of it - 8, 14 or 20 hex digits, such as
                        04:A2:B3:C4:D5:E6:F7. The card charges once this EMSP's operator lets it in.
                    </p>

                    <form id="card-form" class="form-stack" @submit=${add}>

                        <div class="form-grid">
                            <label>UID
                                <input type="text" name="uid" required maxlength="40" autocomplete="off" spellcheck="false"
                                       placeholder="04:A2:B3:C4:D5:E6:F7" />
                            </label>
                            <label>Label
                                <input type="text" name="label" maxlength="64" placeholder="optional, e.g. Blue keyring" />
                            </label>
                        </div>

                        <div class="form-actions">
                            <button type="submit" class="btn primary">Enter the card</button>
                            <span id="card-note"  class="form-notice" role="status"></span>
                            <span id="card-error" class="form-error"  role="alert"></span>
                        </div>

                    </form>

                </section>
            `;

        }


        function list(cards: Cards): TemplateResult {

            return html`

                <h2><i class="fa-solid fa-id-card"></i> ${cards.everyone ? 'Every driver\'s cards' : 'Your cards'}</h2>

                ${cards.everyone && cards.waiting > 0 ? html`
                    <div class="notice warn" id="cards-waiting">
                        ${cards.waiting === 1 ? 'One card waits' : `${cards.waiting} cards wait`} to be let in.
                    </div>
                ` : nothing}

                ${cards.cards.length === 0
                      ? html`<p class="muted">${cards.everyone ? 'No driver brought a card yet.' : 'You have not brought a card yet.'}</p>`
                      : html`
                          <div class="table-scroll">
                              <table class="table">
                                  <thead>
                                      <tr>
                                          <th>UID</th>
                                          ${cards.everyone ? html`<th>Driver</th>` : nothing}
                                          <th>State</th>
                                          <th>Since</th>
                                          <th></th>
                                      </tr>
                                  </thead>
                                  <tbody>
                                      ${cards.cards.map(card => row(card, cards.everyone))}
                                  </tbody>
                              </table>
                          </div>
                      `}

            `;

        }


        function row(card: Card, everyone: boolean): TemplateResult {

            const mine = card.owner.toLowerCase() === (auth.user?.username ?? '').toLowerCase();

            return html`
                <tr data-uid="${card.uid}" class="${card.state === 'blocked' || card.state === 'rejected' ? 'dimmed' : ''}">
                    <td>
                        <code>${card.uid}</code>
                        ${card.label ? html`<div class="small muted">${card.label}</div>` : nothing}
                    </td>
                    ${everyone ? html`<td>${card.owner}</td>` : nothing}
                    <td>
                        <span class="badge ${badgeOf(card)}">${wordFor(card)}</span>
                        ${card.state === 'rejected' && card.reason ? html`<div class="small muted card-reason">${card.reason}</div>` : nothing}
                    </td>
                    <td class="small muted">${formatTimestamp(card.changedAt ?? card.decidedAt ?? card.requestedAt, toTheMinute)}</td>
                    <td class="right">
                        ${card.state === 'requested' && mayManage ? html`
                            <button type="button" class="btn small primary card-approve" @click=${() => void change(card, 'approve')}>Let in</button>
                            <button type="button" class="btn small card-reject"          @click=${() => void change(card, 'reject')}>Turn down</button>
                        ` : nothing}
                        ${card.state === 'active' && (mine || mayManage) ? html`
                            <button type="button" class="btn small card-block"   @click=${() => void change(card, 'block')}>Block</button>
                        ` : nothing}
                        ${card.state === 'blocked' && (mine || mayManage) ? html`
                            <button type="button" class="btn small card-unblock" @click=${() => void change(card, 'unblock')}>Let charge again</button>
                        ` : nothing}
                        ${mine || mayManage ? html`
                            <button type="button" class="btn small danger card-remove" @click=${() => void change(card, 'remove')}>Remove</button>
                        ` : nothing}
                    </td>
                </tr>
            `;

        }


        function add(event: SubmitEvent): void {

            event.preventDefault();

            void bring(event.currentTarget as HTMLFormElement);

        }


        async function bring(form: HTMLFormElement): Promise<void> {

            const error = must<HTMLElement>(content, '#card-error');
            const note  = must<HTMLElement>(content, '#card-note');

            error.textContent = '';
            note.textContent  = '';

            try
            {

                const answer = await api.cards.add(field(form, 'uid'), field(form, 'label'));

                if (cancelled)
                    return;

                store = answer.cards;
                draw();

                // A draw leaves a form as it is typed into; this card was
                // entered, so the form is emptied for the next.
                form.reset();

                note.textContent = answer.message;

            }
            catch (problem)
            {
                if (!cancelled)
                    error.textContent = errorMessage(problem);
            }

        }


        async function change(card: Card, how: 'approve' | 'reject' | 'block' | 'unblock' | 'remove'): Promise<void> {

            let reason = '';

            if (how === 'reject') {

                const typed = window.prompt(`Turn the card ${card.uid} of '${card.owner}' down?\n\nWhy, in a few words for its driver (optional):`, '');

                if (typed === null)
                    return;

                reason = typed;

            }

            if (how === 'block' && !window.confirm(`Block the card ${card.uid}?\n\nIt charges nowhere until it is let charge again. A partner that holds a copy of the tokens learns of it when it next fetches them.`))
                return;

            if (how === 'remove' && !window.confirm(`Remove the card ${card.uid}?\n\nIts token is taken away; to charge with it again, it has to be entered and let in anew.`))
                return;

            try
            {

                let answer: CardChange;

                switch (how) {
                    case 'approve':  answer = await api.cards.approve(card.uid);         break;
                    case 'reject':   answer = await api.cards.reject (card.uid, reason); break;
                    case 'block':    answer = await api.cards.block  (card.uid);         break;
                    case 'unblock':  answer = await api.cards.unblock(card.uid);         break;
                    case 'remove':   answer = await api.cards.remove (card.uid);         break;
                }

                if (cancelled)
                    return;

                store = answer.cards;
                draw();

            }
            catch (problem)
            {
                if (!cancelled)
                {
                    window.alert(errorMessage(problem));
                    // The list goes back to what the EMSP has - the form
                    // left as it is typed.
                    void load();
                }
            }

        }


        async function load(): Promise<void> {

            try
            {
                const cards = await api.cards.get();

                if (cancelled)
                    return;

                store = cards;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The cards could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        /** Loaded anew, the form too, which a draw on its own would leave as typed. */
        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }


        // A card typed and not yet entered is a draft like any other page's:
        // leaving asks first.
        const release = unsaved.heldBy(() => anyFormTypedSinceDrawn(content));

        void load();

        return () => { cancelled = true; release(); };

    }

};


/** Where a card stands, in the words of its badge. */
function wordFor(Card: Card): string {
    switch (Card.state) {
        case 'requested':  return 'waits to be let in';
        case 'active':     return 'charges';
        case 'blocked':    return 'blocked';
        case 'rejected':   return 'turned down';
    }
}

/** The colour of its badge. */
function badgeOf(Card: Card): string {
    switch (Card.state) {
        case 'requested':  return 'warn';
        case 'active':     return 'ok';
        case 'blocked':    return 'warn';
        case 'rejected':   return 'error';
    }
}
