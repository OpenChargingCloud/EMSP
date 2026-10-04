import { api, type Contract, type Contracts } from '../api/client';
import { auth } from '../auth';
import { buildPKCS12, createCSR, fromPEM, generateContractKey } from '../crypto/pkcs';
import { html as stringHTML, must } from '@node/html';
import type { Page } from '@node/router';
import { shell } from '@node/shell';
import { errorMessage, field, formatTimestamp } from '@node/ui';
import { anyFormTypedSinceDrawn, unsaved } from '@node/unsaved';
import { html, nothing, render, type TemplateResult } from '@node/view';

/**
 * The contract certificates: what a driver holds and asks for, and - for
 * whoever may manage them - every contract this EMSP issued.
 *
 * Asking for one happens here in the browser and nowhere else: a key pair is
 * made with WebCrypto, a certificate signing request is signed with it, the
 * EMSP answers with a certificate to a fresh eMAID, and the key, the
 * certificate and the sub-CAs are bundled into a PKCS#12 with a password the
 * driver chose - the file the vehicle imports. The private key never leaves
 * this page, and it is gone when the page is.
 */
export const contractsPage: Page = {

    title: 'Contracts',

    render({ root }) {

        const mayIssue   = auth.can('contracts', 'run');
        const mayManage  = auth.can('contracts', 'edit');

        const content = shell(root, {
            active:    '/contracts',
            title:     'Contracts',
            subtitle:  mayManage
                           ? 'Every contract certificate this EMSP issued, and the MO root they chain up to.'
                           : 'Your contract certificates: what your vehicle presents at a charging station.',
            actions:   stringHTML`<button type="button" id="reload" class="btn small">Reload</button>`
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        // Reload throws two passwords typed for a contract not yet made away
        // as thoroughly as leaving the page does, so it asks first.
        must<HTMLButtonElement>(root, '#reload').addEventListener('click', () => {
            if (unsaved.mayBeLost())
                void reload();
        });

        let cancelled = false;
        let store: Contracts | null = null;

        /** The last bundle made on this page, for a second download. */
        let lastBundle: { emaId: string; bytes: Uint8Array } | null = null;

        /** The contract just made, said under the form until the page is loaded anew. */
        let done: { emaId: string; emaIdCompact: string } | null = null;

        /** Whether the MO root is shown as PEM. */
        let rootShown = false;


        /**
         * The whole page, whenever something on it changed: a draw changes
         * only what differs, so two passwords typed into the form for the next
         * contract - and the focus - stay where they are.
         */
        function draw(): void {

            if (store === null)
                return;

            render(content, html`
                <div class="cards">
                    ${mayIssue ? newContractCard(store) : nothing}
                    <section class="card wide" id="contract-list">${list(store)}</section>
                    ${rootCard(store)}
                </div>
            `);

        }


        function rootCard(contracts: Contracts): TemplateResult {

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-certificate"></i> The MO root</h2>

                    <p class="hint">
                        Every contract of ${contracts.issuer} (${contracts.party}) chains up to this root. A vehicle
                        holds it to believe its own contract, and a charge point operator holds it to believe the
                        drivers of this EMSP.
                    </p>

                    <div class="kv-list">
                        <div class="kv"><span class="k">Subject</span><span class="v">${contracts.moRoot.subject}</span></div>
                        <div class="kv"><span class="k">Fingerprint</span><span class="v"><code>${contracts.moRoot.fingerprint}</code></span></div>
                        <div class="kv"><span class="k">Valid until</span><span class="v">${formatTimestamp(contracts.moRoot.notAfter)}</span></div>
                        <div class="kv"><span class="k">File</span><span class="v small">${contracts.moRoot.file}</span></div>
                    </div>

                    <div class="form-actions">
                        <button type="button" id="download-root" class="btn" @click=${downloadRoot}>Download mo-root.pem</button>
                        <button type="button" id="show-root" class="btn small" @click=${() => { rootShown = !rootShown; draw(); }}>Show</button>
                    </div>

                    <pre id="root-pem" class="pem" ?hidden=${!rootShown}>${contracts.moRoot.pem}</pre>

                </section>
            `;

        }


        function newContractCard(contracts: Contracts): TemplateResult {

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-plus"></i> A new contract certificate</h2>

                    <p class="hint">
                        Your browser makes a key pair and a signing request; this EMSP makes a certificate out to a
                        fresh eMAID of ${contracts.party}, good for ${contracts.validityDays} days. The private
                        key never leaves this page: it is put into a PKCS#12 file together with the certificate and
                        the sub-CAs, encrypted with a password you choose here, and that file is what you import
                        into your vehicle.
                    </p>

                    <form id="contract-form" class="form-stack" @submit=${create}>

                        <div class="form-grid">
                            <label>Password for the file
                                <input type="password" name="password" required minlength="4" autocomplete="off" />
                            </label>
                            <label>Password, again
                                <input type="password" name="password2" required minlength="4" autocomplete="off" />
                            </label>
                        </div>

                        <div class="form-actions">
                            <button type="submit" class="btn primary">Create the certificate</button>
                            <span id="contract-note"  class="form-notice" role="status"></span>
                            <span id="contract-error" class="form-error"  role="alert"></span>
                        </div>

                    </form>

                    <div id="contract-done" ?hidden=${done === null}>
                        ${done === null ? nothing : doneBox(done.emaId, done.emaIdCompact)}
                    </div>

                </section>
            `;

        }


        function doneBox(emaId: string, emaIdCompact: string): TemplateResult {

            const file = `contract-${emaIdCompact}.p12`;

            return html`
                <div class="notice">

                    <p>
                        <strong>Your contract is <span class="emaid">${emaId}</span>.</strong>
                        The file <code>${file}</code> was downloaded; it holds your private key, the certificate and
                        the sub-CAs, encrypted with the password you chose. Keep it: this page cannot make it again,
                        because the key is gone with the page.
                    </p>

                    <p>To charge with it, put it into your vehicle together with the MO root:</p>

                    <ol class="steps">
                        <li>On the vehicle's <em>Certificates</em> page, import <code>mo-root.pem</code> as the
                            <em>MO root</em> and <code>${file}</code> as the <em>contract</em>, with the password.</li>
                        <li>Or from the command line of EVCLI:</li>
                    </ol>

                    <pre class="pem">dotnet run --project EVCLI -- \\
    --import-certificate moRoot=mo-root.pem \\
    --import-certificate contract=${file} \\
    --certificate-password ${'<the password>'}</pre>

                    <p class="small muted">
                        The vehicle presents the certificate at the charging station, which forwards it to its
                        operator - who has to hold the MO root of this EMSP to accept it.
                    </p>

                    <div class="form-actions">
                        <button type="button" id="download-again" class="btn small" @click=${downloadAgain}>Download ${file} again</button>
                        <button type="button" id="download-root-2" class="btn small" @click=${downloadRoot}>Download mo-root.pem</button>
                    </div>

                </div>
            `;

        }


        /** What is in the card of the contracts: its heading, and the contracts. */
        function list(contracts: Contracts): TemplateResult {

            return html`

                <h2><i class="fa-solid fa-file-contract"></i> ${contracts.everyone ? 'Issued contracts' : 'Your contracts'}</h2>

                ${contracts.contracts.length === 0
                      ? html`<p class="muted">${contracts.everyone ? 'No contract issued yet.' : 'You hold no contract yet.'}</p>`
                      : html`
                          <div class="table-scroll">
                              <table class="table">
                                  <thead>
                                      <tr>
                                          <th>eMAID</th>
                                          <th>Status</th>
                                          ${contracts.everyone ? html`<th>Owner</th>` : nothing}
                                          <th>Issued</th>
                                          <th>Valid until</th>
                                          <th>Serial</th>
                                          <th></th>
                                      </tr>
                                  </thead>
                                  <tbody>
                                      ${contracts.contracts.map(contract => row(contract, contracts.everyone))}
                                  </tbody>
                              </table>
                          </div>
                      `}

            `;

        }


        function row(contract: Contract, everyone: boolean): TemplateResult {

            const over      = contract.status !== 'valid';
            const mayRevoke = !over && (mayManage || (mayIssue && contract.owner === auth.user?.username));

            return html`
                <tr class="${over ? 'dimmed' : ''}">
                    <td><code>${contract.emaId}</code><div class="small muted">${contract.emaIdCompact}</div></td>
                    <td>
                        <span class="badge ${contract.status === 'valid' ? 'ok' : 'warn'}">${contract.status}</span>
                        ${contract.revokedAt ? html`<div class="small muted">by ${contract.revokedBy ?? '-'}, ${formatTimestamp(contract.revokedAt)}</div>` : nothing}
                    </td>
                    ${everyone ? html`<td>${contract.owner}</td>` : nothing}
                    <td class="small muted">${formatTimestamp(contract.issuedAt)}</td>
                    <td class="small muted">${formatTimestamp(contract.notAfter)}</td>
                    <td class="small"><code>${contract.serialNumber}</code></td>
                    <td class="right">
                        ${contract.certificate
                              ? html`<button type="button" class="btn small contract-download" data-emaid="${contract.emaIdCompact}"
                                             @click=${() => download(contract.certificate!, `contract-${contract.emaIdCompact}.pem`, 'application/x-pem-file')}>Certificate</button>`
                              : nothing}
                        <button type="button" class="btn small danger contract-revoke" data-emaid="${contract.emaId}"
                                ?disabled=${!mayRevoke} @click=${() => void revoke(contract.emaId)}>
                            Revoke
                        </button>
                    </td>
                </tr>
            `;

        }


        function create(event: SubmitEvent): void {

            event.preventDefault();

            void make(event.currentTarget as HTMLFormElement);

        }


        async function make(form: HTMLFormElement): Promise<void> {

            const note   = must<HTMLElement>(content, '#contract-note');
            const error  = must<HTMLElement>(content, '#contract-error');
            const button = must<HTMLButtonElement>(form, 'button[type="submit"]');

            error.textContent = '';
            note.textContent  = '';

            const password = field(form, 'password', false);

            if (password !== field(form, 'password2', false)) {
                error.textContent = 'The two passwords differ.';
                return;
            }

            button.disabled = true;

            try
            {

                note.textContent = 'Making a key pair ...';
                const keys   = await generateContractKey();

                note.textContent = 'Signing the request ...';
                const csr    = await createCSR(keys, auth.user?.username ?? 'driver');

                note.textContent = 'Asking the EMSP for the certificate ...';
                const issued = await api.contracts.issue(csr);

                if (cancelled)
                    return;

                note.textContent = 'Bundling the key with the certificate ...';

                const leaf   = fromPEM(issued.certificate)[0];
                const chain  = fromPEM(issued.chain);

                if (leaf === undefined)
                    throw new Error('The EMSP answered without a certificate.');

                const bytes  = await buildPKCS12({
                                   privateKey:    keys.privateKey,
                                   certificate:   leaf,
                                   chain,
                                   password,
                                   friendlyName:  issued.contract.emaId
                               });

                lastBundle = { emaId: issued.contract.emaIdCompact, bytes };

                download(bytes, `contract-${issued.contract.emaIdCompact}.p12`, 'application/x-pkcs12');

                store = issued.contracts;
                done  = { emaId: issued.contract.emaId, emaIdCompact: issued.contract.emaIdCompact };

                draw();

                // A draw leaves a form as it is typed into; this contract was
                // made, so the two passwords go, and the button is there for
                // the next.
                form.reset();
                button.disabled  = false;

                note.textContent = issued.message;

            }
            catch (problem)
            {
                if (!cancelled) {
                    error.textContent = errorMessage(problem);
                    note.textContent  = '';
                    button.disabled   = false;
                    done              = null;
                    draw();
                }
            }

        }


        async function revoke(emaId: string): Promise<void> {

            if (!window.confirm(`Revoke the contract ${emaId}?\n\nThe vehicle can no longer charge with it, and this cannot be undone. The token that went with it is taken away as well.`))
                return;

            try
            {
                const answer = await api.contracts.revoke(emaId);

                if (cancelled)
                    return;

                store = answer.contracts;
                draw();
            }
            catch (problem)
            {
                if (!cancelled) {
                    window.alert(errorMessage(problem));
                    // The list goes back to what the EMSP has, which a draw
                    // puts right - the form above left as it is typed.
                    void load();
                }
            }

        }


        function downloadRoot(): void {
            if (store)
                download(store.moRoot.pem, 'mo-root.pem', 'application/x-pem-file');
        }


        function downloadAgain(): void {
            if (lastBundle)
                download(lastBundle.bytes, `contract-${lastBundle.emaId}.p12`, 'application/x-pkcs12');
        }


        /**
         * The contracts as the EMSP has them now, drawn over the page as it is
         * - what is typed into the form kept, as a draw keeps it. Reload
         * empties it itself.
         */
        async function load(): Promise<void> {

            try
            {
                const contracts = await api.contracts.get();

                if (cancelled)
                    return;

                store = contracts;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The contracts could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        /**
         * Loaded anew - Reload - is what the EMSP has: the form emptied, which
         * a draw on its own would leave as typed, and the contract just made
         * no longer said under it, as a page opened anew would not say it.
         */
        async function reload(): Promise<void> {

            done = null;

            await load();

            if (!cancelled)
                content.querySelectorAll('form').forEach(form => form.reset());

        }


        // Two passwords typed for a contract not yet made are a draft like any
        // other page's: leaving asks first.
        const release = unsaved.heldBy(() => anyFormTypedSinceDrawn(content));

        void load();

        return () => { cancelled = true; lastBundle = null; release(); };

    }

};


/** Hand the browser a file to save. */
function download(content: Uint8Array | string, filename: string, type: string): void {

    const blob = new Blob([content as BlobPart], { type });
    const url  = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href     = url;
    link.download = filename;

    document.body.appendChild(link);
    link.click();
    link.remove();

    // Not at once: some browsers start the download after the click returns.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);

}
