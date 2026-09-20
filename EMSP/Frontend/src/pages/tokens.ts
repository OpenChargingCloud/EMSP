import { api, type Token, type Tokens, type TokenSpec } from '../api/client';
import { auth } from '../auth';
import { html, must, render, type HTMLFragment } from '../html';
import type { Page } from '../router';
import { shell } from '../shell';
import { errorMessage, field, formatTimestamp } from '../ui';

/**
 * The tokens this EMSP issued to its customers: the RFID cards and app
 * identities a charging station somewhere asks a roaming partner about.
 *
 * A token lives on one OCPI version, because the partners fetch them per
 * version - a CPO on 2.2.1 reads this EMSP's 2.2.1 tokens. A customer who
 * roams with partners on two versions needs the token on both.
 */
export const tokensPage: Page = {

    title: 'Tokens',

    render({ root }) {

        const content = shell(root, {
            active:    '/configuration/ocpi/tokens',
            title:     'Tokens',
            subtitle:  'What this EMSP handed its customers, and what the partners may authorise.',
            actions:   html`<button type="button" id="reload" class="btn small">Reload</button>`
        });

        render(content, html`<div class="loading">Loading ...</div>`);

        must<HTMLButtonElement>(root, '#reload').addEventListener('click', () => void load());

        const mayManage = auth.can('manageTokens');

        let cancelled = false;
        let store: Tokens | null = null;


        function draw(): void {

            if (store === null)
                return;

            const tokens = store;

            render(content, html`

                ${mayManage ? '' : html`
                    <div class="notice">
                        Signed in as ${auth.user?.roles.join(', ') ?? 'somebody'}, which may look at the tokens but
                        not change them. That needs the EMSP or the system administrator role.
                    </div>
                `}

                <div class="cards">
                    ${listCard(tokens)}
                    ${mayManage ? addCard(tokens) : ''}
                </div>
            `);

            wire();

        }


        function listCard(tokens: Tokens): HTMLFragment {

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-id-card"></i> Issued tokens</h2>

                    <p class="hint">
                        Issued by ${tokens.issuer} (${tokens.partyId}). A partner fetches these for the version it is
                        on, and asks this EMSP in real time about the ones whose whitelist says so.
                    </p>

                    ${tokens.tokens.length === 0
                          ? html`<p class="muted">No token issued yet.</p>`
                          : html`
                              <div class="table-scroll">
                                  <table class="table">
                                      <thead>
                                          <tr>
                                              <th>UID</th>
                                              <th>Type</th>
                                              <th>OCPI</th>
                                              <th>Contract</th>
                                              <th>Whitelist</th>
                                              <th>Valid</th>
                                              <th>Status</th>
                                              <th>Updated</th>
                                              <th></th>
                                          </tr>
                                      </thead>
                                      <tbody>
                                          ${tokens.tokens.map(token => row(token))}
                                      </tbody>
                                  </table>
                              </div>
                          `}

                </section>
            `;

        }


        function row(token: Token): HTMLFragment {

            return html`
                <tr class="${token.valid ? '' : 'dimmed'}">
                    <td>
                        <code>${token.uid}</code>
                        ${token.visual_number ? html`<div class="small muted">${token.visual_number}</div>` : ''}
                    </td>
                    <td>${token.type}</td>
                    <td>${token.version}</td>
                    <td class="small">${token.contract_id ?? token.auth_id ?? '-'}</td>
                    <td><span class="badge">${token.whitelist}</span></td>
                    <td>${token.valid ? 'yes' : html`<span class="warn">no</span>`}</td>
                    <td><span class="badge ${token.status === 'ALLOWED' ? 'ok' : 'warn'}">${token.status}</span></td>
                    <td class="small muted">${formatTimestamp(token.last_updated)}</td>
                    <td class="right">
                        <button type="button" class="btn small danger token-remove" data-version="${token.version}" data-uid="${token.uid}"
                                ${mayManage ? '' : html`disabled`}>
                            Remove
                        </button>
                    </td>
                </tr>
            `;

        }


        function addCard(tokens: Tokens): HTMLFragment {

            const newest = tokens.versions[tokens.versions.length - 1] ?? '';

            return html`
                <section class="card wide">

                    <h2><i class="fa-solid fa-plus"></i> Issue a token</h2>

                    <form id="token-form" class="form-stack">

                        <div class="form-grid">

                            <label>OCPI version
                                <select name="version">
                                    ${tokens.versions.map(version => html`
                                        <option value="${version}" ${version === newest ? html`selected` : ''}>${version}</option>
                                    `)}
                                </select>
                            </label>

                            <label>UID
                                <input type="text" name="uid" placeholder="what the card or the app sends" maxlength="36" required />
                            </label>

                            <label>Type
                                <select name="type">
                                    ${tokens.types.map(type => html`<option value="${type}">${type}</option>`)}
                                </select>
                            </label>

                            <label>Contract
                                <input type="text" name="contractId" placeholder="leave empty to derive one" maxlength="36" />
                            </label>

                            <label>Whitelist
                                <select name="whitelist">
                                    ${tokens.whitelists.map(whitelist => html`
                                        <option value="${whitelist}" ${whitelist === 'ALLOWED' ? html`selected` : ''}>${whitelist}</option>
                                    `)}
                                </select>
                            </label>

                            <label>Visual number
                                <input type="text" name="visualNumber" placeholder="what is printed on the card" maxlength="64" />
                            </label>

                            <label>Issuer
                                <input type="text" name="issuer" placeholder="${tokens.issuer}" maxlength="64" />
                            </label>

                            <label>Language
                                <input type="text" name="language" placeholder="de" maxlength="2" pattern="[a-zA-Z]{2}" />
                            </label>

                        </div>

                        <label class="checkbox">
                            <input type="checkbox" name="valid" checked />
                            Valid
                            <span class="hint">A token that is not valid is still handed to the partners, so that they stop accepting it.</span>
                        </label>

                        <div class="form-actions">
                            <button type="submit" class="btn primary">Issue the token</button>
                            <span id="token-note"  class="form-notice" role="status"></span>
                            <span id="token-error" class="form-error"  role="alert"></span>
                        </div>

                    </form>

                </section>
            `;

        }


        function wire(): void {

            content.querySelectorAll<HTMLButtonElement>('.token-remove').forEach(button => {
                button.addEventListener('click', () => void remove(button.dataset.version ?? '', button.dataset.uid ?? ''));
            });

            content.querySelector<HTMLFormElement>('#token-form')?.addEventListener('submit', event => {
                event.preventDefault();
                void add(event.target as HTMLFormElement);
            });

        }


        async function add(form: HTMLFormElement): Promise<void> {

            const error = must<HTMLElement>(content, '#token-error');
            error.textContent = '';

            const spec: TokenSpec = {
                version:       field(form, 'version'),
                uid:           field(form, 'uid'),
                type:          field(form, 'type'),
                contractId:    field(form, 'contractId')   || undefined,
                issuer:        field(form, 'issuer')       || undefined,
                whitelist:     field(form, 'whitelist'),
                visualNumber:  field(form, 'visualNumber') || undefined,
                language:      field(form, 'language')     || undefined,
                valid:         form.querySelector<HTMLInputElement>('[name="valid"]')?.checked ?? true
            };

            try
            {

                const answer = await api.ocpi.tokens.add(spec);

                if (cancelled)
                    return;

                store = answer.tokens;
                draw();

                must<HTMLElement>(content, '#token-note').textContent = answer.message;

            }
            catch (problem)
            {
                if (!cancelled)
                    error.textContent = errorMessage(problem);
            }

        }


        async function remove(version: string, uid: string): Promise<void> {

            if (!window.confirm(`Take the token '${uid}' (OCPI ${version}) away?\n\nThe partners stop being able to fetch it; whether they stop accepting it is theirs to do.`))
                return;

            try
            {
                const answer = await api.ocpi.tokens.remove(version, uid);

                if (cancelled)
                    return;

                store = answer;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                {
                    window.alert(errorMessage(problem));
                    void load();
                }
            }

        }


        async function load(): Promise<void> {

            try
            {
                const tokens = await api.ocpi.tokens.get();

                if (cancelled)
                    return;

                store = tokens;
                draw();
            }
            catch (problem)
            {
                if (!cancelled)
                    render(content, html`<div class="error-box">The tokens could not be loaded: ${errorMessage(problem)}</div>`);
            }

        }

        void load();

        return () => { cancelled = true; };

    }

};
