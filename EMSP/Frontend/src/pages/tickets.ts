import { api, type AccountCertificate, type AccountKeys, type TicketRecord, type Tickets } from '../api/client';
import { auth } from '../auth';
import { generateTicketKey, newTicketId, ticketPayload, ticketRequest, toHex } from '../crypto/cose';
import { certificateKeyId, createCSR, encryptedPrivateKeyPEM, fromPEM, generateContractKey, openEncryptedPrivateKey } from '../crypto/pkcs';
import { download } from '../download';
import { toTheMinute } from '../time';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage, field, formatNumber, formatTimestamp, numberField } from '@node/ui';
import { anyFormTypedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * A driver's long-term keys and the charging tickets signed with them - and,
 * for whoever may manage them, every driver's.
 *
 * An account key is made here, in the browser: its signing request goes to
 * the EMSP, the certificate comes back, and the key - encrypted with a
 * password the driver chose - and the certificate are saved as one file. It
 * says "this is me" and opens nothing on its own.
 *
 * A charging ticket is made here as well: a key pair for the ticket alone,
 * the ticket written and signed with it and with the account key opened from
 * that file, and sent; the EMSP takes the account's signature off and signs
 * it itself. What comes back is saved with the ticket's key, encrypted with a
 * password of its own: what a charge point operator who believes the EMSP is
 * shown, without learning whose it is.
 */
export const ticketsPage: Page = {

    title: 'Keys & tickets',

    render({ root }) {

        const mayRun     = auth.can('tickets', 'run');
        const mayManage  = auth.can('tickets', 'edit');

        const content = shell(root, {
            active:    '/tickets',
            title:     'Keys & tickets',
            subtitle:  mayManage && !mayRun
                           ? 'Every driver\'s account keys and the charging tickets signed with them.'
                           : 'Your account keys, and the charging tickets you charge with anonymously.',
            actions:   reloadButton(() => reload())
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        let cancelled = false;
        let keys:     AccountKeys | null = null;
        let tickets:  Tickets     | null = null;

        /** The last ticket made on this page, for a second download. */
        let lastTicket: { id: string; ticket: Uint8Array; key: string } | null = null;


        function draw(): void {

            if (keys === null || tickets === null)
                return;

            render(content, html`
                <div class="cards">
                    ${mayRun ? newKeyCard(keys) : nothing}
                    <section class="card wide" id="key-list">${keyList(keys)}</section>
                    ${mayRun ? newTicketCard(tickets) : nothing}
                    <section class="card wide" id="ticket-list">${ticketList(tickets)}</section>
                    ${issuerCard(tickets)}
                </div>
            `);

        }


        // Account keys

        function newKeyCard(accountKeys: AccountKeys): TemplateResult {

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-key"></i> A new account key</h2>

                    <p class="hint">
                        Your browser makes a key pair and a signing request; this EMSP certifies the key as yours,
                        good for ${Math.round(accountKeys.validityDays / 365)} years. The private key never leaves
                        this page but in the file you save: encrypted with the password you choose here, together
                        with its certificate. You need the file and the password to have a charging ticket signed.
                    </p>

                    <form id="key-form" class="form-stack" @submit=${makeKey}>

                        <div class="form-grid">
                            <label>Label
                                <input type="text" name="label" maxlength="64" placeholder="optional, e.g. Phone" />
                            </label>
                            <label>Password for the file
                                <input type="password" name="password" required minlength="8" autocomplete="new-password" />
                            </label>
                            <label>Password, again
                                <input type="password" name="password2" required minlength="8" autocomplete="new-password" />
                            </label>
                        </div>

                        <div class="form-actions">
                            <button type="submit" class="btn primary">Make the key</button>
                            <span id="key-note"  class="form-notice" role="status"></span>
                            <span id="key-error" class="form-error"  role="alert"></span>
                        </div>

                    </form>

                </section>
            `;

        }


        function keyList(accountKeys: AccountKeys): TemplateResult {

            return html`

                <h2><i class="fa-solid fa-id-badge"></i> ${accountKeys.everyone ? 'Every driver\'s account keys' : 'Your account keys'}</h2>

                ${accountKeys.certificates.length === 0
                      ? html`<p class="muted">${accountKeys.everyone ? 'No driver has an account key yet.' : 'You have no account key yet.'}</p>`
                      : html`
                          <div class="table-scroll">
                              <table class="table">
                                  <thead>
                                      <tr>
                                          <th>Key</th>
                                          ${accountKeys.everyone ? html`<th>Driver</th>` : nothing}
                                          <th>Status</th>
                                          <th>Good until</th>
                                          <th></th>
                                      </tr>
                                  </thead>
                                  <tbody>
                                      ${accountKeys.certificates.map(certificate => keyRow(certificate, accountKeys.everyone))}
                                  </tbody>
                              </table>
                          </div>
                      `}

            `;

        }


        function keyRow(certificate: AccountCertificate, everyone: boolean): TemplateResult {

            const mine = certificate.owner.toLowerCase() === (auth.user?.username ?? '').toLowerCase();

            return html`
                <tr data-id="${certificate.id}" class="${certificate.status === 'valid' ? '' : 'dimmed'}">
                    <td>
                        ${certificate.label ?? html`<span class="muted">no label</span>`}
                        <div class="small muted"><code>${certificate.id.slice(0, 16)}...</code></div>
                    </td>
                    ${everyone ? html`<td>${certificate.owner}</td>` : nothing}
                    <td><span class="badge ${certificate.status === 'valid' ? 'ok' : 'warn'}">${certificate.status}</span></td>
                    <td class="small muted">${formatTimestamp(certificate.notAfter, toTheMinute)}</td>
                    <td class="right">
                        ${certificate.status === 'valid' && (mine || mayManage) ? html`
                            <button type="button" class="btn small danger key-revoke" @click=${() => void revoke(certificate)}>Take back</button>
                        ` : nothing}
                    </td>
                </tr>
            `;

        }


        function makeKey(event: SubmitEvent): void {

            event.preventDefault();

            const form      = event.currentTarget as HTMLFormElement;
            const note      = must<HTMLElement>(content, '#key-note');
            const error     = must<HTMLElement>(content, '#key-error');

            note.textContent  = '';
            error.textContent = '';

            const password  = field(form, 'password', false);

            if (password !== field(form, 'password2', false)) {
                error.textContent = 'The two passwords differ.';
                return;
            }

            const label     = field(form, 'label');
            const button    = must<HTMLButtonElement>(form, 'button[type="submit"]');

            button.disabled = true;

            void (async () => {
                try
                {

                    note.textContent = 'Making a key pair ...';
                    const pair       = await generateContractKey();
                    const csr        = await createCSR(pair, auth.user?.username ?? 'driver');

                    note.textContent = 'Asking the EMSP for the certificate ...';
                    const issued     = await api.accountKeys.issue(csr, label);

                    if (cancelled)
                        return;

                    const file       = await encryptedPrivateKeyPEM(pair.privateKey, password) + issued.certificate;

                    download(file, `${auth.user?.username ?? 'driver'}-account-key-${issued.accountCertificate.id.slice(0, 8)}.pem`, 'application/x-pem-file');

                    keys = issued.accountKeys;
                    draw();

                    form.reset();
                    button.disabled  = false;

                    must<HTMLElement>(content, '#key-note').textContent = `${issued.message} The key and its certificate were saved as a file.`;

                }
                catch (problem)
                {
                    if (!cancelled) {
                        error.textContent = errorMessage(problem);
                        note.textContent  = '';
                        button.disabled   = false;
                    }
                }
            })();

        }


        async function revoke(certificate: AccountCertificate): Promise<void> {

            if (!window.confirm(`Take the account key ${certificate.label ?? certificate.id.slice(0, 16)} back?\n\nNo ticket is signed on its word from now on. Tickets signed before stay good.`))
                return;

            try
            {
                const answer = await api.accountKeys.revoke(certificate.id);

                if (cancelled)
                    return;

                keys = answer.accountKeys;
                draw();
            }
            catch (problem)
            {
                if (!cancelled) {
                    window.alert(errorMessage(problem));
                    void load();
                }
            }

        }


        // Charging tickets

        function newTicketCard(store: Tickets): TemplateResult {

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-ticket"></i> A charging ticket</h2>

                    <p class="hint">
                        A ticket lets whoever holds its key charge at a charge point operator who believes this EMSP,
                        without saying who you are. Your browser makes a key for the ticket alone and signs the ticket
                        with it and with your account key; this EMSP takes your signature off and signs it itself.
                        You save the ticket and its key, encrypted with a password of its own. Good for
                        ${store.maxValidityDays} days at the most.
                    </p>

                    <form id="ticket-form" class="form-stack" @submit=${makeTicket}>

                        <div class="form-grid">
                            <label>Your account key file
                                <input type="file" name="keyFile" required accept=".pem,application/x-pem-file" />
                            </label>
                            <label>Its password
                                <input type="password" name="keyPassword" required autocomplete="current-password" />
                            </label>
                            <label>Good for, hours
                                <input type="number" name="hours" required min="1" max="${store.maxValidityDays * 24}" step="1" value="24" />
                            </label>
                            <label>At most, kW
                                <input type="number" name="maxKW" min="0.1" step="0.1" placeholder="no limit" />
                            </label>
                            <label>At most, minutes
                                <input type="number" name="maxMinutes" min="1" step="1" placeholder="no limit" />
                            </label>
                            <label>At most, kWh
                                <input type="number" name="maxKWh" min="0.1" step="0.1" placeholder="no limit" />
                            </label>
                            <label>Password for the ticket's key
                                <input type="password" name="ticketPassword" required minlength="4" autocomplete="new-password" />
                            </label>
                            <label>Password, again
                                <input type="password" name="ticketPassword2" required minlength="4" autocomplete="new-password" />
                            </label>
                        </div>

                        <div class="form-actions">
                            <button type="submit" class="btn primary">Make the ticket</button>
                            ${lastTicket ? html`
                                <button type="button" id="ticket-again" class="btn" @click=${downloadTicketAgain}>Save ticket ${lastTicket.id.slice(0, 8)} again</button>
                            ` : nothing}
                            <span id="ticket-note"  class="form-notice" role="status"></span>
                            <span id="ticket-error" class="form-error"  role="alert"></span>
                        </div>

                    </form>

                </section>
            `;

        }


        function ticketList(store: Tickets): TemplateResult {

            return html`

                <h2><i class="fa-solid fa-ticket-simple"></i> ${store.everyone ? 'Every driver\'s tickets' : 'Your tickets'}</h2>

                ${store.tickets.length === 0
                      ? html`<p class="muted">${store.everyone ? 'No ticket was signed yet.' : 'You have no ticket yet.'}</p>`
                      : html`
                          <div class="table-scroll">
                              <table class="table">
                                  <thead>
                                      <tr>
                                          <th>Ticket</th>
                                          ${store.everyone ? html`<th>Driver</th>` : nothing}
                                          <th>Status</th>
                                          <th>Good from</th>
                                          <th>Until</th>
                                          <th>Limits</th>
                                      </tr>
                                  </thead>
                                  <tbody>
                                      ${store.tickets.map(ticket => ticketRow(ticket, store.everyone))}
                                  </tbody>
                              </table>
                          </div>
                      `}

            `;

        }


        function ticketRow(ticket: TicketRecord, everyone: boolean): TemplateResult {

            const limits = [
                ticket.maxKW      !== null ? `${formatNumber(ticket.maxKW, 1)} kW`   : null,
                ticket.maxMinutes !== null ? `${ticket.maxMinutes} min`               : null,
                ticket.maxKWh     !== null ? `${formatNumber(ticket.maxKWh, 1)} kWh` : null
            ].filter(limit => limit !== null);

            return html`
                <tr data-id="${ticket.id}" class="${ticket.status === 'expired' ? 'dimmed' : ''}">
                    <td><code>${ticket.id.slice(0, 16)}...</code></td>
                    ${everyone ? html`<td>${ticket.owner}</td>` : nothing}
                    <td><span class="badge ${ticket.status === 'valid' ? 'ok' : ''}">${ticket.status}</span></td>
                    <td class="small muted">${formatTimestamp(ticket.notBefore, toTheMinute)}</td>
                    <td class="small muted">${formatTimestamp(ticket.notAfter,  toTheMinute)}</td>
                    <td class="small">${limits.length === 0 ? html`<span class="muted">none</span>` : limits.join(', ')}</td>
                </tr>
            `;

        }


        function issuerCard(store: Tickets): TemplateResult {

            return html`
                <section class="card wide" id="ticket-issuer">

                    <h2><i class="fa-solid fa-stamp"></i> The ticket issuer</h2>

                    <p class="hint">
                        Every ticket of ${store.party} is signed by this key. A charge point operator holds its
                        certificate to believe the tickets.
                    </p>

                    <div class="kv-list">
                        <div class="kv"><span class="k">Subject</span><span class="v">${store.issuer.subject}</span></div>
                        <div class="kv"><span class="k">Fingerprint</span><span class="v"><code>${store.issuer.fingerprint}</code></span></div>
                        <div class="kv"><span class="k">Valid until</span><span class="v">${formatTimestamp(store.issuer.notAfter, toTheMinute)}</span></div>
                    </div>

                    <div class="form-actions">
                        <button type="button" id="download-issuer" class="btn"
                                @click=${() => download(store.issuer.pem, 'ticket-issuer.pem', 'application/x-pem-file')}>Download ticket-issuer.pem</button>
                    </div>

                </section>
            `;

        }


        function makeTicket(event: SubmitEvent): void {

            event.preventDefault();

            const form      = event.currentTarget as HTMLFormElement;
            const note      = must<HTMLElement>(content, '#ticket-note');
            const error     = must<HTMLElement>(content, '#ticket-error');

            note.textContent  = '';
            error.textContent = '';

            const ticketPassword = field(form, 'ticketPassword', false);

            if (ticketPassword !== field(form, 'ticketPassword2', false)) {
                error.textContent = 'The two passwords for the ticket\'s key differ.';
                return;
            }

            const hours      = numberField(form, 'hours');
            const maxKW      = numberField(form, 'maxKW');
            const maxMinutes = numberField(form, 'maxMinutes');
            const maxKWh     = numberField(form, 'maxKWh');

            if (!(hours >= 1) || hours > tickets!.maxValidityDays * 24) {
                error.textContent = `A ticket is good for 1 hour at the least and ${tickets!.maxValidityDays * 24} hours at the most.`;
                return;
            }

            const file       = form.querySelector<HTMLInputElement>('[name="keyFile"]')?.files?.[0];

            if (file === undefined) {
                error.textContent = 'Choose the file of your account key.';
                return;
            }

            const keyPassword = field(form, 'keyPassword', false);
            const button      = must<HTMLButtonElement>(form, 'button[type="submit"]');

            button.disabled = true;

            void (async () => {
                try
                {

                    note.textContent   = 'Opening your account key ...';
                    const text         = await file.text();
                    const accountKey   = await openEncryptedPrivateKey(text, keyPassword);
                    const certificate  = fromPEM(text)[0];

                    if (certificate === undefined)
                        throw new Error('The file holds no certificate beside the key: choose the file this page saved.');

                    note.textContent   = 'Making the ticket ...';
                    const ticketKey    = await generateTicketKey();
                    const id           = newTicketId();
                    const now          = Date.now();

                    const payload      = await ticketPayload({
                                             id,
                                             emsp:        tickets!.party,
                                             publicKey:   ticketKey.publicKey,
                                             notBefore:   new Date(now),
                                             notAfter:    new Date(now + hours * 3_600_000),
                                             ...(Number.isNaN(maxKW)      ? {} : { maxKW }),
                                             ...(Number.isNaN(maxMinutes) ? {} : { maxMinutes: Math.round(maxMinutes) }),
                                             ...(Number.isNaN(maxKWh)     ? {} : { maxKWh })
                                         });

                    const request      = await ticketRequest(payload, ticketKey.privateKey, accountKey, await certificateKeyId(certificate));

                    note.textContent   = 'Asking the EMSP to sign it ...';
                    const issued       = await api.tickets.issue(toBase64(request));

                    if (cancelled)
                        return;

                    const signed       = Uint8Array.from(atob(issued.ticket), c => c.charCodeAt(0));
                    const keyFile      = await encryptedPrivateKeyPEM(ticketKey.privateKey, ticketPassword);

                    lastTicket = { id: toHex(id), ticket: signed, key: keyFile };

                    downloadTicketAgain();

                    tickets = await api.tickets.get();

                    if (cancelled)
                        return;

                    draw();

                    form.reset();
                    button.disabled  = false;

                    must<HTMLElement>(content, '#ticket-note').textContent = `The ticket ${toHex(id).slice(0, 8)} was signed and saved, with its key.`;

                }
                catch (problem)
                {
                    if (!cancelled) {
                        error.textContent = errorMessage(problem);
                        note.textContent  = '';
                        button.disabled   = false;
                    }
                }
            })();

        }


        function downloadTicketAgain(): void {
            if (lastTicket) {
                download(lastTicket.ticket, `ticket-${lastTicket.id.slice(0, 8)}.cose`, 'application/cose');
                download(lastTicket.key,    `ticket-${lastTicket.id.slice(0, 8)}.key.pem`, 'application/x-pem-file');
            }
        }


        async function load(): Promise<void> {

            try
            {
                const [ loadedKeys, loadedTickets ] = await Promise.all([ api.accountKeys.get(), api.tickets.get() ]);

                if (cancelled)
                    return;

                keys    = loadedKeys;
                tickets = loadedTickets;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The keys and tickets could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        /** Loaded anew, the forms too, which a draw on its own would leave as typed. */
        async function reload(): Promise<void> {

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }


        // Passwords typed and a file chosen for a key or a ticket not yet made
        // are a draft like any other page's: leaving asks first.
        const release = unsaved.heldBy(() => anyFormTypedSinceDrawn(content));

        void load();

        return () => { cancelled = true; lastTicket = null; release(); };

    }

};


/** Bytes as Base64, as the request goes in JSON. */
function toBase64(bytes: Uint8Array): string {
    let text = '';
    for (const byte of bytes)
        text += String.fromCharCode(byte);
    return btoa(text);
}
